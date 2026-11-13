using System;
using System.Collections.Generic;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class OverburnReloadTests
    {
        private sealed class FakeHp : IHpPool
        {
            public int Hp { get; set; }
            public int MaxHp { get; set; }
            public readonly List<int> Spent = new List<int>();

            public FakeHp(int hp, int maxHp = 100)
            {
                Hp = hp;
                MaxHp = maxHp;
            }

            public void SpendHp(int amount)
            {
                Spent.Add(amount);
                Hp -= amount;
            }
        }

        private TestCards cards;
        private DeckCycle deck;
        private ActiveReload reload;
        private List<(ReloadGrade grade, bool overburn)> completed;
        private int total;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            deck = new DeckCycle();
            reload = new ActiveReload(deck, new Random(7));
            completed = new List<(ReloadGrade, bool)>();
            reload.Completed += g => completed.Add((g, reload.IsOverburn));
        }

        [TearDown] public void TearDown() => cards.DestroyAll();

        private void Init(params int[] values)
        {
            total = values.Length;
            deck.Initialize(cards.Numbered(values), new Random(42));
        }

        private void AssertConserved() =>
            Assert.AreEqual(total, deck.CyclingCardCount + deck.BurnedCount + deck.DeletedThisCycleCount,
                "No card lost or duplicated.");

        /// <summary>Fires one Sleight from the full hand (burns its catalyst while the deck is above the floor).</summary>
        private void FireSleight()
        {
            for (int s = 0; s < DeckCycle.HandSize; s++) deck.TryStock(s, out _);
        }

        private void EmptyDrawQueue()
        {
            while (deck.DrawCount > 0)
            {
                int slot = Array.FindIndex(new[] { deck.Hand[0], deck.Hand[1], deck.Hand[2] }, c => c != null);
                deck.TryPlay(slot, out _);
            }
        }

        /// <summary>7-card deck with one burned card and an empty draw queue.</summary>
        private void BurnOneAndEmptyQueue()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            FireSleight();
            EmptyDrawQueue();
            Assert.AreEqual(1, deck.BurnedCount);
            Assert.AreEqual(0, deck.DrawCount);
        }

        // ---- begin / refusals ----

        [Test]
        public void BeginOverburn_PaysTenPercent_AndStartsBar()
        {
            BurnOneAndEmptyQueue();
            var hp = new FakeHp(100);

            Assert.IsTrue(reload.BeginOverburn(hp));

            CollectionAssert.AreEqual(new[] { 10 }, hp.Spent);
            Assert.AreEqual(ReloadState.Running, reload.State);
            Assert.IsTrue(reload.IsOverburn);
            Assert.IsTrue(reload.LocksCardPlays);
            Assert.AreEqual(1, reload.OverburnUsesThisRoom);
        }

        [Test]
        public void BeginOverburn_WithCardsInQueue_IsRefused_AndFree()
        {
            Init(1, 2, 3, 4, 5, 6, 7, 8, 9);
            FireSleight();
            Assert.Greater(deck.DrawCount, 0);
            var hp = new FakeHp(100);

            Assert.IsFalse(reload.BeginOverburn(hp));
            Assert.IsEmpty(hp.Spent);
            Assert.AreEqual(ReloadState.Idle, reload.State);
        }

        [Test]
        public void BeginOverburn_NothingBurned_IsRefused()
        {
            Init(1, 2, 3, 4, 5, 6);
            EmptyDrawQueue();
            var hp = new FakeHp(100);

            Assert.IsFalse(reload.BeginOverburn(hp));
            Assert.IsEmpty(hp.Spent);
        }

        [TestCase(10)]
        [TestCase(4)]
        public void BeginOverburn_WhenItWouldBeLethal_IsRefused(int currentHp)
        {
            BurnOneAndEmptyQueue();
            var hp = new FakeHp(currentHp);

            Assert.IsFalse(reload.CanBeginOverburn(hp));
            Assert.IsFalse(reload.BeginOverburn(hp));
            Assert.IsEmpty(hp.Spent);
        }

        [Test]
        public void BeginOverburn_AtCostPlusOne_LeavesOneHp()
        {
            BurnOneAndEmptyQueue();
            var hp = new FakeHp(11);
            Assert.IsTrue(reload.BeginOverburn(hp));
            Assert.AreEqual(1, hp.Hp);
        }

        [Test]
        public void BeginOverburn_WhileReloading_IsRefused_AndFree()
        {
            BurnOneAndEmptyQueue();
            Assert.IsTrue(reload.Begin());
            var hp = new FakeHp(100);

            Assert.IsFalse(reload.BeginOverburn(hp));
            Assert.IsEmpty(hp.Spent);
            Assert.IsFalse(reload.IsOverburn);
        }

        [Test]
        public void BeginOverburn_RejectsNullPayer()
        {
            BurnOneAndEmptyQueue();
            Assert.Throws<ArgumentNullException>(() => reload.BeginOverburn(null));
        }

        // ---- completion ----

        [Test]
        public void Completion_RestoresBurned_AndReshufflesThem()
        {
            BurnOneAndEmptyQueue();
            var burnedCard = deck.BurnedPile[0];
            reload.BeginOverburn(new FakeHp(100));

            reload.Tick(10f);

            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(0, deck.DiscardCount);
            Assert.IsFalse(burnedCard.IsBurnedThisFight);
            Assert.AreEqual(total, deck.CyclingCardCount);
            CollectionAssert.AreEqual(new[] { (ReloadGrade.None, true) }, completed);
            Assert.IsFalse(reload.IsOverburn, "Cleared after the Completed handlers ran.");
            AssertConserved();
        }

        [Test]
        public void Completion_OnPerfect_RestoresInstantly_WithBoost()
        {
            BurnOneAndEmptyQueue();
            reload.BeginOverburn(new FakeHp(100));
            reload.Tick(reload.Target01 * reload.BarDuration);

            Assert.AreEqual(ReloadGrade.Perfect, reload.Press());

            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(ActiveReloadRules.BoostSpeedMultiplier, reload.SpeedMultiplier);
            AssertConserved();
        }

        [Test]
        public void Completion_AfterJam_StillRestores()
        {
            BurnOneAndEmptyQueue();
            reload.BeginOverburn(new FakeHp(100));
            reload.Tick(0.05f);
            Assert.AreEqual(ReloadGrade.Miss, reload.Press());

            reload.Tick(10f);

            Assert.AreEqual(0, deck.BurnedCount);
            CollectionAssert.AreEqual(new[] { (ReloadGrade.Miss, true) }, completed);
            AssertConserved();
        }

        [Test]
        public void OnlyBurnedCardsLeft_OverburnStillWorks()
        {
            // 4 cards: one Sleight burns 1, a normal reload pulls the rest into the hand,
            // leaving queue and discard empty with only the burned card outside.
            Init(1, 2, 3, 4);
            FireSleight();
            Assert.IsTrue(deck.Reload());
            Assert.AreEqual(0, deck.DrawCount);
            Assert.AreEqual(0, deck.DiscardCount);
            Assert.IsFalse(deck.CanReload, "A normal reload is impossible here.");
            Assert.IsFalse(reload.Begin());

            Assert.IsTrue(reload.BeginOverburn(new FakeHp(100)));
            reload.Tick(10f);

            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(1, deck.DrawCount, "The restored card is back in the queue.");
            AssertConserved();
        }

        [Test]
        public void NormalReload_DoesNotRestoreBurned()
        {
            BurnOneAndEmptyQueue();
            Assert.IsTrue(reload.Begin());
            reload.Tick(10f);

            Assert.AreEqual(1, deck.BurnedCount);
            CollectionAssert.AreEqual(new[] { (ReloadGrade.None, false) }, completed);
        }

        [Test]
        public void ThinToFloor_ThenOverburn_RestoresFullDeck()
        {
            Init(1, 2, 3, 4, 5, 6, 7, 8, 9);
            for (int round = 0; round < 20 && deck.CyclingCardCount > SleightRules.BurnFloor; round++)
            {
                if (deck.CanReload) deck.Reload();
                for (int s = 0; s < DeckCycle.HandSize; s++)
                    if (deck.Hand[s] != null) deck.TryStock(s, out _);
            }
            Assert.AreEqual(SleightRules.BurnFloor, deck.CyclingCardCount);
            EmptyDrawQueue();

            Assert.IsTrue(reload.BeginOverburn(new FakeHp(100)));
            reload.Tick(10f);

            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(total, deck.CyclingCardCount);
            AssertConserved();
        }

        // ---- cancel ----

        [Test]
        public void Cancel_RestoresNothing_AndDoesNotRefund()
        {
            BurnOneAndEmptyQueue();
            var hp = new FakeHp(100);
            reload.BeginOverburn(hp);

            reload.Cancel();
            reload.Tick(10f);

            Assert.AreEqual(1, deck.BurnedCount);
            Assert.AreEqual(90, hp.Hp);
            Assert.IsFalse(reload.IsOverburn);
            Assert.IsEmpty(completed);
        }

        // ---- escalation ----

        [Test]
        public void Cost_EscalatesPerUse_AndResets()
        {
            var hp = new FakeHp(100);
            BurnOneAndEmptyQueue();
            Assert.AreEqual(10, reload.NextOverburnCost(hp.MaxHp));

            reload.BeginOverburn(hp);
            reload.Tick(10f);
            Assert.AreEqual(20, reload.NextOverburnCost(hp.MaxHp));

            FireSleight();
            EmptyDrawQueue();
            Assert.IsTrue(reload.BeginOverburn(hp));
            reload.Tick(10f);
            CollectionAssert.AreEqual(new[] { 10, 20 }, hp.Spent);
            Assert.AreEqual(30, reload.NextOverburnCost(hp.MaxHp));

            reload.ResetOverburnCost();
            Assert.AreEqual(0, reload.OverburnUsesThisRoom);
            Assert.AreEqual(10, reload.NextOverburnCost(hp.MaxHp));
        }

        [Test]
        public void EscalatedCost_CanBecomeUnaffordable()
        {
            var hp = new FakeHp(25);
            BurnOneAndEmptyQueue();
            reload.BeginOverburn(hp); // pays 10 -> 15 HP
            reload.Tick(10f);
            FireSleight();
            EmptyDrawQueue();

            Assert.AreEqual(20, reload.NextOverburnCost(hp.MaxHp));
            Assert.IsFalse(reload.BeginOverburn(hp), "15 HP can't pay 20.");
            Assert.AreEqual(15, hp.Hp);
        }
    }
}
