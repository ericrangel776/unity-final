using System.Linq;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class BossHudTextTests
    {
        [Test]
        public void BossPips_AreInDisplayOrder()
        {
            var pips = RunUiText.BossPips(left: 2, stocked: 1, spent: 3, deleted: 1, burned: 1);
            CollectionAssert.AreEqual(new[]
            {
                BossPip.Left, BossPip.Left, BossPip.Stocked, BossPip.Spent, BossPip.Spent, BossPip.Spent,
                BossPip.Deleted, BossPip.Burned,
            }, pips);
        }

        [Test]
        public void BossPips_FullDeckIsFifteenLeft() =>
            Assert.IsTrue(RunUiText.BossPips(15, 0, 0, 0, 0).All(p => p == BossPip.Left));

        [Test]
        public void BossPips_NegativeCountsAreEmpty() =>
            Assert.IsEmpty(RunUiText.BossPips(-1, -2, -3, -4, -5));

        [Test]
        public void BossCounter_ShowsOnlyNonZeroParts() =>
            Assert.AreEqual("15 left", RunUiText.BossCounter(15, 0, 0, 0, 0, 4));

        [Test]
        public void BossCounter_CountsDownToExhausted() =>
            Assert.AreEqual("6 left · 2 stocked · 4 spent · 3 deleted (1 more = EXHAUSTED)",
                RunUiText.BossCounter(6, 2, 4, 3, 0, 4));

        [Test]
        public void BossCounter_AtTheThreshold_SaysExhaustedAtReload() =>
            Assert.AreEqual("0 left · 10 spent · 4 deleted: EXHAUSTED at reload · 1 burned",
                RunUiText.BossCounter(0, 0, 10, 4, 1, 4));

        [Test]
        public void BossCounter_AfterTheCycleEnded_DropsTheHint() =>
            Assert.AreEqual("0 left · 12 spent · 2 deleted · 1 burned",
                RunUiText.BossCounter(0, 0, 12, 2, 1, 4, showHint: false));

        [TestCase(BossState.Idle, 0f, 0, "")]
        [TestCase(BossState.SleightWindup, 1.2f, 17, "SLEIGHT 17")]
        [TestCase(BossState.ReloadWindow, 1.23f, 0, "RELOADING 1.2s")]
        [TestCase(BossState.Exhausted, 3.96f, 0, "EXHAUSTED 4.0s")]
        [TestCase(BossState.Exhausted, -0.5f, 0, "EXHAUSTED 0.0s")]
        public void BossState_Label(BossState state, float remaining, int value, string expected) =>
            Assert.AreEqual(expected, RunUiText.BossState(state, remaining, value));
    }
}
