using System;
using System.Collections.Generic;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class ActiveReloadTests
    {
        private TestCards cards;
        private DeckCycle deck;
        private ActiveReload reload;
        private List<ReloadGrade> completed;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            deck = new DeckCycle();
            deck.Initialize(cards.Numbered(1, 2, 3, 4, 5, 6), new Random(42));
            reload = new ActiveReload(deck);
            completed = new List<ReloadGrade>();
            reload.Completed += g => completed.Add(g);
        }

        [TearDown] public void TearDown() => cards.DestroyAll();

        /// <summary>Plays cards until the draw queue is empty, so a reload is allowed.</summary>
        private void EmptyDrawQueue()
        {
            while (deck.DrawCount > 0) deck.TryPlay(0, out _);
            Assert.IsTrue(deck.CanReload);
        }

        private void StartBar()
        {
            EmptyDrawQueue();
            Assert.IsTrue(reload.Begin());
        }

        // ---- grading ----

        [TestCase(0.60f, ReloadGrade.Perfect)]
        [TestCase(0.54f, ReloadGrade.Perfect)]
        [TestCase(0.66f, ReloadGrade.Perfect)]
        [TestCase(0.50f, ReloadGrade.Good)]
        [TestCase(0.45f, ReloadGrade.Good)]
        [TestCase(0.75f, ReloadGrade.Good)]
        [TestCase(0.44f, ReloadGrade.Miss)]
        [TestCase(0.76f, ReloadGrade.Miss)]
        [TestCase(0.00f, ReloadGrade.Miss)]
        [TestCase(1.20f, ReloadGrade.Miss)]
        public void Grade_ByDistanceFromCenter(float offset, ReloadGrade expected) =>
            Assert.AreEqual(expected, ActiveReloadRules.Grade(offset));

        [TestCase(-0.1f)]
        [TestCase(1.3f)]
        [TestCase(float.NaN)]
        public void Grade_OutsideBar_IsMiss(float offset) =>
            Assert.AreEqual(ReloadGrade.Miss, ActiveReloadRules.Grade(offset));

        // ---- begin ----

        [Test]
        public void Constructor_RejectsNullDeck() =>
            Assert.Throws<ArgumentNullException>(() => new ActiveReload(null));

        [Test]
        public void Begin_WithCardsStillInQueue_IsRejected()
        {
            Assert.IsFalse(reload.Begin());
            Assert.AreEqual(ReloadState.Idle, reload.State);
            Assert.IsFalse(reload.LocksCardPlays);
        }

        [Test]
        public void Begin_WhenReloadable_StartsBar_AndLocksCardPlays()
        {
            StartBar();
            Assert.AreEqual(ReloadState.Running, reload.State);
            Assert.AreEqual(0f, reload.Elapsed);
            Assert.IsTrue(reload.LocksCardPlays);
        }

        [Test]
        public void Begin_WhileRunning_DoesNotRestartBar()
        {
            StartBar();
            reload.Tick(0.3f);
            Assert.IsFalse(reload.Begin());
            Assert.AreEqual(0.3f, reload.Elapsed, 1e-5f);
        }

        // ---- no press ----

        [Test]
        public void NoPress_ReloadsWhenBarEnds()
        {
            StartBar();
            int discard = deck.DiscardCount;

            reload.Tick(1.1f);
            Assert.AreEqual(ReloadState.Running, reload.State);
            Assert.IsEmpty(completed);

            reload.Tick(0.2f);
            Assert.AreEqual(ReloadState.Idle, reload.State);
            CollectionAssert.AreEqual(new[] { ReloadGrade.None }, completed);
            Assert.AreEqual(0, deck.DiscardCount, "Discard was reshuffled.");
            Assert.AreEqual(discard, deck.DrawCount, "Hand was full, so every reshuffled card is in the queue.");
            Assert.AreEqual(1f, reload.SpeedMultiplier);
        }

        [Test]
        public void LargeTick_CompletesOnce()
        {
            StartBar();
            reload.Tick(10f);
            reload.Tick(10f);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(0f, reload.Elapsed);
        }

        [Test]
        public void Tick_IgnoresNonPositiveDelta()
        {
            StartBar();
            reload.Tick(0f);
            reload.Tick(-5f);
            reload.Tick(float.NaN);
            Assert.AreEqual(0f, reload.Elapsed);
        }

        [Test]
        public void Progress_FillsTheBar()
        {
            Assert.AreEqual(0f, reload.Progress01);
            StartBar();
            reload.Tick(0.3f);
            Assert.AreEqual(0.25f, reload.Progress01, 1e-5f);
        }

        // ---- press ----

        [Test]
        public void Press_Perfect_ReloadsInstantly_AndBoostsSpeed()
        {
            StartBar();
            reload.Tick(0.6f);

            Assert.AreEqual(ReloadGrade.Perfect, reload.Press());

            Assert.AreEqual(ReloadState.Idle, reload.State);
            CollectionAssert.AreEqual(new[] { ReloadGrade.Perfect }, completed);
            Assert.AreEqual(0, deck.DiscardCount);
            Assert.AreEqual(ActiveReloadRules.BoostSpeedMultiplier, reload.SpeedMultiplier);
            Assert.AreEqual(ActiveReloadRules.BoostSeconds, reload.BoostRemaining);
        }

        [Test]
        public void Press_Good_ReloadsInstantly_WithoutBoost()
        {
            StartBar();
            reload.Tick(0.5f);

            Assert.AreEqual(ReloadGrade.Good, reload.Press());

            CollectionAssert.AreEqual(new[] { ReloadGrade.Good }, completed);
            Assert.AreEqual(1f, reload.SpeedMultiplier);
        }

        [Test]
        public void Press_Miss_Jams_ThenReloadsAtJamTotal()
        {
            StartBar();
            reload.Tick(0.2f);

            Assert.AreEqual(ReloadGrade.Miss, reload.Press());
            Assert.AreEqual(ReloadState.Jammed, reload.State);
            Assert.IsTrue(reload.LocksCardPlays);

            reload.Tick(1.0f); // 1.2 s: past where a no-press reload would have finished
            Assert.AreEqual(ReloadState.Jammed, reload.State, "Jam takes longer than not pressing.");
            Assert.IsEmpty(completed);

            reload.Tick(0.9f); // 2.1 s
            Assert.AreEqual(ReloadState.Idle, reload.State);
            CollectionAssert.AreEqual(new[] { ReloadGrade.Miss }, completed);
            Assert.AreEqual(0, deck.DiscardCount);
        }

        [Test]
        public void Press_WhileJammed_IsIgnored()
        {
            StartBar();
            reload.Tick(0.2f);
            reload.Press();
            reload.Tick(0.4f); // now at the sweet spot, but jammed

            Assert.AreEqual(ReloadGrade.None, reload.Press());
            Assert.AreEqual(ReloadState.Jammed, reload.State);
            Assert.IsEmpty(completed);
        }

        [Test]
        public void Press_WhileIdle_IsIgnored()
        {
            Assert.AreEqual(ReloadGrade.None, reload.Press());
            Assert.IsEmpty(completed);
        }

        [Test]
        public void JamProgress_UsesJamDuration()
        {
            StartBar();
            reload.Tick(0.2f);
            reload.Press();
            reload.Tick(0.8f); // 1.0 of 2.0
            Assert.AreEqual(0.5f, reload.Progress01, 1e-5f);
        }

        // ---- boost ----

        [Test]
        public void Boost_ExpiresAfterDuration()
        {
            StartBar();
            reload.Tick(0.6f);
            reload.Press();

            reload.Tick(1.5f);
            Assert.AreEqual(ActiveReloadRules.BoostSpeedMultiplier, reload.SpeedMultiplier);
            reload.Tick(0.6f);
            Assert.AreEqual(1f, reload.SpeedMultiplier);
            Assert.AreEqual(0f, reload.BoostRemaining);
        }

        [Test]
        public void Boost_RefreshesInsteadOfStacking()
        {
            StartBar();
            reload.Tick(0.6f);
            reload.Press();
            reload.Tick(1.0f);

            StartBar(); // queue emptied again, second reload
            reload.Tick(0.6f);
            reload.Press();

            Assert.AreEqual(ActiveReloadRules.BoostSeconds, reload.BoostRemaining, 1e-5f);
        }

        // ---- cancel ----

        [Test]
        public void Cancel_StopsWithoutReloading()
        {
            StartBar();
            int discard = deck.DiscardCount;
            reload.Tick(0.4f);

            reload.Cancel();
            reload.Tick(5f);

            Assert.AreEqual(ReloadState.Idle, reload.State);
            Assert.IsEmpty(completed);
            Assert.AreEqual(discard, deck.DiscardCount);
            Assert.IsFalse(reload.LocksCardPlays);
        }
    }
}
