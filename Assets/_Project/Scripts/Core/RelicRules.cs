using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Relic numbers and the relic-choice roll. See docs/specs/m3-relics.md.</summary>
    public static class RelicRules
    {
        public const int ChoiceSize = 2;
        /// <summary>Paid instead of a relic choice when every relic is already owned.</summary>
        public const int FallbackCurrency = 30;

        public const int GildedQuillExtraOptions = 1;
        public const int GildedQuillExtraSkipBonus = 10;
        public const int CinderLanternFreeOverburns = 1;
        public const float AshenSigilSpeedPerStack = 0.1f;
        public const int AshenSigilHealPerBurn = 3;
        public const float HourglassPostureDamage = 15f;

        private static readonly RelicId[] All = (RelicId[])Enum.GetValues(typeof(RelicId));

        /// <summary>Up to <see cref="ChoiceSize"/> distinct relics not in <paramref name="owned"/>, in a seeded order. Empty if all are owned.</summary>
        public static List<RelicId> RollOffer(IReadOnlyCollection<RelicId> owned, Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var remaining = new List<RelicId>();
            foreach (var relic in All)
                if (owned == null || !Contains(owned, relic)) remaining.Add(relic);

            var offer = new List<RelicId>(ChoiceSize);
            while (offer.Count < ChoiceSize && remaining.Count > 0)
            {
                int i = rng.Next(remaining.Count);
                offer.Add(remaining[i]);
                remaining.RemoveAt(i);
            }
            return offer;
        }

        /// <summary>Ashen Sigil: action speed with <paramref name="stacks"/> Sleight burns this room (1 = unchanged).</summary>
        public static float ActionSpeedMultiplier(int stacks)
        {
            if (stacks < 0) throw new ArgumentOutOfRangeException(nameof(stacks));
            return 1f + AshenSigilSpeedPerStack * stacks;
        }

        private static bool Contains(IReadOnlyCollection<RelicId> owned, RelicId relic)
        {
            foreach (var r in owned) if (r == relic) return true;
            return false;
        }
    }
}
