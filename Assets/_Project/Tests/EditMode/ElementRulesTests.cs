using System.Collections.Generic;
using DeckArpg.Core;
using NUnit.Framework;
using UnityEngine;

namespace DeckArpg.Tests.EditMode
{
    public class ElementRulesTests
    {
        private const float Eps = 0.0001f;

        [TestCase(Element.Physical, CardShape.Strike, Delivery.Strike)]
        [TestCase(Element.Physical, CardShape.Throw, Delivery.Projectile)]
        [TestCase(Element.Physical, CardShape.Arcana, Delivery.Burst)]
        [TestCase(Element.Fire, CardShape.Strike, Delivery.Projectile)]
        [TestCase(Element.Fire, CardShape.Throw, Delivery.Projectile)]
        [TestCase(Element.Fire, CardShape.Arcana, Delivery.Projectile)]
        [TestCase(Element.Ice, CardShape.Strike, Delivery.FrostBlast)]
        [TestCase(Element.Ice, CardShape.Throw, Delivery.FrostBlast)]
        [TestCase(Element.Ice, CardShape.Arcana, Delivery.FrostBlast)]
        [TestCase(Element.Lightning, CardShape.Strike, Delivery.ChainArc)]
        [TestCase(Element.Lightning, CardShape.Throw, Delivery.ChainArc)]
        [TestCase(Element.Lightning, CardShape.Arcana, Delivery.ChainArc)]
        [TestCase(Element.Wind, CardShape.Strike, Delivery.Gust)]
        [TestCase(Element.Wind, CardShape.Throw, Delivery.Gust)]
        [TestCase(Element.Wind, CardShape.Arcana, Delivery.Gust)]
        public void DeliveryFor_ElementDecides_PhysicalKeepsShape(Element element, CardShape shape, Delivery expected)
        {
            Assert.AreEqual(expected, ElementRules.DeliveryFor(element, shape));
        }

        [Test]
        public void Wind_IsAppendedLast_SoOldAssetsKeepTheirInts()
        {
            Assert.AreEqual(0, (int)Element.Physical);
            Assert.AreEqual(3, (int)Element.Lightning);
            Assert.AreEqual(4, (int)Element.Wind);
        }

        [TestCase(30, 12)]
        [TestCase(1, 1)]
        [TestCase(10, 4)]
        [TestCase(11, 5)]
        [TestCase(0, 0)]
        [TestCase(-3, 0)]
        public void BurnTotal_IsCeilFortyPercent_AtLeastOne(int hit, int expected)
        {
            Assert.AreEqual(expected, ElementRules.BurnTotal(hit));
        }

        [TestCase(7, 5)]
        [TestCase(8, 5)]
        [TestCase(10, 6)]
        [TestCase(1, 1)]
        [TestCase(0, 0)]
        public void ChainDamage_IsCeilSixtyPercent_AtLeastOne(int hit, int expected)
        {
            Assert.AreEqual(expected, ElementRules.ChainDamage(hit));
        }

        [TestCase(18, 9)]
        [TestCase(7, 4)]
        [TestCase(1, 1)]
        [TestCase(0, 0)]
        public void SlamDamage_IsCeilHalf_AtLeastOne(int hit, int expected)
        {
            Assert.AreEqual(expected, ElementRules.SlamDamage(hit));
        }

        [Test]
        public void JoltSeconds_ScalesWithDurationScale()
        {
            Assert.AreEqual(0.4f, ElementRules.JoltSeconds(1f), Eps);
            Assert.AreEqual(0.2f, ElementRules.JoltSeconds(ElementRules.EliteBossDurationScale), Eps);
            Assert.AreEqual(0f, ElementRules.JoltSeconds(-1f), Eps);
        }

        [Test]
        public void KnockbackDecaySeconds_WindLastsLonger()
        {
            Assert.AreEqual(0.4f, ElementRules.KnockbackDecaySeconds(Element.Wind, 0.15f), Eps);
            Assert.AreEqual(0.15f, ElementRules.KnockbackDecaySeconds(Element.Physical, 0.15f), Eps);
            Assert.AreEqual(0.15f, ElementRules.KnockbackDecaySeconds(Element.Lightning, 0.15f), Eps);
        }

        [Test]
        public void ChainHitStop_StaysUnderTheInputBuffer()
        {
            Assert.Greater(ElementRules.ChainHitStopSeconds, 0f);
            Assert.Less(ElementRules.ChainHitStopSeconds, 0.2f);
        }

        [TestCase(66, 20)]
        [TestCase(60, 18)]
        [TestCase(1, 1)]
        [TestCase(0, 0)]
        public void FinisherDamage_IsCeilThirtyPercent(int total, int expected)
        {
            Assert.AreEqual(expected, ElementRules.FinisherDamage(total));
        }

        [Test]
        public void StatusSeconds_SingleSleightAndScaled()
        {
            Assert.AreEqual(3f, ElementRules.StatusSeconds(Element.Fire, false, 1f), Eps);
            Assert.AreEqual(4f, ElementRules.StatusSeconds(Element.Fire, true, 1f), Eps);
            Assert.AreEqual(2.5f, ElementRules.StatusSeconds(Element.Ice, false, 1f), Eps);
            Assert.AreEqual(4f, ElementRules.StatusSeconds(Element.Ice, true, 1f), Eps);
            Assert.AreEqual(1.5f, ElementRules.StatusSeconds(Element.Fire, false, 0.5f), Eps);
            Assert.AreEqual(2f, ElementRules.StatusSeconds(Element.Ice, true, 0.5f), Eps);
        }

        [TestCase(Element.Physical)]
        [TestCase(Element.Lightning)]
        [TestCase(Element.Wind)]
        public void StatusSeconds_NoTimedStatus_IsZero(Element element)
        {
            Assert.AreEqual(0f, ElementRules.StatusSeconds(element, true, 1f));
        }

        [Test]
        public void KnockbackScale_WindOnly()
        {
            Assert.AreEqual(3f, ElementRules.KnockbackScale(Element.Wind), Eps);
            Assert.AreEqual(1f, ElementRules.KnockbackScale(Element.Physical), Eps);
            Assert.AreEqual(1f, ElementRules.KnockbackScale(Element.Fire), Eps);
        }

        [Test]
        public void PickChain_Empty_ReturnsNoLinks()
        {
            Assert.IsEmpty(ElementRules.PickChain(Vector3.zero, new List<Vector3>()));
            Assert.IsEmpty(ElementRules.PickChain(Vector3.zero, null));
        }

        [Test]
        public void PickChain_HopsNearestFirst()
        {
            var c = new List<Vector3> { new Vector3(3, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0) };
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, ElementRules.PickChain(Vector3.zero, c));
        }

        [Test]
        public void PickChain_RadiusIsMeasuredFromThePreviousLink()
        {
            // 3.5 -> 7 -> 10.5: each hop is 3.5 u, though the last is 10.5 u from the origin.
            var c = new List<Vector3> { new Vector3(3.5f, 0, 0), new Vector3(7f, 0, 0), new Vector3(10.5f, 0, 0) };
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ElementRules.PickChain(Vector3.zero, c));
        }

        [Test]
        public void PickChain_StopsAtAGapWiderThanTheRadius()
        {
            var c = new List<Vector3> { new Vector3(2f, 0, 0), new Vector3(8.5f, 0, 0) };
            CollectionAssert.AreEqual(new[] { 0 }, ElementRules.PickChain(Vector3.zero, c));
        }

        [Test]
        public void PickChain_CapsAtFiveTargets()
        {
            var c = new List<Vector3>
            {
                new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(3, 0, 0), new Vector3(4, 0, 0),
                new Vector3(5, 0, 0), new Vector3(6, 0, 0)
            };
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, ElementRules.PickChain(Vector3.zero, c));
        }

        [Test]
        public void PickChain_TiesGoToTheLowerIndex()
        {
            var c = new List<Vector3> { new Vector3(0, 0, 2), new Vector3(2, 0, 0) };
            Assert.AreEqual(0, ElementRules.PickChain(Vector3.zero, c)[0]);
        }

        private static AttackPayload Card(Element element, int value = 5, bool zero = false) =>
            new AttackPayload(1, Team.Player, value, zero, false, 0, CardShape.Strike, element);

        [Test]
        public void BreakPostureScale_PhysicalWinner_IsOneAndAHalf()
        {
            Assert.AreEqual(1.5f, ElementRules.BreakPostureScale(Card(Element.Physical)), Eps);
            var sleight = new AttackPayload(1, Team.Enemy, 15, false, true, 0, CardShape.Strike, Element.Physical, 5, 3);
            Assert.AreEqual(1.5f, ElementRules.BreakPostureScale(sleight), Eps);
        }

        [TestCase(Element.Fire)]
        [TestCase(Element.Ice)]
        [TestCase(Element.Lightning)]
        [TestCase(Element.Wind)]
        public void BreakPostureScale_OtherElements_IsOne(Element element)
        {
            Assert.AreEqual(1f, ElementRules.BreakPostureScale(Card(element)), Eps);
        }

        [Test]
        public void BreakPostureScale_ZeroCardAndPoke_IsOne()
        {
            Assert.AreEqual(1f, ElementRules.BreakPostureScale(Card(Element.Physical, 0, zero: true)), Eps);
            Assert.AreEqual(1f, ElementRules.BreakPostureScale(AttackPayload.Poke(1, Team.Player, 0)), Eps);
        }

        [Test]
        public void EffectHit_NeverHitStops()
        {
            Assert.AreEqual(0f, HitJuiceRules.BodyHitStopSeconds(Card(Element.Fire, 9), Team.Enemy, 20, isEffect: true));
            Assert.AreEqual(0f, HitJuiceRules.BodyHitStopSeconds(Card(Element.Fire, 9), Team.Player, 20, isEffect: true));
            Assert.Greater(HitJuiceRules.BodyHitStopSeconds(Card(Element.Fire, 9), Team.Enemy, 20), 0f);
        }

        [Test]
        public void TotalDamage_IsTheUnsplitSleightDamage()
        {
            // 20 x 3 = 60, x1.1 = 66 total; split over 3 hits = 22 each.
            var sleight = new AttackPayload(1, Team.Player, 20, false, true, 0, CardShape.Strike, Element.Fire, 1, 3);
            Assert.AreEqual(66, CombatMath.TotalDamage(sleight));
            Assert.AreEqual(22, CombatMath.Damage(sleight));
            Assert.AreEqual(15, CombatMath.TotalDamage(Card(Element.Fire, 5)));
        }

        [Test]
        public void WindColour_IsStormGrey_NotThePhysicalGrey()
        {
            Assert.AreEqual(new Color(0.33f, 0.36f, 0.45f, 0.5f), DeckArpg.Gameplay.ElementColors.For(Element.Wind));
            Assert.AreNotEqual(DeckArpg.Gameplay.ElementColors.For(Element.Physical), DeckArpg.Gameplay.ElementColors.For(Element.Wind));
        }

        [Test]
        public void PickChain_IgnoresHeight()
        {
            var c = new List<Vector3> { new Vector3(3, 10, 0) };
            CollectionAssert.AreEqual(new[] { 0 }, ElementRules.PickChain(Vector3.zero, c));
        }
    }
}
