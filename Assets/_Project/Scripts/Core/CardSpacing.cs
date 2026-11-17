using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Reorders a shuffled play order so marked cards (e.g. boss 9s) are never back to back.</summary>
    public static class CardSpacing
    {
        /// <summary>
        /// Rearranges <paramref name="order"/> in place so no two <paramref name="spaced"/> cards are adjacent, and the
        /// first card isn't one when <paramref name="previousWasSpaced"/> (the card played just before this order).
        /// The other cards keep their shuffled order; spaced cards go into randomly chosen gaps between them.
        /// When there are too many spaced cards to separate, the leftover ones are appended (best effort, no throw).
        /// </summary>
        public static void SpreadApart(List<RuntimeCardInstance> order, Func<RuntimeCardInstance, bool> spaced,
            bool previousWasSpaced, Random rng)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (spaced == null) throw new ArgumentNullException(nameof(spaced));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var marked = new List<RuntimeCardInstance>();
            var others = new List<RuntimeCardInstance>();
            foreach (var c in order) (spaced(c) ? marked : others).Add(c);
            if (marked.Count == 0) return;

            // Gap g sits before others[g] (g == others.Count is the end). Gap 0 is banned after a spaced card.
            var gaps = new List<int>();
            for (int g = previousWasSpaced ? 1 : 0; g <= others.Count; g++) gaps.Add(g);

            var chosen = new HashSet<int>();
            int place = Math.Min(marked.Count, gaps.Count);
            for (int i = 0; i < place; i++)
            {
                int pick = rng.Next(gaps.Count);
                chosen.Add(gaps[pick]);
                gaps.RemoveAt(pick);
            }

            order.Clear();
            int next = 0;
            for (int g = 0; g <= others.Count; g++)
            {
                if (chosen.Contains(g)) order.Add(marked[next++]);
                if (g < others.Count) order.Add(others[g]);
            }
            while (next < marked.Count) order.Add(marked[next++]);
        }
    }
}
