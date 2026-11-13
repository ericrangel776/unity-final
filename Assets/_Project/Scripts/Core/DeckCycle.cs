using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>
    /// Pure-C# deck state for one combatant: draw queue, 3-card hand, reserved 0-Parry slot,
    /// discard, burned pile, and cards deleted from the current cycle by 0-breaks.
    /// Zero-breakers skip the draw queue: they go straight to the Parry slot (overflow waits in
    /// a reserve) and never occupy a hand slot. <c>DeckRuntimeManager</c> wraps this for scenes.
    /// </summary>
    public sealed class DeckCycle
    {
        public const int HandSize = 3;

        private readonly Queue<RuntimeCardInstance> drawQueue = new Queue<RuntimeCardInstance>();
        private readonly Queue<RuntimeCardInstance> zeroReserve = new Queue<RuntimeCardInstance>();
        private readonly RuntimeCardInstance[] hand = new RuntimeCardInstance[HandSize];
        private readonly List<RuntimeCardInstance> discard = new List<RuntimeCardInstance>();
        private readonly List<RuntimeCardInstance> burned = new List<RuntimeCardInstance>();
        private readonly List<RuntimeCardInstance> deletedThisCycle = new List<RuntimeCardInstance>();
        private Random rng = new Random(0);

        /// <summary>Raised after any change to the piles or hand.</summary>
        public event Action Changed;

        public IReadOnlyList<RuntimeCardInstance> Hand => hand;
        public RuntimeCardInstance ParrySlot { get; private set; }
        public int DrawCount => drawQueue.Count;
        public int DiscardCount => discard.Count;
        public int BurnedCount => burned.Count;
        public int DeletedThisCycleCount => deletedThisCycle.Count;
        public int ZeroReserveCount => zeroReserve.Count;
        public IReadOnlyList<RuntimeCardInstance> DiscardPile => discard;
        public IReadOnlyList<RuntimeCardInstance> BurnedPile => burned;

        /// <summary>Cards not burned or deleted: what can still come back on reload.</summary>
        public int CyclingCardCount
        {
            get
            {
                int inHand = ParrySlot != null ? 1 : 0;
                foreach (var c in hand) if (c != null) inHand++;
                return drawQueue.Count + zeroReserve.Count + discard.Count + inHand;
            }
        }

        /// <summary>Reload is only available once the draw queue is empty and something can be reshuffled.</summary>
        public bool CanReload => drawQueue.Count == 0 && (discard.Count > 0 || deletedThisCycle.Count > 0);

        /// <summary>Resets all piles, shuffles <paramref name="cards"/> into the draw queue, and deals the opening hand.</summary>
        public void Initialize(IEnumerable<RuntimeCardInstance> cards, Random random)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            rng = random ?? throw new ArgumentNullException(nameof(random));

            drawQueue.Clear();
            zeroReserve.Clear();
            discard.Clear();
            burned.Clear();
            deletedThisCycle.Clear();
            ParrySlot = null;
            Array.Clear(hand, 0, hand.Length);

            var list = new List<RuntimeCardInstance>();
            foreach (var c in cards) if (c != null) list.Add(c);
            ShuffleInto(list);
            FillEmptySlots();
            Changed?.Invoke();
        }

        /// <summary>Plays the card in <paramref name="slot"/>: it goes to discard and the next card is drawn into that slot.</summary>
        public bool TryPlay(int slot, out RuntimeCardInstance card)
        {
            card = null;
            if (slot < 0 || slot >= HandSize || hand[slot] == null) return false;

            card = hand[slot];
            discard.Add(card);
            hand[slot] = DrawNext();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Plays the 0-card in the Parry slot; the next reserved 0-card (if any) takes its place.</summary>
        public bool TryPlayParry(out RuntimeCardInstance card)
        {
            card = ParrySlot;
            if (card == null) return false;

            discard.Add(card);
            ParrySlot = zeroReserve.Count > 0 ? zeroReserve.Dequeue() : null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Shuffles discard (and cards deleted this cycle) back into the draw queue and refills empty slots.</summary>
        public bool Reload()
        {
            if (!CanReload) return false;

            var pool = new List<RuntimeCardInstance>(discard.Count + deletedThisCycle.Count);
            pool.AddRange(discard);
            pool.AddRange(deletedThisCycle);
            discard.Clear();
            deletedThisCycle.Clear();
            ShuffleInto(pool);
            FillEmptySlots();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// A played card that was broken by a 0 is removed from discard until the next reload.
        /// Returns false if the card is not in the discard pile.
        /// </summary>
        public bool DeleteFromCurrentCycle(RuntimeCardInstance card)
        {
            if (card == null || !discard.Remove(card)) return false;
            deletedThisCycle.Add(card);
            Changed?.Invoke();
            return true;
        }

        private void ShuffleInto(List<RuntimeCardInstance> cards)
        {
            // Fisher-Yates with the injected RNG so tests and seeded runs are deterministic.
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
            // 0-cards never enter the draw queue: they go straight to the Parry slot / reserve.
            foreach (var c in cards)
            {
                if (!c.IsZeroBreaker) drawQueue.Enqueue(c);
                else if (ParrySlot == null) ParrySlot = c;
                else zeroReserve.Enqueue(c);
            }
        }

        private void FillEmptySlots()
        {
            for (int i = 0; i < HandSize; i++)
                if (hand[i] == null) hand[i] = DrawNext();
        }

        private RuntimeCardInstance DrawNext() => drawQueue.Count > 0 ? drawQueue.Dequeue() : null;
    }
}
