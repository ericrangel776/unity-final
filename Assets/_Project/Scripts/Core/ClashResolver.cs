namespace DeckArpg.Core
{
    public enum ClashOutcome
    {
        None,    // not a clash (same team)
        AWins,
        BWins,
        Neutral  // both attacks cancel, no damage
    }

    public readonly struct ClashResult
    {
        public readonly ClashOutcome Outcome;
        /// <summary>True when the winner broke the loser with a 0-card (deletes the card from its cycle).</summary>
        public readonly bool BrokeByZero;

        public ClashResult(ClashOutcome outcome, bool brokeByZero)
        {
            Outcome = outcome;
            BrokeByZero = brokeByZero;
        }

        public override string ToString() => BrokeByZero ? $"{Outcome} (zero break)" : Outcome.ToString();
    }

    /// <summary>Deterministic card-clash rules. Pure function of the two payloads.</summary>
    public static class ClashResolver
    {
        private static readonly ClashResult Neutral = new ClashResult(ClashOutcome.Neutral, false);

        public static ClashResult Resolve(in AttackPayload a, in AttackPayload b)
        {
            if (a.Team == b.Team) return new ClashResult(ClashOutcome.None, false);

            if (a.IsZeroBreaker && b.IsZeroBreaker) return Neutral;
            if (a.IsZeroBreaker) return ResolveZero(a.ActivationTick, b.ActivationTick, ClashOutcome.AWins, ClashOutcome.BWins);
            if (b.IsZeroBreaker) return ResolveZero(b.ActivationTick, a.ActivationTick, ClashOutcome.BWins, ClashOutcome.AWins);

            if (a.Value > b.Value) return new ClashResult(ClashOutcome.AWins, false);
            if (b.Value > a.Value) return new ClashResult(ClashOutcome.BWins, false);
            return Neutral;
        }

        // A 0-card wins only if played strictly second; played first it loses; same tick = bounce.
        private static ClashResult ResolveZero(int zeroTick, int otherTick, ClashOutcome zeroWins, ClashOutcome otherWins)
        {
            if (zeroTick > otherTick) return new ClashResult(zeroWins, true);
            if (zeroTick < otherTick) return new ClashResult(otherWins, false);
            return Neutral;
        }
    }
}
