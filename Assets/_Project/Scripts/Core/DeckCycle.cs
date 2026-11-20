using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Outcome of stocking a card toward a Sleight.</summary>
    public enum StockResult
    {
        Rejected, // nothing to stock; state unchanged
        Stocked,  // added; stock not yet full
        Fired     // 3rd card: the Sleight was composed and the stock emptied
    }

    /// <summary>Why a card was burned (relics react to some causes only).</summary>
    public enum BurnCause
    {
        /// <summary>Fired in a Sleight (slot 1, or Premium in any slot).</summary>
        Sleight,
        /// <summary>A Premium card played on its own.</summary>
        Premium,
        /// <summary>Slot 1 of a partial stock cracked by a Card Break.</summary>
        StockCracked,
        /// <summary>Burned by an effect (e.g. the Hexer trial).</summary>
        Effect
    }

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
        private readonly List<RuntimeCardInstance> stock = new List<RuntimeCardInstance>(SleightRules.StockSize);
        private readonly long[] drawStamp = new long[HandSize];
        private long drawCounter;
        private float stockIdleSeconds;
        private Random rng = new Random(0);
        /// <summary>Last card that left the hand by play or Sleight (spacing across a reload).</summary>
        private RuntimeCardInstance lastPlayed;

        /// <summary>Raised after any change to the piles or hand.</summary>
        public event Action Changed;
        /// <summary>Raised as each card is burned, before <see cref="Changed"/>.</summary>
        public event Action<RuntimeCardInstance, BurnCause> CardBurned;

        public IReadOnlyList<RuntimeCardInstance> Hand => hand;
        /// <summary>The draw queue in order, next card first (read-only peek, for the draw-order strip).</summary>
        public IEnumerable<RuntimeCardInstance> DrawOrder => drawQueue;
        /// <summary>
        /// Optional: cards matching this are never back to back in the draw order, also across a reload
        /// (boss 9s). Applied every time cards are shuffled into the queue. Null = plain shuffle.
        /// </summary>
        public Func<RuntimeCardInstance, bool> SpacingRule { get; set; }

        /// <summary>
        /// The non-empty hand slot whose card was drawn earliest, or -1. Playing this slot each time makes the play
        /// order follow the draw order (the boss does this so its spaced shuffle holds).
        /// </summary>
        public int OldestSlot
        {
            get
            {
                int best = -1;
                for (int i = 0; i < HandSize; i++)
                    if (hand[i] != null && (best < 0 || drawStamp[i] < drawStamp[best])) best = i;
                return best;
            }
        }
        public RuntimeCardInstance ParrySlot { get; private set; }
        public int DrawCount => drawQueue.Count;
        public int DiscardCount => discard.Count;
        public int BurnedCount => burned.Count;
        public int DeletedThisCycleCount => deletedThisCycle.Count;
        public int ZeroReserveCount => zeroReserve.Count;
        /// <summary>0-cards ready to play right now: the Parry slot plus the reserve behind it.</summary>
        public int ZerosReady => (ParrySlot != null ? 1 : 0) + zeroReserve.Count;
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
            lastPlayed = null;
            Array.Clear(hand, 0, hand.Length);

            var list = new List<RuntimeCardInstance>();
            foreach (var c in cards) if (c != null) list.Add(c);
            ShuffleInto(list);
            FillEmptySlots();
            Changed?.Invoke();
        }

        /// <summary>
        /// Plays the card in <paramref name="slot"/>: it goes to discard (Premium: burned, Floor of 3 permitting)
        /// and the next card is drawn into that slot.
        /// </summary>
        public bool TryPlay(int slot, out RuntimeCardInstance card)
        {
            card = null;
            if (slot < 0 || slot >= HandSize || hand[slot] == null) return false;

            card = hand[slot];
            DiscardOrBurnPremium(card);
            lastPlayed = card;
            DrawInto(slot);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Plays the 0-card in the Parry slot; the next reserved 0-card (if any) takes its place.</summary>
        public bool TryPlayParry(out RuntimeCardInstance card)
        {
            card = ParrySlot;
            if (card == null) return false;

            DiscardOrBurnPremium(card);
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
            DrawInto(slot);
            lastPlayed = card;
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
                if (i == 0 && burnFirst) Burn(stock[i], BurnCause.StockCracked);
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
            // Slot 1 always tries to burn; Premium cards in any slot do too. Count down as cards burn
            // so several burns in one Sleight still stop at the Floor of 3.
            int cycling = CyclingCardCount;
            for (int i = 0; i < stock.Count; i++)
            {
                bool wantsBurn = i == 0 || stock[i].IsPremium;
                if (wantsBurn && SleightRules.CanBurn(cycling))
                {
                    Burn(stock[i], BurnCause.Sleight);
                    cycling--;
                }
                else discard.Add(stock[i]);
            }
            ClearStock();
            Changed?.Invoke();
            return StockResult.Fired;
        }

        /// <summary>
        /// Overburn: every burned card goes back to discard (and loses its burned flag), so the next
        /// reload shuffles it in. Returns how many came back.
        /// </summary>
        public int RestoreBurned()
        {
            int count = burned.Count;
            if (count == 0) return 0;
            foreach (var c in burned)
            {
                c.IsBurnedThisFight = false;
                discard.Add(c);
            }
            burned.Clear();
            Changed?.Invoke();
            return count;
        }

        /// <summary>
        /// A single-played card goes to discard, unless it's Premium and the Floor of 3 allows a burn.
        /// Call it while the card is still counted as cycling (before it leaves its slot).
        /// </summary>
        private void DiscardOrBurnPremium(RuntimeCardInstance card)
        {
            if (card.IsPremium && SleightRules.CanBurn(CyclingCardCount)) Burn(card, BurnCause.Premium);
            else discard.Add(card);
        }

        private void Burn(RuntimeCardInstance card, BurnCause cause)
        {
            card.IsBurnedThisFight = true;
            burned.Add(card);
            CardBurned?.Invoke(card, cause);
        }

        /// <summary>
        /// Shattered Hourglass: a random discard card goes to the bottom of the draw queue (straight into an empty
        /// hand slot if the queue was empty). Returns it, or null if no discard card qualifies.
        /// 0-cards are never returned: the 0 that just won is already in the discard when the relic fires, so it
        /// could hand itself back and parry forever (M3 review).
        /// </summary>
        public RuntimeCardInstance ReturnRandomSpentToDrawQueue() => MoveRandomToDrawQueue(discard, card => !card.IsZeroBreaker);

        /// <summary>
        /// Tempered Coil: a random burned card is restored (no longer burned) to the bottom of the draw queue, or an
        /// empty hand slot. Returns it, or null if nothing is burned.
        /// </summary>
        public RuntimeCardInstance RestoreRandomBurnedToDrawQueue() => MoveRandomToDrawQueue(burned, null);

        /// <summary>Moves a random card of <paramref name="pile"/> that passes <paramref name="eligible"/> (null = any).</summary>
        private RuntimeCardInstance MoveRandomToDrawQueue(List<RuntimeCardInstance> pile, Predicate<RuntimeCardInstance> eligible)
        {
            var candidates = eligible == null ? pile : pile.FindAll(eligible);
            if (candidates.Count == 0) return null;
            var card = candidates[rng.Next(candidates.Count)];
            pile.Remove(card);
            card.IsBurnedThisFight = false;
            if (card.IsZeroBreaker)
            {
                // 0-cards never sit in the draw queue.
                if (ParrySlot == null) ParrySlot = card;
                else zeroReserve.Enqueue(card);
            }
            else drawQueue.Enqueue(card);
            FillEmptySlots();
            Changed?.Invoke();
            return card;
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

        /// <summary>
        /// A boss Sleight broken by a 0: each of its cards is set aside until the next reload, from discard or, for a
        /// slot-1 card that already burned, from the burned pile (the 0-break turns that burn into a delete).
        /// Returns how many cards were set aside.
        /// </summary>
        public int DeleteSleight(IReadOnlyList<RuntimeCardInstance> cards)
        {
            if (cards == null) return 0;
            int count = 0;
            foreach (var card in cards)
            {
                if (card == null || deletedThisCycle.Contains(card)) continue;
                if (burned.Remove(card)) card.IsBurnedThisFight = false;
                else if (!RemoveFromCycle(card)) continue;
                deletedThisCycle.Add(card);
                count++;
            }
            if (count > 0) Changed?.Invoke();
            return count;
        }

        /// <summary>
        /// Burns <paramref name="card"/> for the room from wherever it sits in the cycle (discard, set aside by a
        /// 0-break, hand or draw queue). The Floor of 3 applies; a card set aside this cycle still counts toward
        /// it because it would come back on reload. Returns false if blocked, already burned or not in the cycle.
        /// </summary>
        public bool BurnFromCycle(RuntimeCardInstance card)
        {
            if (card == null || card.IsBurnedThisFight || stock.Contains(card)) return false;
            bool setAside = deletedThisCycle.Contains(card);
            int cycling = CyclingCardCount + (setAside ? 1 : 0);
            if (!SleightRules.CanBurn(cycling)) return false;

            if (setAside) deletedThisCycle.Remove(card);
            else if (!RemoveFromCycle(card)) return false;
            Burn(card, BurnCause.Effect);
            Changed?.Invoke();
            return true;
        }

        private bool RemoveFromCycle(RuntimeCardInstance card)
        {
            if (discard.Remove(card)) return true;

            for (int i = 0; i < HandSize; i++)
            {
                if (hand[i] != card) continue;
                DrawInto(i);
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
            ListShuffle.Shuffle(cards, rng);
            if (SpacingRule != null)
            {
                // Only the queue part is spaced: 0-cards never enter it.
                var numbered = cards.FindAll(c => !c.IsZeroBreaker);
                var queued = new List<RuntimeCardInstance>(drawQueue);
                var previous = queued.Count > 0 ? queued[queued.Count - 1] : NewestInHand() ?? lastPlayed;
                CardSpacing.SpreadApart(numbered, SpacingRule, previous != null && SpacingRule(previous), rng);
                numbered.AddRange(cards.FindAll(c => c.IsZeroBreaker));
                cards = numbered;
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
                if (hand[i] == null) DrawInto(i);
        }

        private void DrawInto(int slot)
        {
            hand[slot] = DrawNext();
            drawStamp[slot] = ++drawCounter;
        }

        /// <summary>The hand card drawn last (it's played just before the queue continues), or null.</summary>
        private RuntimeCardInstance NewestInHand()
        {
            int best = -1;
            for (int i = 0; i < HandSize; i++)
                if (hand[i] != null && (best < 0 || drawStamp[i] > drawStamp[best])) best = i;
            return best < 0 ? null : hand[best];
        }

        private RuntimeCardInstance DrawNext() => drawQueue.Count > 0 ? drawQueue.Dequeue() : null;
    }
}
