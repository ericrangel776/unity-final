using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Fisher-Yates with an injected RNG, so tests and seeded runs are deterministic.</summary>
    internal static class ListShuffle
    {
        public static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
