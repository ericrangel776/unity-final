using System;

namespace DeckArpg.Core
{
    /// <summary>
    /// Price of an Overburn Reload (pay HP to bring every burned card back). The n-th use in a room
    /// costs n x 10% of max HP, rounded up, and it may never be lethal.
    /// </summary>
    public static class OverburnRules
    {
        public const int CostPercentPerUse = 10;

        /// <summary>Cost of the next Overburn after <paramref name="usesThisRoom"/> earlier ones. Integer math, rounded up.</summary>
        public static int Cost(int maxHp, int usesThisRoom)
        {
            if (maxHp < 1) throw new ArgumentOutOfRangeException(nameof(maxHp));
            if (usesThisRoom < 0) throw new ArgumentOutOfRangeException(nameof(usesThisRoom));
            long scaled = (long)maxHp * (usesThisRoom + 1) * CostPercentPerUse;
            return (int)((scaled + 99) / 100);
        }

        /// <summary>Never lethal: you must keep at least 1 HP after paying.</summary>
        public static bool CanAfford(int hp, int cost) => hp > cost;
    }
}
