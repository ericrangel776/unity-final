using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class EnemyVariantRulesTests
    {
        private static MapCard Room(MapCardKind kind, int n) =>
            new MapCard(kind, n, kind == MapCardKind.EliteTrial ? EliteTrial.Veiled : EliteTrial.None);

        private static int CountOf(IReadOnlyList<EnemyVariant> mix, EnemyVariant v) => mix.Count(x => x == v);

        // ---- Composition ----

        [TestCase(1, 1, 0)]
        [TestCase(2, 1, 1)]
        [TestCase(3, 2, 1)]
        [TestCase(4, 2, 2)]
        public void Composition_HexfireIsHalfCastersRoundedUp(int n, int casters, int brutes)
        {
            var mix = EnemyVariantRules.Composition(Room(MapCardKind.Hexfire, 5), n, new Random(1));
            Assert.AreEqual(n, mix.Count);
            Assert.AreEqual(casters, CountOf(mix, EnemyVariant.Caster));
            Assert.AreEqual(brutes, CountOf(mix, EnemyVariant.Brute));
        }

        [TestCase(1, 0, 1)]
        [TestCase(2, 1, 1)]
        [TestCase(3, 1, 2)]
        [TestCase(4, 2, 2)]
        public void Composition_VanguardIsHalfRushersRoundedDown(int n, int rushers, int brutes)
        {
            var mix = EnemyVariantRules.Composition(Room(MapCardKind.Vanguard, 5), n, new Random(1));
            Assert.AreEqual(n, mix.Count);
            Assert.AreEqual(rushers, CountOf(mix, EnemyVariant.Rusher));
            Assert.AreEqual(brutes, CountOf(mix, EnemyVariant.Brute));
        }

        [Test]
        public void Composition_BrutesComeFirst()
        {
            var mix = EnemyVariantRules.Composition(Room(MapCardKind.Vanguard, 5), 4, null);
            CollectionAssert.AreEqual(
                new[] { EnemyVariant.Brute, EnemyVariant.Brute, EnemyVariant.Rusher, EnemyVariant.Rusher }, mix);
        }

        [TestCase(MapCardKind.KindledGround)]
        [TestCase(MapCardKind.BountyWind)]
        public void Composition_GreenHasOneSeededVariant_AndSeedsCoverBoth(MapCardKind kind)
        {
            var seen = new HashSet<EnemyVariant>();
            for (int seed = 0; seed < 20; seed++)
            {
                var mix = EnemyVariantRules.Composition(Room(kind, 5), 3, new Random(seed));
                Assert.AreEqual(2, CountOf(mix, EnemyVariant.Brute), $"seed {seed}");
                Assert.AreEqual(EnemyVariant.Brute, mix[0]);
                seen.Add(mix[2]);

                var again = EnemyVariantRules.Composition(Room(kind, 5), 3, new Random(seed));
                CollectionAssert.AreEqual(mix, again, "same seed, same mix");
            }
            CollectionAssert.AreEquivalent(new[] { EnemyVariant.Caster, EnemyVariant.Rusher }, seen);
        }

        [Test]
        public void Composition_GreenWaveOfOneIsTheVariant()
        {
            var mix = EnemyVariantRules.Composition(Room(MapCardKind.BountyWind, 5), 1, new Random(3));
            Assert.AreNotEqual(EnemyVariant.Brute, mix[0]);
        }

        [TestCase(MapCardKind.EliteTrial)]
        [TestCase(MapCardKind.Boss)]
        public void Composition_BigFightsAreAllBrutes(MapCardKind kind)
        {
            var mix = EnemyVariantRules.Composition(Room(kind, 9), 1, null);
            CollectionAssert.AreEqual(new[] { EnemyVariant.Brute }, mix);
        }

        [Test]
        public void Composition_RejectsNonCombatRoom() =>
            Assert.Throws<ArgumentException>(() =>
                EnemyVariantRules.Composition(new MapCard(MapCardKind.Shop, 5), 2, new Random(1)));

        [TestCase(0)]
        [TestCase(-1)]
        public void Composition_RejectsCountBelowOne(int n) =>
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                EnemyVariantRules.Composition(Room(MapCardKind.Vanguard, 5), n, new Random(1)));

        // ---- Wave two ----

        [TestCase(MapCardKind.Vanguard, 3, 0)]
        [TestCase(MapCardKind.Vanguard, 4, 1)]
        [TestCase(MapCardKind.Hexfire, 6, 1)]
        [TestCase(MapCardKind.KindledGround, 7, 2)]
        [TestCase(MapCardKind.BountyWind, 9, 2)]
        [TestCase(MapCardKind.Hexfire, 1, 0)]
        [TestCase(MapCardKind.EliteTrial, 9, 0)]
        [TestCase(MapCardKind.Boss, 9, 0)]
        public void WaveTwoCount_ByBandAndKind(MapCardKind kind, int n, int expected) =>
            Assert.AreEqual(expected, EnemyVariantRules.WaveTwoCount(Room(kind, n)));

        [Test]
        public void WaveTwoCount_RejectsNonCombatRoom() =>
            Assert.Throws<ArgumentException>(() => EnemyVariantRules.WaveTwoCount(new MapCard(MapCardKind.Rest, 5)));

        // ---- Stats ----

        [TestCase(EnemyVariant.Brute, 100, 100, 100, 100, 100)]
        [TestCase(EnemyVariant.Rusher, 180, 50, 75, 50, 60)]
        [TestCase(EnemyVariant.Caster, 90, 70, 100, 100, 80)]
        public void Stats_MatchTheSpecTable(EnemyVariant v, int move, int hp, int telegraph, int recover, int posture)
        {
            var s = EnemyVariantRules.Stats(v);
            Assert.AreEqual(move, s.MovePercent);
            Assert.AreEqual(hp, s.HpPercent);
            Assert.AreEqual(telegraph, s.TelegraphPercent);
            Assert.AreEqual(recover, s.RecoverPercent);
            Assert.AreEqual(posture, s.PosturePercent);
        }

        // ---- Caster movement ----

        [TestCase(3.99f, CasterMove.Retreat)]
        [TestCase(4f, CasterMove.Hold)]
        [TestCase(7f, CasterMove.Hold)]
        [TestCase(7.01f, CasterMove.Advance)]
        [TestCase(9f, CasterMove.Advance)]
        [TestCase(9.01f, CasterMove.Advance)]
        public void CasterMovement_Boundaries(float d, CasterMove expected) =>
            Assert.AreEqual(expected, EnemyVariantRules.CasterMovement(d));

        [TestCase(3.99f, 0f, false)]
        [TestCase(4f, 0f, true)]
        [TestCase(7f, 0f, true)]
        [TestCase(9f, 0f, true)]
        [TestCase(9.01f, 0f, false)]
        [TestCase(2f, 1.49f, false)]
        [TestCase(2f, 1.5f, true)]
        public void CasterMayCast_RangeOrCornered(float d, float close, bool expected) =>
            Assert.AreEqual(expected, EnemyVariantRules.CasterMayCast(d, close));
    }
}
