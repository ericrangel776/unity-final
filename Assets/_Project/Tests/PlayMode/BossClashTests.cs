using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// The global boss-Sleight clash (docs/specs/m4-overview.md step 4): a higher/lower/equal player Sleight against the
    /// wind-up, a 0 inside/outside the post-activation window, single cards on the body, and the Big Deadlock.
    /// Boss and player stand far apart so only the global clash can connect them. Every boss card is a 5, so its
    /// Sleight is always 15. The Deadlock clock is faked and the boss roll forced, so outcomes are exact.
    /// </summary>
    public class BossClashTests
    {
        private const int BossCard = 5;
        private const int BossSum = BossCard * 3;
        private const float Start = 100f;

        private readonly List<Object> spawned = new List<Object>();
        private readonly List<ClashEvent> clashes = new List<ClashEvent>();
        private readonly List<DeadlockResult> deadlocks = new List<DeadlockResult>();
        private BossEnemy boss;
        private BossSleightClash clash;
        private CombatActionController player;
        private DeadlockController deadlock;
        private float fakeNow;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            fakeNow = Start;
            var fx = new GameObject("ClashFx");
            spawned.Add(fx);
            deadlock = fx.AddComponent<DeadlockController>();
            deadlock.Clock = () => fakeNow;

            var p = new GameObject("BossClashPlayer");
            spawned.Add(p);
            p.transform.position = new Vector3(0f, 0f, -20f);
            p.AddComponent<CombatantStatus>().Team = Team.Player;
            player = p.AddComponent<CombatActionController>();

            var b = new GameObject("BossClashBoss");
            spawned.Add(b);
            b.transform.position = new Vector3(0f, 0f, 20f);
            b.AddComponent<CharacterController>();
            var body = b.AddComponent<TrainingDummyEnemy>();
            body.RespawnOnDeath = false;
            boss = b.AddComponent<BossEnemy>();
            clash = b.AddComponent<BossSleightClash>();
            clash.Watch(player);
            var cards = new List<CardData>();
            for (int i = 0; i < 15; i++)
            {
                var data = CardData.Create($"bc_{i}", BossCard, CardShape.Arcana, Element.Physical);
                cards.Add(data);
                spawned.Add(data);
            }
            body.Deck.SetStartingDeck(cards, newSeed: 2);
            body.Status.SetMaxHp(5000);

            ClashSystem.ClashOccurred += RecordClash;
            DeadlockController.Resolved += RecordDeadlock;
        }

        [TearDown]
        public void TearDown()
        {
            ClashSystem.ClashOccurred -= RecordClash;
            DeadlockController.Resolved -= RecordDeadlock;
            clashes.Clear();
            deadlocks.Clear();
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private void RecordClash(ClashEvent e) => clashes.Add(e);
        private void RecordDeadlock(DeadlockResult r) => deadlocks.Add(r);

        private AttackPayload PlayerSleight(int value) =>
            new AttackPayload(player.Status.OwnerId, Team.Player, value, false, true, Time.frameCount, CardShape.Arcana,
                Element.Physical);

        private AttackPayload PlayerCard(int value, bool zero = false) =>
            new AttackPayload(player.Status.OwnerId, Team.Player, value, zero, false, Time.frameCount, CardShape.Arcana,
                Element.Physical);

        private IEnumerator Windup()
        {
            yield return null; // Start() resets the body
            Assert.IsTrue(boss.ForceSleight());
            Assert.AreEqual(BossSum, boss.PendingSleight.Value.Value);
        }

        private IEnumerator Fired()
        {
            yield return Windup();
            boss.ForceStateEnd();
            yield return null;
            Assert.Greater(boss.LastSleightHitboxes.Count, 0, "Setup: the boss Sleight is live.");
            yield return null; // a later frame, so a player 0 counts as played second
        }

        private void FinishDeadlock(DeadlockGrade? playerPress, DeadlockGrade bossGrade)
        {
            Assert.IsTrue(deadlock.IsActive && deadlock.IsBig, "Setup: a Big Deadlock is running.");
            if (playerPress == DeadlockGrade.Perfect)
            {
                fakeNow = Start + DeadlockRules.TargetFor(DeadlockRules.BigWindowSeconds);
                Assert.IsTrue(deadlock.SubmitPlayer(DeadlockChoice.Poke));
            }
            fakeNow = Start + DeadlockRules.BigWindowSeconds + 0.01f;
        }

        private IEnumerator BigTie()
        {
            yield return Windup();
            deadlock.EnemyRollOverride = (DeadlockGrade.Miss, false);
            player.SpawnSleight(PlayerSleight(BossSum), Vector3.forward);
        }

        // ---- Wind-up ----

        [UnityTest]
        public IEnumerator Windup_HigherPlayerSleight_BreaksIt()
        {
            yield return Windup();
            player.SpawnSleight(PlayerSleight(BossSum + 3), Vector3.forward);

            Assert.AreEqual(BossState.Idle, boss.State);
            Assert.IsTrue(boss.Status.IsStunned);
            Assert.AreEqual(1, boss.Deck.Cycle.BurnedCount, "the boss catalyst cracks");
            Assert.AreEqual(0, boss.Deck.Cycle.StockCount);
            Assert.Greater(player.ActiveHitboxCount, 0, "the player's Sleight goes on");
            Assert.AreEqual(ClashOutcome.AWins, clashes[0].Result.Outcome);
        }

        [UnityTest]
        public IEnumerator Windup_LowerPlayerSleight_IsBroken()
        {
            yield return Windup();
            player.SpawnSleight(PlayerSleight(BossSum - 2), Vector3.forward);

            Assert.AreEqual(BossState.SleightWindup, boss.State, "the wind-up continues");
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.IsTrue(player.Status.IsStunned);
        }

        [UnityTest]
        public IEnumerator Windup_ZeroDoesNothing()
        {
            yield return Windup();
            player.Spawn(PlayerCard(1, zero: true), Vector3.forward);
            Assert.AreEqual(BossState.SleightWindup, boss.State);
            Assert.IsEmpty(clashes);
        }

        [UnityTest]
        public IEnumerator Windup_SingleCardOnTheBody_ClashesTheSum()
        {
            yield return Windup();
            var hb = player.Spawn(PlayerCard(9), Vector3.forward);
            boss.Body.ResolveTelegraphClash(hb);

            Assert.AreEqual(ClashOutcome.BWins, clashes[0].Result.Outcome, "a 9 loses to a 15");
            Assert.IsTrue(player.Status.IsStunned);
            Assert.AreEqual(BossState.SleightWindup, boss.State);
        }

        // ---- Post-activation window ----

        [UnityTest]
        public IEnumerator Window_ZeroBreaksTheSleightAndDeletesAllThree()
        {
            yield return Fired();
            player.Spawn(PlayerCard(1, zero: true), Vector3.forward);

            Assert.AreEqual(1, clashes.Count);
            Assert.IsTrue(clashes[0].Result.BrokeByZero);
            Assert.AreEqual(ClashOutcome.AWins, clashes[0].Result.Outcome);
            Assert.AreEqual(3, boss.Deck.Cycle.DeletedThisCycleCount);
            Assert.AreEqual(0, boss.Deck.Cycle.BurnedCount, "the slot-1 burn became a delete");
            Assert.AreEqual(0, boss.Combat.ActiveHitboxCount);
        }

        [UnityTest]
        public IEnumerator Window_OnlyTheFirstAnswerCounts()
        {
            yield return Fired();
            player.Spawn(PlayerCard(1, zero: true), Vector3.forward);
            player.Spawn(PlayerCard(1, zero: true), Vector3.forward);
            Assert.AreEqual(1, clashes.Count);
        }

        [UnityTest]
        public IEnumerator Window_SingleNumberedCardDoesNotClash()
        {
            yield return Fired();
            player.Spawn(PlayerCard(9), Vector3.forward);
            Assert.IsEmpty(clashes);
            Assert.Greater(boss.Combat.ActiveHitboxCount, 0);
        }

        [UnityTest]
        public IEnumerator Window_HigherPlayerSleightBreaksTheLiveSleight()
        {
            yield return Fired();
            player.SpawnSleight(PlayerSleight(BossSum + 1), Vector3.forward);
            Assert.AreEqual(ClashOutcome.AWins, clashes[0].Result.Outcome);
            Assert.AreEqual(0, boss.Combat.ActiveHitboxCount);
            Assert.AreEqual(0, boss.Deck.Cycle.DeletedThisCycleCount, "only a 0 deletes");
        }

        [UnityTest]
        public IEnumerator AfterTheWindow_ZeroDoesNotClash()
        {
            yield return Fired();
            float end = Time.realtimeSinceStartup + 2f;
            while (Time.time - boss.LastSleightTime <= BossRules.GlobalZeroWindowSeconds && Time.realtimeSinceStartup < end)
                yield return null;
            player.Spawn(PlayerCard(1, zero: true), Vector3.forward);
            Assert.IsEmpty(clashes);
            Assert.AreEqual(0, boss.Deck.Cycle.DeletedThisCycleCount);
        }

        // ---- Big Deadlock ----

        [UnityTest]
        public IEnumerator Windup_EqualSleight_StartsTheBigDeadlock()
        {
            yield return BigTie();
            Assert.IsTrue(deadlock.IsActive);
            Assert.IsTrue(deadlock.IsBig);
            Assert.AreEqual(DeadlockRules.BigWindowSeconds, deadlock.WindowSeconds);
            Assert.AreEqual(BossState.Idle, boss.State, "the boss fired on the spot");
            Assert.Greater(boss.LastSleightHitboxes.Count, 0);
        }

        [UnityTest]
        public IEnumerator BigDeadlock_PlayerWins_CombinedHitThenExhausted()
        {
            yield return BigTie();
            int hpBefore = boss.Status.Hp;
            FinishDeadlock(DeadlockGrade.Perfect, DeadlockGrade.Miss);
            yield return null;

            Assert.AreEqual(1, deadlocks.Count);
            Assert.IsTrue(deadlocks[0].IsBig);
            Assert.AreEqual(ClashOutcome.AWins, deadlocks[0].Outcome);
            var combined = new AttackPayload(0, Team.Player, BossSum * 2, false, true, 0, CardShape.Arcana, Element.Physical);
            Assert.AreEqual(CombatMath.Damage(combined), hpBefore - boss.Status.Hp, "both sums combined, one hit");
            Assert.AreEqual(BossState.Exhausted, boss.State);
            Assert.AreEqual(0, boss.Combat.ActiveHitboxCount);
            Assert.IsFalse(deadlock.IsBig, "reset after finishing");
            Assert.AreEqual(DeadlockRules.WindowSeconds, deadlock.WindowSeconds);
        }

        [UnityTest]
        public IEnumerator BigDeadlock_BossWinsWithPerfectRoll()
        {
            player.Status.SetMaxHp(5);
            yield return Windup();
            deadlock.EnemyRollOverride = (DeadlockGrade.Perfect, false);
            player.SpawnSleight(PlayerSleight(BossSum), Vector3.forward);
            FinishDeadlock(null, DeadlockGrade.Perfect);
            yield return null;

            Assert.AreEqual(ClashOutcome.BWins, deadlocks[0].Outcome);
            Assert.AreEqual(1, player.Status.Hp, "heavy, but never lethal");
            Assert.IsFalse(player.Status.IsDead);
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.AreEqual(0, boss.Combat.ActiveHitboxCount);
            Assert.AreNotEqual(BossState.Exhausted, boss.State);
        }

        [UnityTest]
        public IEnumerator BigDeadlock_Tie_BothShatter()
        {
            yield return BigTie();
            int hpBefore = boss.Status.Hp;
            FinishDeadlock(null, DeadlockGrade.Miss);
            yield return null;

            Assert.AreEqual(ClashOutcome.Neutral, deadlocks[0].Outcome);
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.AreEqual(0, boss.Combat.ActiveHitboxCount);
            Assert.AreEqual(hpBefore, boss.Status.Hp);
            Assert.IsTrue(clashes.Exists(c => c.FromDeadlock && c.Result.Outcome == ClashOutcome.Neutral));
        }

        [UnityTest]
        public IEnumerator BigDeadlock_Grade_ScalesTheTargetWithTheWindow()
        {
            yield return BigTie();
            fakeNow = Start + DeadlockRules.TargetTime; // the normal target is too early in the long window
            deadlock.SubmitPlayer(DeadlockChoice.Poke);
            Assert.AreEqual(DeadlockGrade.Miss, deadlock.PlayerGrade);
        }
    }
}
