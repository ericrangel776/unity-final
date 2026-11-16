using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class MapOfferGeneratorTests
    {
        private const int Seeds = 200;

        private static List<MapCard> Deal(int room, int seed, bool purpleBefore = false, Momentum m = null) =>
            MapOfferGenerator.Deal(room, purpleBefore, m ?? new Momentum(), new Random(seed));

        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 4)]
        [TestCase(4, 6)]
        [TestCase(5, 7)]
        public void Center_ClimbsWithDepth(int room, int center) => Assert.AreEqual(center, MapOfferGenerator.Center(room));

        [TestCase(0)]
        [TestCase(6)]
        public void Deal_RejectsRoomsOutsideOneToFive(int room) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Deal(room, 1));

        [Test]
        public void Deal_AlwaysThreeCards()
        {
            for (int room = 1; room <= 5; room++)
                for (int seed = 0; seed < Seeds; seed++)
                    Assert.AreEqual(3, Deal(room, seed).Count);
        }

        [Test]
        public void Room1_OnlyRedAndGreen_BothPresent_DistinctKinds()
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var offer = Deal(1, seed);
                Assert.IsTrue(offer.All(c => c.Family == MapFamily.Red || c.Family == MapFamily.Green), $"seed {seed}");
                Assert.IsTrue(offer.Any(c => c.Family == MapFamily.Red));
                Assert.IsTrue(offer.Any(c => c.Family == MapFamily.Green));
                Assert.AreEqual(3, offer.Select(c => c.Kind).Distinct().Count());
            }
        }

        [Test]
        public void Rooms2To5_ThreeDistinctFamilies_AlwaysACombatCard()
        {
            for (int room = 2; room <= 5; room++)
                for (int seed = 0; seed < Seeds; seed++)
                {
                    var offer = Deal(room, seed, purpleBefore: true);
                    Assert.AreEqual(3, offer.Select(c => c.Family).Distinct().Count(), $"room {room} seed {seed}");
                    Assert.IsTrue(offer.Any(c => c.Family == MapFamily.Red || c.Family == MapFamily.Green));
                }
        }

        [Test]
        public void Rooms2And3_PurpleSometimesOffered()
        {
            int withPurple = Enumerable.Range(0, Seeds).Count(s => Deal(2, s).Any(c => c.Family == MapFamily.Purple));
            Assert.That(withPurple, Is.InRange(1, Seeds - 1));
        }

        [Test]
        public void Room4_ForcesPurple_WhenNoneOfferedYet()
        {
            for (int seed = 0; seed < Seeds; seed++)
                Assert.IsTrue(Deal(4, seed, purpleBefore: false).Any(c => c.Kind == MapCardKind.EliteTrial), $"seed {seed}");
        }

        [Test]
        public void Room4_PurpleOptional_WhenAlreadyOffered()
        {
            int withPurple = Enumerable.Range(0, Seeds).Count(s => Deal(4, s, purpleBefore: true).Any(c => c.Family == MapFamily.Purple));
            Assert.Less(withPurple, Seeds);
        }

        [Test]
        public void Numbers_OneLowOneMidOneHigh()
        {
            for (int room = 1; room <= 5; room++)
            {
                int c = MapOfferGenerator.Center(room);
                for (int seed = 0; seed < Seeds; seed++)
                {
                    var n = Deal(room, seed).Select(card => card.Number).OrderBy(x => x).ToArray();
                    Assert.That(n[0], Is.InRange(CardValue.Clamp(c - 2), CardValue.Clamp(c - 1)), $"low, room {room}");
                    Assert.AreEqual(c, n[1], $"mid, room {room}");
                    Assert.That(n[2], Is.InRange(CardValue.Clamp(c + 1), CardValue.Clamp(c + 3)), $"high, room {room}");
                }
            }
        }

        [Test]
        public void Momentum_RaisesOnlyBlueQuality()
        {
            var streak = new Momentum();
            for (int i = 0; i < 3; i++) streak.AddStack();
            int blueSeen = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plain = Deal(3, seed, true);
                var boosted = Deal(3, seed, true, streak);
                for (int i = 0; i < 3; i++)
                {
                    Assert.AreEqual(plain[i].Kind, boosted[i].Kind);
                    int expected = plain[i].Family == MapFamily.Blue ? CardValue.Clamp(plain[i].Number + 3) : plain[i].Number;
                    if (plain[i].Family == MapFamily.Blue) blueSeen++;
                    Assert.AreEqual(expected, boosted[i].Number);
                }
            }
            Assert.Greater(blueSeen, 0);
        }

        [Test]
        public void EliteAlwaysHasATrial()
        {
            var trials = new HashSet<EliteTrial>();
            for (int seed = 0; seed < Seeds; seed++)
                foreach (var card in Deal(4, seed).Where(c => c.Kind == MapCardKind.EliteTrial))
                {
                    Assert.AreNotEqual(EliteTrial.None, card.Trial);
                    trials.Add(card.Trial);
                }
            Assert.AreEqual(3, trials.Count, "all three trials show up");
        }

        [Test]
        public void SameSeed_SameOffer() => CollectionAssert.AreEqual(Deal(3, 42), Deal(3, 42));
    }
}
