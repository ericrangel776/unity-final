using System;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class OverburnRulesTests
    {
        [TestCase(100, 0, 10)]
        [TestCase(100, 1, 20)]
        [TestCase(100, 2, 30)]
        [TestCase(25, 0, 3)]  // 2.5 -> 3
        [TestCase(25, 1, 5)]
        [TestCase(25, 2, 8)]  // 7.5 -> 8
        [TestCase(1, 0, 1)]
        public void Cost_EscalatesTenPercentOfMaxPerUse_RoundedUp(int maxHp, int uses, int expected) =>
            Assert.AreEqual(expected, OverburnRules.Cost(maxHp, uses));

        [Test]
        public void Cost_RejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OverburnRules.Cost(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => OverburnRules.Cost(100, -1));
        }

        [TestCase(11, 10, true)]
        [TestCase(10, 10, false)]
        [TestCase(5, 10, false)]
        public void CanAfford_IsNeverLethal(int hp, int cost, bool expected) =>
            Assert.AreEqual(expected, OverburnRules.CanAfford(hp, cost));
    }
}
