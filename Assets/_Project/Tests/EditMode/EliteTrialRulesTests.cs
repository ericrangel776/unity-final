using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Core rules behind the Purple Elite trials (docs/specs/m3-elite-trials.md).</summary>
    public class EliteTrialRulesTests
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

        private void Init(IEnumerable<RuntimeCardInstance> list) => deck.Initialize(list, new Random(42));

        // ---- CounterRules ----

        [TestCase(false, 0, 0)]
        [TestCase(false, 1, 1)]
        [TestCase(false, 3, 1)]
        [TestCase(true, 0, 0)]
        [TestCase(true, 1, 0)]
        [TestCase(true, 2, 2)]
        [TestCase(true, 3, 2)]
        public void ZerosToSpend_SingleCostsOne_SleightNeedsTwo(bool sleight, int ready, int expected)
        {
            Assert.AreEqual(expected, CounterRules.ZerosToSpend(sleight, ready));
        }

        // ---- ZerosReady ----

        [Test]
        public void ZerosReady_CountsParrySlotAndReserve()
        {
            var list = cards.Numbered(1, 2, 3, 4);
            list.Add(cards.Zero());
            list.Add(cards.Zero());
            Init(list);
            Assert.AreEqual(2, deck.ZerosReady);

            deck.TryPlayParry(out _);
            Assert.AreEqual(1, deck.ZerosReady);
            deck.TryPlayParry(out _);
            Assert.AreEqual(0, deck.ZerosReady);
        }

        // ---- BurnFromCycle ----

        [Test]
        public void BurnFromCycle_PlayedCard_MovesFromDiscardToBurned()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            deck.TryPlay(0, out var played);

            Assert.IsTrue(deck.BurnFromCycle(played));
            Assert.IsTrue(played.IsBurnedThisFight);
            CollectionAssert.DoesNotContain(deck.DiscardPile, played);
            CollectionAssert.Contains(deck.BurnedPile, played);
            Assert.AreEqual(5, deck.CyclingCardCount);
        }

        [Test]
        public void BurnFromCycle_CardSetAsideByZero_IsBurnedNotReturnedOnReload()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5));
            deck.TryPlay(0, out var played);
            deck.DeleteFromCurrentCycle(played);

            Assert.IsTrue(deck.BurnFromCycle(played));
            Assert.AreEqual(0, deck.DeletedThisCycleCount);
            CollectionAssert.Contains(deck.BurnedPile, played);
        }

        [Test]
        public void BurnFromCycle_AtFloorOfThree_DoesNothing()
        {
            Init(cards.Numbered(1, 2, 3));
            deck.TryPlay(0, out var played);

            Assert.IsFalse(deck.BurnFromCycle(played));
            Assert.IsFalse(played.IsBurnedThisFight);
            CollectionAssert.Contains(deck.DiscardPile, played);
        }

        [Test]
        public void BurnFromCycle_SetAsideCard_StillCountsTowardTheFloor()
        {
            // 4 cards: one set aside by a 0 leaves 3 cycling, but it would come back, so 4 count and it may burn.
            Init(cards.Numbered(1, 2, 3, 4));
            deck.TryPlay(0, out var played);
            deck.DeleteFromCurrentCycle(played);
            Assert.AreEqual(3, deck.CyclingCardCount);

            Assert.IsTrue(deck.BurnFromCycle(played));
        }

        [Test]
        public void BurnFromCycle_AlreadyBurned_ReturnsFalse_AndDoesNotBurnTwice()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            deck.TryPlay(0, out var played);
            deck.BurnFromCycle(played);

            Assert.IsFalse(deck.BurnFromCycle(played));
            Assert.AreEqual(1, deck.BurnedCount);
        }

        [Test]
        public void BurnFromCycle_CardNotInThisDeck_ReturnsFalse()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            Assert.IsFalse(deck.BurnFromCycle(cards.Card(5)));
            Assert.AreEqual(0, deck.BurnedCount);
        }

        [Test]
        public void BurnFromCycle_Null_ReturnsFalse()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            Assert.IsFalse(deck.BurnFromCycle(null));
        }

        [Test]
        public void BurnFromCycle_StockedCard_IsLeftToTheStockRules()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            var stocked = deck.Hand[0];
            deck.TryStock(0, out _);

            Assert.IsFalse(deck.BurnFromCycle(stocked));
            CollectionAssert.Contains(deck.Stock, stocked);
        }

        [Test]
        public void BurnFromCycle_BurnedCard_ComesBackWithOverburn()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            deck.TryPlay(0, out var played);
            deck.BurnFromCycle(played);

            Assert.AreEqual(1, deck.RestoreBurned());
            Assert.IsFalse(played.IsBurnedThisFight);
            Assert.IsTrue(deck.DiscardPile.Contains(played));
        }
    }
}
