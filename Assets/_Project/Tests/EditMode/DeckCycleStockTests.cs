using System;
using System.Collections.Generic;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class DeckCycleStockTests
    {
        private TestCards cards;
        private DeckCycle deck;
        private int total;
        private int changes;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            deck = new DeckCycle();
            deck.Changed += () => changes++;
        }

        [TearDown] public void TearDown() => cards.DestroyAll();

        private void Init(List<RuntimeCardInstance> list, int seed = 42)
        {
            total = list.Count;
            deck.Initialize(list, new Random(seed));
            changes = 0;
        }

        private void AssertConserved() =>
            Assert.AreEqual(total, deck.CyclingCardCount + deck.BurnedCount + deck.DeletedThisCycleCount,
                "No card lost or duplicated.");

        private StockResult Stock(int slot) => deck.TryStock(slot, out _);

        // ---- stocking ----

        [Test]
        public void TryStock_MovesCardToStock_AndDrawsIntoSlot()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            var card = deck.Hand[0];

            Assert.AreEqual(StockResult.Stocked, Stock(0));

            Assert.AreSame(card, deck.Stock[0]);
            Assert.IsNotNull(deck.Hand[0]);
            Assert.AreNotSame(card, deck.Hand[0]);
            Assert.AreEqual(0, deck.DiscardCount);
            Assert.AreEqual(1, changes);
            AssertConserved();
        }

        [Test]
        public void StockedCards_StillCountAsCycling()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            Stock(0);
            Stock(1);
            Assert.AreEqual(6, deck.CyclingCardCount);
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void TryStock_InvalidSlot_IsRejected(int slot)
        {
            Init(cards.Numbered(1, 2, 3, 4));
            Assert.AreEqual(StockResult.Rejected, Stock(slot));
            Assert.AreEqual(0, deck.StockCount);
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void TryStock_EmptySlot_IsRejected()
        {
            Init(cards.Numbered(1, 2, 3));
            Stock(0); // queue empty, slot 0 stays empty
            Assert.IsNull(deck.Hand[0]);
            Assert.AreEqual(StockResult.Rejected, Stock(0));
            Assert.AreEqual(1, deck.StockCount);
        }

        [Test]
        public void TryStockParry_StocksZero_AndRefillsFromReserve()
        {
            var list = cards.Numbered(1, 2, 3, 4);
            var zeroA = cards.Zero();
            var zeroB = cards.Zero();
            list.Add(zeroA);
            list.Add(zeroB);
            Init(list);
            var first = deck.ParrySlot;

            Assert.AreEqual(StockResult.Stocked, deck.TryStockParry(out _));

            Assert.AreSame(first, deck.Stock[0]);
            Assert.IsNotNull(deck.ParrySlot);
            Assert.AreNotSame(first, deck.ParrySlot);
            Assert.AreEqual(0, deck.ZeroReserveCount);
            AssertConserved();
        }

        [Test]
        public void TryStockParry_EmptyParrySlot_IsRejected()
        {
            Init(cards.Numbered(1, 2, 3, 4));
            Assert.AreEqual(StockResult.Rejected, deck.TryStockParry(out _));
            Assert.AreEqual(0, changes);
        }

        // ---- firing ----

        [Test]
        public void ThirdCard_FiresComposedSleight()
        {
            Init(cards.Numbered(2, 4, 6, 8, 9, 1, 3));
            var a = deck.Hand[0];
            Stock(0);
            var b = deck.Hand[1];
            Stock(1);
            var c = deck.Hand[2];

            Assert.AreEqual(StockResult.Fired, deck.TryStock(2, out var sleight));

            int expected = a.GetEffectiveValue() + b.GetEffectiveValue() + c.GetEffectiveValue();
            Assert.AreEqual(expected, sleight.Value);
            Assert.AreEqual(c.GetEffectiveValue(), sleight.FinisherValue);
            Assert.AreEqual(0, deck.StockCount);
            AssertConserved();
        }

        [Test]
        public void Fire_BurnsSlot1_AndDiscardsTheOthers()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6, 7));
            var catalyst = deck.Hand[0];
            Stock(0);
            var second = deck.Hand[1];
            Stock(1);
            var third = deck.Hand[2];
            Stock(2);

            CollectionAssert.AreEqual(new[] { catalyst }, deck.BurnedPile);
            Assert.IsTrue(catalyst.IsBurnedThisFight);
            CollectionAssert.AreEquivalent(new[] { second, third }, deck.DiscardPile);
            Assert.AreEqual(6, deck.CyclingCardCount);
            AssertConserved();
        }

        [Test]
        public void Fire_WithFourCycling_BurnsDownToThree()
        {
            Init(cards.Numbered(1, 2, 3, 4));
            Stock(0);
            Stock(1);
            Stock(2);
            Assert.AreEqual(1, deck.BurnedCount);
            Assert.AreEqual(3, deck.CyclingCardCount);
            AssertConserved();
        }

        [Test]
        public void Fire_AtFloorOfThree_DoesNotBurn()
        {
            Init(cards.Numbered(1, 2, 3));
            var catalyst = deck.Hand[0];
            Stock(0);
            Stock(1);
            Assert.AreEqual(StockResult.Fired, Stock(2));

            Assert.AreEqual(0, deck.BurnedCount);
            Assert.IsFalse(catalyst.IsBurnedThisFight);
            Assert.AreEqual(3, deck.DiscardCount);
            AssertConserved();
        }

        [Test]
        public void RepeatedSleights_NeverThinBelowFloor()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6, 7, 8, 9));
            for (int round = 0; round < 20; round++)
            {
                if (deck.CanReload) deck.Reload();
                for (int s = 0; s < DeckCycle.HandSize; s++)
                    if (deck.Hand[s] != null) Stock(s);
                AssertConserved();
            }
            Assert.GreaterOrEqual(deck.CyclingCardCount, SleightRules.BurnFloor);
            Assert.AreEqual(9 - SleightRules.BurnFloor, deck.BurnedCount);
        }

        [Test]
        public void StockedZero_MirrorsAndIsBurnedAsCatalyst()
        {
            var list = cards.Numbered(3, 9, 5, 4, 2);
            var zero = cards.Zero();
            list.Add(zero);
            Init(list);

            deck.TryStockParry(out _);
            int best = Math.Max(deck.Hand[0].GetEffectiveValue(), deck.Hand[1].GetEffectiveValue());
            int sum = deck.Hand[0].GetEffectiveValue() + deck.Hand[1].GetEffectiveValue();
            Stock(0);
            Assert.AreEqual(StockResult.Fired, deck.TryStock(1, out var sleight));

            Assert.AreEqual(sum + best, sleight.Value);
            CollectionAssert.AreEqual(new[] { zero }, deck.BurnedPile);
            Assert.IsNull(deck.ParrySlot, "Burned 0 does not come back this room.");
            AssertConserved();
        }

        [Test]
        public void Reload_KeepsPartialStock()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5));
            Stock(0);
            deck.TryPlay(1, out _);
            deck.TryPlay(2, out _); // queue now empty
            var stocked = deck.Stock[0];

            Assert.IsTrue(deck.Reload());

            Assert.AreEqual(1, deck.StockCount);
            Assert.AreSame(stocked, deck.Stock[0]);
            AssertConserved();
        }

        // ---- restore (Overburn) ----

        [Test]
        public void RestoreBurned_MovesBurnedToDiscard_AndClearsFlag()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6, 7));
            var catalyst = deck.Hand[0];
            Stock(0);
            Stock(1);
            Stock(2);
            int discardBefore = deck.DiscardCount;
            changes = 0;

            Assert.AreEqual(1, deck.RestoreBurned());

            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(discardBefore + 1, deck.DiscardCount);
            CollectionAssert.Contains(deck.DiscardPile, catalyst);
            Assert.IsFalse(catalyst.IsBurnedThisFight);
            Assert.AreEqual(1, changes);
            AssertConserved();
        }

        [Test]
        public void RestoreBurned_NothingBurned_IsNoOp()
        {
            Init(cards.Numbered(1, 2, 3, 4));
            Assert.AreEqual(0, deck.RestoreBurned());
            Assert.AreEqual(0, changes);
        }

        // ---- interrupt (Card Break) ----

        [Test]
        public void Interrupt_OneCard_BurnsIt()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            var card = deck.Hand[0];
            Stock(0);
            changes = 0;

            Assert.IsTrue(deck.InterruptStock());

            CollectionAssert.AreEqual(new[] { card }, deck.BurnedPile);
            Assert.AreEqual(0, deck.StockCount);
            Assert.AreEqual(1, changes);
            AssertConserved();
        }

        [Test]
        public void Interrupt_TwoCards_BurnsSlot1_DiscardsSlot2()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            var first = deck.Hand[0];
            Stock(0);
            var second = deck.Hand[1];
            Stock(1);

            deck.InterruptStock();

            CollectionAssert.AreEqual(new[] { first }, deck.BurnedPile);
            CollectionAssert.AreEqual(new[] { second }, deck.DiscardPile);
            AssertConserved();
        }

        [Test]
        public void Interrupt_AtFloorOfThree_DiscardsEverything()
        {
            Init(cards.Numbered(1, 2, 3));
            Stock(0);
            Stock(1);

            deck.InterruptStock();

            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(2, deck.DiscardCount);
            AssertConserved();
        }

        [Test]
        public void Interrupt_EmptyStock_IsNoOp()
        {
            Init(cards.Numbered(1, 2, 3, 4));
            Assert.IsFalse(deck.InterruptStock());
            Assert.AreEqual(0, changes);
        }

        // ---- timeout ----

        [Test]
        public void Timer_BeforeTimeout_KeepsStock()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5));
            Stock(0);
            Assert.IsFalse(deck.AdvanceStockTimer(SleightRules.StockTimeoutSeconds - 0.1f));
            Assert.AreEqual(1, deck.StockCount);
        }

        [Test]
        public void Timer_AtTimeout_DiscardsStock_WithoutBurning()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            Stock(0);
            Stock(1);
            changes = 0;

            Assert.IsTrue(deck.AdvanceStockTimer(SleightRules.StockTimeoutSeconds));

            Assert.AreEqual(0, deck.StockCount);
            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(2, deck.DiscardCount);
            Assert.AreEqual(0f, deck.StockIdleSeconds);
            Assert.AreEqual(1, changes);
            AssertConserved();
        }

        [Test]
        public void Timer_ResetsWhenAnotherCardIsStocked()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5, 6));
            Stock(0);
            deck.AdvanceStockTimer(SleightRules.StockTimeoutSeconds - 1f);
            Stock(1);

            Assert.IsFalse(deck.AdvanceStockTimer(SleightRules.StockTimeoutSeconds - 1f));
            Assert.AreEqual(2, deck.StockCount);
        }

        [Test]
        public void Timer_IgnoresEmptyStock_AndNegativeDelta()
        {
            Init(cards.Numbered(1, 2, 3, 4, 5));
            Assert.IsFalse(deck.AdvanceStockTimer(100f));
            Stock(0);
            Assert.IsFalse(deck.AdvanceStockTimer(-100f));
            Assert.AreEqual(0f, deck.StockIdleSeconds);
            Assert.AreEqual(1, deck.StockCount);
        }

        [Test]
        public void Initialize_ClearsStock()
        {
            var list = cards.Numbered(1, 2, 3, 4, 5);
            Init(list);
            Stock(0);
            deck.AdvanceStockTimer(1f);

            Init(list);

            Assert.AreEqual(0, deck.StockCount);
            Assert.AreEqual(0f, deck.StockIdleSeconds);
            AssertConserved();
        }
    }
}
