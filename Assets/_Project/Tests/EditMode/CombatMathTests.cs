using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class CombatMathTests
    {
        [Test]
        public void Damage_ScalesWithValue()
        {
            var p = new AttackPayload(1, Team.Player, 5, false, false, 0, CardShape.Strike, Element.Physical);
            Assert.AreEqual(5 * CombatMath.DamagePerPoint, CombatMath.Damage(p));
        }

        [Test]
        public void Damage_ZeroBreaker_DealsNoHpDamage()
        {
            var p = new AttackPayload(1, Team.Player, 0, true, false, 0, CardShape.Strike, Element.Physical);
            Assert.AreEqual(0, CombatMath.Damage(p));
        }

        [Test]
        public void Damage_Poke_IsSmallestNonZero()
        {
            Assert.AreEqual(CombatMath.DamagePerPoint, CombatMath.Damage(AttackPayload.Poke(1, Team.Player, 0)));
        }

        private static AttackPayload Sleight(int value, int finisher) =>
            new AttackPayload(1, Team.Player, value, false, true, 0, CardShape.Strike, Element.Physical, finisher);

        [Test]
        public void Damage_Sleight_AppliesFinisherMultiplier()
        {
            // 20 * 3 = 60, x1.5 = 90
            Assert.AreEqual(90, CombatMath.Damage(Sleight(20, 5)));
        }

        [Test]
        public void Damage_Sleight_RoundsUpPartialPoints()
        {
            // 7 * 3 = 21, x1.3 = 27.3 -> 28
            Assert.AreEqual(28, CombatMath.Damage(Sleight(7, 3)));
        }

        [Test]
        public void Damage_Sleight_HasNoFloatRoundingError()
        {
            // 10 * 3 = 30, x1.1 = 33 exactly; float math gives ceil(33.0000014) = 34.
            Assert.AreEqual(33, CombatMath.Damage(Sleight(10, 1)));
        }

        [Test]
        public void Damage_MultiHitSleight_SplitsTotalPerHit_RoundedUp()
        {
            // 20 * 3 = 60, x1.5 = 90 total; 3 hits -> 30 each; 2 hits -> 45 each.
            Assert.AreEqual(30, CombatMath.Damage(new AttackPayload(1, Team.Player, 20, false, true, 0,
                CardShape.Strike, Element.Physical, 5, hitCount: 3)));
            Assert.AreEqual(45, CombatMath.Damage(new AttackPayload(1, Team.Player, 20, false, true, 0,
                CardShape.Throw, Element.Physical, 5, hitCount: 2)));
            // 7 * 3 = 21, x1.3 = 28 total; 3 hits -> 9.33 -> 10.
            Assert.AreEqual(10, CombatMath.Damage(new AttackPayload(1, Team.Player, 7, false, true, 0,
                CardShape.Strike, Element.Physical, 3, hitCount: 3)));
        }

        private static AttackPayload Card(int value, bool zero = false) =>
            new AttackPayload(1, Team.Player, value, zero, false, 0, CardShape.Strike, Element.Physical);

        [Test]
        public void BreakMargin_IsValueGap()
        {
            Assert.AreEqual(1, CombatMath.BreakMargin(Card(6), Card(5)));
            Assert.AreEqual(7, CombatMath.BreakMargin(Card(9), Card(2)));
        }

        [Test]
        public void BreakMargin_ZeroBreak_IsMax()
        {
            Assert.AreEqual(CombatMath.MaxBreakMargin, CombatMath.BreakMargin(Card(0, zero: true), Card(9)));
        }

        [Test]
        public void BreakMargin_BeatingAZeroPlayedFirst_UsesWinnerValue()
        {
            Assert.AreEqual(2, CombatMath.BreakMargin(Card(2), Card(0, zero: true)));
        }

        [Test]
        public void BreakMargin_IsClampedToValidRange()
        {
            // A non-positive gap can't be a break; treat it as the smallest one.
            Assert.AreEqual(1, CombatMath.BreakMargin(Card(5), Card(5)));
            Assert.AreEqual(CombatMath.MaxBreakMargin, CombatMath.ClampMargin(50));
        }

        [Test]
        public void BreakScales_AreOne_AtBaselineMargin()
        {
            Assert.AreEqual(1f, CombatMath.BreakPostureScale(CombatMath.BaselineBreakMargin), 1e-5f);
            Assert.AreEqual(1f, CombatMath.BreakStunScale(CombatMath.BaselineBreakMargin), 1e-5f);
        }

        [Test]
        public void BreakEffects_GrowWithMargin()
        {
            Assert.Greater(CombatMath.BreakPostureScale(7), CombatMath.BreakPostureScale(1));
            Assert.Greater(CombatMath.BreakStunScale(7), CombatMath.BreakStunScale(1));
            Assert.Greater(CombatMath.BreakShoveSpeed(7), CombatMath.BreakShoveSpeed(1));
        }

        [Test]
        public void CounterDamage_IsOneAndAHalfTimes_RoundedUp()
        {
            Assert.AreEqual(30, CombatMath.CounterDamage(20));
            Assert.AreEqual(5, CombatMath.CounterDamage(3), "A poke (3) still gains: 4.5 rounds up to 5.");
        }

        [Test]
        public void CounterDamage_ZeroOrNegative_IsZero()
        {
            Assert.AreEqual(0, CombatMath.CounterDamage(0));
            Assert.AreEqual(0, CombatMath.CounterDamage(-4));
        }

        [Test]
        public void KnockbackSpeed_ZeroBreaker_IsZero()
        {
            var p = new AttackPayload(1, Team.Player, 0, true, false, 0, CardShape.Strike, Element.Physical);
            Assert.AreEqual(0f, CombatMath.KnockbackSpeed(p));
        }

        [Test]
        public void KnockbackSpeed_IncreasesWithValue()
        {
            var low = new AttackPayload(1, Team.Player, 2, false, false, 0, CardShape.Strike, Element.Physical);
            var high = new AttackPayload(1, Team.Player, 8, false, false, 0, CardShape.Strike, Element.Physical);
            Assert.Greater(CombatMath.KnockbackSpeed(low), 0f);
            Assert.Greater(CombatMath.KnockbackSpeed(high), CombatMath.KnockbackSpeed(low));
        }
    }
}
