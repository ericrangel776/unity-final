namespace DeckArpg.Core
{
    /// <summary>Tunable combat numbers that are pure functions of a payload.</summary>
    public static class CombatMath
    {
        public const int DamagePerPoint = 3;

        /// <summary>
        /// HP damage an attack deals on a body hit. 0-cards are parries and deal no HP damage.
        /// Sleights scale by <c>1 + finisher*0.1</c>, rounded up in integer tenths (floats would
        /// turn 30 * 1.1 into 34).
        /// </summary>
        public static int Damage(in AttackPayload payload)
        {
            if (payload.IsZeroBreaker) return 0;
            int baseDamage = payload.Value * DamagePerPoint;
            if (!payload.IsSleight) return baseDamage;
            int tenths = baseDamage * (10 + payload.FinisherValue);
            return (tenths + 9) / 10;
        }

        public const float BaseKnockback = 3f;
        public const float KnockbackPerPoint = 0.6f;

        /// <summary>Initial push speed (m/s) a body hit gives its victim. 0-cards don't push.</summary>
        public static float KnockbackSpeed(in AttackPayload payload) =>
            payload.IsZeroBreaker ? 0f : BaseKnockback + payload.Value * KnockbackPerPoint;

        /// <summary>Margin used when nothing better is known; scales break effects by exactly 1.</summary>
        public const int BaselineBreakMargin = 2;
        public const int MaxBreakMargin = 8;

        /// <summary>
        /// How decisively a break was won: the value gap, clamped to [1, 8]. A Zero Break is the most
        /// decisive possible (max), since the 0 is a flag, never integer math.
        /// </summary>
        public static int BreakMargin(in AttackPayload winner, in AttackPayload loser)
        {
            if (winner.IsZeroBreaker) return MaxBreakMargin;
            int gap = winner.Value - (loser.IsZeroBreaker ? 0 : loser.Value);
            return ClampMargin(gap);
        }

        public static int ClampMargin(int margin) =>
            margin < 1 ? 1 : margin > MaxBreakMargin ? MaxBreakMargin : margin;

        /// <summary>Multiplier on a combatant's base break posture damage. 1 at the baseline margin.</summary>
        public static float BreakPostureScale(int margin) => 0.6f + 0.2f * ClampMargin(margin);

        /// <summary>Multiplier on a combatant's base break stun. 1 at the baseline margin.</summary>
        public static float BreakStunScale(int margin) => 0.8f + 0.1f * ClampMargin(margin);

        /// <summary>Speed (m/s) the loser of a break is shoved away from the winner.</summary>
        public static float BreakShoveSpeed(int margin) => 2f + 0.8f * ClampMargin(margin);

        public const float CounterMultiplier = 1.5f;

        /// <summary>
        /// Damage for a counter hit (the first hit on someone whose card was just broken): x1.5,
        /// rounded up so even a poke gains something. 0 stays 0.
        /// </summary>
        public static int CounterDamage(int baseDamage) =>
            baseDamage <= 0 ? 0 : (int)System.Math.Ceiling(baseDamage * (double)CounterMultiplier);

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
