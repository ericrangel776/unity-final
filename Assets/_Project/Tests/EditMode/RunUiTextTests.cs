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

        // ---- Map Cards ----

        [Test]
        public void MapCardName_IsDefinedForEveryKind()
        {
            foreach (MapCardKind kind in System.Enum.GetValues(typeof(MapCardKind)))
                Assert.IsFalse(string.IsNullOrEmpty(RunUiText.MapCardName(kind)), $"{kind} has no name");
        }

        [Test]
        public void NumberLabel_IsIntensityOnFights_QualityOnBlue()
        {
            Assert.AreEqual("INTENSITY", RunUiText.NumberLabel(new MapCard(MapCardKind.Vanguard, 4)));
            Assert.AreEqual("INTENSITY", RunUiText.NumberLabel(new MapCard(MapCardKind.EliteTrial, 4, EliteTrial.Hexer)));
            Assert.AreEqual("QUALITY", RunUiText.NumberLabel(new MapCard(MapCardKind.Shop, 4)));
        }

        [TestCase(1, "Muted ●○○○")]
        [TestCase(3, "Muted ●○○○")]
        [TestCase(4, "Steady ●●○○")]
        [TestCase(7, "Fierce ●●●○")]
        [TestCase(9, "Frantic ●●●●")]
        public void TierLine_ShowsTierNameAndPips(int intensity, string expected)
        {
            Assert.AreEqual(expected, RunUiText.TierLine(new MapCard(MapCardKind.Hexfire, intensity)));
        }

        [Test]
        public void TierLine_IsEmptyForBlue_AndFinalForBoss()
        {
            Assert.AreEqual("", RunUiText.TierLine(new MapCard(MapCardKind.Rest, 5)));
            Assert.AreEqual("Final fight", RunUiText.TierLine(new MapCard(MapCardKind.Boss, RunState.BossIntensity)));
        }

        [Test]
        public void Blurb_KindledGround_NamesTheElementAndBuff()
        {
            var card = new MapCard(MapCardKind.KindledGround, 5, buffElement: Element.Ice);
            StringAssert.Contains($"Ice cards +{RoomIntensity.PlayerElementBuff(card)}", RunUiText.MapCardBlurb(card));
        }

        [Test]
        public void Blurb_Forge_SaysAndOnlyAtHighQuality()
        {
            StringAssert.Contains("or make one Premium", RunUiText.MapCardBlurb(new MapCard(MapCardKind.Forge, 4)));
            StringAssert.Contains("and make another Premium", RunUiText.MapCardBlurb(new MapCard(MapCardKind.Forge, 8)));
        }

        [Test]
        public void Blurb_Elite_NamesTheTrial()
        {
            StringAssert.Contains("Counterpuncher",
                RunUiText.MapCardBlurb(new MapCard(MapCardKind.EliteTrial, 5, EliteTrial.Counterpuncher)));
        }

        [Test]
        public void Blurb_CountsTheScaledEnemies()
        {
            StringAssert.StartsWith($"{RoomIntensity.EnemyCount(new MapCard(MapCardKind.Vanguard, 7))} enemies",
                RunUiText.MapCardBlurb(new MapCard(MapCardKind.Vanguard, 7)));
        }

        [Test]
        public void Blurb_IsDefinedForEveryKind()
        {
            foreach (MapCardKind kind in System.Enum.GetValues(typeof(MapCardKind)))
            {
                var trial = kind == MapCardKind.EliteTrial ? EliteTrial.Veiled : EliteTrial.None;
                Assert.IsNotEmpty(RunUiText.MapCardBlurb(new MapCard(kind, 5, trial)), $"{kind} has no blurb");
            }
        }

        [Test]
        public void RelicShortName_IsDefinedForEveryRelic()
        {
            foreach (RelicId relic in System.Enum.GetValues(typeof(RelicId)))
                Assert.AreNotEqual(relic.ToString(), RunUiText.RelicShortName(relic), $"{relic} has no short name");
        }
    }
}
