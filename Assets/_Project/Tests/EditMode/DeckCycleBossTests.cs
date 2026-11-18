using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>The M4 additions to <see cref="DeckCycle"/>: draw order, 9 spacing, FIFO play and Sleight deletes.</summary>
    public class DeckCycleBossTests
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

        private static readonly int[] Warden = { 4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 9, 9, 9 };

        private static bool IsNine(RuntimeCardInstance c) => c.GetEffectiveValue() == 9;

        [Test]
        public void DrawOrder_IsTheQueueNextCardFirst()
        {
            deck.Initialize(cards.Numbered(1, 2, 3, 4, 5, 6), new Random(3));
            var before = deck.DrawOrder.ToList();
            Assert.AreEqual(3, before.Count);

            deck.TryPlay(0, out _);
            Assert.AreSame(before[0], deck.Hand[0], "the first queued card is the one drawn next");
            CollectionAssert.AreEqual(before.Skip(1), deck.DrawOrder);
        }

        [Test]
        public void OldestSlot_PlaysInDrawOrder()
        {
            deck.Initialize(cards.Numbered(1, 2, 3, 4, 5, 6, 7), new Random(9));
            var expected = deck.Hand.Concat(deck.DrawOrder).ToList();

            var played = new List<RuntimeCardInstance>();
            while (deck.OldestSlot >= 0)
            {
                Assert.IsTrue(deck.TryPlay(deck.OldestSlot, out var card));
                played.Add(card);
            }
            CollectionAssert.AreEqual(expected, played);
        }

        /// <summary>
        /// The boss plays oldest-first and reloads when its hand is empty: over many seeds and several cycles,
        /// two 9s are never played back to back, including across reloads.
        /// </summary>
        [Test]
        public void SpacingRule_NoBackToBackNinesAcrossReloads()
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                deck = new DeckCycle { SpacingRule = IsNine };
                deck.Initialize(cards.Numbered(Warden), new Random(seed));

                RuntimeCardInstance previous = null;
                int nines = 0;
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    while (deck.OldestSlot >= 0)
                    {
                        deck.TryPlay(deck.OldestSlot, out var card);
                        if (IsNine(card)) nines++;
                        Assert.IsFalse(previous != null && IsNine(previous) && IsNine(card),
                            $"seed {seed}, cycle {cycle}: 9s back to back");
                        previous = card;
                    }
                    Assert.IsTrue(deck.Reload());
                }
                Assert.AreEqual(9, nines);
            }
        }

        [Test]
        public void SpacingRule_Null_IsThePlainShuffle()
        {
            var list = cards.Numbered(Warden);
            var plain = new DeckCycle();
            var spaced = new DeckCycle { SpacingRule = c => false };
            plain.Initialize(list, new Random(4));
            spaced.Initialize(list, new Random(4));
            CollectionAssert.AreEqual(plain.Hand.Concat(plain.DrawOrder), spaced.Hand.Concat(spaced.DrawOrder));
        }

        [Test]
        public void DeleteSleight_TakesTheBurnedSlotOneBackAsADelete()
        {
            deck.Initialize(cards.Numbered(5, 6, 7, 2, 3, 4, 8), new Random(1));
            var stocked = new List<RuntimeCardInstance>();
            for (int i = 0; i < 3; i++)
            {
                stocked.Add(deck.Hand[0]);
                deck.TryStock(0, out _);
            }
            Assert.AreEqual(1, deck.BurnedCount, "slot 1 burned at fire");
            Assert.IsTrue(stocked[0].IsBurnedThisFight);

            Assert.AreEqual(3, deck.DeleteSleight(stocked));
            Assert.AreEqual(3, deck.DeletedThisCycleCount);
            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(0, deck.DiscardCount);
            Assert.IsFalse(stocked[0].IsBurnedThisFight);

            // They come back on the next reload.
            while (deck.OldestSlot >= 0) deck.TryPlay(deck.OldestSlot, out _);
            Assert.IsTrue(deck.Reload());
            Assert.AreEqual(7, deck.CyclingCardCount);
        }

        [Test]
        public void DeleteSleight_SkipsUnknownAndRepeatedCards()
        {
            deck.Initialize(cards.Numbered(5, 6, 7, 2), new Random(1));
            deck.TryPlay(0, out var played);
            var stranger = cards.Card(4);
            Assert.AreEqual(1, deck.DeleteSleight(new[] { played, played, stranger, null }));
            Assert.AreEqual(0, deck.DeleteSleight(null));
        }
    }
}
