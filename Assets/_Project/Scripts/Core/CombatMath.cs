namespace DeckArpg.Core
{
    /// <summary>Tunable combat numbers that are pure functions of a payload.</summary>
    public static class CombatMath
    {
        public const int DamagePerPoint = 3;

        /// <summary>HP damage an attack deals on a body hit. 0-cards are parries and deal no HP damage.</summary>
        public static int Damage(in AttackPayload payload) =>
            payload.IsZeroBreaker ? 0 : payload.Value * DamagePerPoint;

        public const float BaseKnockback = 3f;
        public const float KnockbackPerPoint = 0.6f;

        /// <summary>Initial push speed (m/s) a body hit gives its victim. 0-cards don't push.</summary>
        public static float KnockbackSpeed(in AttackPayload payload) =>
            payload.IsZeroBreaker ? 0f : BaseKnockback + payload.Value * KnockbackPerPoint;

        public const float NeutralHitStop = 0.05f;
        public const float CardBreakHitStop = 0.12f;
        public const float ZeroBreakHitStop = 0.18f;

        /// <summary>
        /// Freeze length for a clash. Every tier stays under the input buffer window so presses
        /// made during the freeze still fire after it.
        /// </summary>
        public static float HitStopSeconds(ClashOutcome outcome, bool brokeByZero)
        {
            switch (outcome)
            {
                case ClashOutcome.Neutral: return NeutralHitStop;
                case ClashOutcome.AWins:
                case ClashOutcome.BWins: return brokeByZero ? ZeroBreakHitStop : CardBreakHitStop;
                default: return 0f;
            }
        }
    }
}
