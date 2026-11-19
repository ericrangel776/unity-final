using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class HitJuiceRulesTests
    {
        private const float Eps = 0.0001f;

        private static AttackPayload Card(int value, Team team = Team.Player) =>
            new AttackPayload(1, team, value, false, false, 0, CardShape.Strike, Element.Fire);

        private static AttackPayload Sleight(int value) =>
            new AttackPayload(1, Team.Player, value, false, true, 0, CardShape.Strike, Element.Fire, 3);

        [Test]
        public void HitStop_Poke_IsZero()
        {
            Assert.AreEqual(0f, HitJuiceRules.BodyHitStopSeconds(AttackPayload.Poke(1, Team.Player, 0), Team.Enemy, 3));
        }

        [Test]
        public void HitStop_ZeroBreaker_IsZero()
        {
            var zero = new AttackPayload(1, Team.Player, 0, true, false, 0, CardShape.Strike, Element.Physical);
            Assert.AreEqual(0f, HitJuiceRules.BodyHitStopSeconds(zero, Team.Enemy, 5));
        }

        [Test]
        public void HitStop_NoDamage_IsZero()
        {
            Assert.AreEqual(0f, HitJuiceRules.BodyHitStopSeconds(Card(9), Team.Enemy, 0));
            Assert.AreEqual(0f, HitJuiceRules.BodyHitStopSeconds(Card(9, Team.Enemy), Team.Player, 0));
        }

        [TestCase(1, 0.03f)]
        [TestCase(5, 0.04f)]
        [TestCase(9, 0.05f)]
        public void HitStop_PlayerCard_ScalesWithValue(int value, float expected)
        {
            Assert.AreEqual(expected, HitJuiceRules.BodyHitStopSeconds(Card(value), Team.Enemy, value * 3), Eps);
        }

        [Test]
        public void HitStop_Sleight_UsesCap()
        {
            Assert.AreEqual(HitJuiceRules.MaxCardHitStop, HitJuiceRules.BodyHitStopSeconds(Sleight(4), Team.Enemy, 12), Eps);
            Assert.AreEqual(HitJuiceRules.MaxCardHitStop, HitJuiceRules.BodyHitStopSeconds(Sleight(24), Team.Enemy, 80), Eps);
        }

        [TestCase(1)]
        [TestCase(9)]
        public void HitStop_EnemyHitsPlayer_IsFlat(int value)
        {
            Assert.AreEqual(HitJuiceRules.PlayerHurtHitStop,
                HitJuiceRules.BodyHitStopSeconds(Card(value, Team.Enemy), Team.Player, value * 3), Eps);
        }

        [Test]
        public void HitStop_StaysUnderInputBuffer()
        {
            Assert.Less(HitJuiceRules.MaxCardHitStop, 0.2f);
            Assert.Less(HitJuiceRules.PlayerHurtHitStop, 0.2f);
        }

        [TestCase(0, 1f)]
        [TestCase(-5, 1f)]
        [TestCase(3, 1.05f)]
        [TestCase(30, 1.5f)]
        [TestCase(60, 2f)]
        [TestCase(200, 2f)]
        public void DamageNumberScale_GrowsAndClamps(int damage, float expected)
        {
            Assert.AreEqual(expected, HitJuiceRules.DamageNumberScale(damage), Eps);
        }
    }
}
