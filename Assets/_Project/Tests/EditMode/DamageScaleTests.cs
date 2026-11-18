using System;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class DamageScaleTests
    {
        private static AttackPayload Card(int value) =>
            new AttackPayload(1, Team.Enemy, value, false, false, 0, CardShape.Strike, Element.Physical);

        [Test]
        public void Default_IsOne()
        {
            Assert.AreEqual(1f, Card(5).DamageScale);
            Assert.AreEqual(1f, default(AttackPayload).DamageScale);
        }

        [TestCase(9, 0.5f, 14)]   // 27 x 0.5 = 13.5 -> 14 (half away from zero)
        [TestCase(4, 0.5f, 6)]    // 12 x 0.5
        [TestCase(1, 0.1f, 1)]    // never below 1
        [TestCase(5, 2f, 30)]
        public void Damage_IsScaled(int value, float scale, int expected) =>
            Assert.AreEqual(expected, CombatMath.Damage(Card(value).WithDamageScale(scale)));

        [Test]
        public void Scale_KeepsValueAndEverythingElse()
        {
            var sleight = new AttackPayload(7, Team.Enemy, 20, false, true, 42, CardShape.Throw, Element.Fire, 6, 2);
            var scaled = sleight.WithDamageScale(0.5f);
            Assert.AreEqual(sleight.Value, scaled.Value);
            Assert.AreEqual(sleight.ActivationTick, scaled.ActivationTick);
            Assert.AreEqual(sleight.HitCount, scaled.HitCount);
            Assert.AreEqual(sleight.FinisherValue, scaled.FinisherValue);
            Assert.AreEqual(sleight.Shape, scaled.Shape);
            Assert.AreEqual(0.5f, scaled.DamageScale);
            var player = new AttackPayload(2, Team.Player, 20, false, true, 43, CardShape.Throw, Element.Fire);
            Assert.AreEqual(ClashResolver.Resolve(player, sleight).Outcome, ClashResolver.Resolve(player, scaled).Outcome,
                "clashes use the value only");
            Assert.AreEqual(ClashOutcome.Neutral, ClashResolver.Resolve(player, scaled).Outcome);
        }

        [Test]
        public void Zero_StaysZeroDamage() =>
            Assert.AreEqual(0, CombatMath.Damage(new AttackPayload(1, Team.Player, 1, true, false, 0, CardShape.Strike,
                Element.Physical).WithDamageScale(2f)));

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void WithDamageScale_RejectsNonPositive(float scale) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Card(5).WithDamageScale(scale));
    }
}
