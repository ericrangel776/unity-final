using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Wayfarer's Stall numbers, by Quality band.</summary>
    public static class ShopRules
    {
        public const int MinDeckSize = 6;
        public const int PurgeBaseCost = 40;
        public const int PurgeCostStep = 20;

        public static int Price(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return 30;
                case Rarity.Uncommon: return 50;
                default: return 80;
            }
        }

        public static int StockCount(int quality) => Band(quality, 3, 4, 5);
        public static int StockRarityShift(int quality) => Band(quality, 0, 1, 1);

        /// <summary><c>40 + 20 x earlier purges</c>, halved (rounded up) at Quality 7-9.</summary>
        public static int PurgeCost(int earlierPurges, int quality)
        {
            if (earlierPurges < 0) throw new ArgumentOutOfRangeException(nameof(earlierPurges));
            int cost = PurgeBaseCost + PurgeCostStep * earlierPurges;
            return RoomIntensity.Band(quality) == IntensityBand.High ? (cost + 1) / 2 : cost;
        }

        /// <summary>A purge may not take the deck below <see cref="MinDeckSize"/>.</summary>
        public static bool CanPurge(int deckCount) => deckCount > MinDeckSize;

        internal static int Band(int quality, int low, int mid, int high)
        {
            switch (RoomIntensity.Band(quality))
            {
                case IntensityBand.Low: return low;
                case IntensityBand.Mid: return mid;
                default: return high;
            }
        }
    }

    /// <summary>Ember Forge numbers, by Quality band.</summary>
    public static class ForgeRules
    {
        public static int UpgradeAmount(int quality) => ShopRules.Band(quality, 1, 2, 3);

        /// <summary>Quality 7-9: one upgrade AND one Premium (on a different card). Otherwise one or the other.</summary>
        public static bool GrantsBoth(int quality) => RoomIntensity.Band(quality) == IntensityBand.High;

        /// <summary>No upgrades on 0-cards or on cards whose base + permanent mods (+Premium) already reach 9.</summary>
        public static bool CanUpgrade(RuntimeCardInstance card)
        {
            if (card == null || card.IsZeroBreaker) return false;
            int premium = card.IsPremium ? RuntimeCardInstance.PremiumBonus : 0;
            return card.Data.BaseValue + card.PermanentRunUpgradeMod + premium < CardValue.Max;
        }

        public static bool CanMakePremium(RuntimeCardInstance card) =>
            card != null && !card.IsZeroBreaker && !card.IsPremium;
    }

    /// <summary>Still Waters numbers, by Quality band.</summary>
    public static class RestRules
    {
        public static int HealPercent(int quality) => ShopRules.Band(quality, 20, 30, 45);

        /// <summary>Percent of max HP, rounded up.</summary>
        public static int HealAmount(int maxHp, int quality)
        {
            if (maxHp < 1) throw new ArgumentOutOfRangeException(nameof(maxHp));
            return (int)(((long)maxHp * HealPercent(quality) + 99) / 100);
        }
    }

    /// <summary>One visit to a shop: fixed stock, each card sold once, purges priced by the run's purge count.</summary>
    public sealed class ShopVisit
    {
        private readonly List<CardData> stock;
        private readonly bool[] sold;

        public int Quality { get; }
        public IReadOnlyList<CardData> Stock => stock;

        public ShopVisit(int quality, IReadOnlyList<CardData> pool, Random rng)
        {
            RoomIntensity.Band(quality); // validates
            Quality = quality;
            stock = RewardRoller.RollCards(pool, ShopRules.StockCount(quality), ShopRules.StockRarityShift(quality), rng);
            sold = new bool[stock.Count];
        }

        public bool IsSold(int index) => index >= 0 && index < sold.Length && sold[index];

        public bool TryBuy(int index, RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (index < 0 || index >= stock.Count || sold[index]) return false;
            if (!run.TrySpend(ShopRules.Price(stock[index].Rarity))) return false;

            run.AddCard(stock[index]);
            sold[index] = true;
            return true;
        }

        public int PurgeCost(RunState run) => ShopRules.PurgeCost(run.PurgeCount, Quality);

        public bool TryPurge(RuntimeCardInstance card, RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (card == null || !run.OwnsCard(card) || !ShopRules.CanPurge(run.Deck.Count)) return false;
            if (!run.TrySpend(PurgeCost(run))) return false;

            run.RemoveCard(card);
            run.PurgeCount++;
            return true;
        }
    }

    /// <summary>One forge visit. Below Quality 7: one upgrade or one Premium. Quality 7-9: one of each, on different cards.</summary>
    public sealed class ForgeVisit
    {
        private RuntimeCardInstance upgraded;
        private RuntimeCardInstance premiumed;

        public int Quality { get; }
        public int UpgradeAmount => ForgeRules.UpgradeAmount(Quality);
        public bool CanStillUpgrade => upgraded == null && (premiumed == null || ForgeRules.GrantsBoth(Quality));
        public bool CanStillMakePremium => premiumed == null && (upgraded == null || ForgeRules.GrantsBoth(Quality));
        public bool IsDone => !CanStillUpgrade && !CanStillMakePremium;

        public ForgeVisit(int quality)
        {
            RoomIntensity.Band(quality);
            Quality = quality;
        }

        public bool TryUpgrade(RuntimeCardInstance card, RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (!CanStillUpgrade || card == premiumed || !run.OwnsCard(card) || !ForgeRules.CanUpgrade(card))
                return false;

            card.PermanentRunUpgradeMod += UpgradeAmount;
            upgraded = card;
            return true;
        }

        public bool TryMakePremium(RuntimeCardInstance card, RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (!CanStillMakePremium || card == upgraded || !run.OwnsCard(card) || !ForgeRules.CanMakePremium(card))
                return false;

            card.IsPremiumOverride = true;
            premiumed = card;
            return true;
        }
    }

    /// <summary>One rest visit: heal, or Meditate for +1 Momentum. Only one.</summary>
    public sealed class RestVisit
    {
        public int Quality { get; }
        public bool IsDone { get; private set; }

        public RestVisit(int quality)
        {
            RoomIntensity.Band(quality);
            Quality = quality;
        }

        public int HealAmount(RunState run) => RestRules.HealAmount(run.MaxHp, Quality);

        public bool TryHeal(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (IsDone) return false;
            run.Heal(HealAmount(run));
            IsDone = true;
            return true;
        }

        public bool TryMeditate(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (IsDone) return false;
            run.Momentum.AddStack();
            IsDone = true;
            return true;
        }
    }
}
