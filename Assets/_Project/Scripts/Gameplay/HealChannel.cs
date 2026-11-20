using System;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// A heal card played from the hand: a <see cref="HealRules.ChannelSeconds"/> channel on scaled time (hit-stop pauses
    /// it) at half move speed, then <see cref="CombatantStatus.Heal"/>. A direct damaging hit, a Card Break, any flinch
    /// or stun, a dash or death cancels it with no heal (the card is already in the discard). Effect damage (burn
    /// ticks) does not. See docs/specs/heal-rework.md.
    /// </summary>
    public sealed class HealChannel : MonoBehaviour
    {
        private CombatantStatus status;
        private PlayerMotor motor;
        private float elapsed;

        public bool IsChannelling { get; private set; }
        /// <summary>0 to 1 over the channel; 0 when idle.</summary>
        public float Progress01 => IsChannelling ? Mathf.Clamp01(elapsed / HealRules.ChannelSeconds) : 0f;
        /// <summary>HP the channel will heal on completion (before clamping to max).</summary>
        public int PendingAmount { get; private set; }
        /// <summary>
        /// Factor for the motor's move speed: <see cref="HealRules.ChannelMoveMultiplier"/> while channelling, else 1.
        /// <see cref="PlayerCombatInput"/> multiplies it into the per-frame speed (with the reload boost and chill), so
        /// the previous multiplier comes back as soon as the channel ends.
        /// </summary>
        public float MoveMultiplier => IsChannelling ? HealRules.ChannelMoveMultiplier : 1f;

        /// <summary>The channel finished: the HP actually restored (0 if HP was already full by then).</summary>
        public event Action<int> Completed;
        /// <summary>The channel was cancelled before finishing; nothing was healed.</summary>
        public event Action Interrupted;

        private void Awake()
        {
            status = GetComponent<CombatantStatus>();
            motor = GetComponent<PlayerMotor>();
        }

        private void OnEnable()
        {
            if (status != null)
            {
                status.HitTaken += HandleHitTaken; // direct, non-lethal damaging hits only (effects never raise it)
                status.Broken += Cancel;
                status.Flinched += Cancel;
                status.Died += Cancel;
            }
            if (motor != null) motor.Dashed += Cancel;
        }

        private void OnDisable()
        {
            Cancel();
            if (status != null)
            {
                status.HitTaken -= HandleHitTaken;
                status.Broken -= Cancel;
                status.Flinched -= Cancel;
                status.Died -= Cancel;
            }
            if (motor != null) motor.Dashed -= Cancel;
        }

        /// <summary>
        /// Starts channelling <paramref name="card"/>'s heal. False (nothing starts) if it isn't a heal card, a channel
        /// is already running, or we are dead, stunned or at full HP.
        /// </summary>
        public bool Begin(RuntimeCardInstance card)
        {
            if (card == null || !card.IsHeal || IsChannelling || status == null) return false;
            if (status.IsDead || status.IsStunned || status.Hp >= status.MaxHp) return false;
            PendingAmount = HealRules.Amount(card.GetEffectiveValue());
            elapsed = 0f;
            IsChannelling = true;
            return true;
        }

        /// <summary>Stops the channel with no heal. Raises <see cref="Interrupted"/> if one was running.</summary>
        public void Cancel()
        {
            if (!IsChannelling) return;
            Stop();
            Interrupted?.Invoke();
        }

        private void Update()
        {
            if (!IsChannelling) return;
            // An effect stun (Lightning jolt) raises no event, so it is caught here.
            if (status.IsDead || status.IsStunned)
            {
                Cancel();
                return;
            }
            elapsed += Time.deltaTime; // scaled: hit-stop pauses the channel
            if (elapsed < HealRules.ChannelSeconds) return;

            int amount = PendingAmount;
            Stop();
            Completed?.Invoke(status.Heal(amount));
        }

        private void HandleHitTaken(int damage, Vector3 direction) => Cancel();

        private void Stop()
        {
            IsChannelling = false;
            elapsed = 0f;
            PendingAmount = 0;
        }
    }
}
