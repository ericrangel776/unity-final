using System;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>HP, posture, stun and i-frames for anything that can be hit.</summary>
    public sealed class CombatantStatus : MonoBehaviour
    {
        [SerializeField] private Team team = Team.Enemy;
        [SerializeField] private int maxHp = 100;
        [SerializeField] private float maxPosture = 100f;
        [SerializeField] private float postureDecayPerSecond = 10f;
        [Tooltip("Posture added when one of our cards is broken, at the baseline margin (scaled by the value gap).")]
        [SerializeField] private float breakPostureDamage = 25f;
        [Tooltip("Stun when one of our cards is broken, at the baseline margin (scaled by the value gap).")]
        [SerializeField] private float breakStunSeconds = 0.8f;
        [Tooltip("Stun when posture fills up.")]
        [SerializeField] private float postureBreakStunSeconds = 2f;
        [Header("Hit reactions")]
        [Tooltip("Stun from a damaging body hit.")]
        [SerializeField] private float hitFlinchSeconds = 0.3f;
        [Tooltip("After a flinch, hits can't flinch again for this long (prevents poke stunlock).")]
        [SerializeField] private float flinchCooldownSeconds = 1f;
        [Tooltip("Seconds for knockback to decay to zero.")]
        [SerializeField] private float knockbackDecaySeconds = 0.15f;
        [Tooltip("After one of our cards is broken, the first damaging hit within this window is a counter (x1.5).")]
        [SerializeField] private float counterWindowSeconds = 1f;

        private float stunRemaining;
        private float counterWindowRemaining;
        private float flinchCooldownRemaining;
        private float knockbackDecayRate;

        public Team Team { get => team; set => team = value; }
        public int MaxHp => maxHp;
        public int Hp { get; private set; }
        public float MaxPosture => maxPosture;
        public float Posture { get; private set; }
        public bool IsStunned => stunRemaining > 0f;
        public float StunRemaining => Mathf.Max(0f, stunRemaining);
        public bool IsDead => Hp <= 0;
        /// <summary>Set by the dash (i-frames). Hits are ignored while true.</summary>
        public bool IsInvulnerable { get; set; }
        public int OwnerId => gameObject.GetInstanceID();
        public float HitFlinchSeconds { get => hitFlinchSeconds; set => hitFlinchSeconds = Mathf.Max(0f, value); }
        /// <summary>Push from the last body hit, decaying to zero. Movers add this to their motion.</summary>
        public Vector3 KnockbackVelocity { get; private set; }

        public event Action<int> Damaged;
        /// <summary>A damaging body hit landed: damage and the flat direction it pushed us.</summary>
        public event Action<int, Vector3> HitTaken;
        /// <summary>A body hit flinched us (not raised while the flinch cooldown runs).</summary>
        public event Action Flinched;
        public event Action Broken;
        public event Action Died;
        /// <summary>True while a counter hit is possible (just after one of our cards was broken).</summary>
        public bool IsCounterWindowOpen => counterWindowRemaining > 0f;

        /// <summary>Any combatant took a counter hit: the victim and the boosted damage. For global feedback.</summary>
        public static event Action<CombatantStatus, int> CounterHit;

        private void Awake()
        {
            Hp = maxHp;
        }

        private void Update()
        {
            if (stunRemaining > 0f) stunRemaining -= Time.deltaTime;
            if (Posture > 0f) Posture = Mathf.Max(0f, Posture - postureDecayPerSecond * Time.deltaTime);
            if (flinchCooldownRemaining > 0f) flinchCooldownRemaining -= Time.deltaTime;
            // Scaled time on purpose: the break's own hit-stop shouldn't eat the window.
            if (counterWindowRemaining > 0f) counterWindowRemaining -= Time.deltaTime;
            if (KnockbackVelocity != Vector3.zero)
                KnockbackVelocity = Vector3.MoveTowards(KnockbackVelocity, Vector3.zero, knockbackDecayRate * Time.deltaTime);
        }

        /// <summary>Applies a body hit with no knockback. Returns false if the hit was ignored.</summary>
        public bool TakeHit(in AttackPayload payload) => TakeHit(payload, Vector3.zero);

        /// <summary>
        /// Applies a body hit pushing us along <paramref name="fromDirection"/> (attacker to us).
        /// <paramref name="canCounter"/> is false for the swing that broke our card, so only a separate
        /// follow-up gets the counter bonus. Returns false if the hit was ignored (i-frames, dead, or friendly).
        /// </summary>
        public bool TakeHit(in AttackPayload payload, Vector3 fromDirection, bool canCounter = true)
        {
            if (IsInvulnerable || IsDead || payload.Team == team) return false;

            int damage = CombatMath.Damage(payload);
            // 0-damage hits (0-cards) and the breaking swing itself don't spend the counter window.
            bool counter = canCounter && damage > 0 && IsCounterWindowOpen;
            if (counter)
            {
                damage = CombatMath.CounterDamage(damage);
                counterWindowRemaining = 0f;
            }
            Hp = Mathf.Max(0, Hp - damage);
            Damaged?.Invoke(damage);
            if (counter) CounterHit?.Invoke(this, damage);
            if (Hp == 0)
            {
                Died?.Invoke();
                return true;
            }
            if (damage > 0) React(payload, fromDirection, damage);
            return true;
        }

        private void React(in AttackPayload payload, Vector3 fromDirection, int damage)
        {
            var direction = Push(fromDirection, CombatMath.KnockbackSpeed(payload));
            HitTaken?.Invoke(damage, direction);

            if (flinchCooldownRemaining > 0f || hitFlinchSeconds <= 0f) return;
            stunRemaining = Mathf.Max(stunRemaining, hitFlinchSeconds);
            flinchCooldownRemaining = flinchCooldownSeconds;
            Flinched?.Invoke();
        }

        /// <summary>Sets a decaying push along the flattened direction. Returns the unit direction used.</summary>
        private Vector3 Push(Vector3 direction, float speed)
        {
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            KnockbackVelocity = direction * speed;
            knockbackDecayRate = knockbackDecaySeconds > 0f ? speed / knockbackDecaySeconds : float.MaxValue;
            return direction;
        }

        /// <summary>Card break at the baseline margin with no shove.</summary>
        public void ApplyBreak() => ApplyBreak(CombatMath.BaselineBreakMargin, Vector3.zero);

        /// <summary>
        /// Called when one of our cards loses a clash: posture damage and stun scaled by how decisively
        /// we lost (<paramref name="margin"/>), plus a shove along <paramref name="awayFromWinner"/>.
        /// </summary>
        public void ApplyBreak(int margin, Vector3 awayFromWinner)
        {
            if (IsDead) return;
            Posture += breakPostureDamage * CombatMath.BreakPostureScale(margin);
            float stun = breakStunSeconds * CombatMath.BreakStunScale(margin);
            if (awayFromWinner != Vector3.zero) Push(awayFromWinner, CombatMath.BreakShoveSpeed(margin));
            if (Posture >= maxPosture)
            {
                Posture = 0f;
                stun = postureBreakStunSeconds;
            }
            stunRemaining = Mathf.Max(stunRemaining, stun);
            counterWindowRemaining = counterWindowSeconds;
            Broken?.Invoke();
        }

        public void Revive()
        {
            Hp = maxHp;
            Posture = 0f;
            stunRemaining = 0f;
            flinchCooldownRemaining = 0f;
            counterWindowRemaining = 0f;
            KnockbackVelocity = Vector3.zero;
        }
    }
}
