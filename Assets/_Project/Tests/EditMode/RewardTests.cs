using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class RewardTests
    {
        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        private static MapCard Room(MapCardKind kind, int n) =>
            new MapCard(kind, n, kind == MapCardKind.EliteTrial ? EliteTrial.Hexer : EliteTrial.None);

        private static Momentum WithStacks(int stacks)
        {
            var m = new Momentum();
            for (int i = 0; i < stacks; i++) m.AddStack();
            return m;
        }

        private List<CardData> MixedPool(int each = 4)
        {
            var pool = new List<CardData>();
            pool.AddRange(cards.DataSet(each, Rarity.Common));
            pool.AddRange(cards.DataSet(each, Rarity.Uncommon));
            pool.AddRange(cards.DataSet(each, Rarity.Rare));
            return pool;
        }

        // ---- tiers ----

        [TestCase(1, RewardTier.Muted)]
        [TestCase(3, RewardTier.Muted)]
        [TestCase(4, RewardTier.Steady)]
        [TestCase(6, RewardTier.Steady)]
        [TestCase(7, RewardTier.Fierce)]
        [TestCase(8, RewardTier.Fierce)]
        [TestCase(9, RewardTier.Frantic)]
        public void Tier_FromIntensity(int n, RewardTier tier) => Assert.AreEqual(tier, RewardRules.Tier(n));

        [Test]
        public void Tiers_HarderNeverPaysLess()
        {
            var tiers = (RewardTier[])Enum.GetValues(typeof(RewardTier));
            for (int i = 1; i < tiers.Length; i++)
            {
                Assert.GreaterOrEqual(RewardRules.CurrencyPercent(tiers[i]), RewardRules.CurrencyPercent(tiers[i - 1]));
                Assert.GreaterOrEqual(RewardRules.DraftOptions(tiers[i]), RewardRules.DraftOptions(tiers[i - 1]));
                Assert.GreaterOrEqual(RewardRules.DraftPicks(tiers[i]), RewardRules.DraftPicks(tiers[i - 1]));
                Assert.GreaterOrEqual(RewardRules.DraftUpgrade(tiers[i]), RewardRules.DraftUpgrade(tiers[i - 1]));
            }
        }

        [TestCase(MapCardKind.Vanguard, 5, 0, 15)]
        [TestCase(MapCardKind.Hexfire, 2, 0, 9)]
        [TestCase(MapCardKind.KindledGround, 7, 0, 23)]  // 22.5 -> 23
        [TestCase(MapCardKind.Vanguard, 9, 0, 30)]
        [TestCase(MapCardKind.EliteTrial, 7, 0, 45)]
        [TestCase(MapCardKind.Boss, 7, 0, 75)]
        [TestCase(MapCardKind.BountyWind, 5, 0, 23)]     // 15 -> 22.5 -> 23
        [TestCase(MapCardKind.BountyWind, 8, 0, 40)]     // 15 -> 23 -> 40.25 -> 40
        [TestCase(MapCardKind.BountyWind, 8, 3, 52)]     // 40 x 1.3
        public void Currency_TierThenBountyThenMomentum(MapCardKind kind, int n, int stacks, int expected) =>
            Assert.AreEqual(expected, RewardRules.Currency(Room(kind, n), WithStacks(stacks)));

        [Test]
        public void Currency_RejectsBlueRoom() =>
            Assert.Throws<ArgumentException>(() => RewardRules.Currency(new MapCard(MapCardKind.Rest, 5), new Momentum()));

        [TestCase(MapCardKind.BountyWind, 2, 0)]
        [TestCase(MapCardKind.Vanguard, 5, 1)]
        [TestCase(MapCardKind.KindledGround, 7, 1)]
        [TestCase(MapCardKind.Vanguard, 7, 2)]
        [TestCase(MapCardKind.Hexfire, 9, 2)] // capped
        [TestCase(MapCardKind.EliteTrial, 9, 1)]
        public void RarityShift_TierPlusRed_Capped(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RewardRules.RarityShift(Room(kind, n)));

        [Test]
        public void RarityWeights_ShiftTowardRare()
        {
            CollectionAssert.AreEqual(new[] { 70, 25, 5 }, RewardRules.RarityWeights(0));
            CollectionAssert.AreEqual(new[] { 40, 45, 15 }, RewardRules.RarityWeights(1));
            CollectionAssert.AreEqual(new[] { 20, 50, 30 }, RewardRules.RarityWeights(2));
            CollectionAssert.AreEqual(new[] { 20, 50, 30 }, RewardRules.RarityWeights(5));
        }

        // ---- rolling cards ----

        [Test]
        public void RollCards_DistinctAndRequestedCount()
        {
            var pool = MixedPool();
            for (int seed = 0; seed < 50; seed++)
            {
                var roll = RewardRoller.RollCards(pool, 5, 1, new Random(seed));
                Assert.AreEqual(5, roll.Count);
                Assert.AreEqual(5, roll.Distinct().Count());
            }
        }

        [Test]
        public void RollCards_SmallPool_ReturnsWhatExists()
        {
            var pool = cards.DataSet(2);
            Assert.AreEqual(2, RewardRoller.RollCards(pool, 4, 0, new Random(1)).Count);
        }

        [Test]
        public void RollCards_EmptyPool_ReturnsEmpty() =>
            Assert.IsEmpty(RewardRoller.RollCards(new List<CardData>(), 3, 0, new Random(1)));

        [Test]
        public void RollCards_MissingRarity_FallsBackToOthers()
        {
            var pool = cards.DataSet(3, Rarity.Rare); // only Rares, but shift 0 mostly rolls Common
            var roll = RewardRoller.RollCards(pool, 3, 0, new Random(3));
            CollectionAssert.AreEquivalent(pool, roll);
        }

        [Test]
        public void RollCards_SameSeed_SameResult()
        {
            var pool = MixedPool();
            CollectionAssert.AreEqual(RewardRoller.RollCards(pool, 3, 1, new Random(9)),
                RewardRoller.RollCards(pool, 3, 1, new Random(9)));
        }

        [Test]
        public void RollCards_HigherShift_RollsMoreRares()
        {
            var pool = MixedPool(20);
            int RareCount(int shift)
            {
                var rng = new Random(11);
                int rares = 0;
                for (int i = 0; i < 300; i++)
                    rares += RewardRoller.RollCards(pool, 1, shift, rng).Count(c => c.Rarity == Rarity.Rare);
                return rares;
            }
            Assert.Greater(RareCount(2), RareCount(0));
        }

        [Test]
        public void RollCards_RejectsBadInput()
        {
            Assert.Throws<ArgumentNullException>(() => RewardRoller.RollCards(null, 1, 0, new Random()));
            Assert.Throws<ArgumentNullException>(() => RewardRoller.RollCards(new List<CardData>(), 1, 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => RewardRoller.RollCards(new List<CardData>(), -1, 0, new Random()));
        }

        // ---- combat reward ----

        [TestCase(2, 2, 1, 0)]
        [TestCase(5, 3, 1, 0)]
        [TestCase(7, 3, 1, 1)]
        [TestCase(9, 3, 2, 1)]
        public void CombatReward_ShapeFollowsTier(int n, int options, int picks, int upgrade)
        {
            var reward = RewardRoller.RollCombatReward(Room(MapCardKind.KindledGround, n), MixedPool(), new Momentum(), new Random(1));
            Assert.AreEqual(options, reward.Options.Count);
            Assert.AreEqual(picks, reward.PicksAllowed);
            Assert.AreEqual(upgrade, reward.DraftUpgrade);
        }

        [Test]
        public void CombatReward_ExtraOptionsAddCards()
        {
            var reward = RewardRoller.RollCombatReward(Room(MapCardKind.Vanguard, 5), MixedPool(), new Momentum(), new Random(1), 1);
            Assert.AreEqual(4, reward.Options.Count);
        }

        [Test]
        public void CombatReward_EliteAlwaysOffersRelic()
        {
            for (int seed = 0; seed < 20; seed++)
                Assert.IsTrue(RewardRoller.RollCombatReward(Room(MapCardKind.EliteTrial, 2), MixedPool(), new Momentum(), new Random(seed)).OffersRelic);
        }

        [Test]
        public void CombatReward_RelicChance_OnlyOnFrantic_AndNotAlways()
        {
            int relics = 0;
            for (int seed = 0; seed < 90; seed++)
            {
                Assert.IsFalse(RewardRoller.RollCombatReward(Room(MapCardKind.Vanguard, 8), MixedPool(), new Momentum(), new Random(seed)).OffersRelic);
                if (RewardRoller.RollCombatReward(Room(MapCardKind.Vanguard, 9), MixedPool(), new Momentum(), new Random(seed)).OffersRelic) relics++;
            }
            Assert.That(relics, Is.InRange(10, 50), "about 1 in 3");
        }

        [Test]
        public void CombatReward_BossNeverOffersRelic() =>
            Assert.IsFalse(RewardRoller.RollCombatReward(new MapCard(MapCardKind.Boss, 9), MixedPool(), new Momentum(), new Random(1)).OffersRelic);

        private RunState NewRun() => new RunState(cards.DataSet(10), 100, 1);

        [Test]
        public void Pick_AddsCardWithUpgrade_AndClosesAfterAllowedPicks()
        {
            var run = NewRun();
            var reward = new CombatReward(0, cards.DataSet(3), 1, 1, false);
            Assert.IsTrue(reward.TryPick(1, run));
            Assert.AreEqual(11, run.Deck.Count);
            Assert.AreSame(reward.Options[1], run.Deck[10].Data);
            Assert.AreEqual(1, run.Deck[10].PermanentRunUpgradeMod);
            Assert.IsTrue(reward.IsDraftClosed);
            Assert.IsFalse(reward.TryPick(0, run));
        }

        [Test]
        public void Pick_TwoPicks_CannotTakeSameCardTwice()
        {
            var run = NewRun();
            var reward = new CombatReward(0, cards.DataSet(3), 2, 0, false);
            Assert.IsTrue(reward.TryPick(0, run));
            Assert.IsFalse(reward.TryPick(0, run));
            Assert.IsTrue(reward.TryPick(2, run));
            Assert.IsTrue(reward.IsDraftClosed);
            Assert.AreEqual(12, run.Deck.Count);
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void Pick_BadIndex_Rejected(int index) =>
            Assert.IsFalse(new CombatReward(0, cards.DataSet(3), 1, 0, false).TryPick(index, NewRun()));

        [Test]
        public void Skip_BeforePicking_PaysBonus()
        {
            var run = NewRun();
            var reward = new CombatReward(0, cards.DataSet(3), 1, 0, false);
            Assert.IsTrue(reward.Skip(run));
            Assert.AreEqual(RewardRules.SkipBonus, run.Currency);
            Assert.AreEqual(10, run.Deck.Count);
            Assert.IsFalse(reward.Skip(run), "only once");
            Assert.AreEqual(RewardRules.SkipBonus, run.Currency);
        }

        [Test]
        public void Skip_AfterOnePickOfTwo_ClosesWithoutBonus()
        {
            var run = NewRun();
            var reward = new CombatReward(0, cards.DataSet(3), 2, 0, false);
            reward.TryPick(0, run);
            Assert.IsTrue(reward.Skip(run));
            Assert.AreEqual(0, run.Currency);
            Assert.IsTrue(reward.IsDraftClosed);
        }

        [Test]
        public void EmptyDraft_IsClosed_AndSkipPaysNothing()
        {
            var run = NewRun();
            var reward = new CombatReward(0, new List<CardData>(), 1, 0, false);
            Assert.IsTrue(reward.IsDraftClosed);
            Assert.IsFalse(reward.Skip(run));
            Assert.AreEqual(0, run.Currency);
        }
    }
}
