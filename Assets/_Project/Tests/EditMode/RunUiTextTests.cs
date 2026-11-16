using DeckArpg.Core;
using DeckArpg.UI;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class RunUiTextTests
    {
        [Test]
        public void Room_ShowsNumberOutOfSix()
        {
            Assert.AreEqual("Room 3/6", RunUiText.Room(3));
        }

        [Test]
        public void Room_MarksTheBossRoom()
        {
            Assert.AreEqual("Room 6/6 · Boss", RunUiText.Room(RunState.BossRoom));
        }

        [TestCase(0, 3, "○○○")]
        [TestCase(2, 3, "●●○")]
        [TestCase(3, 3, "●●●")]
        [TestCase(-1, 3, "○○○")]
        [TestCase(5, 3, "●●●")]
        [TestCase(2, 0, "")]
        public void Pips_FillsAndClamps(int filled, int total, string expected)
        {
            Assert.AreEqual(expected, RunUiText.Pips(filled, total));
        }

        [Test]
        public void Momentum_ShowsMaxStacksPips()
        {
            Assert.AreEqual("Momentum ●●○", RunUiText.Momentum(2));
        }

        [Test]
        public void Relics_NullOrEmpty_SaysNoRelics()
        {
            Assert.AreEqual("No relics", RunUiText.Relics(null, 0));
            Assert.AreEqual("No relics", RunUiText.Relics(new RelicId[0], 0));
        }

        [Test]
        public void Relics_AreListedInEnumOrder_RegardlessOfInput()
        {
            var relics = new[] { RelicId.GildedQuill, RelicId.ShatteredHourglass, RelicId.TemperedCoil };
            Assert.AreEqual("Hourglass · Coil · Quill", RunUiText.Relics(relics, 0));
        }

        [Test]
        public void Relics_AshenShowsRoomStacks_OnlyAboveZero()
        {
            var relics = new[] { RelicId.AshenSigil };
            Assert.AreEqual("Ashen", RunUiText.Relics(relics, 0));
            Assert.AreEqual("Ashen x2", RunUiText.Relics(relics, 2));
        }

        [Test]
        public void RelicShortName_IsDefinedForEveryRelic()
        {
            foreach (RelicId relic in System.Enum.GetValues(typeof(RelicId)))
                Assert.AreNotEqual(relic.ToString(), RunUiText.RelicShortName(relic), $"{relic} has no short name");
        }
    }
}
