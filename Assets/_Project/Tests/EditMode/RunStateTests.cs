using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class RunStateTests
    {
        private TestCards cards;
        private List<CardData> pool;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            pool = cards.DataSet(6, Rarity.Common).Concat(cards.DataSet(3, Rarity.Rare)).ToList();
        }

        [TearDown] public void TearDown() => cards.DestroyAll();

        private RunState NewRun(int seed = 1) => new RunState(cards.DataSet(10), 100, seed);

        private static int IndexOf(IReadOnlyList<MapCard> offer, Func<MapCard, bool> match)
        {
            for (int i = 0; i < offer.Count; i++) if (match(offer[i])) return i;
            return -1;
        }

        [Test]
        public void NewRun_StartsAtDoorOne_FullHp_NoCurrency()
        {
            var run = NewRun();
            Assert.AreEqual(10, run.Deck.Count);
            Assert.AreEqual(100, run.Hp);
            Assert.AreEqual(0, run.Currency);
            Assert.AreEqual(1, run.RoomNumber);
            Assert.AreEqual(RunPhase.ChoosingDoor, run.Phase);
            Assert.AreEqual(0, run.Momentum.Stacks);
        }

        [Test]
        public void Constructor_RejectsBadInput()
        {
            Assert.Throws<ArgumentNullException>(() => new RunState(null, 100, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunState(new List<CardData>(), 0, 1));
        }

        [Test]
        public void DealOffer_IsStableUntilARoomIsEntered()
        {
            var run = NewRun();
            var first = run.DealOffer();
            Assert.AreSame(first, run.DealOffer());
        }

        [Test]
        public void EnterRoom_BeforeDeal_Throws() =>
            Assert.Throws<InvalidOperationException>(() => NewRun().EnterRoom(0));

        [TestCase(-1)]
        [TestCase(3)]
        public void EnterRoom_BadIndex_Throws(int index)
        {
            var run = NewRun();
            run.DealOffer();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.EnterRoom(index));
        }

        [Test]
        public void CompleteRoom_WhileChoosing_Throws() =>
            Assert.Throws<InvalidOperationException>(() => NewRun().CompleteRoom(pool));

        [Test]
        public void CombatClear_PaysCurrency_UpdatesMomentum_AdvancesRoom()
        {
            var run = NewRun();
            var offer = run.DealOffer();
            var room = run.EnterRoom(0);
            Assert.AreEqual(RunPhase.InRoom, run.Phase);

            int expectedCurrency = RewardRules.Currency(room, new Momentum());
            var reward = run.CompleteRoom(pool);

            Assert.IsNotNull(reward);
            Assert.AreEqual(expectedCurrency, reward.Currency);
            Assert.AreEqual(expectedCurrency, run.Currency);
            var expectedStreak = new Momentum();
            expectedStreak.RecordCombatClear(room.Number);
            Assert.AreEqual(expectedStreak.Stacks, run.Momentum.Stacks);
            Assert.AreEqual(2, run.RoomNumber);
            Assert.AreEqual(RunPhase.ChoosingDoor, run.Phase);
            CollectionAssert.AreEqual(new[] { offer[0] }, run.History);
        }

        [Test]
        public void Currency_UsesStreakHeldBeforeTheClear()
        {
            // Room 1 tops out at Intensity 5, so seed the streak directly and check the payout used it.
            var run = NewRun();
            run.Momentum.AddStack();
            run.DealOffer();
            var room = run.EnterRoom(0);
            var before = new Momentum();
            before.AddStack();
            Assert.AreEqual(RewardRules.Currency(room, before), run.CompleteRoom(pool).Currency);
        }

        [Test]
        public void BlueRoom_ReturnsNoReward_AndKeepsMomentum()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var run = NewRun(seed);
                run.DealOffer();
                run.EnterRoom(0);
                run.CompleteRoom(pool);
                var offer = run.DealOffer(); // room 2
                int blue = IndexOf(offer, c => c.Family == MapFamily.Blue);
                if (blue < 0) continue;

                int stacks = run.Momentum.Stacks;
                int currency = run.Currency;
                run.EnterRoom(blue);
                Assert.IsNull(run.CompleteRoom(null));
                Assert.AreEqual(stacks, run.Momentum.Stacks);
                Assert.AreEqual(currency, run.Currency);
                Assert.AreEqual(3, run.RoomNumber);
                return;
            }
            Assert.Fail("No seed offered a Blue card in room 2.");
        }

        [Test]
        public void FullFloor_FiveChosenRooms_ThenBoss_ThenComplete()
        {
            var run = NewRun(7);
            for (int room = 1; room <= 5; room++)
            {
                Assert.AreEqual(room, run.RoomNumber);
                Assert.AreEqual(3, run.DealOffer().Count);
                run.EnterRoom(0);
                run.CompleteRoom(pool);
            }

            Assert.IsTrue(run.IsBossRoom);
            var bossOffer = run.DealOffer();
            Assert.AreEqual(1, bossOffer.Count);
            Assert.AreEqual(MapCardKind.Boss, bossOffer[0].Kind);
            Assert.AreEqual(RunState.BossIntensity, bossOffer[0].Number);

            run.EnterRoom(0);
            Assert.IsNotNull(run.CompleteRoom(pool));
            Assert.AreEqual(RunPhase.Complete, run.Phase);
            Assert.AreEqual(6, run.History.Count);
            Assert.Throws<InvalidOperationException>(() => run.DealOffer());
        }

        [Test]
        public void PurpleIsAlwaysOfferedByRoomFour()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var run = NewRun(seed);
                for (int room = 1; room <= 4; room++)
                {
                    var offer = run.DealOffer();
                    int pick = IndexOf(offer, c => c.Kind != MapCardKind.EliteTrial);
                    run.EnterRoom(pick);
                    run.CompleteRoom(pool);
                }
                Assert.IsTrue(run.PurpleOffered, $"seed {seed}");
            }
        }

        [Test]
        public void AverageIntensity_IgnoresBlueRooms()
        {
            var run = NewRun();
            Assert.AreEqual(0f, run.AverageIntensity);
            var offer = run.DealOffer();
            var room = run.EnterRoom(0);
            run.CompleteRoom(pool);
            Assert.AreEqual(room.Number, run.AverageIntensity, 1e-5f);
        }

        [Test]
        public void AverageIntensity_LeavesOutTheFixedBossRoom()
        {
            var run = NewRun();
            var chosen = new List<int>();
            for (int room = 1; room < RunState.BossRoom; room++)
            {
                run.DealOffer();
                var entered = run.EnterRoom(0);
                if (entered.IsCombat) chosen.Add(entered.Number);
                run.CompleteRoom(pool);
            }
            Assume.That(chosen.Count, Is.GreaterThan(0), "seed 1 should pick at least one fight");
            float before = run.AverageIntensity;

            run.DealOffer();
            Assert.AreEqual(MapCardKind.Boss, run.EnterRoom(0).Kind);
            run.CompleteRoom(pool);

            Assert.AreEqual((float)chosen.Sum() / chosen.Count, run.AverageIntensity, 1e-5f);
            Assert.AreEqual(before, run.AverageIntensity, 1e-5f, "clearing the boss must not move the average");
        }

        // ---- resources ----

        [Test]
        public void TrySpend_NeverGoesNegative()
        {
            var run = NewRun();
            run.AddCurrency(20);
            Assert.IsFalse(run.TrySpend(21));
            Assert.AreEqual(20, run.Currency);
            Assert.IsTrue(run.TrySpend(20));
            Assert.AreEqual(0, run.Currency);
        }

        [Test]
        public void Currency_RejectsNegativeAmounts()
        {
            var run = NewRun();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.AddCurrency(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.TrySpend(-1));
        }

        [TestCase(-5, 0)]
        [TestCase(40, 40)]
        [TestCase(150, 100)]
        public void SetHp_Clamps(int hp, int expected)
        {
            var run = NewRun();
            run.SetHp(hp);
            Assert.AreEqual(expected, run.Hp);
        }

        [Test]
        public void AddCard_AppliesUpgrade_RemoveCardRemovesIt()
        {
            var run = NewRun();
            var card = run.AddCard(cards.Data(4), 1);
            Assert.AreEqual(5, card.GetEffectiveValue());
            Assert.IsTrue(run.RemoveCard(card));
            Assert.IsFalse(run.RemoveCard(card));
        }

        [Test]
        public void Relics_OwnedOnce()
        {
            var run = NewRun();
            Assert.IsTrue(run.AddRelic(RelicId.GildedQuill));
            Assert.IsFalse(run.AddRelic(RelicId.GildedQuill));
            Assert.IsTrue(run.HasRelic(RelicId.GildedQuill));
            Assert.IsFalse(run.HasRelic(RelicId.AshenSigil));
        }

        [Test]
        public void SameSeed_SameOffers()
        {
            CollectionAssert.AreEqual(NewRun(5).DealOffer(), NewRun(5).DealOffer());
        }

        // ---- debug offer (docs/specs/debug-menu.md) ----

        [Test]
        public void DebugSetOffer_ReplacesTheOffer_AndEnterRoomUsesIt()
        {
            var run = NewRun();
            run.DealOffer();
            var forced = new MapCard(MapCardKind.Hexfire, 7);
            run.DebugSetOffer(new[] { forced });

            CollectionAssert.AreEqual(new[] { forced }, run.CurrentOffer);
            Assert.AreEqual(forced, run.EnterRoom(0));
        }

        [Test]
        public void DebugSetOffer_DoesNotDrawFromTheRng()
        {
            var a = NewRun(9);
            var b = NewRun(9);
            a.DebugSetOffer(new[] { new MapCard(MapCardKind.Rest, 3) });
            Assert.AreEqual(a.Rng.Next(), b.Rng.Next());
        }

        [Test]
        public void DebugSetOffer_EliteMarksPurpleOffered()
        {
            var run = NewRun();
            run.DebugSetOffer(new[] { new MapCard(MapCardKind.EliteTrial, 5, EliteTrial.Hexer) });
            Assert.IsTrue(run.PurpleOffered);
        }

        [Test]
        public void DebugSetOffer_OnlyAtADoor()
        {
            var run = NewRun();
            run.DealOffer();
            run.EnterRoom(0);
            Assert.Throws<InvalidOperationException>(() =>
                run.DebugSetOffer(new[] { new MapCard(MapCardKind.Vanguard, 2) }));
        }

        [TestCase(0)]
        [TestCase(4)]
        public void DebugSetOffer_RejectsBadSizes(int n) =>
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NewRun().DebugSetOffer(Enumerable.Repeat(new MapCard(MapCardKind.Vanguard, 2), n).ToList()));

        [Test]
        public void DebugSetOffer_RejectsNull() =>
            Assert.Throws<ArgumentNullException>(() => NewRun().DebugSetOffer(null));
    }
}
