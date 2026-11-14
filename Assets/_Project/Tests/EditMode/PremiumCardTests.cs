using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class PremiumCardTests
    {
        private TestCards cards;
        private DeckCycle deck;
        private int total;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            deck = new DeckCycle();
        }

        [TearDown] public void TearDown() => cards.DestroyAll();

        private void Init(params int[] values)
        {
            var list = cards.Numbered(values);
            total = list.Count;
            deck.Initialize(list, new System.Random(7));
        }

        private void AssertConserved() =>
            Assert.AreEqual(total, deck.CyclingCardCount + deck.BurnedCount + deck.DeletedThisCycleCount,
                "No card lost or duplicated.");

        // ---- value ----

        [Test]
        public void Premium_AddsTwoToEffectiveValue()
        {
            var card = cards.Card(4);
            card.IsPremiumOverride = true;
            Assert.AreEqual(6, card.GetEffectiveValue());
        }

        [Test]
        public void Premium_ValueStaysClampedToNine()
        {
            var card = cards.Card(8);
            card.IsPremiumOverride = true;
            card.PermanentRunUpgradeMod = 1;
            Assert.AreEqual(CardValue.Max, card.GetEffectiveValue());
        }

        [Test]
        public void Premium_ZeroBreakerStaysZero()
        {
            var zero = cards.Zero();
            zero.IsPremiumOverride = true;
            Assert.AreEqual(0, zero.GetEffectiveValue());
        }

        [Test]
        public void Premium_AssetFlagAlsoCounts()
        {
            var data = CardData.Create("premium_asset", 3, premium: true);
            try
            {
                var card = new RuntimeCardInstance(data);
                Assert.IsTrue(card.IsPremium);
                Assert.AreEqual(5, card.GetEffectiveValue());
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }

        [Test]
        public void PremiumOverride_DoesNotTouchTheAsset()
        {
            var card = cards.Card(3);
            card.IsPremiumOverride = true;
            Assert.IsFalse(card.Data.IsPremium);
        }

        // ---- single play ----

        [Test]
        public void PlayingPremium_BurnsIt()
        {
            Init(2, 3, 4, 5, 6);
            var card = deck.Hand[0];
            card.IsPremiumOverride = true;

            Assert.IsTrue(deck.TryPlay(0, out var played));
            Assert.AreSame(card, played);
            CollectionAssert.Contains(deck.BurnedPile, card);
            CollectionAssert.DoesNotContain(deck.DiscardPile, card);
            Assert.IsTrue(card.IsBurnedThisFight);
            AssertConserved();
        }

        [Test]
        public void PlayingPremium_AtFloorOfThree_DiscardsInstead()
        {
            Init(2, 3, 4);
            var card = deck.Hand[0];
            card.IsPremiumOverride = true;

            deck.TryPlay(0, out _);
            Assert.AreEqual(0, deck.BurnedCount);
            CollectionAssert.Contains(deck.DiscardPile, card);
            AssertConserved();
        }

        [Test]
        public void PlayingNormalCard_StillDiscards()
        {
            Init(2, 3, 4, 5, 6);
            var card = deck.Hand[0];
            deck.TryPlay(0, out _);
            CollectionAssert.Contains(deck.DiscardPile, card);
            Assert.AreEqual(0, deck.BurnedCount);
        }

        // ---- Sleights ----

        /// <summary>Stocks the first hand card still present (small decks run out of draws and leave gaps).</summary>
        private RuntimeCardInstance StockFromSlot0(bool premium)
        {
            int slot = 0;
            while (deck.Hand[slot] == null) slot++;
            var card = deck.Hand[slot];
            card.IsPremiumOverride = premium;
            deck.TryStock(slot, out _);
            return card;
        }

        [Test]
        public void Sleight_PremiumInLaterSlots_BurnsThemToo()
        {
            Init(2, 3, 4, 5, 6, 7, 8);
            var first = StockFromSlot0(false);
            var second = StockFromSlot0(true);
            var third = StockFromSlot0(false);

            CollectionAssert.AreEquivalent(new[] { first, second }, deck.BurnedPile);
            CollectionAssert.Contains(deck.DiscardPile, third);
            AssertConserved();
        }

        [Test]
        public void Sleight_ManyPremiumBurns_StopAtFloorOfThree()
        {
            Init(2, 3, 4, 5); // 4 cycling: only one burn allowed
            var first = StockFromSlot0(true);
            var second = StockFromSlot0(true);
            var third = StockFromSlot0(true);

            CollectionAssert.AreEqual(new[] { first }, deck.BurnedPile);
            CollectionAssert.Contains(deck.DiscardPile, second);
            CollectionAssert.Contains(deck.DiscardPile, third);
            Assert.AreEqual(SleightRules.BurnFloor, deck.CyclingCardCount);
            AssertConserved();
        }

        [Test]
        public void Sleight_AllPremium_WithRoom_BurnsAllThree()
        {
            Init(2, 3, 4, 5, 6, 7);
            StockFromSlot0(true);
            StockFromSlot0(true);
            StockFromSlot0(true);

            Assert.AreEqual(3, deck.BurnedCount);
            Assert.AreEqual(3, deck.CyclingCardCount);
            AssertConserved();
        }

        [Test]
        public void Sleight_PremiumValueCountsTowardTotal()
        {
            Init(2, 2, 2, 2, 2);
            StockFromSlot0(true);
            StockFromSlot0(false);
            var card = deck.Hand[0];
            card.IsPremiumOverride = false;
            Assert.AreEqual(StockResult.Fired, deck.TryStock(0, out var sleight));
            Assert.AreEqual(4 + 2 + 2, sleight.Value);
        }
    }
}
