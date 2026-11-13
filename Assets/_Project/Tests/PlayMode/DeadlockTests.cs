using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Equal-value player/enemy clashes become a Deadlock. The controller's clock is faked so press
    /// timing is exact, and the enemy's roll is forced, so every outcome is deterministic.
    /// </summary>
    public class DeadlockTests
    {
        private const float Start = 100f;
        private readonly List<Object> spawned = new List<Object>();
        private readonly List<DeadlockResult> results = new List<DeadlockResult>();
        private DeadlockController controller;
        private float fakeNow;

        [SetUp]
        public void SetUp()
        {
            fakeNow = Start;
            var go = new GameObject("ClashFx");
            spawned.Add(go);
            controller = go.AddComponent<DeadlockController>(); // adds HitStop too
            controller.Clock = () => fakeNow;
            DeadlockController.Resolved += RecordResult;
        }

        [TearDown]
        public void TearDown()
        {
            DeadlockController.Resolved -= RecordResult;
            results.Clear();
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private void RecordResult(DeadlockResult r) => results.Add(r);

        private CombatActionController MakeCombatant(Team team, Vector3 position, params int[] deckValues)
        {
            var go = new GameObject($"{team}_combatant");
            spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CombatantStatus>().Team = team;
            var combat = go.AddComponent<CombatActionController>();
            if (deckValues.Length > 0)
            {
                var cards = new List<CardData>();
                foreach (int v in deckValues)
                {
                    var card = CardData.Create($"{team}_{v}_{cards.Count}", v);
                    spawned.Add(card);
                    cards.Add(card);
                }
                go.AddComponent<DeckRuntimeManager>().SetStartingDeck(cards, newSeed: 1);
            }
            return combat;
        }

        private static AttackPayload Payload(CombatActionController c, int value, int tick, bool zero = false) =>
            new AttackPayload(c.Status.OwnerId, c.Status.Team, value, zero, false, tick, CardShape.Strike, Element.Physical);

        private static IEnumerator WaitUntilRealtime(System.Func<bool> done, float timeout = 1f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        /// <summary>Spawns an equal-value clash and waits for the Deadlock to start.</summary>
        private IEnumerator StartTie(CombatActionController player, CombatActionController enemy, int value = 5)
        {
            enemy.Spawn(Payload(enemy, value, tick: 1), Vector3.back);
            player.Spawn(Payload(player, value, tick: 2), Vector3.forward);
            yield return WaitUntilRealtime(() => controller.IsActive);
            Assert.IsTrue(controller.IsActive, "An equal-value clash should start a Deadlock.");
        }

        /// <summary>Advances the fake clock past the window and lets the controller resolve.</summary>
        private IEnumerator FinishWindow()
        {
            fakeNow = Start + DeadlockRules.WindowSeconds + 0.01f;
            yield return WaitUntilRealtime(() => !controller.IsActive);
            Assert.IsFalse(controller.IsActive);
            Assert.AreEqual(1, results.Count, "Exactly one result.");
        }

        private (CombatActionController player, CombatActionController enemy) Pair(int[] playerDeck = null, int[] enemyDeck = null) =>
            (MakeCombatant(Team.Player, Vector3.zero, playerDeck ?? new int[0]),
             MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 2.2f), enemyDeck ?? new int[0]));

        [UnityTest]
        public IEnumerator Tie_StartsDeadlock_InsteadOfCancelling_AndFreezesTime()
        {
            var (player, enemy) = Pair();
            yield return StartTie(player, enemy);

            Assert.AreEqual(1, player.ActiveHitboxCount, "Both attacks stay locked together during the Deadlock.");
            Assert.AreEqual(1, enemy.ActiveHitboxCount);
            Assert.AreEqual(0f, Time.timeScale, "The world is frozen during the Deadlock.");
        }

        [UnityTest]
        public IEnumerator PerfectWithCommittedCard_BeatsEnemyMiss_AndSpendsTheCard()
        {
            var (player, enemy) = Pair(playerDeck: new[] { 7, 7, 7, 7 });
            controller.EnemyRollOverride = (DeadlockGrade.Miss, false);
            yield return StartTie(player, enemy);

            fakeNow = Start + DeadlockRules.TargetTime;
            Assert.IsTrue(controller.SubmitPlayer(DeadlockChoice.Slot1));
            Assert.AreEqual(DeadlockGrade.Perfect, controller.PlayerGrade);
            Assert.AreEqual(7, controller.PlayerCommittedValue);
            Assert.AreEqual(1, player.GetComponent<DeckRuntimeManager>().Cycle.DiscardCount, "Committed card is spent.");

            yield return FinishWindow();

            Assert.AreEqual(ClashOutcome.AWins, results[0].Outcome);
            Assert.AreEqual(DeadlockRules.PerfectBonus + 7, results[0].PlayerPower);
            Assert.IsTrue(enemy.Status.IsStunned, "The enemy's attack is broken.");
            Assert.AreEqual(0, enemy.ActiveHitboxCount);
            Assert.AreEqual(1, player.ActiveHitboxCount, "The winner's attack goes through.");
        }

        [UnityTest]
        public IEnumerator NoPress_VsEnemyGood_BreaksThePlayer()
        {
            var (player, enemy) = Pair();
            controller.EnemyRollOverride = (DeadlockGrade.Good, false);
            yield return StartTie(player, enemy);

            yield return FinishWindow();

            Assert.AreEqual(ClashOutcome.BWins, results[0].Outcome);
            Assert.AreEqual(DeadlockGrade.Miss, results[0].PlayerGrade, "No press counts as a miss.");
            Assert.IsTrue(player.Status.IsStunned);
            Assert.AreEqual(0, player.ActiveHitboxCount);
        }

        [UnityTest]
        public IEnumerator EqualPushes_CancelBoth_LikeANormalTie()
        {
            var (player, enemy) = Pair();
            controller.EnemyRollOverride = (DeadlockGrade.Good, false);
            yield return StartTie(player, enemy);

            fakeNow = Start + DeadlockRules.TargetTime + 0.1f; // Good
            controller.SubmitPlayer(DeadlockChoice.Poke);
            yield return FinishWindow();

            Assert.AreEqual(ClashOutcome.Neutral, results[0].Outcome);
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.AreEqual(0, enemy.ActiveHitboxCount);
            Assert.IsFalse(player.Status.IsStunned);
            Assert.IsFalse(enemy.Status.IsStunned);
        }

        [UnityTest]
        public IEnumerator EnemyCommit_SpendsEnemyCard_AndCanOutpushAPerfectPoke()
        {
            var (player, enemy) = Pair(enemyDeck: new[] { 4, 4, 4, 4 });
            controller.EnemyRollOverride = (DeadlockGrade.Miss, true);
            yield return StartTie(player, enemy);

            fakeNow = Start + DeadlockRules.TargetTime;
            controller.SubmitPlayer(DeadlockChoice.Poke); // Perfect, no card: 3
            yield return FinishWindow();

            Assert.AreEqual(4, results[0].EnemyCommittedValue);
            Assert.AreEqual(ClashOutcome.BWins, results[0].Outcome, "Enemy's committed 4 beats a perfect poke (3).");
            Assert.AreEqual(1, enemy.GetComponent<DeckRuntimeManager>().Cycle.DiscardCount);
        }

        [UnityTest]
        public IEnumerator SecondPress_IsIgnored()
        {
            var (player, enemy) = Pair(playerDeck: new[] { 6, 6, 6, 6 });
            yield return StartTie(player, enemy);

            fakeNow = Start + DeadlockRules.TargetTime;
            Assert.IsTrue(controller.SubmitPlayer(DeadlockChoice.Poke));
            Assert.IsFalse(controller.SubmitPlayer(DeadlockChoice.Slot1), "Only one press counts.");
            Assert.AreEqual(0, controller.PlayerCommittedValue);
            Assert.AreEqual(0, player.GetComponent<DeckRuntimeManager>().Cycle.DiscardCount, "No card spent.");
        }

        [UnityTest]
        public IEnumerator CommittingAnEmptySlot_ActsAsAPoke()
        {
            var (player, enemy) = Pair(playerDeck: new[] { 6, 6 }); // slot 3 stays empty
            yield return StartTie(player, enemy);

            fakeNow = Start + DeadlockRules.TargetTime;
            controller.SubmitPlayer(DeadlockChoice.Slot3);

            Assert.AreEqual(0, controller.PlayerCommittedValue);
            Assert.AreEqual(DeadlockGrade.Perfect, controller.PlayerGrade, "Timing still counts.");
        }

        [UnityTest]
        public IEnumerator ZeroBounce_DoesNotStartDeadlock()
        {
            var (player, enemy) = Pair();
            enemy.Spawn(Payload(enemy, 0, tick: 3, zero: true), Vector3.back);
            player.Spawn(Payload(player, 0, tick: 3, zero: true), Vector3.forward);

            yield return WaitUntilRealtime(() => player.ActiveHitboxCount == 0);

            Assert.IsFalse(controller.IsActive, "0-cards keep their own rules.");
            Assert.AreEqual(0, enemy.ActiveHitboxCount, "Plain tie cancel.");
        }

        [UnityTest]
        public IEnumerator UnequalClash_DoesNotStartDeadlock()
        {
            var (player, enemy) = Pair();
            enemy.Spawn(Payload(enemy, 3, tick: 1), Vector3.back);
            player.Spawn(Payload(player, 8, tick: 2), Vector3.forward);

            yield return WaitUntilRealtime(() => enemy.Status.IsStunned);

            Assert.IsFalse(controller.IsActive);
            Assert.IsTrue(enemy.Status.IsStunned, "Higher value still breaks lower directly.");
        }

        [UnityTest]
        public IEnumerator DisablingController_MidDeadlock_CancelsAndUnfreezes()
        {
            var (player, enemy) = Pair();
            yield return StartTie(player, enemy);

            controller.gameObject.SetActive(false);

            Assert.IsFalse(controller.IsActive);
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.AreEqual(0, enemy.ActiveHitboxCount);
            Assert.AreEqual(1f, Time.timeScale, "A dropped Deadlock must not leave the game frozen.");
        }

        [UnityTest]
        public IEnumerator EnemyDestroyedMidDeadlock_FallsBackWithoutErrors()
        {
            var (player, enemy) = Pair();
            controller.EnemyRollOverride = (DeadlockGrade.Miss, false);
            yield return StartTie(player, enemy);

            Object.Destroy(enemy.gameObject);
            yield return null;
            fakeNow = Start + DeadlockRules.TargetTime;
            controller.SubmitPlayer(DeadlockChoice.Poke);
            yield return FinishWindow();

            Assert.AreEqual(ClashOutcome.Neutral, results[0].Outcome, "Reported as a tie, not a phantom win.");
            Assert.IsFalse(player.Status.IsStunned);
            Assert.AreEqual(0, player.ActiveHitboxCount, "Falls back to a plain tie cancel.");
        }

        [UnityTest]
        public IEnumerator View_ShowsRingAndHand_ThenGrade_ThenResult()
        {
            var viewGo = new GameObject("DeadlockView");
            spawned.Add(viewGo);
            var view = viewGo.AddComponent<DeadlockView>();
            var (player, enemy) = Pair(playerDeck: new[] { 2, 2, 2, 2 });
            controller.EnemyRollOverride = (DeadlockGrade.Miss, false);
            yield return StartTie(player, enemy);
            yield return null; // let the view draw

            Assert.IsTrue(view.IsRingVisible);
            StringAssert.Contains("1:[2]", view.HandText);
            Assert.IsNull(view.GradeText, "No grade before you press.");

            fakeNow = Start + DeadlockRules.TargetTime;
            controller.SubmitPlayer(DeadlockChoice.Slot2);
            yield return null;
            StringAssert.StartsWith("PERFECT", view.GradeText);
            StringAssert.Contains("+2 card", view.GradeText);

            yield return FinishWindow();
            yield return null;
            Assert.IsFalse(view.IsRingVisible);
            Assert.AreEqual("YOU 5  vs  0 FOE", view.ResultText);
        }
    }
}
