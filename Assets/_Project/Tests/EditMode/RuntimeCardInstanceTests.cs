using System;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class RuntimeCardInstanceTests
    {
        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        [Test]
        public void EffectiveValue_IsBaseValue_WhenNoMods()
        {
            Assert.AreEqual(5, cards.Card(5).GetEffectiveValue());
        }

        [Test]
        public void EffectiveValue_AddsPermanentAndRoomMods()
        {
            var card = cards.Card(4);
            card.PermanentRunUpgradeMod = 2;
            card.TemporaryRoomMod = 1;
            Assert.AreEqual(7, card.GetEffectiveValue());
        }

        [Test]
        public void EffectiveValue_ClampsToNine()
        {
            var card = cards.Card(8);
            card.PermanentRunUpgradeMod = 5;
            Assert.AreEqual(CardValue.Max, card.GetEffectiveValue());
        }

        [Test]
        public void EffectiveValue_OneMinusOne_ClampsToOne_NotZero()
        {
            var card = cards.Card(1);
            card.TemporaryRoomMod = -1;
            Assert.AreEqual(CardValue.Min, card.GetEffectiveValue());
            Assert.IsFalse(card.IsZeroBreaker);
        }

        [Test]
        public void ZeroBreaker_IgnoresPositiveMods()
        {
            var zero = cards.Zero();
            zero.TemporaryRoomMod = 1;
            zero.PermanentRunUpgradeMod = 3;
            Assert.IsTrue(zero.IsZeroBreaker);
            Assert.AreEqual(0, zero.GetEffectiveValue());
        }

        [Test]
        public void Mods_DoNotMutateCardData()
        {
            var card = cards.Card(3);
            card.PermanentRunUpgradeMod = 4;
            Assert.AreEqual(3, card.Data.BaseValue);
        }

        [Test]
        public void Constructor_RejectsNullData()
        {
            Assert.Throws<ArgumentNullException>(() => new RuntimeCardInstance(null));
        }

        [TestCase(0, 1)]
        [TestCase(-4, 1)]
        [TestCase(12, 9)]
        public void CardDataCreate_ClampsOutOfRangeBaseValue(int input, int expected)
        {
            Assert.AreEqual(expected, cards.Card(input).Data.BaseValue);
        }
    }
}
