using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class DeckCycleTests
    {
        private TestCards cards;
        private DeckCycle deck;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            deck = new DeckCycle();
        }

        [TearDown] public void TearDown() => cards.DestroyAll();

        private void Init(IEnumerable<RuntimeCardInstance> list, int seed = 42) => deck.Initialize(list, new Random(seed));

        [Test]
        public void Initialize_DealsThreeCardHand_RestInDrawQueue()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6, 7));
            Assert.IsTrue(deck.Hand.All(c => c != null));
            Assert.AreEqual(4, deck.DrawCount);
            Assert.AreEqual(7, deck.CyclingCardCount);
        }

        [Test]
        public void Initialize_SameSeed_GivesSameOrder()
        {
            var list = cards.Numbered(1, 2, 3, 4, 5, 6, 7, 8, 9);
            var other = new DeckCycle();
            deck.Initialize(list, new Random(7));
            other.Initialize(list, new Random(7));
            CollectionAssert.AreEqual(deck.Hand, other.Hand);
        }

        [Test]
        public void TryPlay_MovesCardToDiscard_AndDrawsNextIntoSameSlot()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5));
            var before = deck.Hand[1];

            Assert.IsTrue(deck.TryPlay(1, out var played));

            Assert.AreSame(before, played);
            Assert.AreEqual(1, deck.DiscardCount);
            Assert.IsNotNull(deck.Hand[1]);
            Assert.AreNotSame(before, deck.Hand[1]);
            Assert.AreEqual(1, deck.DrawCount);
        }

        [Test]
        public void TryPlay_WhenQueueEmpty_LeavesSlotEmpty()
        {
            Init(cards.Numbered(1, 2, 3));
            Assert.IsTrue(deck.TryPlay(0, out _));
            Assert.IsNull(deck.Hand[0]);
            Assert.IsFalse(deck.TryPlay(0, out var none));
            Assert.IsNull(none);
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void TryPlay_InvalidSlot_ReturnsFalse(int slot)
        {
            Init(cards.Numbered(1, 2, 3));
            Assert.IsFalse(deck.TryPlay(slot, out _));
        }

        [Test]
        public void CanReload_OnlyWhenDrawQueueEmpty()
        {
            Init(cards.Numbered(1, 2, 3, 4));
            deck.TryPlay(0, out _);
            Assert.AreEqual(0, deck.DrawCount);
            Assert.IsTrue(deck.CanReload);

            Init(cards.Numbered(1, 2, 3, 4, 5));
            deck.TryPlay(0, out _);
            Assert.AreEqual(1, deck.DrawCount);
            Assert.IsFalse(deck.CanReload);
            Assert.IsFalse(deck.Reload());
        }

        [Test]
        public void Reload_ReturnsDiscardToCycle_AndRefillsEmptySlots()
        {
            Init(cards.Numbered(1, 2, 3, 4));
            for (int i = 0; i < 3; i++) deck.TryPlay(i, out _); // 3 plays; one slot refilled from queue, then empties

            Assert.IsTrue(deck.Reload());

            Assert.AreEqual(0, deck.DiscardCount);
            Assert.IsTrue(deck.Hand.All(c => c != null));
            Assert.AreEqual(4, deck.CyclingCardCount);
        }

        [Test]
        public void ZeroCard_IsRoutedToParrySlot_NotHand()
        {
            var zero = cards.Zero();
            var list = cards.Numbered(1, 2, 3, 4);
            list.Add(zero);
            Init(list);

            Assert.AreSame(zero, deck.ParrySlot);
            Assert.IsFalse(deck.Hand.Contains(zero));
        }

        [Test]
        public void SecondZero_WaitsInReserve_ThenFillsParrySlot()
        {
            var z1 = cards.Zero();
            var z2 = cards.Zero();
            Init(new List<RuntimeCardInstance> { z1, z2, cards.Card(5) });

            Assert.IsNotNull(deck.ParrySlot);
            Assert.AreEqual(1, deck.ZeroReserveCount);

            Assert.IsTrue(deck.TryPlayParry(out var first));
            Assert.IsNotNull(deck.ParrySlot);
            Assert.AreNotSame(first, deck.ParrySlot);
            Assert.AreEqual(0, deck.ZeroReserveCount);

            Assert.IsTrue(deck.TryPlayParry(out _));
            Assert.IsNull(deck.ParrySlot);
            Assert.IsFalse(deck.TryPlayParry(out _));
        }

        [Test]
        public void EmptyDeck_DoesNotThrow_AndHasNothingToPlay()
        {
            Init(new List<RuntimeCardInstance>());
            Assert.IsTrue(deck.Hand.All(c => c == null));
            Assert.IsFalse(deck.TryPlay(0, out _));
            Assert.IsFalse(deck.CanReload);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void DeckSmallerThanHand_FillsWhatItCan(int size)
        {
            Init(cards.Numbered(Enumerable.Range(1, size).ToArray()));
            Assert.AreEqual(size, deck.Hand.Count(c => c != null));
            Assert.AreEqual(0, deck.DrawCount);
        }

        [Test]
        public void Initialize_SkipsNullEntries()
        {
            Init(new List<RuntimeCardInstance> { cards.Card(2), null, cards.Card(3) });
            Assert.AreEqual(2, deck.CyclingCardCount);
        }

        [Test]
        public void DeleteFromCurrentCycle_RemovesUntilReload()
        {
            Init(cards.Numbered(1, 2, 3, 4));
            deck.TryPlay(0, out var played);

            Assert.IsTrue(deck.DeleteFromCurrentCycle(played));
            Assert.AreEqual(0, deck.DiscardCount);
            Assert.AreEqual(1, deck.DeletedThisCycleCount);
            Assert.AreEqual(3, deck.CyclingCardCount);

            Assert.IsTrue(deck.Reload());
            Assert.AreEqual(0, deck.DeletedThisCycleCount);
            Assert.AreEqual(4, deck.CyclingCardCount);
        }

        [Test]
        public void DeleteFromCurrentCycle_CardOutsideCycle_ReturnsFalse()
        {
            Init(cards.Numbered(1, 2, 3));
            Assert.IsFalse(deck.DeleteFromCurrentCycle(cards.Card(5)));
            Assert.IsFalse(deck.DeleteFromCurrentCycle(null));
        }

        [Test]
        public void DeleteFromCurrentCycle_CardReloadedIntoHand_IsRemovedAndSlotRefilled()
        {
            // Slow projectile: card played, owner reloads before the 0-break lands.
            // 3-card deck: the only discarded card must come back into the empty slot 0.
            Init(cards.Numbered(1, 2, 3));
            deck.TryPlay(0, out var played);
            Assert.IsTrue(deck.Reload());
            Assert.AreSame(played, deck.Hand[0]);

            Assert.IsTrue(deck.DeleteFromCurrentCycle(played));

            Assert.IsNull(deck.Hand[0], "Slot refills from the (empty) queue.");
            Assert.AreEqual(1, deck.DeletedThisCycleCount);
            Assert.AreEqual(3, deck.CyclingCardCount + deck.DeletedThisCycleCount, "No card lost or duplicated.");
        }

        [Test]
        public void DeleteFromCurrentCycle_CardInDrawQueue_IsRemoved()
        {
            var all = cards.Numbered(1, 2, 3, 4, 5);
            Init(all);
            var queued = all.First(c => !deck.Hand.Contains(c)); // 2 of the 5 were not dealt

            Assert.IsTrue(deck.DeleteFromCurrentCycle(queued));

            Assert.AreEqual(1, deck.DrawCount);
            Assert.AreEqual(1, deck.DeletedThisCycleCount);
            Assert.AreEqual(5, deck.CyclingCardCount + deck.DeletedThisCycleCount, "No card lost or duplicated.");
            deck.TryPlay(0, out _);
            deck.TryPlay(0, out _);
            Assert.IsFalse(deck.DiscardPile.Contains(queued), "Deleted card is never drawn.");
        }

        [Test]
        public void Changed_FiresOnPlay()
        {
            Init(cards.Numbered(1, 2, 3));
            int count = 0;
            deck.Changed += () => count++;
            deck.TryPlay(0, out _);
            Assert.AreEqual(1, count);
        }
    }
}
