using System;
using System.Collections.Generic;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class SleightRulesTests
    {
        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        private static List<RuntimeCardInstance> Of(params RuntimeCardInstance[] c) => new List<RuntimeCardInstance>(c);

        [Test]
        public void Compose_SumsValues_ShapeFromSlot1_ElementFromSlot2_FinisherFromSlot3()
        {
            var s = SleightRules.Compose(Of(
                cards.Card(4, CardShape.Throw, Element.Physical),
                cards.Card(6, CardShape.Strike, Element.Fire),
                cards.Card(3, CardShape.Arcana, Element.Ice)));

            Assert.AreEqual(13, s.Value);
            Assert.AreEqual(CardShape.Throw, s.Shape);
            Assert.AreEqual(Element.Fire, s.Element);
            Assert.AreEqual(3, s.FinisherValue);
            Assert.AreEqual(1.3f, s.DamageMultiplier, 1e-5f);
            Assert.IsFalse(s.AllSameElement);
        }

        [Test]
        public void Compose_UsesEffectiveValues_ClampedPerCard()
        {
            var boosted = cards.Card(8);
            boosted.TemporaryRoomMod = 5; // clamps to 9
            var s = SleightRules.Compose(Of(boosted, cards.Card(1), cards.Card(1)));
            Assert.AreEqual(11, s.Value);
        }

        [Test]
        public void Compose_ReportsAllSameElement()
        {
            var s = SleightRules.Compose(Of(
                cards.Card(2, element: Element.Lightning),
                cards.Card(2, element: Element.Lightning),
                cards.Card(2, element: Element.Lightning)));
            Assert.IsTrue(s.AllSameElement);
        }

        [Test]
        public void Zero_MirrorsHighestNumberedCard()
        {
            var s = SleightRules.Compose(Of(cards.Zero(), cards.Card(9), cards.Card(5)));
            Assert.AreEqual(23, s.Value);
            Assert.AreEqual(5, s.FinisherValue);
        }

        [Test]
        public void ZeroInSlot3_MirrorsIntoFinisher()
        {
            var s = SleightRules.Compose(Of(cards.Card(5), cards.Card(9), cards.Zero()));
            Assert.AreEqual(23, s.Value);
            Assert.AreEqual(9, s.FinisherValue);
            Assert.AreEqual(1.9f, s.DamageMultiplier, 1e-5f);
        }

        [Test]
        public void ZeroInSlot1_TakesMirroredShape_ZeroInSlot2_TakesMirroredElement()
        {
            var source = cards.Card(7, CardShape.Arcana, Element.Ice);
            var s = SleightRules.Compose(Of(cards.Zero(), cards.Zero(), source));
            Assert.AreEqual(CardShape.Arcana, s.Shape);
            Assert.AreEqual(Element.Ice, s.Element);
            Assert.AreEqual(21, s.Value);
            Assert.IsTrue(s.AllSameElement);
        }

        [Test]
        public void Zero_MirrorTie_CopiesLowestSlot()
        {
            var first = cards.Card(9, CardShape.Throw, Element.Fire);
            var second = cards.Card(9, CardShape.Arcana, Element.Ice);
            var s = SleightRules.Compose(Of(cards.Zero(), first, second));
            Assert.AreEqual(CardShape.Throw, s.Shape, "Slot 1 mirrors the earlier 9.");
        }

        [Test]
        public void AllZeros_EachCountsAsMin()
        {
            var s = SleightRules.Compose(Of(cards.Zero(), cards.Zero(), cards.Zero()));
            Assert.AreEqual(3 * CardValue.Min, s.Value);
            Assert.AreEqual(CardValue.Min, s.FinisherValue);
        }

        [Test]
        public void ComposedValue_ReportsMirroredSlotValue()
        {
            var stocked = Of(cards.Card(2), cards.Zero(), cards.Card(6));
            Assert.AreEqual(2, SleightRules.ComposedValue(stocked, 0));
            Assert.AreEqual(6, SleightRules.ComposedValue(stocked, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SleightRules.ComposedValue(stocked, 3));
        }

        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        public void CanBurn_OnlyAboveFloorOfThree(int cycling, bool expected) =>
            Assert.AreEqual(expected, SleightRules.CanBurn(cycling));

        [TestCase(CardShape.Strike, 3)]
        [TestCase(CardShape.Throw, 2)]
        [TestCase(CardShape.Arcana, 1)]
        public void HitCount_PerShape(CardShape shape, int expected) =>
            Assert.AreEqual(expected, SleightRules.HitCount(shape));

        [Test]
        public void Compose_RejectsBadInput()
        {
            Assert.Throws<ArgumentNullException>(() => SleightRules.Compose(null));
            Assert.Throws<ArgumentException>(() => SleightRules.Compose(Of(cards.Card(1), cards.Card(2))));
            Assert.Throws<ArgumentException>(() => SleightRules.Compose(Of(cards.Card(1), null, cards.Card(2))));
        }
    }
}
