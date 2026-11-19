using System;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// One combatant's element statuses (docs/specs/element-mechanics.md): burn ticks deal effect damage, chill slows
    /// movement (read through <see cref="SpeedMultiplier"/>), and a Wind hit opens a short window in which a wall or
    /// body bump adds posture once (the slam). The rules live in <see cref="ElementStatusState"/>.
    /// </summary>
    [RequireComponent(typeof(CombatantStatus))]
    public sealed class ElementStatusEffects : MonoBehaviour
    {
        private readonly ElementStatusState state = new ElementStatusState();
        private CombatantStatus status;
        private AttackPayload burnSource;
        private float slamWindow;
        private bool wasBurning, wasChilled;

        public CombatantStatus Status => status;
        public bool IsBurning => state.IsBurning;
        public bool IsChilled => state.IsChilled;
        public float BurnRemaining => state.BurnRemaining;
        public float ChillRemaining => state.ChillRemaining;
        /// <summary>x0.7 while chilled, else 1. Movers multiply their speed (and enemies their timers) by it.</summary>
        public float SpeedMultiplier => state.SpeedMultiplier;
        /// <summary>Multiplies status durations (0.5 on the boss and Elites).</summary>
        public float DurationScale { get => state.DurationScale; set => state.DurationScale = value; }
        /// <summary>True while a Wind hit's slam window is open.</summary>
        public bool IsSlamWindowOpen => slamWindow > 0f;

        /// <summary>A status started, refreshed or ended (pips, HUD chips).</summary>
        public event Action StatusChanged;

        private void Awake() => status = GetComponent<CombatantStatus>();

        private void OnEnable()
        {
            status.KnockedBackBy += HandleKnockedBack;
            status.Died += Clear;
            status.Revived += Clear;
        }

        private void OnDisable()
        {
            status.KnockedBackBy -= HandleKnockedBack;
            status.Died -= Clear;
            status.Revived -= Clear;
        }

        private void Update()
        {
            if (status.IsDead) return;
            float dt = Time.deltaTime; // scaled: hit-stop pauses burns
            if (slamWindow > 0f) slamWindow -= dt;
            int burn = state.Tick(dt);
            if (burn > 0) status.TakeEffectDamage(burn, Element.Fire, burnSource);
            RaiseIfChanged();
        }

        /// <summary>Starts or refreshes a burn of <paramref name="totalDamage"/> from <paramref name="source"/>.</summary>
        public void ApplyBurn(int totalDamage, bool isSleight, in AttackPayload source)
        {
            if (status.IsDead) return;
            burnSource = source;
            state.ApplyBurn(totalDamage, isSleight);
            RaiseIfChanged(force: true);
        }

        public void ApplyChill(bool isSleight)
        {
            if (status.IsDead) return;
            state.ApplyChill(isSleight);
            RaiseIfChanged(force: true);
        }

        public void Clear()
        {
            state.Clear();
            slamWindow = 0f;
            RaiseIfChanged();
        }

        /// <summary>
        /// The mover's knockback ran into a wall or body. Inside a Wind hit's window, with enough push left, it adds
        /// <see cref="ElementRules.WindSlamPostureFraction"/> of max posture once and closes the window.
        /// </summary>
        public void NotifyKnockbackBlocked()
        {
            if (slamWindow <= 0f || status.IsDead) return;
            if (status.KnockbackVelocity.magnitude < ElementRules.WindSlamMinSpeed) return;
            slamWindow = 0f;
            status.AddPostureDamage(ElementRules.WindSlamPostureFraction * status.MaxPosture);
        }

        private void HandleKnockedBack(AttackPayload payload)
        {
            if (payload.Element == Element.Wind) slamWindow = ElementRules.WindSlamWindowSeconds;
        }

        private void RaiseIfChanged(bool force = false)
        {
            bool burning = state.IsBurning, chilled = state.IsChilled;
            if (!force && burning == wasBurning && chilled == wasChilled) return;
            wasBurning = burning;
            wasChilled = chilled;
            StatusChanged?.Invoke();
        }
    }
}
