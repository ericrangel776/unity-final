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
    /// <summary>Outcome of stocking a card toward a Sleight.</summary>
    public enum StockResult
    {
        Rejected, // nothing to stock; state unchanged
        Stocked,  // added; stock not yet full
        Fired     // 3rd card: the Sleight was composed and the stock emptied
    }

    public sealed class DeckCycle
    {
        public const int HandSize = 3;

        private readonly Queue<RuntimeCardInstance> drawQueue = new Queue<RuntimeCardInstance>();
        private readonly Queue<RuntimeCardInstance> zeroReserve = new Queue<RuntimeCardInstance>();
        private readonly RuntimeCardInstance[] hand = new RuntimeCardInstance[HandSize];
        private readonly List<RuntimeCardInstance> discard = new List<RuntimeCardInstance>();
        private readonly List<RuntimeCardInstance> burned = new List<RuntimeCardInstance>();
        private readonly List<RuntimeCardInstance> deletedThisCycle = new List<RuntimeCardInstance>();
        private readonly List<RuntimeCardInstance> stock = new List<RuntimeCardInstance>(SleightRules.StockSize);
        private float stockIdleSeconds;
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
        /// <summary>Cards stocked toward the next Sleight, in slot order (0-2 entries).</summary>
        public IReadOnlyList<RuntimeCardInstance> Stock => stock;
        public int StockCount => stock.Count;
        /// <summary>Seconds since the last card was stocked (0 while the stock is empty).</summary>
        public float StockIdleSeconds => stockIdleSeconds;

        /// <summary>Cards not burned or deleted: what can still come back on reload. Includes stocked cards.</summary>
        public int CyclingCardCount
        {
            get
            {
                int inHand = ParrySlot != null ? 1 : 0;
                foreach (var c in hand) if (c != null) inHand++;
                return drawQueue.Count + zeroReserve.Count + discard.Count + stock.Count + inHand;
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
            stock.Clear();
            stockIdleSeconds = 0f;
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

        /// <summary>
        /// Moves the hand card in <paramref name="slot"/> into the Sleight stock and draws into that slot.
        /// The 3rd stocked card fires the Sleight: <paramref name="sleight"/> is filled, slot 1 is burned
        /// (unless the Floor of 3 protects it) and the other two go to discard.
        /// </summary>
        public StockResult TryStock(int slot, out SleightResult sleight)
        {
            sleight = default;
            if (slot < 0 || slot >= HandSize || hand[slot] == null) return StockResult.Rejected;

            var card = hand[slot];
            hand[slot] = DrawNext();
            return AddToStock(card, out sleight);
        }

        /// <summary>Stocks the 0-card from the Parry slot (it mirrors the best numbered card in the Sleight).</summary>
        public StockResult TryStockParry(out SleightResult sleight)
        {
            sleight = default;
            var card = ParrySlot;
            if (card == null) return StockResult.Rejected;

            ParrySlot = zeroReserve.Count > 0 ? zeroReserve.Dequeue() : null;
            return AddToStock(card, out sleight);
        }

        /// <summary>
        /// Card Break penalty ("crack the catalyst"): the slot-1 stocked card is burned and the rest go to
        /// discard. At or below the Floor of 3 nothing burns. Returns false if nothing was stocked.
        /// </summary>
        public bool InterruptStock()
        {
            if (stock.Count == 0) return false;
            bool burnFirst = SleightRules.CanBurn(CyclingCardCount);
            for (int i = 0; i < stock.Count; i++)
            {
                if (i == 0 && burnFirst) Burn(stock[i]);
                else discard.Add(stock[i]);
            }
            ClearStock();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Ages a partial stock. Once no card has been added for <see cref="SleightRules.StockTimeoutSeconds"/>,
        /// every stocked card falls into discard (no burn) and this returns true.
        /// </summary>
        public bool AdvanceStockTimer(float deltaSeconds)
        {
            if (stock.Count == 0 || deltaSeconds <= 0f) return false;
            stockIdleSeconds += deltaSeconds;
            if (stockIdleSeconds < SleightRules.StockTimeoutSeconds) return false;

            discard.AddRange(stock);
            ClearStock();
            Changed?.Invoke();
            return true;
        }

        private StockResult AddToStock(RuntimeCardInstance card, out SleightResult sleight)
        {
            sleight = default;
            stock.Add(card);
            stockIdleSeconds = 0f;
            if (stock.Count < SleightRules.StockSize)
            {
                Changed?.Invoke();
                return StockResult.Stocked;
            }

            sleight = SleightRules.Compose(stock);
            if (SleightRules.CanBurn(CyclingCardCount)) Burn(stock[0]);
            else discard.Add(stock[0]);
            for (int i = 1; i < stock.Count; i++) discard.Add(stock[i]);
            ClearStock();
            Changed?.Invoke();
            return StockResult.Fired;
        }

        private void Burn(RuntimeCardInstance card)
        {
            card.IsBurnedThisFight = true;
            burned.Add(card);
        }

        private void ClearStock()
        {
            stock.Clear();
            stockIdleSeconds = 0f;
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
        /// A played card that was broken by a 0 is set aside until the next reload. Usually it is in
        /// discard, but a slow projectile can outlive a reload, so the hand (slot is refilled) and the
        /// draw queue are searched too. Returns false if the card is not in this cycle.
        /// </summary>
        public bool DeleteFromCurrentCycle(RuntimeCardInstance card)
        {
            if (card == null || !RemoveFromCycle(card)) return false;
            deletedThisCycle.Add(card);
            Changed?.Invoke();
            return true;
        }

        private bool RemoveFromCycle(RuntimeCardInstance card)
        {
            if (discard.Remove(card)) return true;

            for (int i = 0; i < HandSize; i++)
            {
                if (hand[i] != card) continue;
                hand[i] = DrawNext();
                return true;
            }

            if (!drawQueue.Contains(card)) return false;
            var remaining = drawQueue.ToArray();
            drawQueue.Clear();
            foreach (var c in remaining) if (c != card) drawQueue.Enqueue(c);
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
