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
        /// <summary>Sum of the boss hand's values (what a Sleight from it would be worth).</summary>
        public readonly int HandSum;
        /// <summary>Attacks since the last boss Sleight (all attacks if there was none).</summary>
        public readonly int AttacksSinceSleight;
        /// <summary>0-cards the player could answer a Sleight with right now.</summary>
        public readonly int PlayerZerosReady;
        /// <summary>The player is reloading (bar running or jammed): no card answers.</summary>
        public readonly bool PlayerReloading;
        public readonly bool PlayerStunned;

        public BossTurnContext(int attacksSoFar, int handCount, float hpFraction, int handSum = 0,
            int attacksSinceSleight = 0, int playerZerosReady = 1, bool playerReloading = false, bool playerStunned = false)
        {
            AttacksSoFar = attacksSoFar;
            HandCount = handCount;
            HpFraction = hpFraction;
            HandSum = handSum;
            AttacksSinceSleight = attacksSinceSleight;
            PlayerZerosReady = playerZerosReady;
            PlayerReloading = playerReloading;
            PlayerStunned = playerStunned;
        }
    }

    /// <summary>Chooses the boss's next attack.</summary>
    public interface IBossAttackPolicy
    {
        BossAction Next(in BossTurnContext context);
    }

    /// <summary>Every <c>every</c>-th attack is a Sleight, if the hand is full; otherwise a swing. Predictable (tests).</summary>
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

    /// <summary>
    /// The "natural" boss (user, 2026-11-17): it punishes openings, likes strong hands, and rolls the dice. The Sleight
    /// chance starts low and rises when the player can't answer (no 0 ready, reloading, stunned), when its hand sum is high,
    /// and the longer it has gone without one. It never Sleights twice in a row and needs a full hand.
    /// </summary>
    public sealed class NaturalBossPolicy : IBossAttackPolicy
    {
        public const float BaseChance = 0.15f;
        public const float NoZeroBonus = 0.35f;
        public const float OpeningBonus = 0.30f;
        /// <summary>Per point of hand sum above <see cref="StrongHandSum"/>, capped at <see cref="MaxHandBonus"/>.</summary>
        public const float HandBonusPerPoint = 0.04f;
        public const int StrongHandSum = 15;
        public const float MaxHandBonus = 0.30f;
        /// <summary>Per swing beyond <see cref="PatienceSwings"/> since the last Sleight (it gets impatient).</summary>
        public const float ImpatiencePerSwing = 0.10f;
        public const int PatienceSwings = 3;
        public const float MaxChance = 0.9f;

        private readonly Random random;

        public NaturalBossPolicy(Random random) => this.random = random ?? throw new ArgumentNullException(nameof(random));

        /// <summary>The chance (0..<see cref="MaxChance"/>) of a Sleight on this turn; 0 when one isn't allowed.</summary>
        public static float SleightChance(in BossTurnContext c)
        {
            if (c.HandCount < SleightRules.StockSize) return 0f;
            bool hadSleight = c.AttacksSinceSleight < c.AttacksSoFar;
            if (hadSleight && c.AttacksSinceSleight < 1) return 0f; // never twice in a row

            float chance = BaseChance;
            if (c.PlayerZerosReady <= 0) chance += NoZeroBonus;
            if (c.PlayerReloading || c.PlayerStunned) chance += OpeningBonus;
            chance += Math.Min(MaxHandBonus, Math.Max(0f, (c.HandSum - StrongHandSum) * HandBonusPerPoint));
            chance += Math.Max(0, c.AttacksSinceSleight - PatienceSwings) * ImpatiencePerSwing;
            return Math.Min(MaxChance, chance);
        }

        public BossAction Next(in BossTurnContext context) =>
            random.NextDouble() < SleightChance(context) ? BossAction.Sleight : BossAction.Swing;
    }
}
