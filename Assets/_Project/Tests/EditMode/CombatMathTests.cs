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
