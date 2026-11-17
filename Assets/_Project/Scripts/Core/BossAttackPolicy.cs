using System;

namespace DeckArpg.Core
{
    public enum BossAction { Swing, Sleight }

    /// <summary>What a boss policy may look at when choosing its next attack.</summary>
    public readonly struct BossTurnContext
    {
        /// <summary>Attacks started so far this fight (swings and Sleights), not counting this one.</summary>
        public readonly int AttacksSoFar;
        /// <summary>Numbered cards in the boss hand right now (0-3).</summary>
        public readonly int HandCount;
        /// <summary>0..1.</summary>
        public readonly float HpFraction;

        public BossTurnContext(int attacksSoFar, int handCount, float hpFraction)
        {
            AttacksSoFar = attacksSoFar;
            HandCount = handCount;
            HpFraction = hpFraction;
        }
    }

    /// <summary>Chooses the boss's next attack. A seam: a more "natural" AI can replace the pattern later.</summary>
    public interface IBossAttackPolicy
    {
        BossAction Next(in BossTurnContext context);
    }

    /// <summary>Every <c>every</c>-th attack is a Sleight, if the hand is full; otherwise a swing.</summary>
    public sealed class PatternBossPolicy : IBossAttackPolicy
    {
        private readonly int every;

        public PatternBossPolicy(int every = BossRules.SleightEvery)
        {
            if (every < 1) throw new ArgumentOutOfRangeException(nameof(every));
            this.every = every;
        }

        public BossAction Next(in BossTurnContext context)
        {
            bool sleightTurn = (context.AttacksSoFar + 1) % every == 0;
            return sleightTurn && context.HandCount >= SleightRules.StockSize ? BossAction.Sleight : BossAction.Swing;
        }
    }
}
