using DeckArpg.Core;
using DeckArpg.UI;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class RunUiTextTests
    {
        [TestCase(0, "0")]
        [TestCase(9, "9")]
        [TestCase(99, "99")]
        [TestCase(100, "100")]
        [TestCase(-3, "-3")]
        public void Number_FormatsLikeToString(int n, string expected)
        {
            Assert.AreEqual(expected, RunUiText.Number(n));
        }

        [Test]
        public void Number_ReusesTheSameStringForSmallValues()
        {
            Assert.AreSame(RunUiText.Number(7), RunUiText.Number(7));
        }

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
        public void Blurb_Vanguard_MentionsRushers() =>
            StringAssert.Contains("3 enemies, Rushers among them.",
                RunUiText.MapCardBlurb(new MapCard(MapCardKind.Vanguard, 2)));

        [Test]
        public void Blurb_Hexfire_MentionsCasters() =>
            StringAssert.Contains("2 enemies, Casters among them, stronger elemental cards.",
                RunUiText.MapCardBlurb(new MapCard(MapCardKind.Hexfire, 2)));

        [TestCase(MapCardKind.Vanguard, 3)]
        [TestCase(MapCardKind.Hexfire, 1)]
        [TestCase(MapCardKind.BountyWind, 2)]
        public void Blurb_LowRoom_HasNoWaveLine(MapCardKind kind, int n) =>
            StringAssert.DoesNotContain("wave 2", RunUiText.MapCardBlurb(new MapCard(kind, n)));

        [TestCase(MapCardKind.Vanguard, 4, 1)]
        [TestCase(MapCardKind.Hexfire, 6, 1)]
        [TestCase(MapCardKind.KindledGround, 7, 2)]
        [TestCase(MapCardKind.BountyWind, 9, 2)]
        public void Blurb_WaveRoom_AddsTheWaveLine(MapCardKind kind, int n, int k) =>
            StringAssert.EndsWith($"\n+{k} wave 2, currency +25%.", RunUiText.MapCardBlurb(new MapCard(kind, n)));

        [Test]
        public void Blurb_IsAtMostThreeLines()
        {
            foreach (MapCardKind kind in System.Enum.GetValues(typeof(MapCardKind)))
                for (int n = 1; n <= 9; n++)
                {
                    var trial = kind == MapCardKind.EliteTrial ? EliteTrial.Veiled : EliteTrial.None;
                    var blurb = RunUiText.MapCardBlurb(new MapCard(kind, n, trial));
                    Assert.LessOrEqual(blurb.Split('\n').Length, 3, $"{kind} {n}: {blurb}");
                }
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

        // ---- run end ----

        private static RunState NewRun(TestCards cards, out System.Collections.Generic.List<CardData> pool)
        {
            var deck = new System.Collections.Generic.List<CardData>();
            for (int i = 1; i <= 8; i++) deck.Add(cards.Data(i));
            pool = new System.Collections.Generic.List<CardData> { cards.Data(5), cards.Data(6), cards.Data(7) };
            var run = new RunState(deck, 60, seed: 3);
            run.DealOffer();
            return run;
        }

        [Test]
        public void RunEndStats_BeforeAnyRoom_AreEmpty()
        {
            var cards = new TestCards();
            var run = NewRun(cards, out _);
            Assert.AreEqual(0, RunUiText.RoomsCleared(run));
            Assert.AreEqual("-", RunUiText.AverageIntensity(run));
            Assert.AreEqual("", RunUiText.Path(run));
            Assert.AreEqual(0, RunUiText.RoomsCleared(null));
            cards.DestroyAll();
        }

        [Test]
        public void RoomsCleared_DoesNotCountTheRoomInProgress()
        {
            var cards = new TestCards();
            var run = NewRun(cards, out var pool);
            var room = run.EnterRoom(0);
            Assert.AreEqual(0, RunUiText.RoomsCleared(run), "died in room 1");
            run.CompleteRoom(pool);
            Assert.AreEqual(1, RunUiText.RoomsCleared(run));
            Assert.AreEqual(room.Number.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                RunUiText.AverageIntensity(run));
            Assert.AreEqual($"{RunUiText.MapCardName(room.Kind)} {room.Number}", RunUiText.Path(run));
            cards.DestroyAll();
        }

        // ---- cards ----

        [Test]
        public void CardValue_AddsUpgrade_ClampedToNine()
        {
            var card = CardData.Create("c", 8);
            Assert.AreEqual("8", RunUiText.CardValue(card));
            Assert.AreEqual("9", RunUiText.CardValue(card, 1));
            Assert.AreEqual("9", RunUiText.CardValue(card, 3));
            UnityEngine.Object.DestroyImmediate(card);
        }

        [Test]
        public void CardValue_ZeroCard_StaysZero_EvenUpgraded()
        {
            var zero = CardData.Create("z", 1, zeroBreaker: true);
            Assert.AreEqual("0", RunUiText.CardValue(zero, 1));
            StringAssert.DoesNotContain("Pre-upgraded", RunUiText.CardDetails(zero, 1));
            UnityEngine.Object.DestroyImmediate(zero);
        }

        [Test]
        public void CardDetails_HealCard_ReadsHealAmountAndChannel()
        {
            var mend = CardData.Create("mend_3", 3, heal: true);
            Assert.AreEqual("Heal · restores 9 HP (0.5 s channel)", RunUiText.CardDetails(mend));
            StringAssert.StartsWith("Heal · restores 15 HP (0.5 s channel)", RunUiText.CardDetails(mend, 2));
            UnityEngine.Object.DestroyImmediate(mend);
        }

        [Test]
        public void HandValue_HealCardShowsPlus_OthersShowValue()
        {
            var mend = CardData.Create("mend_5", 5, heal: true);
            var strike = CardData.Create("strike_5", 5);
            Assert.AreEqual("+5", RunUiText.HandValue(new RuntimeCardInstance(mend)));
            Assert.AreEqual("5", RunUiText.HandValue(new RuntimeCardInstance(strike)));
            Assert.AreEqual("", RunUiText.HandValue(null));
            UnityEngine.Object.DestroyImmediate(mend);
            UnityEngine.Object.DestroyImmediate(strike);
        }

        [Test]
        public void HandKind_HealCardSaysHeal_OthersShowDelivery()
        {
            var mend = CardData.Create("mend_3", 3, heal: true);
            var ember = CardData.Create("ember_3", 3, CardShape.Throw, Element.Fire);
            Assert.AreEqual("Heal", RunUiText.HandKind(mend));
            Assert.AreEqual(RunUiText.DeliveryName(ember), RunUiText.HandKind(ember));
            Assert.AreEqual("Fireball", RunUiText.HandKind(ember));
            UnityEngine.Object.DestroyImmediate(mend);
            UnityEngine.Object.DestroyImmediate(ember);
        }

        [Test]
        public void ShopHealLabel_StatesCostOrWhyDisabled()
        {
            Assert.AreEqual("Heal 25 HP (40)", RunUiText.ShopHealLabel(25, false, false, 100));
            Assert.AreEqual("Heal (bought)", RunUiText.ShopHealLabel(25, true, false, 100));
            Assert.AreEqual("Heal (full HP)", RunUiText.ShopHealLabel(25, false, true, 100));
            Assert.AreEqual("Heal 40 (need 15 more)", RunUiText.ShopHealLabel(25, false, false, 25));
        }

        [Test]
        public void ShopDraughtLabel_StatesCostOrWhyDisabled()
        {
            Assert.AreEqual("Max HP +10 (60)", RunUiText.ShopDraughtLabel(false, 60));
            Assert.AreEqual("Draught (bought)", RunUiText.ShopDraughtLabel(true, 100));
            Assert.AreEqual("Draught 60 (need 59 more)", RunUiText.ShopDraughtLabel(false, 1));
        }

        [Test]
        public void HeartwoodSeed_Text()
        {
            Assert.AreEqual("Heartwood Seed", RunUiText.RelicName(RelicId.HeartwoodSeed));
            Assert.AreEqual("Seed", RunUiText.RelicShortName(RelicId.HeartwoodSeed));
            StringAssert.StartsWith("+15 max HP", RunUiText.RelicDescription(RelicId.HeartwoodSeed));
            StringAssert.Contains("Overburn", RunUiText.RelicDescription(RelicId.HeartwoodSeed));
        }

        [Test]
        public void CardDetails_ListsDeliveryElement_PremiumAndUpgrade()
        {
            var card = CardData.Create("c", 4, CardShape.Throw, Element.Fire, premium: true);
            var details = RunUiText.CardDetails(card, 1);
            StringAssert.StartsWith("Fireball · Fire", details);
            StringAssert.Contains("Premium", details);
            StringAssert.Contains("Pre-upgraded +1", details);
            StringAssert.DoesNotContain("Pre-upgraded", RunUiText.CardDetails(card));
            UnityEngine.Object.DestroyImmediate(card);
        }

        [Test]
        public void DeckCardLabel_ShowsNameWithUpgradeAndPremiumTags()
        {
            var data = CardData.Create("c", 4, displayName: "Spark 4");
            var card = new RuntimeCardInstance(data);
            Assert.AreEqual("Spark 4", RunUiText.DeckCardLabel(card));
            card.PermanentRunUpgradeMod = 2;
            Assert.AreEqual("Spark 4  +2", RunUiText.DeckCardLabel(card));
            card.IsPremiumOverride = true;
            Assert.AreEqual("Spark 4  +2  P", RunUiText.DeckCardLabel(card));
            UnityEngine.Object.DestroyImmediate(data);
        }

        // ---- Blue-room previews (docs/specs/m3-blue-polish.md) ----

        [Test]
        public void DeckValue_AddsUpgradesAndPremium_ClampedToNine()
        {
            var data = CardData.Create("c", 7, displayName: "Strike 7");
            var card = new RuntimeCardInstance(data);
            Assert.AreEqual(7, RunUiText.DeckValue(card));
            Assert.AreEqual(9, RunUiText.DeckValue(card, 1, asPremium: true), "7 + 1 + 2 clamps to 9");
            card.PermanentRunUpgradeMod = 1;
            Assert.AreEqual(8, RunUiText.DeckValue(card));
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void DeckCell_ShowsValueNameKind_AndTheBlockedReason()
        {
            var data = CardData.Create("c", 4, CardShape.Arcana, Element.Ice, displayName: "Frost 4");
            var card = new RuntimeCardInstance(data) { PermanentRunUpgradeMod = 2 };
            Assert.AreEqual("6   Frost 4  +2   ·   Frost Blast · Ice", RunUiText.DeckCell(card));
            Assert.AreEqual("6   Frost 4  +2   ·   Frost Blast · Ice   (already Premium)", RunUiText.DeckCell(card, "already Premium"));
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void DeckCell_HealCard_SaysHeal()
        {
            var data = CardData.Create("mend_3", 3, heal: true, displayName: "Mend 3");
            var card = new RuntimeCardInstance(data);
            Assert.AreEqual("3   Mend 3   ·   Heal", RunUiText.DeckCell(card));
            Assert.AreEqual("3   Mend 3   ·   Heal   (already Premium)", RunUiText.DeckCell(card, "already Premium"));
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void StatusChip_ShowsSecondsRoundedUpToATenth()
        {
            Assert.AreEqual("BURN 2.5s", RunUiText.StatusChip("BURN", 2.5f));
            Assert.AreEqual("CHILL 1.9s", RunUiText.StatusChip("CHILL", 1.81f));
            Assert.AreEqual("BURN 0.1s", RunUiText.StatusChip("BURN", 0.02f));
            Assert.AreEqual("BURN 0.0s", RunUiText.StatusChip("BURN", -1f));
        }

        [TestCase(CardShape.Strike, Element.Physical, "Strike")]
        [TestCase(CardShape.Throw, Element.Physical, "Throw")]
        [TestCase(CardShape.Arcana, Element.Physical, "Burst")]
        [TestCase(CardShape.Throw, Element.Fire, "Fireball")]
        [TestCase(CardShape.Arcana, Element.Ice, "Frost Blast")]
        [TestCase(CardShape.Throw, Element.Lightning, "Chain Arc")]
        [TestCase(CardShape.Strike, Element.Wind, "Gust")]
        public void DeliveryName_FollowsTheElementDecidesDeliveryRule(CardShape shape, Element element, string expected)
        {
            var data = CardData.Create("d", 4, shape, element);
            Assert.AreEqual(expected, RunUiText.DeliveryName(data));
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void Previews_ShowTheChange()
        {
            var data = CardData.Create("c", 4, displayName: "Strike 4");
            var card = new RuntimeCardInstance(data);
            Assert.AreEqual("Strike 4: 4 -> 6", RunUiText.UpgradePreview(card, 2));
            Assert.AreEqual("Strike 4: 4 -> 6, burns when played", RunUiText.PremiumPreview(card));
            Assert.AreEqual("Remove Strike 4: -40 currency, deck 10 -> 9", RunUiText.PurgePreview(card, 40, 10));
            UnityEngine.Object.DestroyImmediate(data);
        }

        [TestCase(80, 50, "Need 30 more currency")]
        [TestCase(50, 50, "")]
        [TestCase(30, 50, "")]
        public void Shortfall_SaysHowMuchIsMissing(int price, int currency, string expected)
        {
            Assert.AreEqual(expected, RunUiText.Shortfall(price, currency));
        }

        [Test]
        public void BlockedReasons_ExplainZeroCardsMaxValueAndPremium()
        {
            var zeroData = CardData.Create("z", 0, zeroBreaker: true);
            var highData = CardData.Create("h", 9);
            var okData = CardData.Create("o", 4);
            var zero = new RuntimeCardInstance(zeroData);
            var high = new RuntimeCardInstance(highData);
            var ok = new RuntimeCardInstance(okData);

            Assert.AreEqual("0-cards can't change", RunUiText.UpgradeBlockedReason(zero));
            Assert.AreEqual("0-cards can't change", RunUiText.PremiumBlockedReason(zero));
            Assert.AreEqual("already at 9", RunUiText.UpgradeBlockedReason(high));
            Assert.IsNull(RunUiText.UpgradeBlockedReason(ok));
            Assert.IsNull(RunUiText.PremiumBlockedReason(ok));
            ok.IsPremiumOverride = true;
            Assert.AreEqual("already Premium", RunUiText.PremiumBlockedReason(ok));

            foreach (var d in new[] { zeroData, highData, okData }) UnityEngine.Object.DestroyImmediate(d);
        }

        [Test]
        public void RelicNameAndDescription_AreDefinedForEveryRelic()
        {
            foreach (RelicId relic in System.Enum.GetValues(typeof(RelicId)))
            {
                Assert.AreNotEqual(relic.ToString(), RunUiText.RelicName(relic), $"{relic} has no name");
                Assert.IsNotEmpty(RunUiText.RelicDescription(relic), $"{relic} has no description");
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
