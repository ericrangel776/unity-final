using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Seeded rolls for card drafts and shop stock. No asset dependencies: the pool is passed in.</summary>
    public static class RewardRoller
    {
        /// <summary>
        /// Up to <paramref name="count"/> distinct cards. Each option rolls a rarity from the shift's weights,
        /// then a random unoffered pool card of that rarity; an exhausted rarity falls back to the nearest one
        /// that still has cards (lower first on a tie). Shorter than <paramref name="count"/> only when the pool runs out.
        /// </summary>
        public static List<CardData> RollCards(IReadOnlyList<CardData> pool, int count, int rarityShift, Random rng)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            var remaining = new List<CardData>();
            foreach (var c in pool) if (c != null && !remaining.Contains(c)) remaining.Add(c);

            var weights = RewardRules.RarityWeights(rarityShift);
            var result = new List<CardData>(count);
            while (result.Count < count && remaining.Count > 0)
            {
                var rarity = RollRarity(weights, rng);
                var candidates = NearestAvailable(remaining, rarity);
                var card = candidates[rng.Next(candidates.Count)];
                result.Add(card);
                remaining.Remove(card);
            }
            return result;
        }

        /// <summary>
        /// Rolls the post-fight reward for a cleared combat room. Momentum is the streak before this clear.
        /// An earned relic rolls a choice from the relics not in <paramref name="ownedRelics"/>; with none left it
        /// pays <see cref="RelicRules.FallbackCurrency"/> instead.
        /// </summary>
        public static CombatReward RollCombatReward(in MapCard room, IReadOnlyList<CardData> pool, Momentum momentum,
            Random rng, int extraOptions = 0, int extraSkipBonus = 0, IReadOnlyCollection<RelicId> ownedRelics = null)
        {
            if (extraOptions < 0) throw new ArgumentOutOfRangeException(nameof(extraOptions));
            if (extraSkipBonus < 0) throw new ArgumentOutOfRangeException(nameof(extraSkipBonus));
            var tier = RewardRules.Tier(room.Number);
            int currency = RewardRules.Currency(room, momentum);
            var options = RollCards(pool, RewardRules.DraftOptions(tier) + extraOptions, RewardRules.RarityShift(room), rng);

            bool relic = room.Kind == MapCardKind.EliteTrial
                         || (room.Kind != MapCardKind.Boss && RewardRules.HasRelicChance(tier)
                             && rng.Next(RewardRules.FranticRelicOdds) == 0);

            List<RelicId> relicOptions = null;
            if (relic)
            {
                relicOptions = RelicRules.RollOffer(ownedRelics, rng);
                if (relicOptions.Count == 0) currency += RelicRules.FallbackCurrency;
            }

            return new CombatReward(currency, options, RewardRules.DraftPicks(tier), RewardRules.DraftUpgrade(tier), relic,
                relicOptions, RewardRules.SkipBonus + extraSkipBonus);
        }

        private static Rarity RollRarity(IReadOnlyList<int> weights, Random rng)
        {
            int total = 0;
            foreach (var w in weights) total += w;
            int roll = rng.Next(total);
            for (int i = 0; i < weights.Count; i++)
            {
                if (roll < weights[i]) return (Rarity)i;
                roll -= weights[i];
            }
            return Rarity.Rare;
        }

        private static List<CardData> NearestAvailable(List<CardData> remaining, Rarity wanted)
        {
            int rarityCount = Enum.GetValues(typeof(Rarity)).Length;
            for (int distance = 0; distance < rarityCount; distance++)
            {
                foreach (int r in new[] { (int)wanted - distance, (int)wanted + distance })
                {
                    if (r < 0 || r >= rarityCount) continue;
                    var matches = remaining.FindAll(c => (int)c.Rarity == r);
                    if (matches.Count > 0) return matches;
                }
            }
            return remaining; // unreachable while remaining is non-empty
        }
    }

    /// <summary>
    /// A cleared room's spoils. Currency is paid by <see cref="RunState.CompleteRoom"/>; the draft is claimed
    /// here: take up to <see cref="PicksAllowed"/> cards, or skip before taking any for <see cref="RewardRules.SkipBonus"/>.
    /// </summary>
    public sealed class CombatReward
    {
        private readonly List<CardData> options;
        private readonly bool[] taken;
        private readonly List<RelicId> relicOptions;

        public int Currency { get; }
        public IReadOnlyList<CardData> Options => options;
        public int PicksAllowed { get; }
        /// <summary>Permanent upgrade applied to each drafted card (Fierce/Frantic).</summary>
        public int DraftUpgrade { get; }
        /// <summary>The room earned a relic: a choice from <see cref="RelicOptions"/>, or the fallback currency if all are owned.</summary>
        public bool OffersRelic { get; }
        /// <summary>Relics to choose one from. Empty when no relic was earned or every relic is already owned.</summary>
        public IReadOnlyList<RelicId> RelicOptions => relicOptions;
        /// <summary>A relic was earned but all were owned, so <see cref="RelicRules.FallbackCurrency"/> is in <see cref="Currency"/>.</summary>
        public bool RelicFallbackPaid => OffersRelic && relicOptions.Count == 0;
        public bool IsRelicClaimed { get; private set; }
        /// <summary>Currency paid by <see cref="Skip"/> when no card was taken.</summary>
        public int SkipBonus { get; }
        public int PicksMade { get; private set; }
        public bool IsDraftClosed { get; private set; }

        public CombatReward(int currency, List<CardData> options, int picksAllowed, int draftUpgrade, bool offersRelic,
            List<RelicId> relicOptions = null, int skipBonus = RewardRules.SkipBonus)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (skipBonus < 0) throw new ArgumentOutOfRangeException(nameof(skipBonus));
            this.relicOptions = offersRelic && relicOptions != null ? new List<RelicId>(relicOptions) : new List<RelicId>();
            SkipBonus = skipBonus;
            taken = new bool[options.Count];
            Currency = currency;
            PicksAllowed = picksAllowed;
            DraftUpgrade = draftUpgrade;
            OffersRelic = offersRelic;
            IsDraftClosed = options.Count == 0;
        }

        public bool IsTaken(int index) => index >= 0 && index < taken.Length && taken[index];

        /// <summary>Adds option <paramref name="index"/> to the run deck (with the tier upgrade).</summary>
        public bool TryPick(int index, RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (IsDraftClosed || index < 0 || index >= options.Count || taken[index]) return false;

            run.AddCard(options[index], DraftUpgrade);
            taken[index] = true;
            PicksMade++;
            if (PicksMade >= PicksAllowed || PicksMade >= options.Count) IsDraftClosed = true;
            return true;
        }

        /// <summary>Takes relic option <paramref name="index"/>. Only one per reward; false if claimed, out of range or already owned.</summary>
        public bool TryPickRelic(int index, RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (IsRelicClaimed || index < 0 || index >= relicOptions.Count) return false;
            if (!run.AddRelic(relicOptions[index])) return false;
            IsRelicClaimed = true;
            return true;
        }

        /// <summary>Closes the draft. Pays the skip bonus only if no card was taken. False if already closed.</summary>
        public bool Skip(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (IsDraftClosed) return false;
            if (PicksMade == 0) run.AddCurrency(SkipBonus);
            IsDraftClosed = true;
            return true;
        }
    }
}
