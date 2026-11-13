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
        [Tooltip("Posture added when one of our cards is broken.")]
        [SerializeField] private float breakPostureDamage = 25f;
        [SerializeField] private float breakStunSeconds = 0.8f;
        [Tooltip("Stun when posture fills up.")]
        [SerializeField] private float postureBreakStunSeconds = 2f;

        private float stunRemaining;

        public Team Team { get => team; set => team = value; }
        public int MaxHp => maxHp;
        public int Hp { get; private set; }
        public float MaxPosture => maxPosture;
        public float Posture { get; private set; }
        public bool IsStunned => stunRemaining > 0f;
        public bool IsDead => Hp <= 0;
        /// <summary>Set by the dash (i-frames). Hits are ignored while true.</summary>
        public bool IsInvulnerable { get; set; }
        public int OwnerId => gameObject.GetInstanceID();

        public event Action<int> Damaged;
        public event Action Broken;
        public event Action Died;

        private void Awake()
        {
            Hp = maxHp;
        }

        private void Update()
        {
            if (stunRemaining > 0f) stunRemaining -= Time.deltaTime;
            if (Posture > 0f) Posture = Mathf.Max(0f, Posture - postureDecayPerSecond * Time.deltaTime);
        }

        /// <summary>Applies a body hit. Returns false if the hit was ignored (i-frames, dead, or friendly).</summary>
        public bool TakeHit(in AttackPayload payload)
        {
            if (IsInvulnerable || IsDead || payload.Team == team) return false;

            int damage = CombatMath.Damage(payload);
            Hp = Mathf.Max(0, Hp - damage);
            Damaged?.Invoke(damage);
            if (Hp == 0) Died?.Invoke();
            return true;
        }

        /// <summary>Called when one of our cards loses a clash: stun plus posture damage.</summary>
        public void ApplyBreak()
        {
            if (IsDead) return;
            Posture += breakPostureDamage;
            float stun = breakStunSeconds;
            if (Posture >= maxPosture)
            {
                Posture = 0f;
                stun = postureBreakStunSeconds;
            }
            stunRemaining = Mathf.Max(stunRemaining, stun);
            Broken?.Invoke();
        }

        public void Revive()
        {
            Hp = maxHp;
            Posture = 0f;
            stunRemaining = 0f;
        }
    }
}
