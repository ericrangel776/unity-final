using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class ClashResolverTests
    {
        private static AttackPayload Player(int value, int tick = 10, bool sleight = false) =>
            new AttackPayload(1, Team.Player, value, false, sleight, tick, CardShape.Strike, Element.Physical);

        private static AttackPayload Enemy(int value, int tick = 10, bool sleight = false) =>
            new AttackPayload(2, Team.Enemy, value, false, sleight, tick, CardShape.Strike, Element.Physical);

        private static AttackPayload PlayerZero(int tick) =>
            new AttackPayload(1, Team.Player, 0, true, false, tick, CardShape.Strike, Element.Physical);

        private static AttackPayload EnemyZero(int tick) =>
            new AttackPayload(2, Team.Enemy, 0, true, false, tick, CardShape.Strike, Element.Physical);

        [Test]
        public void HigherValue_Wins()
        {
            var r = ClashResolver.Resolve(Player(7), Enemy(5));
            Assert.AreEqual(ClashOutcome.AWins, r.Outcome);
            Assert.IsFalse(r.BrokeByZero);
        }

        [Test]
        public void HigherValue_Wins_RegardlessOfArgumentOrder()
        {
            Assert.AreEqual(ClashOutcome.BWins, ClashResolver.Resolve(Enemy(5), Player(7)).Outcome);
        }

        [Test]
        public void EqualValues_AreNeutral()
        {
            Assert.AreEqual(ClashOutcome.Neutral, ClashResolver.Resolve(Player(6), Enemy(6)).Outcome);
        }

        [Test]
        public void Sleight_BeatsSingleNine()
        {
            Assert.AreEqual(ClashOutcome.BWins, ClashResolver.Resolve(Player(9), Enemy(15, sleight: true)).Outcome);
        }

        [Test]
        public void Zero_PlayedSecond_BreaksNine()
        {
            var r = ClashResolver.Resolve(PlayerZero(tick: 11), Enemy(9, tick: 10));
            Assert.AreEqual(ClashOutcome.AWins, r.Outcome);
            Assert.IsTrue(r.BrokeByZero);
        }

        [Test]
        public void Zero_PlayedSecond_BreaksSleight()
        {
            var r = ClashResolver.Resolve(Enemy(24, tick: 10, sleight: true), PlayerZero(tick: 30));
            Assert.AreEqual(ClashOutcome.BWins, r.Outcome);
            Assert.IsTrue(r.BrokeByZero);
        }

        [Test]
        public void Zero_PlayedFirst_LosesToAnyCard()
        {
            var r = ClashResolver.Resolve(PlayerZero(tick: 9), Enemy(1, tick: 10));
            Assert.AreEqual(ClashOutcome.BWins, r.Outcome);
            Assert.IsFalse(r.BrokeByZero);
        }

        [Test]
        public void Zero_SameTick_Bounces()
        {
            Assert.AreEqual(ClashOutcome.Neutral, ClashResolver.Resolve(PlayerZero(tick: 10), Enemy(9, tick: 10)).Outcome);
        }

        [Test]
        public void Zero_VersusZero_IsNeutral()
        {
            Assert.AreEqual(ClashOutcome.Neutral, ClashResolver.Resolve(PlayerZero(tick: 5), EnemyZero(tick: 50)).Outcome);
        }

        [Test]
        public void SameTeam_DoesNotClash()
        {
            Assert.AreEqual(ClashOutcome.None, ClashResolver.Resolve(Player(9), Player(1)).Outcome);
        }

        [Test]
        public void EnemyZero_PlayedSecond_BreaksPlayerCard()
        {
            var r = ClashResolver.Resolve(Player(9, tick: 3), EnemyZero(tick: 4));
            Assert.AreEqual(ClashOutcome.BWins, r.Outcome);
            Assert.IsTrue(r.BrokeByZero);
        }
    }
}
