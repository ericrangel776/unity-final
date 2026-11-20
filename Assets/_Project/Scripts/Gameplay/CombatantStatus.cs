using System;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>A damaging body hit that landed, for global feedback (hit-stop, sparks, damage numbers).</summary>
    public readonly struct HitInfo
    {
        public readonly CombatantStatus Victim;
        public readonly AttackPayload Payload;
        /// <summary>Final damage after multipliers, the counter bonus and any non-lethal cap. Always above 0.</summary>
        public readonly int Damage;
        public readonly bool WasCounter;
        public readonly bool Killed;
        /// <summary>Where to show the hit: the victim's position, raised to body height.</summary>
        public readonly Vector3 Position;
        /// <summary>
        /// Effect damage (burn tick, chain jump, finisher shockwave): no hit-stop, no shake, never applies a status.
        /// </summary>
        public readonly bool IsEffect;

        public HitInfo(CombatantStatus victim, in AttackPayload payload, int damage, bool wasCounter, bool killed,
            Vector3 position, bool isEffect = false)
        {
            Victim = victim;
            Payload = payload;
            Damage = damage;
            WasCounter = wasCounter;
            Killed = killed;
            Position = position;
            IsEffect = isEffect;
        }
    }

    /// <summary>HP, posture, stun and i-frames for anything that can be hit.</summary>
    public sealed class CombatantStatus : MonoBehaviour, IHpPool
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
        /// <summary>Set while an enemy winds up a telegraph: hits still damage but can't flinch it out of the attack.</summary>
        public bool FlinchArmor { get; set; }
        /// <summary>Multiplies HP damage taken from hits (boss Exhausted: 2). 1 = normal.</summary>
        public float DamageTakenMultiplier { get; set; } = 1f;
        /// <summary>Multiplies posture damage taken from breaks and effects (boss reload window / Exhausted). 1 = normal.</summary>
        public float PostureTakenMultiplier { get; set; } = 1f;
        /// <summary>Push from the last body hit, decaying to zero. Movers add this to their motion.</summary>
        public Vector3 KnockbackVelocity { get; private set; }
        /// <summary>Multiplies the push from Wind hits only (the boss: 0.5). 1 = normal.</summary>
        public float KnockbackTakenMultiplier { get; set; } = 1f;
        /// <summary>A non-poke body hit pushed us (Wind slam detection listens for Wind payloads).</summary>
        public event Action<AttackPayload> KnockedBackBy;

        private static readonly System.Collections.Generic.List<CombatantStatus> all =
            new System.Collections.Generic.List<CombatantStatus>();
        /// <summary>Every enabled combatant (chain targeting). Dead ones stay listed; filter on <see cref="IsDead"/>.</summary>
        public static System.Collections.Generic.IReadOnlyList<CombatantStatus> All => all;

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
        /// <summary>Any combatant took a damaging body hit: the victim and the damage. For global feedback (SFX).</summary>
        public static event Action<CombatantStatus, int> AnyHitTaken;
        /// <summary>
        /// Any combatant took a damaging body hit (damage above 0), lethal hits included. Raised after HP changes and
        /// before <see cref="Died"/>, so feedback for the killing blow plays before the death.
        /// </summary>
        public static event Action<HitInfo> AnyHitLanded;
        /// <summary>We healed: the HP actually restored (always above 0).</summary>
        public event Action<int> Healed;
        /// <summary>Any combatant healed: who and the HP actually restored (above 0). For damage numbers and SFX.</summary>
        public static event Action<CombatantStatus, int> AnyHealed;
        /// <summary>Raised by <see cref="Revive"/>, so FX can restore visuals when a body respawns.</summary>
        public event Action Revived;
        /// <summary>Element of the last damaging hit we took (the death pop colour). Physical until hit.</summary>
        public Element LastHitElement { get; private set; } = Element.Physical;

        /// <summary>Height above the root at which hit feedback is shown.</summary>
        public const float HitFeedbackHeight = 1f;

        private void Awake()
        {
            Hp = maxHp;
        }

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable() => all.Remove(this);

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
        public bool TakeHit(in AttackPayload payload, Vector3 fromDirection, bool canCounter = true) =>
            TakeHit(payload, fromDirection, canCounter, nonLethal: false);

        /// <summary>
        /// As <see cref="TakeHit(in AttackPayload, Vector3, bool)"/>; with <paramref name="nonLethal"/> the damage is
        /// capped so at least 1 HP remains (a lost Big Deadlock is heavy but never kills).
        /// </summary>
        public bool TakeHit(in AttackPayload payload, Vector3 fromDirection, bool canCounter, bool nonLethal)
        {
            if (IsInvulnerable || IsDead || payload.Team == team) return false;

            int damage = CombatMath.Damage(payload);
            if (DamageTakenMultiplier != 1f) damage = Mathf.RoundToInt(damage * Mathf.Max(0f, DamageTakenMultiplier));
            // 0-damage hits (0-cards) and the breaking swing itself don't spend the counter window.
            bool counter = canCounter && damage > 0 && IsCounterWindowOpen;
            if (counter)
            {
                damage = CombatMath.CounterDamage(damage);
                counterWindowRemaining = 0f;
            }
            if (nonLethal) damage = BossRules.NonLethalDamage(damage, Hp);
            Hp = Mathf.Max(0, Hp - damage);
            Damaged?.Invoke(damage);
            if (counter) CounterHit?.Invoke(this, damage);
            if (damage > 0)
            {
                AnyHitTaken?.Invoke(this, damage);
                LastHitElement = payload.Element;
                AnyHitLanded?.Invoke(new HitInfo(this, payload, damage, counter, Hp == 0,
                    transform.position + Vector3.up * HitFeedbackHeight));
            }
            if (Hp == 0)
            {
                Died?.Invoke();
                return true;
            }
            if (damage > 0) React(payload, fromDirection, damage);
            return true;
        }

        /// <summary>
        /// Effect damage (burn tick, chain jump, finisher shockwave): HP damage only, with no flinch, knockback,
        /// counter or clash. Respects i-frames, death, teams and <see cref="DamageTakenMultiplier"/>. Raises the
        /// usual hit events with <see cref="HitInfo.IsEffect"/> set, and <see cref="Died"/> if lethal.
        /// Returns false if ignored.
        /// </summary>
        public bool TakeEffectDamage(int amount, Element element, in AttackPayload source)
        {
            if (IsInvulnerable || IsDead || source.Team == team || amount <= 0) return false;
            int damage = amount;
            if (DamageTakenMultiplier != 1f) damage = Mathf.RoundToInt(damage * Mathf.Max(0f, DamageTakenMultiplier));
            if (damage <= 0) return false;
            Hp = Mathf.Max(0, Hp - damage);
            Damaged?.Invoke(damage);
            AnyHitTaken?.Invoke(this, damage);
            LastHitElement = element;
            AnyHitLanded?.Invoke(new HitInfo(this, source, damage, false, Hp == 0,
                transform.position + Vector3.up * HitFeedbackHeight, isEffect: true));
            if (Hp == 0) Died?.Invoke();
            return true;
        }

        /// <summary>
        /// A push with no damage (the Wind finisher shockwave, a Gust that lost a telegraph clash): knockback from
        /// <paramref name="payload"/> along <paramref name="fromDirection"/>, with the Wind resistance applied. Raises
        /// <see cref="KnockedBackBy"/>.
        /// </summary>
        public void ApplyKnockback(in AttackPayload payload, Vector3 fromDirection)
        {
            if (IsInvulnerable || IsDead || payload.Team == team || payload.IsPoke) return;
            Push(fromDirection, payload);
            KnockedBackBy?.Invoke(payload);
        }

        /// <summary>
        /// An effect stun (Lightning jolt): at least <paramref name="seconds"/> of stun. No flinch event, so it pauses a
        /// telegraph instead of cancelling it. Ignored when dead or for non-positive durations.
        /// </summary>
        public void ApplyStun(float seconds)
        {
            if (IsDead || seconds <= 0f) return;
            stunRemaining = Mathf.Max(stunRemaining, seconds);
        }

        /// <summary>
        /// Pays HP as a cost (e.g. Overburn). Not a hit: no flinch, knockback, counter or events, and it
        /// never kills (floors at 1). Ignored when dead or for non-positive amounts.
        /// </summary>
        public void SpendHp(int amount)
        {
            if (IsDead || amount <= 0) return;
            Hp = Mathf.Max(1, Hp - amount);
        }

        /// <summary>
        /// Restores up to <paramref name="amount"/> HP, clamped to max. Ignored when dead or for non-positive amounts.
        /// Returns the HP actually restored; raises <see cref="Healed"/> and <see cref="AnyHealed"/> only when above 0.
        /// </summary>
        public int Heal(int amount)
        {
            if (IsDead || amount <= 0) return 0;
            int before = Hp;
            Hp = Mathf.Min(maxHp, Hp + amount);
            int healed = Hp - before;
            if (healed <= 0) return 0;
            Healed?.Invoke(healed);
            AnyHealed?.Invoke(this, healed);
            return healed;
        }

        private void React(in AttackPayload payload, Vector3 fromDirection, int damage)
        {
            // A poke is damage only: no shove (can't keep enemies at bay) and no flinch.
            var direction = payload.IsPoke ? Flatten(fromDirection) : Push(fromDirection, payload);
            HitTaken?.Invoke(damage, direction);
            if (!payload.IsPoke) KnockedBackBy?.Invoke(payload);

            if (payload.IsPoke || FlinchArmor || flinchCooldownRemaining > 0f || hitFlinchSeconds <= 0f) return;
            stunRemaining = Mathf.Max(stunRemaining, hitFlinchSeconds);
            flinchCooldownRemaining = flinchCooldownSeconds;
            Flinched?.Invoke();
        }

        /// <summary>Push speed from a payload; Wind pushes are scaled by <see cref="KnockbackTakenMultiplier"/>.</summary>
        private float KnockbackSpeedFrom(in AttackPayload payload) =>
            CombatMath.KnockbackSpeed(payload) *
            (payload.Element == Element.Wind ? Mathf.Max(0f, KnockbackTakenMultiplier) : 1f);

        /// <summary>Pushes along the flattened direction, decaying at the payload element's rate (Wind lasts longer).</summary>
        private Vector3 Push(Vector3 direction, in AttackPayload payload) =>
            Push(direction, KnockbackSpeedFrom(payload),
                ElementRules.KnockbackDecaySeconds(payload.Element, knockbackDecaySeconds));

        /// <summary>Sets a decaying push along the flattened direction. Returns the unit direction used.</summary>
        private Vector3 Push(Vector3 direction, float speed) => Push(direction, speed, knockbackDecaySeconds);

        private Vector3 Push(Vector3 direction, float speed, float decaySeconds)
        {
            direction = Flatten(direction);
            KnockbackVelocity = direction * speed;
            knockbackDecayRate = decaySeconds > 0f ? speed / decaySeconds : float.MaxValue;
            return direction;
        }

        private static Vector3 Flatten(Vector3 direction) => FlatDirection.Of(direction, Vector3.zero);

        /// <summary>Card break at the baseline margin with no shove.</summary>
        public void ApplyBreak() => ApplyBreak(CombatMath.BaselineBreakMargin, Vector3.zero);

        /// <summary>
        /// Posture damage from an effect (e.g. Shattered Hourglass). Filling the bar posture-breaks: long stun, bar
        /// resets. No shove and no counter window. Ignored when dead or for non-positive amounts.
        /// </summary>
        public void AddPostureDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Posture += amount * PostureTakenMultiplier;
            if (Posture < maxPosture) return;
            Posture = 0f;
            stunRemaining = Mathf.Max(stunRemaining, postureBreakStunSeconds);
            Broken?.Invoke();
        }

        /// <summary>
        /// Called when one of our cards loses a clash: posture damage and stun scaled by how decisively
        /// we lost (<paramref name="margin"/>), plus a shove along <paramref name="awayFromWinner"/>.
        /// <paramref name="stunScale"/> multiplies the final stun (the player's crowd scaling); <paramref name="postureScale"/>
        /// multiplies the posture damage (a Physical winner: x1.5).
        /// </summary>
        public void ApplyBreak(int margin, Vector3 awayFromWinner, float stunScale = 1f, float postureScale = 1f)
        {
            if (IsDead) return;
            Posture += breakPostureDamage * CombatMath.BreakPostureScale(margin) * PostureTakenMultiplier *
                       Mathf.Max(0f, postureScale);
            float stun = breakStunSeconds * CombatMath.BreakStunScale(margin);
            if (awayFromWinner != Vector3.zero) Push(awayFromWinner, CombatMath.BreakShoveSpeed(margin));
            if (Posture >= maxPosture)
            {
                Posture = 0f;
                stun = postureBreakStunSeconds;
            }
            stunRemaining = Mathf.Max(stunRemaining, stun * Mathf.Max(0f, stunScale));
            counterWindowRemaining = counterWindowSeconds;
            Broken?.Invoke();
        }

        /// <summary>Sets max HP (at least 1) and refills to it. Used when a run scales enemies or syncs the player.</summary>
        public void SetMaxHp(int value)
        {
            maxHp = Mathf.Max(1, value);
            Hp = maxHp;
        }

        /// <summary>Sets max posture (at least 1) and clears current posture.</summary>
        public void SetMaxPosture(float value)
        {
            maxPosture = Mathf.Max(1f, value);
            Posture = 0f;
        }

        /// <summary>
        /// Run sync: sets max HP (at least 1) and HP (clamped to [1, max]) without refilling. HP is left alone when
        /// dead. Used after max-HP upgrades (Fortify, Vital Draught, Heartwood Seed) and at room start.
        /// </summary>
        public void SetMaxHpKeepHp(int maxHp, int hp)
        {
            this.maxHp = Mathf.Max(1, maxHp);
            if (IsDead) return;
            Hp = Mathf.Clamp(hp, 1, this.maxHp);
        }

        /// <summary>Sets HP without a hit (run HP carry-over). Clamped to [1, max]; ignored when dead.</summary>
        public void SetHp(int value)
        {
            if (IsDead) return;
            Hp = Mathf.Clamp(value, 1, maxHp);
        }

        public void Revive()
        {
            DamageTakenMultiplier = 1f;
            PostureTakenMultiplier = 1f;
            Hp = maxHp;
            Posture = 0f;
            stunRemaining = 0f;
            flinchCooldownRemaining = 0f;
            counterWindowRemaining = 0f;
            KnockbackVelocity = Vector3.zero;
            LastHitElement = Element.Physical;
            Revived?.Invoke();
        }
    }
}
