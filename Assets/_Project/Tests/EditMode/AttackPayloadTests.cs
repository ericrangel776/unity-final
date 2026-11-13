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
    }
}
