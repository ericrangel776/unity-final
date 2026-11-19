using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class ElementStatusStateTests
    {
        private const float Eps = 0.0001f;

        private static int TickFor(ElementStatusState s, float seconds, float step = 0.02f)
        {
            int total = 0;
            // One extra step so float drift over many small steps can't leave the last tick unfired.
            int steps = (int)System.Math.Round(seconds / step) + 1;
            for (int i = 0; i < steps; i++) total += s.Tick(step);
            return total;
        }

        [Test]
        public void Fresh_HasNoStatus()
        {
            var s = new ElementStatusState();
            Assert.IsFalse(s.IsBurning);
            Assert.IsFalse(s.IsChilled);
            Assert.AreEqual(1f, s.SpeedMultiplier);
            Assert.AreEqual(0, s.Tick(1f));
        }

        [Test]
        public void Burn_DeliversItsTotalExactlyOverItsDuration()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(12, false);
            Assert.IsTrue(s.IsBurning);
            Assert.AreEqual(3f, s.BurnRemaining, Eps);
            Assert.AreEqual(0, s.Tick(0.49f), "no tick before 0.5 s");
            Assert.AreEqual(2, s.Tick(0.01f), "12 over 6 ticks = 2 per tick");
            Assert.AreEqual(10, TickFor(s, 2.5f));
            Assert.IsFalse(s.IsBurning);
            Assert.AreEqual(0, s.Tick(1f));
        }

        [Test]
        public void Burn_FewerTicksThanDamage_DealsMorePerTick_NeverOverdeals()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(13, false); // 6 ticks: 3,2,2,2,2,2
            int total = 0;
            for (int i = 0; i < 6; i++) total += s.Tick(0.5f);
            Assert.AreEqual(13, total);
            Assert.IsFalse(s.IsBurning);
        }

        [Test]
        public void Burn_LessDamageThanTicks_DealsOnePerTickUntilEmpty()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(2, false);
            Assert.AreEqual(1, s.Tick(0.5f));
            Assert.AreEqual(1, s.Tick(0.5f));
            Assert.IsFalse(s.IsBurning);
        }

        [Test]
        public void Burn_HugeDt_DealsOnlyThePendingTotal()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(12, true);
            Assert.AreEqual(12, s.Tick(100f));
            Assert.IsFalse(s.IsBurning);
        }

        [Test]
        public void Burn_Sleight_LastsFourSeconds()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(8, true);
            Assert.AreEqual(4f, s.BurnRemaining, Eps);
            Assert.AreEqual(8, TickFor(s, 4f));
        }

        [Test]
        public void Burn_Refresh_ResetsDuration_KeepsTheBiggerPending()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(12, false);
            s.Tick(1f); // 4 dealt, 8 pending
            Assert.AreEqual(8, s.BurnPending);
            s.ApplyBurn(4, false);
            Assert.AreEqual(8, s.BurnPending, "max(8, 4)");
            Assert.AreEqual(3f, s.BurnRemaining, Eps);
            s.ApplyBurn(20, false);
            Assert.AreEqual(20, s.BurnPending, "max(8, 20), never stacked to 28");
        }

        [Test]
        public void Chill_SlowsAndExpires()
        {
            var s = new ElementStatusState();
            s.ApplyChill(false);
            Assert.IsTrue(s.IsChilled);
            Assert.AreEqual(0.7f, s.SpeedMultiplier, Eps);
            Assert.AreEqual(2.5f, s.ChillRemaining, Eps);
            s.Tick(2.4f);
            Assert.IsTrue(s.IsChilled);
            s.Tick(0.2f);
            Assert.IsFalse(s.IsChilled);
            Assert.AreEqual(1f, s.SpeedMultiplier);
        }

        [Test]
        public void Chill_Refresh_TakesTheLongerRemaining()
        {
            var s = new ElementStatusState();
            s.ApplyChill(true); // 4 s
            s.Tick(1f);         // 3 s left
            s.ApplyChill(false); // 2.5 s < 3 s
            Assert.AreEqual(3f, s.ChillRemaining, Eps);
            s.Tick(1f);         // 2 s left
            s.ApplyChill(false);
            Assert.AreEqual(2.5f, s.ChillRemaining, Eps);
            Assert.AreEqual(0.7f, s.SpeedMultiplier, Eps, "the slow never stacks");
        }

        [Test]
        public void DurationScaleHalf_HalvesBothDurations_AndBurnTotal()
        {
            var s = new ElementStatusState { DurationScale = 0.5f };
            s.ApplyChill(false);
            Assert.AreEqual(1.25f, s.ChillRemaining, Eps);
            s.ApplyBurn(12, false);
            Assert.AreEqual(1.5f, s.BurnRemaining, Eps);
            Assert.AreEqual(6, s.BurnPending);
            Assert.AreEqual(6, s.Tick(10f));
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(12, false);
            s.ApplyChill(false);
            s.Clear();
            Assert.IsFalse(s.IsBurning);
            Assert.IsFalse(s.IsChilled);
            Assert.AreEqual(0, s.Tick(5f));
        }

        [Test]
        public void ZeroOrNegativeBurn_IsIgnored()
        {
            var s = new ElementStatusState();
            s.ApplyBurn(0, false);
            s.ApplyBurn(-4, true);
            Assert.IsFalse(s.IsBurning);
        }
    }
}
