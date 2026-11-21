using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Reward tier of a combat room, from its Intensity. Harder tiers pay more than they cost.</summary>
    public enum RewardTier
    {
        Muted,   // 1-3
        Steady,  // 4-6
        Fierce,  // 7-8
        Frantic  // 9
    }

    /// <summary>Numbers behind the reward tiers, currency and draft rarity.</summary>
    public static class RewardRules
    {
        public const int CombatBaseCurrency = 15;
        public const int EliteBaseCurrency = 30;
        public const int BossBaseCurrency = 50;
        /// <summary>Paid for skipping a draft without taking any card. Not Momentum-scaled.</summary>
        public const int SkipBonus = 10;
        public const int MaxRarityShift = 2;
        /// <summary>Frantic rooms offer a relic choice 1 time in this many.</summary>
        public const int FranticRelicOdds = 3;

        public static RewardTier Tier(int intensity)
        {
            if (intensity < CardValue.Min || intensity > CardValue.Max)
                throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Must be in [1, 9].");
            if (intensity <= 3) return RewardTier.Muted;
            if (intensity <= 6) return RewardTier.Steady;
            if (intensity <= 8) return RewardTier.Fierce;
            return RewardTier.Frantic;
        }

        public static int CurrencyPercent(RewardTier tier) => Pick(tier, 60, 100, 150, 200);
        public static int DraftOptions(RewardTier tier) => Pick(tier, 2, 3, 3, 3);
        public static int DraftPicks(RewardTier tier) => Pick(tier, 1, 1, 1, 2);
        public static int TierRarityShift(RewardTier tier) => Pick(tier, 0, 0, 1, 1);
        /// <summary>Permanent upgrade on cards drafted from this tier.</summary>
        public static int DraftUpgrade(RewardTier tier) => Pick(tier, 0, 0, 1, 1);
        public static bool HasRelicChance(RewardTier tier) => tier == RewardTier.Frantic;

        public static int BaseCurrency(in MapCard room)
        {
            RequireCombat(room);
            if (room.Kind == MapCardKind.Boss) return BossBaseCurrency;
            if (room.Kind == MapCardKind.EliteTrial) return EliteBaseCurrency;
            return CombatBaseCurrency;
        }

        /// <summary>Tier shift plus Red's +1, capped at <see cref="MaxRarityShift"/>.</summary>
        public static int RarityShift(in MapCard room)
        {
            RequireCombat(room);
            int shift = TierRarityShift(Tier(room.Number));
            if (room.Family == MapFamily.Red) shift++;
            return Math.Min(MaxRarityShift, shift);
        }

        /// <summary>
        /// base x tier% x BountyWind% x second-wave% (<see cref="EnemyVariantRules.WaveCurrencyPercent"/>, only
        /// when the room has a wave 2), each step rounded half up, then the Momentum bonus.
        /// <paramref name="momentum"/> is the streak held before this clear.
        /// </summary>
        public static int Currency(in MapCard room, Momentum momentum)
        {
            if (momentum == null) throw new ArgumentNullException(nameof(momentum));
            int amount = PercentHalfUp(BaseCurrency(room), CurrencyPercent(Tier(room.Number)));
            amount = PercentHalfUp(amount, RoomIntensity.BountyCurrencyPercent(room));
            if (EnemyVariantRules.WaveTwoCount(room) > 0)
                amount = PercentHalfUp(amount, EnemyVariantRules.WaveCurrencyPercent);
            return momentum.ApplyCurrencyBonus(amount);
        }

        /// <summary>Common/Uncommon/Rare weights for a rarity shift (clamped to [0, 2]).</summary>
        public static IReadOnlyList<int> RarityWeights(int shift)
        {
            switch (Math.Max(0, Math.Min(MaxRarityShift, shift)))
            {
                case 0: return new[] { 70, 25, 5 };
                case 1: return new[] { 40, 45, 15 };
                default: return new[] { 20, 50, 30 };
            }
        }

        public static int PercentHalfUp(int value, int percent)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (percent < 0) throw new ArgumentOutOfRangeException(nameof(percent));
            return (int)(((long)value * percent + 50) / 100);
        }

        /// <summary><paramref name="percent"/>% of <paramref name="value"/>, rounded up. Integer math.</summary>
        public static int PercentCeil(int value, int percent)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (percent < 0) throw new ArgumentOutOfRangeException(nameof(percent));
            return (int)(((long)value * percent + 99) / 100);
        }

        private static int Pick(RewardTier tier, int muted, int steady, int fierce, int frantic)
        {
            switch (tier)
            {
                case RewardTier.Muted: return muted;
                case RewardTier.Steady: return steady;
                case RewardTier.Fierce: return fierce;
                default: return frantic;
            }
        }

        private static void RequireCombat(in MapCard room)
        {
            if (!room.IsCombat) throw new ArgumentException($"{room.Kind} has no combat reward.", nameof(room));
        }
    }
}
