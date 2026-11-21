using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>
    /// Heal card numbers (docs/specs/heal-rework.md). A hand heal is a 0.5 s interruptible channel; a heal card in a
    /// Sleight heals on the fire frame. Both heal <see cref="HealPerValue"/> HP per point of value, like damage.
    /// </summary>
    public static class HealRules
    {
        public const int HealPerValue = 3;
        /// <summary>Hand-heal channel length, on scaled time (hit-stop pauses it).</summary>
        public const float ChannelSeconds = 0.5f;
        /// <summary>Move speed multiplier while channelling.</summary>
        public const float ChannelMoveMultiplier = 0.5f;

        /// <summary>HP restored by a heal of <paramref name="value"/>; 0 for a value of 0 or less.</summary>
        public static int Amount(int value) => value <= 0 ? 0 : value * HealPerValue;

        /// <summary>Sum of <see cref="Amount"/> over the heal cards in a stock (effective values). Null-safe.</summary>
        public static int SleightHeal(IReadOnlyList<RuntimeCardInstance> stocked)
        {
            if (stocked == null) return 0;
            int total = 0;
            for (int i = 0; i < stocked.Count; i++)
            {
                var card = stocked[i];
                if (card != null && card.IsHeal) total += Amount(card.GetEffectiveValue());
            }
            return total;
        }
    }
}
