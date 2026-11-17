using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class BossRulesTests
    {
        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        [TestCase(0, BossCycleEnd.ReloadWindow)]
        [TestCase(3, BossCycleEnd.ReloadWindow)]
        [TestCase(4, BossCycleEnd.Exhausted)]
        [TestCase(15, BossCycleEnd.Exhausted)]
        public void CycleEnd_ExhaustedFromFourDeletes(int deleted, BossCycleEnd expected) =>
            Assert.AreEqual(expected, BossRules.CycleEnd(deleted));

        [TestCase(10, 50, 10)]
        [TestCase(60, 50, 49)]
        [TestCase(5, 1, 0)]
        [TestCase(5, 0, 0)]
        [TestCase(-3, 50, 0)]
        public void NonLethalDamage_LeavesAtLeastOneHp(int damage, int hp, int expected) =>
            Assert.AreEqual(expected, BossRules.NonLethalDamage(damage, hp));

        [Test]
        public void Pattern_EveryThirdAttackIsASleight()
        {
            var policy = new PatternBossPolicy();
            var actions = Enumerable.Range(0, 9)
                .Select(n => policy.Next(new BossTurnContext(n, 3, 1f))).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                BossAction.Swing, BossAction.Swing, BossAction.Sleight,
                BossAction.Swing, BossAction.Swing, BossAction.Sleight,
                BossAction.Swing, BossAction.Swing, BossAction.Sleight,
            }, actions);
        }

        [TestCase(2)]
        [TestCase(0)]
        public void Pattern_SwingsWhenTheHandIsNotFull(int handCount) =>
            Assert.AreEqual(BossAction.Swing, new PatternBossPolicy().Next(new BossTurnContext(2, handCount, 1f)));

        [Test]
        public void Pattern_RejectsZeroInterval() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new PatternBossPolicy(0));

        private static bool IsNine(RuntimeCardInstance c) => c.GetEffectiveValue() == 9;

        [Test]
        public void SpreadApart_NeverPutsTwoMarkedCardsTogether([Values(false, true)] bool previousWasNine)
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                var order = cards.Numbered(4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 9, 9, 9);
                var rng = new Random(seed);
                CardSpacing.SpreadApart(order, IsNine, previousWasNine, rng);

                Assert.AreEqual(15, order.Count);
                Assert.AreEqual(3, order.Count(IsNine));
                if (previousWasNine) Assert.IsFalse(IsNine(order[0]), $"seed {seed}: starts with a 9 after a 9");
                for (int i = 1; i < order.Count; i++)
                    Assert.IsFalse(IsNine(order[i]) && IsNine(order[i - 1]), $"seed {seed}: 9s back to back at {i}");
            }
        }

        [Test]
        public void SpreadApart_KeepsEveryCardAndIsDeterministic()
        {
            var original = cards.Numbered(9, 9, 2, 3, 4, 5);
            var a = new List<RuntimeCardInstance>(original);
            var b = new List<RuntimeCardInstance>(original);
            CardSpacing.SpreadApart(a, IsNine, false, new Random(5));
            CardSpacing.SpreadApart(b, IsNine, false, new Random(5));
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreEquivalent(original, a);
        }

        [Test]
        public void SpreadApart_TooManyMarked_IsBestEffort()
        {
            var order = cards.Numbered(9, 9, 9, 9, 1);
            Assert.DoesNotThrow(() => CardSpacing.SpreadApart(order, IsNine, false, new Random(1)));
            Assert.AreEqual(5, order.Count);
        }

        [Test]
        public void SpreadApart_SpreadsTheNinesAround()
        {
            // Not always in the same gaps: the first card is a 9 for some seeds and not for others.
            var firsts = new HashSet<bool>();
            for (int seed = 0; seed < 50; seed++)
            {
                var order = cards.Numbered(9, 1, 2, 3, 4);
                CardSpacing.SpreadApart(order, IsNine, false, new Random(seed));
                firsts.Add(IsNine(order[0]));
            }
            Assert.AreEqual(2, firsts.Count);
        }
    }
}
