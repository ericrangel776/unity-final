using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Elite rework rules (docs/specs/elite-rework.md).</summary>
    public class EliteRulesTests
    {
        /// <summary>The authored Elite deck (StarterContentBuilder.BuildEliteDeck).</summary>
        private static readonly int[] EliteDeck = { 5, 6, 7, 7, 8, 9 };

        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        private static MapCard Elite(int n) => new MapCard(MapCardKind.EliteTrial, n, EliteTrial.Hexer);

        [TestCase(2, new[] { 3, 4, 5, 5, 6, 7 })]
        [TestCase(5, new[] { 4, 5, 6, 6, 7, 8 })]
        [TestCase(9, new[] { 5, 6, 7, 7, 8, 9 })]
        public void EliteDeck_SitsBelowTheMobCurve_AndRisesWithIntensity(int intensity, int[] expected)
        {
            int bonus = RoomIntensity.EnemyCardValueBonus(Elite(intensity));
            var values = EliteDeck.Select(v =>
            {
                var card = cards.Card(v);
                card.TemporaryRoomMod = bonus;
                return card.GetEffectiveValue();
            });
            CollectionAssert.AreEqual(expected, values);
        }

        [Test]
        public void EliteOffset_StillClampsToOne()
        {
            var card = cards.Card(1);
            card.TemporaryRoomMod = RoomIntensity.EnemyCardValueBonus(Elite(1));
            Assert.AreEqual(1, card.GetEffectiveValue());
        }

        [TestCase(5, false)]
        [TestCase(6, true)]
        [TestCase(9, true)]
        public void IsHeavy_FromSixUp(int value, bool expected) =>
            Assert.AreEqual(expected, EliteRules.IsHeavy(cards.Card(value)));

        [Test]
        public void IsHeavy_UsesTheEffectiveValue()
        {
            var card = cards.Card(5);
            card.TemporaryRoomMod = 1;
            Assert.IsTrue(EliteRules.IsHeavy(card));
        }

        [Test]
        public void IsHeavy_FalseForZeroAndNoCard()
        {
            Assert.IsFalse(EliteRules.IsHeavy(cards.Zero()));
            Assert.IsFalse(EliteRules.IsHeavy(null));
        }

        [Test]
        public void HeavyShare_RisesWithIntensity()
        {
            int Heavy(int n) => EliteDeck.Count(v =>
            {
                var card = cards.Card(v);
                card.TemporaryRoomMod = RoomIntensity.EnemyCardValueBonus(Elite(n));
                return EliteRules.IsHeavy(card);
            });
            Assert.AreEqual(2, Heavy(2));
            Assert.AreEqual(5, Heavy(9));
        }
    }
}
