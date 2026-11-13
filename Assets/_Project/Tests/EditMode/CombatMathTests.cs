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
