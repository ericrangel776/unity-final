using System;
using System.Collections.Generic;
using DeckArpg.Core;
using NUnit.Framework;
using UnityEngine;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Heal rework Core rules (docs/specs/heal-rework.md, step 1).</summary>
    public class HealRulesTests
    {
        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        private static List<RuntimeCardInstance> Of(params RuntimeCardInstance[] c) => new List<RuntimeCardInstance>(c);
        private RunState NewRun(int maxHp = 100) => new RunState(cards.DataSet(10), maxHp, 1);

        // ---- amounts ----

        [TestCase(1, 3)]
        [TestCase(3, 9)]
        [TestCase(5, 15)]
        [TestCase(9, 27)]
        public void Amount_IsThreePerValue(int value, int heal) => Assert.AreEqual(heal, HealRules.Amount(value));

        [TestCase(0)]
        [TestCase(-4)]
        public void Amount_ZeroOrNegative_IsZero(int value) => Assert.AreEqual(0, HealRules.Amount(value));

        [Test]
        public void Constants_MatchSpec()
        {
            Assert.AreEqual(3, HealRules.HealPerValue);
            Assert.AreEqual(0.5f, HealRules.ChannelSeconds, 1e-6f);
            Assert.AreEqual(0.5f, HealRules.ChannelMoveMultiplier, 1e-6f);
        }

        [Test]
        public void SleightHeal_NullOrEmpty_IsZero()
        {
            Assert.AreEqual(0, HealRules.SleightHeal(null));
            Assert.AreEqual(0, HealRules.SleightHeal(new List<RuntimeCardInstance>()));
        }

        // ---- Sleight heal ----

        [Test]
        public void Compose_NoHealCards_HealsZero()
        {
            var s = SleightRules.Compose(Of(cards.Card(4), cards.Card(5), cards.Card(6)));
            Assert.AreEqual(0, s.HealAmount);
        }

        [Test]
        public void Compose_OneHealCard_HealsThreeTimesItsValue_AndStillAddsValue()
        {
            var s = SleightRules.Compose(Of(cards.Card(4), cards.Heal(3), cards.Card(6)));
            Assert.AreEqual(9, s.HealAmount);
            Assert.AreEqual(13, s.Value);
        }

        [Test]
        public void Compose_ThreeHealCards_SumsEachHeal_UsingEffectiveValues()
        {
            var boosted = cards.Heal(5);
            boosted.PermanentRunUpgradeMod = 2; // 7
            var s = SleightRules.Compose(Of(cards.Heal(3), boosted, cards.Heal(7)));
            Assert.AreEqual(9 + 21 + 21, s.HealAmount);
            Assert.AreEqual(Element.Physical, s.Element);
            Assert.AreEqual(CardShape.Strike, s.Shape);
        }

        [Test]
        public void SleightResult_DefaultsHealToZero()
        {
            Assert.AreEqual(0, new SleightResult(12, CardShape.Strike, Element.Fire, 4, false).HealAmount);
        }

        // ---- card data guard ----

        [Test]
        public void Create_HealAndZeroBreaker_IsNeverAHeal()
        {
            var data = CardData.Create("zero_heal", 3, zeroBreaker: true, heal: true);
            try
            {
                Assert.IsTrue(data.IsZeroBreaker);
                Assert.IsFalse(data.IsHeal);
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }

        [Test]
        public void RuntimeCard_ExposesIsHeal()
        {
            Assert.IsTrue(cards.Heal(3).IsHeal);
            Assert.IsFalse(cards.Card(3).IsHeal);
        }

        // ---- max HP ----

        [Test]
        public void NewRun_StartingMaxHp_EqualsMaxHp()
        {
            var run = NewRun(100);
            Assert.AreEqual(100, run.StartingMaxHp);
            Assert.AreEqual(100, run.MaxHp);
        }

        [Test]
        public void IncreaseMaxHp_RaisesMax_AndHealsSameAmount()
        {
            var run = NewRun(100);
            run.SetHp(50);
            run.IncreaseMaxHp(10);
            Assert.AreEqual(110, run.MaxHp);
            Assert.AreEqual(60, run.Hp);
            Assert.AreEqual(100, run.StartingMaxHp);
        }

        [Test]
        public void IncreaseMaxHp_AtFullHp_StaysFull_AndClamps()
        {
            var run = NewRun(100);
            run.IncreaseMaxHp(12);
            Assert.AreEqual(112, run.MaxHp);
            Assert.AreEqual(112, run.Hp);
            run.Heal(50);
            Assert.AreEqual(112, run.Hp);
        }

        [Test]
        public void IncreaseMaxHp_Negative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NewRun().IncreaseMaxHp(-1));
        }

        [Test]
        public void HeartwoodSeed_AppliesOnce()
        {
            var run = NewRun(100);
            run.SetHp(40);
            Assert.IsTrue(run.AddRelic(RelicId.HeartwoodSeed));
            Assert.AreEqual(100 + RelicRules.HeartwoodSeedMaxHp, run.MaxHp);
            Assert.AreEqual(55, run.Hp);

            Assert.IsFalse(run.AddRelic(RelicId.HeartwoodSeed));
            Assert.AreEqual(115, run.MaxHp);
            Assert.AreEqual(55, run.Hp);
            Assert.AreEqual(100, run.StartingMaxHp);
        }

        [Test]
        public void HeartwoodSeed_IsLastRelicId_AndInOfferPool()
        {
            var all = (RelicId[])Enum.GetValues(typeof(RelicId));
            Assert.AreEqual(RelicId.HeartwoodSeed, all[all.Length - 1]);
            Assert.AreEqual(6, (int)RelicId.HeartwoodSeed, "appended last: existing serialized ints must not shift");

            bool offered = false;
            for (int seed = 0; seed < 200 && !offered; seed++)
                offered = RelicRules.RollOffer(null, new System.Random(seed)).Contains(RelicId.HeartwoodSeed);
            Assert.IsTrue(offered);
        }

        [Test]
        public void OtherRelics_DoNotChangeMaxHp()
        {
            var run = NewRun(100);
            run.AddRelic(RelicId.GildedQuill);
            Assert.AreEqual(100, run.MaxHp);
        }
    }
}
