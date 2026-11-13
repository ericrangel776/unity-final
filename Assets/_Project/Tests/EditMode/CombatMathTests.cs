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
    }
}
