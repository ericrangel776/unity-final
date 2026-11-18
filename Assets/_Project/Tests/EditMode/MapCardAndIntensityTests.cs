using System;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class MapCardAndIntensityTests
    {
        // ---- MapCard ----

        [TestCase(0)]
        [TestCase(10)]
        public void MapCard_RejectsNumberOutsideOneToNine(int number) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new MapCard(MapCardKind.Vanguard, number));

        [Test]
        public void MapCard_EliteRequiresTrial() =>
            Assert.Throws<ArgumentException>(() => new MapCard(MapCardKind.EliteTrial, 5));

        [Test]
        public void MapCard_NonEliteRejectsTrial() =>
            Assert.Throws<ArgumentException>(() => new MapCard(MapCardKind.Shop, 5, EliteTrial.Hexer));

        [TestCase(MapCardKind.Vanguard, MapFamily.Red, true)]
        [TestCase(MapCardKind.Hexfire, MapFamily.Red, true)]
        [TestCase(MapCardKind.KindledGround, MapFamily.Green, true)]
        [TestCase(MapCardKind.BountyWind, MapFamily.Green, true)]
        [TestCase(MapCardKind.Shop, MapFamily.Blue, false)]
        [TestCase(MapCardKind.Forge, MapFamily.Blue, false)]
        [TestCase(MapCardKind.Rest, MapFamily.Blue, false)]
        [TestCase(MapCardKind.Boss, MapFamily.Purple, true)]
        public void MapCard_FamilyAndCombat(MapCardKind kind, MapFamily family, bool combat)
        {
            Assert.AreEqual(family, MapCard.FamilyOf(kind));
            Assert.AreEqual(combat, MapCard.IsCombatKind(kind));
        }

        [Test]
        public void MapCard_BuffElementOnlyKeptForKindledGround()
        {
            Assert.AreEqual(Element.Ice, new MapCard(MapCardKind.KindledGround, 4, buffElement: Element.Ice).BuffElement);
            Assert.AreEqual(Element.Physical, new MapCard(MapCardKind.Vanguard, 4, buffElement: Element.Ice).BuffElement);
        }

        // ---- RoomIntensity ----

        private static MapCard Room(MapCardKind kind, int n) =>
            new MapCard(kind, n, kind == MapCardKind.EliteTrial ? EliteTrial.Veiled : EliteTrial.None);

        [TestCase(1, IntensityBand.Low)]
        [TestCase(3, IntensityBand.Low)]
        [TestCase(4, IntensityBand.Mid)]
        [TestCase(6, IntensityBand.Mid)]
        [TestCase(7, IntensityBand.High)]
        [TestCase(9, IntensityBand.High)]
        public void Band_SplitsIntoThirds(int n, IntensityBand band) => Assert.AreEqual(band, RoomIntensity.Band(n));

        [TestCase(MapCardKind.Vanguard, 1, 3)]
        [TestCase(MapCardKind.Vanguard, 4, 4)]
        [TestCase(MapCardKind.Vanguard, 9, 5)]
        [TestCase(MapCardKind.Hexfire, 1, 2)]
        [TestCase(MapCardKind.BountyWind, 9, 4)]
        [TestCase(MapCardKind.EliteTrial, 9, 1)]
        [TestCase(MapCardKind.Boss, 7, 1)]
        public void EnemyCount_BasePlusIntensity_BigFightsStaySingle(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.EnemyCount(Room(kind, n)));

        [TestCase(MapCardKind.Vanguard, 1, 90)]
        [TestCase(MapCardKind.Vanguard, 9, 170)]
        [TestCase(MapCardKind.BountyWind, 2, 115)]
        [TestCase(MapCardKind.BountyWind, 5, 155)]
        [TestCase(MapCardKind.BountyWind, 9, 205)]
        public void EnemyHpPercent(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.EnemyHpPercent(Room(kind, n)));

        [TestCase(MapCardKind.Vanguard, 1, 95)]
        [TestCase(MapCardKind.Vanguard, 9, 135)]
        [TestCase(MapCardKind.KindledGround, 2, 110)]
        [TestCase(MapCardKind.KindledGround, 9, 165)]
        public void EnemyTempoPercent(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.EnemyTempoPercent(Room(kind, n)));

        [TestCase(MapCardKind.Vanguard, 1, 0)]
        [TestCase(MapCardKind.Vanguard, 5, 1)]
        [TestCase(MapCardKind.Vanguard, 9, 2)]
        [TestCase(MapCardKind.Hexfire, 1, 1)]
        [TestCase(MapCardKind.Hexfire, 9, 3)]
        [TestCase(MapCardKind.Boss, 7, 0)]
        [TestCase(MapCardKind.Boss, 9, 0)]
        public void EnemyCardValueBonus(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.EnemyCardValueBonus(Room(kind, n)));

        [TestCase(MapCardKind.KindledGround, 2, 1)]
        [TestCase(MapCardKind.KindledGround, 5, 2)]
        [TestCase(MapCardKind.KindledGround, 8, 3)]
        [TestCase(MapCardKind.Vanguard, 8, 0)]
        public void PlayerElementBuff(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.PlayerElementBuff(Room(kind, n)));

        [TestCase(MapCardKind.BountyWind, 3, 125)]
        [TestCase(MapCardKind.BountyWind, 6, 150)]
        [TestCase(MapCardKind.BountyWind, 7, 175)]
        [TestCase(MapCardKind.Hexfire, 7, 100)]
        public void BountyCurrencyPercent(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.BountyCurrencyPercent(Room(kind, n)));

        [TestCase(MapCardKind.Vanguard, 1, 54)]     // 60 x 0.9
        [TestCase(MapCardKind.Vanguard, 9, 102)]    // 60 x 1.7
        [TestCase(MapCardKind.EliteTrial, 5, 195)]  // 60 x 1.3 = 78, x 2.5
        [TestCase(MapCardKind.Boss, 7, 900)]        // 60 x 1.5 = 90, x 10 (M4 boss tuning after playtest)
        public void EnemyMaxHp_ScalesByIntensityAndSize(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.EnemyMaxHp(60, Room(kind, n)));

        [Test]
        public void EnemyMaxHp_RejectsBadBase() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomIntensity.EnemyMaxHp(0, Room(MapCardKind.Vanguard, 5)));

        [TestCase(MapCardKind.Vanguard, 100)]
        [TestCase(MapCardKind.EliteTrial, 150)]
        [TestCase(MapCardKind.Boss, 200)]
        public void EnemyPosturePercent_BySize(MapCardKind kind, int expected) =>
            Assert.AreEqual(expected, RoomIntensity.EnemyPosturePercent(Room(kind, 5)));

        [Test]
        public void EnemyRules_RejectBlueRooms()
        {
            var shop = new MapCard(MapCardKind.Shop, 5);
            Assert.Throws<ArgumentException>(() => RoomIntensity.EnemyCount(shop));
            Assert.Throws<ArgumentException>(() => RoomIntensity.EnemyHpPercent(shop));
            Assert.Throws<ArgumentException>(() => RoomIntensity.EnemyTempoPercent(shop));
            Assert.Throws<ArgumentException>(() => RoomIntensity.EnemyCardValueBonus(shop));
        }

        [Test]
        public void Veiled_RevealShrinksFromPoint4ToPoint15()
        {
            Assert.AreEqual(0.40f, RoomIntensity.VeiledRevealSeconds(1), 1e-5f);
            Assert.AreEqual(0.15f, RoomIntensity.VeiledRevealSeconds(9), 1e-5f);
            Assert.Less(RoomIntensity.VeiledRevealSeconds(6), RoomIntensity.VeiledRevealSeconds(5));
        }

        [TestCase(3, 1)]
        [TestCase(4, 2)]
        [TestCase(9, 3)]
        public void Counterpuncher_ZerosByBand(int n, int zeros) =>
            Assert.AreEqual(zeros, RoomIntensity.CounterpuncherZeros(n));

        [TestCase(6, 0)]
        [TestCase(7, 2)]
        public void Hexer_HpCostOnlyAtSevenPlus(int n, int hp) =>
            Assert.AreEqual(hp, RoomIntensity.HexerHpPerBreak(n));
    }
}
