using System;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class MomentumTests
    {
        private static Momentum WithStacks(int stacks)
        {
            var m = new Momentum();
            for (int i = 0; i < stacks; i++) m.RecordCombatClear(9);
            return m;
        }

        [Test]
        public void StartsAtZero() => Assert.AreEqual(0, new Momentum().Stacks);

        [TestCase(6)]
        [TestCase(7)]
        [TestCase(9)]
        public void HardClear_AddsStack(int intensity)
        {
            var m = WithStacks(1);
            m.RecordCombatClear(intensity);
            Assert.AreEqual(2, m.Stacks);
        }

        [Test]
        public void Stacks_CapAtThree()
        {
            var m = WithStacks(5);
            Assert.AreEqual(Momentum.MaxStacks, m.Stacks);
        }

        [TestCase(1)]
        [TestCase(3)]
        public void EasyClear_ResetsStreak(int intensity)
        {
            var m = WithStacks(3);
            m.RecordCombatClear(intensity);
            Assert.AreEqual(0, m.Stacks);
        }

        [TestCase(4)]
        [TestCase(5)]
        public void MidClear_LeavesStreakUnchanged(int intensity)
        {
            var m = WithStacks(2);
            m.RecordCombatClear(intensity);
            Assert.AreEqual(2, m.Stacks);
        }

        [TestCase(0)]
        [TestCase(10)]
        [TestCase(-1)]
        public void RecordCombatClear_RejectsOutOfRangeIntensity(int intensity) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new Momentum().RecordCombatClear(intensity));

        [TestCase(0, 15, 15)]
        [TestCase(1, 15, 17)] // 16.5 -> 17 (half up)
        [TestCase(2, 15, 18)]
        [TestCase(3, 15, 20)] // 19.5 -> 20
        [TestCase(3, 0, 0)]
        [TestCase(3, 100, 130)]
        public void CurrencyBonus_TenPercentPerStack_RoundedHalfUp(int stacks, int baseAmount, int expected) =>
            Assert.AreEqual(expected, WithStacks(stacks).ApplyCurrencyBonus(baseAmount));

        [Test]
        public void CurrencyBonus_RejectsNegative() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new Momentum().ApplyCurrencyBonus(-1));

        [TestCase(0, 4, 4)]
        [TestCase(2, 4, 6)]
        [TestCase(3, 8, 9)] // clamped to Max
        [TestCase(3, 9, 9)]
        [TestCase(0, 0, 1)] // clamped to Min
        public void QualityBonus_AddsStacks_Clamped(int stacks, int rolled, int expected) =>
            Assert.AreEqual(expected, WithStacks(stacks).ApplyQualityBonus(rolled));

        [Test]
        public void QualityBonus_DoesNotSpendStacks()
        {
            var m = WithStacks(2);
            m.ApplyQualityBonus(3);
            m.ApplyQualityBonus(3);
            Assert.AreEqual(2, m.Stacks);
        }

        [Test]
        public void Reset_ClearsStacks()
        {
            var m = WithStacks(3);
            m.Reset();
            Assert.AreEqual(0, m.Stacks);
        }
    }
}
