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
            reload = new ActiveReload(deck, new Random(7));
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

        /// <summary>Advances the running bar from 0 to <paramref name="progress01"/> of its rolled duration.</summary>
        private void TickToProgress(float progress01) => reload.Tick(progress01 * reload.BarDuration);

        // ---- grading (normalized to the bar) ----

        [TestCase(0.60f, ReloadGrade.Perfect)]
        [TestCase(0.55f, ReloadGrade.Perfect)]
        [TestCase(0.65f, ReloadGrade.Perfect)]
        [TestCase(0.52f, ReloadGrade.Good)]
        [TestCase(0.475f, ReloadGrade.Good)]
        [TestCase(0.725f, ReloadGrade.Good)]
        [TestCase(0.46f, ReloadGrade.Miss)]
        [TestCase(0.74f, ReloadGrade.Miss)]
        [TestCase(0.00f, ReloadGrade.Miss)]
        [TestCase(1.00f, ReloadGrade.Miss)]
        public void Grade_ByDistanceFromTarget(float progress, ReloadGrade expected) =>
            Assert.AreEqual(expected, ActiveReloadRules.Grade(progress, 0.6f));

        [Test]
        public void Grade_FollowsTheTarget()
        {
            Assert.AreEqual(ReloadGrade.Perfect, ActiveReloadRules.Grade(0.85f, 0.85f));
            Assert.AreEqual(ReloadGrade.Miss, ActiveReloadRules.Grade(0.6f, 0.85f));
        }

        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        public void Grade_OutsideBar_IsMiss(float progress) =>
            Assert.AreEqual(ReloadGrade.Miss, ActiveReloadRules.Grade(progress, 0.6f));

        [Test]
        public void Zones_StayInsideBar_ForEveryTarget()
        {
            Assert.GreaterOrEqual(ActiveReloadRules.MinTarget01 - ActiveReloadRules.GoodTolerance01, 0f);
            Assert.LessOrEqual(ActiveReloadRules.MaxTarget01 + ActiveReloadRules.GoodTolerance01, 1f);
        }

        // ---- construction / begin ----

        [Test]
        public void Constructor_RejectsNulls()
        {
            Assert.Throws<ArgumentNullException>(() => new ActiveReload(null, new Random(0)));
            Assert.Throws<ArgumentNullException>(() => new ActiveReload(deck, null));
        }

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
        public void Begin_WhileRunning_DoesNotRestartOrReroll()
        {
            StartBar();
            float duration = reload.BarDuration, target = reload.Target01;
            var anchor = reload.Anchor;
            reload.Tick(0.3f);

            Assert.IsFalse(reload.Begin());

            Assert.AreEqual(0.3f, reload.Elapsed, 1e-5f);
            Assert.AreEqual(duration, reload.BarDuration);
            Assert.AreEqual(target, reload.Target01);
            Assert.AreEqual(anchor, reload.Anchor);
        }

        // ---- randomized rolls ----

        /// <summary>Runs <paramref name="count"/> reloads to completion, recording each bar's rolls.</summary>
        private List<(float duration, float target, ReloadPromptAnchor anchor)> RollMany(int count)
        {
            var rolls = new List<(float, float, ReloadPromptAnchor)>();
            for (int i = 0; i < count; i++)
            {
                StartBar();
                rolls.Add((reload.BarDuration, reload.Target01, reload.Anchor));
                reload.Tick(10f);
            }
            return rolls;
        }

        [Test]
        public void Rolls_StayInRange_AndCoverIt()
        {
            var rolls = RollMany(200);
            float minD = float.MaxValue, maxD = float.MinValue, minT = float.MaxValue, maxT = float.MinValue;
            foreach (var (d, t, _) in rolls)
            {
                Assert.That(d, Is.InRange(ActiveReloadRules.MinBarSeconds, ActiveReloadRules.MaxBarSeconds));
                Assert.That(t, Is.InRange(ActiveReloadRules.MinTarget01, ActiveReloadRules.MaxTarget01));
                minD = Math.Min(minD, d); maxD = Math.Max(maxD, d);
                minT = Math.Min(minT, t); maxT = Math.Max(maxT, t);
            }
            Assert.Greater(maxD - minD, 0.8f * (ActiveReloadRules.MaxBarSeconds - ActiveReloadRules.MinBarSeconds),
                "Bar speed actually varies.");
            Assert.Greater(maxT - minT, 0.8f * (ActiveReloadRules.MaxTarget01 - ActiveReloadRules.MinTarget01),
                "Sweet spot actually moves.");
        }

        [Test]
        public void Anchor_NeverRepeatsBackToBack_AndUsesEveryAnchor()
        {
            var rolls = RollMany(200);
            var seen = new HashSet<ReloadPromptAnchor>();
            for (int i = 0; i < rolls.Count; i++)
            {
                seen.Add(rolls[i].anchor);
                if (i > 0) Assert.AreNotEqual(rolls[i - 1].anchor, rolls[i].anchor, $"Repeat at reload {i}.");
            }
            Assert.AreEqual(ActiveReloadRules.AnchorCount, seen.Count);
        }

        [Test]
        public void Anchor_StillMovesAfterCancel()
        {
            StartBar();
            var first = reload.Anchor;
            reload.Cancel();
            Assert.IsTrue(reload.Begin());
            Assert.AreNotEqual(first, reload.Anchor);
        }

        [Test]
        public void SameSeed_GivesSameRolls()
        {
            var otherDeck = new DeckCycle();
            otherDeck.Initialize(cards.Numbered(1, 2, 3, 4, 5, 6), new Random(42));
            var other = new ActiveReload(otherDeck, new Random(7));
            while (otherDeck.DrawCount > 0) otherDeck.TryPlay(0, out _);

            StartBar();
            Assert.IsTrue(other.Begin());

            Assert.AreEqual(reload.BarDuration, other.BarDuration);
            Assert.AreEqual(reload.Target01, other.Target01);
            Assert.AreEqual(reload.Anchor, other.Anchor);
        }

        // ---- no press ----

        [Test]
        public void NoPress_ReloadsWhenBarEnds()
        {
            StartBar();
            int discard = deck.DiscardCount;

            reload.Tick(reload.BarDuration - 0.05f);
            Assert.AreEqual(ReloadState.Running, reload.State);
            Assert.IsEmpty(completed);

            reload.Tick(0.1f);
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
            TickToProgress(0.25f);
            Assert.AreEqual(0.25f, reload.Progress01, 1e-4f);
        }

        // ---- press ----

        [Test]
        public void Press_Perfect_ReloadsInstantly_AndBoostsSpeed()
        {
            StartBar();
            TickToProgress(reload.Target01);

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
            TickToProgress(reload.Target01 + 0.09f);

            Assert.AreEqual(ReloadGrade.Good, reload.Press());

            CollectionAssert.AreEqual(new[] { ReloadGrade.Good }, completed);
            Assert.AreEqual(1f, reload.SpeedMultiplier);
        }

        [Test]
        public void Press_AtOldFixedCenter_MissesWhenTargetIsFar()
        {
            // Seed-independent: pick a reload whose target is far from 0.6 and press at 0.6.
            for (int i = 0; i < 50; i++)
            {
                StartBar();
                if (Math.Abs(reload.Target01 - 0.6f) > ActiveReloadRules.GoodTolerance01 + 0.01f)
                {
                    TickToProgress(0.6f);
                    Assert.AreEqual(ReloadGrade.Miss, reload.Press(), "Muscle memory for a fixed spot no longer works.");
                    return;
                }
                reload.Tick(10f);
            }
            Assert.Fail("No far-from-center target rolled in 50 reloads.");
        }

        [Test]
        public void Press_Miss_Jams_ThenReloadsAfterJamExtra()
        {
            StartBar();
            TickToProgress(0.1f);

            Assert.AreEqual(ReloadGrade.Miss, reload.Press());
            Assert.AreEqual(ReloadState.Jammed, reload.State);
            Assert.IsTrue(reload.LocksCardPlays);

            reload.Tick(reload.BarDuration); // well past where a no-press reload would have finished
            Assert.AreEqual(ReloadState.Jammed, reload.State, "Jam takes longer than not pressing.");
            Assert.IsEmpty(completed);

            reload.Tick(ActiveReloadRules.JamExtraSeconds);
            Assert.AreEqual(ReloadState.Idle, reload.State);
            CollectionAssert.AreEqual(new[] { ReloadGrade.Miss }, completed);
            Assert.AreEqual(0, deck.DiscardCount);
        }

        [Test]
        public void Press_WhileJammed_IsIgnored()
        {
            StartBar();
            TickToProgress(0.1f);
            reload.Press();
            TickToProgress(reload.Target01 - 0.1f); // now at the sweet spot, but jammed

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
            reload.Tick(0.05f);
            reload.Press(); // 0.05 s is under 7% of any bar: always a miss
            reload.Tick(reload.JamTotalSeconds / 2f - 0.05f);
            Assert.AreEqual(0.5f, reload.Progress01, 1e-4f);
        }

        // ---- boost ----

        [Test]
        public void Boost_ExpiresAfterDuration()
        {
            StartBar();
            TickToProgress(reload.Target01);
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
            TickToProgress(reload.Target01);
            reload.Press();
            reload.Tick(1.0f);

            StartBar(); // queue emptied again, second reload
            TickToProgress(reload.Target01);
            reload.Press();

            Assert.AreEqual(ActiveReloadRules.BoostSeconds, reload.BoostRemaining, 1e-5f);
        }

        [Test]
        public void ResetBoost_EndsBoostImmediately()
        {
            StartBar();
            TickToProgress(reload.Target01);
            reload.Press();

            reload.ResetBoost();

            Assert.AreEqual(0f, reload.BoostRemaining);
            Assert.AreEqual(1f, reload.SpeedMultiplier);
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
