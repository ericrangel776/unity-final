using System;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class DeadlockRulesTests
    {
        private const float T = DeadlockRules.TargetTime;

        [Test]
        public void Grade_OnTarget_IsPerfect()
        {
            Assert.AreEqual(DeadlockGrade.Perfect, DeadlockRules.Grade(T));
            Assert.AreEqual(DeadlockGrade.Perfect, DeadlockRules.Grade(T - 0.05f));
            Assert.AreEqual(DeadlockGrade.Perfect, DeadlockRules.Grade(T + 0.05f));
        }

        [Test]
        public void Grade_SlightlyOff_IsGood()
        {
            Assert.AreEqual(DeadlockGrade.Good, DeadlockRules.Grade(T - 0.1f));
            Assert.AreEqual(DeadlockGrade.Good, DeadlockRules.Grade(T + 0.1f));
        }

        [Test]
        public void Grade_FarOff_IsMiss()
        {
            Assert.AreEqual(DeadlockGrade.Miss, DeadlockRules.Grade(T - 0.3f));
            Assert.AreEqual(DeadlockGrade.Miss, DeadlockRules.Grade(0f));
        }

        [Test]
        public void Grade_NoPressOrOutsideWindow_IsMiss()
        {
            Assert.AreEqual(DeadlockGrade.Miss, DeadlockRules.Grade(float.NaN));
            Assert.AreEqual(DeadlockGrade.Miss, DeadlockRules.Grade(-0.1f));
            Assert.AreEqual(DeadlockGrade.Miss, DeadlockRules.Grade(DeadlockRules.WindowSeconds + 0.1f));
        }

        [Test]
        public void Power_IsBonusPlusCommittedCard()
        {
            Assert.AreEqual(3, DeadlockRules.Power(DeadlockGrade.Perfect, 0), "Poke: timing only.");
            Assert.AreEqual(10, DeadlockRules.Power(DeadlockGrade.Perfect, 7));
            Assert.AreEqual(7, DeadlockRules.Power(DeadlockGrade.Miss, 7), "A committed card still counts on a miss.");
        }

        [Test]
        public void Power_CommittedValue_IsClamped()
        {
            Assert.AreEqual(1 + CardValue.Max, DeadlockRules.Power(DeadlockGrade.Good, 42));
        }

        [Test]
        public void Resolve_HigherPowerWins_EqualIsNeutral()
        {
            Assert.AreEqual(ClashOutcome.AWins, DeadlockRules.Resolve(5, 4));
            Assert.AreEqual(ClashOutcome.BWins, DeadlockRules.Resolve(1, 3));
            Assert.AreEqual(ClashOutcome.Neutral, DeadlockRules.Resolve(3, 3));
        }

        [Test]
        public void RollEnemy_IsDeterministicForASeed()
        {
            var a = DeadlockRules.RollEnemy(new Random(1234));
            var b = DeadlockRules.RollEnemy(new Random(1234));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void RollEnemy_ProducesEveryGradeAndBothCommitChoices()
        {
            var rng = new Random(7);
            bool miss = false, good = false, perfect = false, commit = false, noCommit = false;
            for (int i = 0; i < 500; i++)
            {
                var (grade, c) = DeadlockRules.RollEnemy(rng);
                miss |= grade == DeadlockGrade.Miss;
                good |= grade == DeadlockGrade.Good;
                perfect |= grade == DeadlockGrade.Perfect;
                commit |= c;
                noCommit |= !c;
            }
            Assert.IsTrue(miss && good && perfect && commit && noCommit);
        }

        [Test]
        public void RollEnemy_NullRandom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DeadlockRules.RollEnemy(null));
        }

        [Test]
        public void SlotIndex_MapsChoices()
        {
            Assert.AreEqual(-1, DeadlockRules.SlotIndex(DeadlockChoice.Poke));
            Assert.AreEqual(0, DeadlockRules.SlotIndex(DeadlockChoice.Slot1));
            Assert.AreEqual(2, DeadlockRules.SlotIndex(DeadlockChoice.Slot3));
        }

        [Test]
        public void Window_FitsTargetAndTolerances()
        {
            Assert.Less(DeadlockRules.TargetTime + DeadlockRules.GoodTolerance, DeadlockRules.WindowSeconds);
            Assert.Greater(DeadlockRules.TargetTime - DeadlockRules.GoodTolerance, 0f);
        }
    }
}
