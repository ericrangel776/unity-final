using System;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class AttackPayloadTests
    {
        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        [Test]
        public void FromCard_SnapshotsValue_AndIgnoresLaterMods()
        {
            var card = cards.Card(4, CardShape.Throw, Element.Fire);
            var payload = AttackPayload.FromCard(card, ownerId: 7, Team.Player, activationTick: 100);

            card.TemporaryRoomMod = 4;

            Assert.AreEqual(4, payload.Value);
            Assert.AreEqual(CardShape.Throw, payload.Shape);
            Assert.AreEqual(Element.Fire, payload.Element);
            Assert.AreEqual(100, payload.ActivationTick);
            Assert.AreEqual(7, payload.OwnerId);
        }

        [Test]
        public void FromCard_CarriesZeroBreakerFlag()
        {
            var payload = AttackPayload.FromCard(cards.Zero(), 1, Team.Player, 1);
            Assert.IsTrue(payload.IsZeroBreaker);
            Assert.AreEqual(0, payload.Value);
        }

        [Test]
        public void FromCard_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => AttackPayload.FromCard(null, 1, Team.Player, 1));
        }

        [Test]
        public void Poke_IsValueOnePhysicalStrike()
        {
            var poke = AttackPayload.Poke(1, Team.Player, 5);
            Assert.AreEqual(1, poke.Value);
            Assert.IsFalse(poke.IsZeroBreaker);
            Assert.AreEqual(CardShape.Strike, poke.Shape);
            Assert.AreEqual(Element.Physical, poke.Element);
        }

        [TestCase(0, 1)]
        [TestCase(14, 9)]
        public void SingleCardPayload_IsClamped(int raw, int expected)
        {
            var p = new AttackPayload(1, Team.Player, raw, false, false, 0, CardShape.Strike, Element.Physical);
            Assert.AreEqual(expected, p.Value);
        }

        [Test]
        public void SleightPayload_MayExceedNine()
        {
            var p = new AttackPayload(1, Team.Player, 21, false, true, 0, CardShape.Strike, Element.Physical);
            Assert.AreEqual(21, p.Value);
        }

        [Test]
        public void FromSleight_CarriesComposedStats()
        {
            var s = new SleightResult(23, CardShape.Arcana, Element.Ice, 5, false);
            var p = AttackPayload.FromSleight(s, 3, Team.Player, 40);

            Assert.IsTrue(p.IsSleight);
            Assert.IsFalse(p.IsZeroBreaker);
            Assert.AreEqual(23, p.Value);
            Assert.AreEqual(CardShape.Arcana, p.Shape);
            Assert.AreEqual(Element.Ice, p.Element);
            Assert.AreEqual(5, p.FinisherValue);
            Assert.AreEqual(1.5f, p.DamageMultiplier, 1e-5f);
            Assert.AreEqual(40, p.ActivationTick);
        }

        [Test]
        public void FinisherValue_IsZeroForSingleCards()
        {
            var p = new AttackPayload(1, Team.Player, 5, false, false, 0, CardShape.Strike, Element.Physical, finisherValue: 7);
            Assert.AreEqual(0, p.FinisherValue);
            Assert.AreEqual(1f, p.DamageMultiplier);
        }

        [Test]
        public void HitCount_DefaultsToOne_AndIsOneForSingleCards()
        {
            var s = new SleightResult(12, CardShape.Strike, Element.Fire, 4, false);
            Assert.AreEqual(1, AttackPayload.FromSleight(s, 1, Team.Player, 0).HitCount);
            Assert.AreEqual(3, AttackPayload.FromSleight(s, 1, Team.Player, 0, hitCount: 3).HitCount);
            Assert.AreEqual(1, new AttackPayload(1, Team.Player, 5, false, false, 0, CardShape.Strike,
                Element.Physical, 0, hitCount: 3).HitCount);
            Assert.AreEqual(1, new AttackPayload(1, Team.Player, 10, false, true, 0, CardShape.Strike,
                Element.Physical, 0, hitCount: -2).HitCount);
        }

        [TestCase(-3, 0)]
        [TestCase(14, 9)]
        public void FinisherValue_IsClamped(int raw, int expected)
        {
            var p = new AttackPayload(1, Team.Player, 10, false, true, 0, CardShape.Strike, Element.Physical, raw);
            Assert.AreEqual(expected, p.FinisherValue);
        }
    }
}
