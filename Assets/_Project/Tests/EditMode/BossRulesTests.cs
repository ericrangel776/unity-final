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

        // ---- Natural policy ----

        private static BossTurnContext Turn(int soFar = 4, int since = 2, int hand = 3, int sum = 15, int zeros = 1,
            bool reloading = false, bool stunned = false) =>
            new BossTurnContext(soFar, hand, 1f, sum, since, zeros, reloading, stunned);

        [Test]
        public void Natural_BaseChanceIsLow() =>
            Assert.AreEqual(NaturalBossPolicy.BaseChance, NaturalBossPolicy.SleightChance(Turn()), 1e-5f);

        [Test]
        public void Natural_NeedsAFullHand() =>
            Assert.AreEqual(0f, NaturalBossPolicy.SleightChance(Turn(hand: 2, zeros: 0, reloading: true)));

        [Test]
        public void Natural_NeverTwiceInARow() =>
            Assert.AreEqual(0f, NaturalBossPolicy.SleightChance(Turn(soFar: 5, since: 0, zeros: 0, sum: 27)));

        [Test]
        public void Natural_FirstAttackOfTheFightMayBeASleight() =>
            Assert.Greater(NaturalBossPolicy.SleightChance(Turn(soFar: 0, since: 0)), 0f);

        [Test]
        public void Natural_PunishesAPlayerWithoutAZero() =>
            Assert.AreEqual(NaturalBossPolicy.BaseChance + NaturalBossPolicy.NoZeroBonus,
                NaturalBossPolicy.SleightChance(Turn(zeros: 0)), 1e-5f);

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Natural_PunishesReloadsAndStuns(bool reloading, bool stunned) =>
            Assert.AreEqual(NaturalBossPolicy.BaseChance + NaturalBossPolicy.OpeningBonus,
                NaturalBossPolicy.SleightChance(Turn(reloading: reloading, stunned: stunned)), 1e-5f);

        [TestCase(15, 0f)]
        [TestCase(20, 0.20f)]
        [TestCase(27, 0.30f)]  // capped
        [TestCase(9, 0f)]
        public void Natural_StrongHandsRaiseTheChance(int sum, float bonus) =>
            Assert.AreEqual(NaturalBossPolicy.BaseChance + bonus, NaturalBossPolicy.SleightChance(Turn(sum: sum)), 1e-5f);

        [Test]
        public void Natural_GetsImpatient() =>
            Assert.AreEqual(NaturalBossPolicy.BaseChance + 2 * NaturalBossPolicy.ImpatiencePerSwing,
                NaturalBossPolicy.SleightChance(Turn(soFar: 9, since: NaturalBossPolicy.PatienceSwings + 2)), 1e-5f);

        [Test]
        public void Natural_ChanceIsCapped() =>
            Assert.AreEqual(NaturalBossPolicy.MaxChance,
                NaturalBossPolicy.SleightChance(Turn(since: 20, soFar: 30, sum: 27, zeros: 0, reloading: true)), 1e-5f);

        [Test]
        public void Natural_RollsMatchTheChance()
        {
            var policy = new NaturalBossPolicy(new Random(12));
            var ctx = Turn(zeros: 0); // 50%
            int sleights = Enumerable.Range(0, 4000).Count(_ => policy.Next(ctx) == BossAction.Sleight);
            Assert.AreEqual(0.5, sleights / 4000.0, 0.03);
        }

        [Test]
        public void Natural_IsDeterministicPerSeed()
        {
            var a = new NaturalBossPolicy(new Random(3));
            var b = new NaturalBossPolicy(new Random(3));
            var ctx = Turn(zeros: 0);
            for (int i = 0; i < 50; i++) Assert.AreEqual(a.Next(ctx), b.Next(ctx));
        }

        [Test]
        public void Natural_RejectsNullRandom() =>
            Assert.Throws<ArgumentNullException>(() => new NaturalBossPolicy(null));

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
