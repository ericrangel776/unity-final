using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class HitStopTimerTests
    {
        [Test]
        public void Request_FreezesUntilDurationEnds()
        {
            var timer = new HitStopTimer();
            timer.Request(0.1f, now: 10f);

            Assert.IsTrue(timer.IsFrozen(10f));
            Assert.IsTrue(timer.IsFrozen(10.09f));
            Assert.IsFalse(timer.IsFrozen(10.11f));
        }

        [Test]
        public void NewTimer_IsNotFrozen()
        {
            var timer = new HitStopTimer();
            Assert.IsFalse(timer.IsFrozen(0f));
            Assert.AreEqual(0f, timer.Remaining(0f));
        }

        [Test]
        public void OverlappingShorterRequest_DoesNotShortenFreeze()
        {
            var timer = new HitStopTimer();
            timer.Request(0.2f, now: 0f);
            timer.Request(0.05f, now: 0.1f);

            Assert.IsTrue(timer.IsFrozen(0.19f));
        }

        [Test]
        public void OverlappingLongerRequest_ExtendsToLaterEnd_WithoutStacking()
        {
            var timer = new HitStopTimer();
            timer.Request(0.1f, now: 0f);
            timer.Request(0.1f, now: 0.05f);

            Assert.IsTrue(timer.IsFrozen(0.14f));
            Assert.IsFalse(timer.IsFrozen(0.16f), "Freeze ends at the later end time (0.15), not the sum (0.2).");
        }

        [Test]
        public void NonPositiveDuration_IsIgnored()
        {
            var timer = new HitStopTimer();
            timer.Request(0f, now: 0f);
            timer.Request(-1f, now: 0f);

            Assert.IsFalse(timer.IsFrozen(0f));
        }

        [Test]
        public void Clear_EndsFreeze()
        {
            var timer = new HitStopTimer();
            timer.Request(1f, now: 0f);
            timer.Clear();

            Assert.IsFalse(timer.IsFrozen(0.5f));
        }

        [Test]
        public void HitStopSeconds_TiersAreOrdered()
        {
            float neutral = CombatMath.HitStopSeconds(ClashOutcome.Neutral, false);
            float cardBreak = CombatMath.HitStopSeconds(ClashOutcome.AWins, false);
            float zeroBreak = CombatMath.HitStopSeconds(ClashOutcome.BWins, true);

            Assert.Greater(neutral, 0f);
            Assert.Greater(cardBreak, neutral);
            Assert.Greater(zeroBreak, cardBreak);
            Assert.AreEqual(0f, CombatMath.HitStopSeconds(ClashOutcome.None, false));
        }

        [Test]
        public void HitStopSeconds_EveryTier_FitsInsideInputBufferWindow()
        {
            Assert.Less(CombatMath.ZeroBreakHitStop, InputBuffer<int>.DefaultWindowSeconds);
        }

        [Test]
        public void PressMadeAtFreezeStart_SurvivesLongestFreeze()
        {
            var buffer = new InputBuffer<int>();
            var timer = new HitStopTimer();
            timer.Request(CombatMath.ZeroBreakHitStop, now: 5f);
            buffer.Enqueue(42, time: 5f);

            float freezeEnd = 5f + timer.Remaining(5f);
            Assert.IsTrue(buffer.TryDequeue(freezeEnd, out int action));
            Assert.AreEqual(42, action);
        }
    }
}
