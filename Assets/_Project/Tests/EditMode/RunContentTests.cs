using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;
using UnityEditor;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.EditMode
{
    public class RunContentTests
    {
        private const string AssetPath = "Assets/_Project/ScriptableObjects/RunContent.asset";

        private readonly List<Object> created = new List<Object>();
        private int nextId;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        private CardData Card(int value, CardShape shape = CardShape.Strike, Element element = Element.Physical,
            Rarity rarity = Rarity.Common, bool zero = false, string id = null)
        {
            var c = CardData.Create(id ?? $"rc_{nextId++}", value, shape, element, zero, rarity: rarity);
            created.Add(c);
            return c;
        }

        private List<CardData> Cards(int count, params int[] values) =>
            Enumerable.Range(0, count).Select(i => Card(values.Length > 0 ? values[i % values.Length] : 5)).ToList();

        /// <summary>A pool that covers every rarity, shape and element, plus a 0-card, with 15+ cards.</summary>
        private List<CardData> ValidPool()
        {
            var pool = new List<CardData>();
            var shapes = (CardShape[])Enum.GetValues(typeof(CardShape));
            var elements = (Element[])Enum.GetValues(typeof(Element));
            var rarities = (Rarity[])Enum.GetValues(typeof(Rarity));
            for (int i = 0; i < 15; i++)
                pool.Add(Card(1 + i % 9, shapes[i % shapes.Length], elements[i % elements.Length], rarities[i % rarities.Length]));
            pool.Add(Card(1, zero: true, rarity: Rarity.Rare));
            return pool;
        }

        private RunContent Content(List<CardData> starter = null, List<CardData> pool = null, List<CardData> mob = null,
            List<CardData> hex = null, List<CardData> elite = null, List<CardData> boss = null, CardData zero = null,
            bool noZero = false, List<CardData> caster = null, List<CardData> rusher = null)
        {
            var content = RunContent.Create(starter ?? Cards(10), pool ?? ValidPool(), mob ?? Cards(5),
                hex ?? Cards(5), elite ?? Cards(6), boss ?? Cards(RunContent.BossDeckSize),
                caster ?? Cards(4), rusher ?? Cards(6),
                noZero ? null : zero ?? Card(1, zero: true));
            created.Add(content);
            return content;
        }

        private static void AssertProblem(RunContent content, string fragment)
        {
            var problems = content.Validate();
            Assert.IsTrue(problems.Any(p => p.Contains(fragment)),
                $"Expected a problem containing '{fragment}', got: [{string.Join(" | ", problems)}]");
        }

        // ---- validation rules ----

        [Test]
        public void Validate_ValidContent_HasNoProblems() => CollectionAssert.IsEmpty(Content().Validate());

        [Test]
        public void Validate_SmallStarterDeck() => AssertProblem(Content(starter: Cards(5)), "StarterDeck has 5");

        [Test]
        public void Validate_SmallDraftPool() => AssertProblem(Content(pool: ValidPool().Take(10).ToList()), "DraftPool has 10");

        [Test]
        public void Validate_DuplicateDraftEntry()
        {
            var pool = ValidPool();
            pool.Add(pool[0]);
            AssertProblem(Content(pool: pool), "same card twice");
        }

        [Test]
        public void Validate_PoolMissingRarity() =>
            AssertProblem(Content(pool: ValidPool().Where(c => c.Rarity != Rarity.Uncommon).Concat(Cards(5)).ToList()),
                "no Uncommon");

        [Test]
        public void Validate_PoolMissingElement() =>
            AssertProblem(Content(pool: ValidPool().Where(c => c.IsZeroBreaker || c.Element != Element.Ice).Concat(Cards(5)).ToList()),
                "no Ice");

        [Test]
        public void Validate_PoolMissingShape() =>
            AssertProblem(Content(pool: ValidPool().Where(c => c.IsZeroBreaker || c.Shape != CardShape.Arcana).Concat(Cards(5)).ToList()),
                "no Arcana");

        [Test]
        public void Validate_PoolWithoutZero() =>
            AssertProblem(Content(pool: ValidPool().Where(c => !c.IsZeroBreaker).ToList()), "no 0-card");

        [Test]
        public void Validate_EnemyDeckAtFloor() => AssertProblem(Content(mob: Cards(3)), "MobDeck has 3");

        [Test]
        public void Validate_EnemyDeckWithZero()
        {
            var elite = Cards(5);
            elite.Add(Card(1, zero: true));
            AssertProblem(Content(elite: elite), "EliteDeck contains 0-card");
        }

        [TestCase(14)]
        [TestCase(16)]
        public void Validate_BossDeckMustBeExactlyFifteen(int size) =>
            AssertProblem(Content(boss: Cards(size)), $"BossDeck has {size}");

        [Test]
        public void Validate_CasterDeckAtFloor() => AssertProblem(Content(caster: Cards(3)), "CasterDeck has 3");

        [Test]
        public void Validate_RusherDeckAtFloor() => AssertProblem(Content(rusher: Cards(3)), "RusherDeck has 3");

        [Test]
        public void Validate_CasterDeckWithZero()
        {
            var caster = Cards(4);
            caster.Add(Card(1, zero: true));
            AssertProblem(Content(caster: caster), "CasterDeck contains 0-card");
        }

        [Test]
        public void Validate_RusherDeckWithZero()
        {
            var rusher = Cards(4);
            rusher.Add(Card(1, zero: true));
            AssertProblem(Content(rusher: rusher), "RusherDeck contains 0-card");
        }

        [Test]
        public void Validate_VariantDeckNullEntries()
        {
            var caster = Cards(4);
            caster.Add(null);
            var rusher = Cards(4);
            rusher.Add(null);
            var content = Content(caster: caster, rusher: rusher);
            AssertProblem(content, "CasterDeck has an empty entry");
            AssertProblem(content, "RusherDeck has an empty entry");
        }

        [Test]
        public void Validate_DuplicateIdAcrossVariantDecks()
        {
            var caster = Cards(4);
            caster.Add(Card(5, id: "vdupe"));
            var rusher = Cards(4);
            rusher.Add(Card(2, id: "vdupe"));
            AssertProblem(Content(caster: caster, rusher: rusher), "'vdupe' is used by two");
        }

        [Test]
        public void Validate_DraftPoolCannotHoldVariantCards()
        {
            var pool = ValidPool();
            var caster = Cards(4);
            var rusher = Cards(4);
            pool.Add(caster[0]);
            pool.Add(rusher[0]);
            var problems = Content(pool: pool, caster: caster, rusher: rusher).Validate();
            Assert.AreEqual(2, problems.Count(p => p.Contains("is an enemy card")), string.Join(" | ", problems));
        }

        [Test]
        public void Validate_EnemyZeroMissing() => AssertProblem(Content(noZero: true), "EnemyZero is missing");

        [Test]
        public void Validate_EnemyZeroNotAZero() => AssertProblem(Content(zero: Card(5)), "not a 0-card");

        [Test]
        public void Validate_DuplicateIdAcrossAssets()
        {
            var mob = Cards(4);
            mob.Add(Card(5, id: "dupe"));
            var hex = Cards(4);
            hex.Add(Card(6, id: "dupe"));
            AssertProblem(Content(mob: mob, hex: hex), "'dupe' is used by two");
        }

        [Test]
        public void Validate_SameAssetInTwoListsIsFine()
        {
            var shared = Card(5);
            var mob = Cards(4);
            mob.Add(shared);
            var hex = Cards(4);
            hex.Add(shared);
            CollectionAssert.IsEmpty(Content(mob: mob, hex: hex).Validate());
        }

        [Test]
        public void Validate_DraftPoolCannotHoldEnemyCards()
        {
            var pool = ValidPool();
            var mob = Cards(5);
            pool.Add(mob[0]);
            AssertProblem(Content(pool: pool, mob: mob), "is an enemy card");
        }

        [Test]
        public void Validate_NullEntry()
        {
            var hex = Cards(5);
            hex.Add(null);
            AssertProblem(Content(hex: hex), "HexfireDeck has an empty entry");
        }

        // ---- deck lookup ----

        [TestCase(MapCardKind.Vanguard)]
        [TestCase(MapCardKind.KindledGround)]
        [TestCase(MapCardKind.BountyWind)]
        public void EnemyDeckFor_MobRooms(MapCardKind kind)
        {
            var content = Content();
            Assert.AreSame(content.MobDeck, content.EnemyDeckFor(kind));
        }

        [Test]
        public void EnemyDeckFor_SpecialRooms()
        {
            var content = Content();
            Assert.AreSame(content.HexfireDeck, content.EnemyDeckFor(MapCardKind.Hexfire));
            Assert.AreSame(content.EliteDeck, content.EnemyDeckFor(MapCardKind.EliteTrial));
            Assert.AreSame(content.BossDeck, content.EnemyDeckFor(MapCardKind.Boss));
        }

        [TestCase(MapCardKind.Shop)]
        [TestCase(MapCardKind.Forge)]
        [TestCase(MapCardKind.Rest)]
        public void EnemyDeckFor_BlueRoomsThrow(MapCardKind kind) =>
            Assert.Throws<ArgumentException>(() => Content().EnemyDeckFor(kind));

        [TestCase(MapCardKind.Shop, EnemyVariant.Caster)]
        [TestCase(MapCardKind.Rest, EnemyVariant.Rusher)]
        public void EnemyDeckFor_VariantInBlueRoomThrows(MapCardKind kind, EnemyVariant variant) =>
            Assert.Throws<ArgumentException>(() => Content().EnemyDeckFor(kind, variant));

        [TestCase(MapCardKind.Vanguard)]
        [TestCase(MapCardKind.Hexfire)]
        [TestCase(MapCardKind.KindledGround)]
        [TestCase(MapCardKind.BountyWind)]
        public void EnemyDeckFor_VariantsUseTheirOwnDeckInAnyMobRoom(MapCardKind kind)
        {
            var content = Content();
            Assert.AreSame(content.CasterDeck, content.EnemyDeckFor(kind, EnemyVariant.Caster));
            Assert.AreSame(content.RusherDeck, content.EnemyDeckFor(kind, EnemyVariant.Rusher));
            Assert.AreSame(content.EnemyDeckFor(kind), content.EnemyDeckFor(kind, EnemyVariant.Brute));
        }

        [Test]
        public void GeneratedCasterDeck_IsProjectileCards_3456()
        {
            var deck = Generated().CasterDeck;
            CollectionAssert.AreEqual(new[] { "caster_bolt_3", "caster_shard_4", "caster_bolt_5", "caster_shard_6" },
                deck.Select(c => c.CardId));
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 6 }, deck.Select(c => c.BaseValue));
            foreach (var c in deck)
            {
                Assert.AreEqual(CardShape.Throw, c.Shape, c.CardId);
                Assert.That(c.Element, Is.EqualTo(Element.Fire).Or.EqualTo(Element.Physical), c.CardId);
            }
        }

        [Test]
        public void GeneratedRusherDeck_IsLowPhysicalStrikes()
        {
            var deck = Generated().RusherDeck;
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 3, 3, 4 }, deck.Select(c => c.BaseValue));
            foreach (var c in deck)
            {
                Assert.AreEqual(CardShape.Strike, c.Shape, c.CardId);
                Assert.AreEqual(Element.Physical, c.Element, c.CardId);
                Assert.IsFalse(c.IsHeal || c.IsZeroBreaker, c.CardId);
            }
        }

        // ---- the generated asset (DeckArpg/Build Run Content) ----

        private static RunContent Generated()
        {
            var content = AssetDatabase.LoadAssetAtPath<RunContent>(AssetPath);
            Assert.IsNotNull(content, $"Missing {AssetPath}; run DeckArpg/Build Run Content.");
            return content;
        }

        [Test]
        public void GeneratedContent_IsValid() => CollectionAssert.IsEmpty(Generated().Validate());

        [Test]
        public void GeneratedPool_HasTwentyThreeCards_NineEightSix()
        {
            var pool = Generated().DraftPool;
            Assert.AreEqual(23, pool.Count);
            Assert.AreEqual(9, pool.Count(c => c.Rarity == Rarity.Common));
            Assert.AreEqual(8, pool.Count(c => c.Rarity == Rarity.Uncommon));
            Assert.AreEqual(6, pool.Count(c => c.Rarity == Rarity.Rare));
        }

        [Test]
        public void GeneratedPool_HasThreeHealCards_Mend357_OneOfEachRarity()
        {
            var heals = Generated().DraftPool.Where(c => c.IsHeal).OrderBy(c => c.BaseValue).ToList();
            CollectionAssert.AreEqual(new[] { "mend_3", "mend_5", "mend_7" }, heals.Select(c => c.CardId));
            CollectionAssert.AreEqual(new[] { 3, 5, 7 }, heals.Select(c => c.BaseValue));
            CollectionAssert.AreEqual(new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare }, heals.Select(c => c.Rarity));
        }

        [Test]
        public void GeneratedHealCards_ArePhysicalStrike()
        {
            var content = Generated();
            var heals = content.DraftPool.Concat(content.StarterDeck).Where(c => c != null && c.IsHeal).ToList();
            Assert.IsNotEmpty(heals);
            foreach (var c in heals)
            {
                Assert.AreEqual(Element.Physical, c.Element, c.CardId);
                Assert.AreEqual(CardShape.Strike, c.Shape, c.CardId);
            }
        }

        [Test]
        public void GeneratedContent_NoZeroBreakerIsAHeal()
        {
            var content = Generated();
            var all = content.DraftPool.Concat(content.StarterDeck).Concat(content.MobDeck).Concat(content.HexfireDeck)
                .Concat(content.EliteDeck).Concat(content.BossDeck).Append(content.EnemyZero).Where(c => c != null);
            foreach (var c in all) Assert.IsFalse(c.IsZeroBreaker && c.IsHeal, c.CardId);
        }

        [Test]
        public void GeneratedEnemyDecks_HaveNoHealCards()
        {
            var content = Generated();
            var enemy = content.MobDeck.Concat(content.HexfireDeck).Concat(content.EliteDeck).Concat(content.BossDeck)
                .Concat(content.CasterDeck).Concat(content.RusherDeck);
            Assert.IsFalse(enemy.Any(c => c != null && c.IsHeal), "enemy decks stay as they are");
        }

        [Test]
        public void GeneratedPool_HasWindCards_OneOfEachRarity()
        {
            var wind = Generated().DraftPool.Where(c => c.Element == Element.Wind).ToList();
            Assert.AreEqual(3, wind.Count);
            CollectionAssert.AreEquivalent(new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare }, wind.Select(c => c.Rarity));
        }

        [TestCase(Element.Fire, CardShape.Throw)]
        [TestCase(Element.Ice, CardShape.Arcana)]
        [TestCase(Element.Lightning, CardShape.Throw)]
        [TestCase(Element.Wind, CardShape.Strike)]
        public void GeneratedCards_AuthorTheShapeThatMatchesTheirDelivery(Element element, CardShape shape)
        {
            var content = Generated();
            var all = content.DraftPool.Concat(content.StarterDeck).Concat(content.HexfireDeck)
                .Where(c => c != null && !c.IsZeroBreaker && c.Element == element).ToList();
            Assert.IsNotEmpty(all);
            foreach (var c in all) Assert.AreEqual(shape, c.Shape, c.CardId);
        }

        [Test]
        public void GeneratedStarterDeck_IsTheM1DeckPlusMend()
        {
            var starter = Generated().StarterDeck;
            Assert.AreEqual(11, starter.Count);
            Assert.AreEqual(1, starter.Count(c => c.IsZeroBreaker));
            Assert.AreEqual(1, starter.Count(c => c.IsHeal));
            Assert.AreEqual("mend_3", starter.Single(c => c.IsHeal).CardId);
        }

        [Test]
        public void GeneratedPool_WorksWithTheDraftRoller()
        {
            var pool = Generated().DraftPool;
            for (int seed = 0; seed < 50; seed++)
                Assert.AreEqual(5, RewardRoller.RollCards(pool, 5, 2, new Random(seed)).Distinct().Count());
        }
    }
}
