using System;
using System.Collections;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Drives the M3 run scene through the RunDirector API (the step 6 screens will call the same API).</summary>
    public class RunLoopTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private CombatantStatus player;
        private DeckRuntimeManager playerDeck;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
            player = director.Player.GetComponent<CombatantStatus>();
            playerDeck = director.Player.GetComponent<DeckRuntimeManager>();
        }

        /// <summary>Unload so live enemies can't affect later fixtures (see CLAUDE.md lessons).</summary>
        [UnityTearDown]
        public IEnumerator UnloadRun()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private int IndexOf(Func<MapCard, bool> match)
        {
            var offer = director.Run.CurrentOffer;
            for (int i = 0; i < offer.Count; i++) if (match(offer[i])) return i;
            return -1;
        }

        /// <summary>Restarts with seeds until the current door offers a matching card. Returns its index.</summary>
        private int RestartUntilOffer(Func<MapCard, bool> match, Action afterRestart = null)
        {
            for (int seed = 1; seed < 300; seed++)
            {
                director.RestartRun(seed);
                afterRestart?.Invoke();
                int index = IndexOf(match);
                if (index >= 0) return index;
            }
            Assert.Fail("No seed produced a matching offer.");
            return -1;
        }

        private void ClearRoom1()
        {
            director.ChooseDoor(0);
            director.DebugDefeatAllEnemies();
            director.FinishReward();
        }

        /// <summary>Empties the player's draw queue and starts the reload bar; returns its rolls.</summary>
        private (float, float, ReloadPromptAnchor) RollPlayerReload()
        {
            var cycle = playerDeck.Cycle;
            for (int guard = 0; guard < 100 && cycle.DrawCount > 0; guard++)
            {
                int slot = 0;
                while (slot < DeckCycle.HandSize && cycle.Hand[slot] == null) slot++;
                Assert.IsTrue(cycle.TryPlay(slot, out _));
            }
            Assert.IsTrue(playerDeck.Reload.Begin(), "the reload bar should start on an empty draw queue");
            var rolls = (playerDeck.Reload.BarDuration, playerDeck.Reload.Target01, playerDeck.Reload.Anchor);
            playerDeck.Reload.Cancel();
            return rolls;
        }

        // ---- tests ----

        [Test]
        public void SeededRestart_ReplaysTheSameReloadBar()
        {
            director.RestartRun(5);
            var first = RollPlayerReload();
            director.RestartRun(5);
            Assert.AreEqual(first, RollPlayerReload(), "a seeded run must replay the reload timing too");
        }

        [Test]
        public void Start_DealsFirstOffer_OnStarterDeck()
        {
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.AreEqual(3, director.Run.CurrentOffer.Count);
            Assert.AreEqual(director.Content.StarterDeck.Count, playerDeck.Cycle.CyclingCardCount);
            Assert.IsEmpty(director.Enemies);
            Assert.IsFalse(director.Player.RespawnOnDeath);
        }

        [Test]
        public void CombatDoor_SpawnsEnemiesScaledByIntensity()
        {
            director.RestartRun(5);
            director.ChooseDoor(0); // room 1 is always combat
            var room = director.Run.CurrentRoom;
            var prefab = director.EnemyPrefab;
            float tempo = RoomIntensity.EnemyTempoPercent(room) / 100f;
            int bonus = RoomIntensity.EnemyCardValueBonus(room);

            Assert.AreEqual(RunDirectorState.Fighting, director.State);
            Assert.AreEqual(RoomIntensity.EnemyCount(room), director.Enemies.Count);
            foreach (var enemy in director.Enemies)
            {
                Assert.AreEqual(RoomIntensity.EnemyMaxHp(director.EnemyBaseHp, room), enemy.Status.MaxHp);
                Assert.AreEqual(enemy.Status.MaxHp, enemy.Status.Hp);
                Assert.IsFalse(enemy.RespawnOnDeath);
                Assert.AreSame(director.Player.transform, enemy.Target);
                Assert.AreEqual(prefab.MoveSpeed * tempo, enemy.MoveSpeed, 1e-4f);
                Assert.AreEqual(prefab.TelegraphSeconds / tempo, enemy.TelegraphSeconds, 1e-4f);
                foreach (var card in enemy.Deck.Cycle.Hand.Where(c => c != null))
                    Assert.AreEqual(bonus, card.TemporaryRoomMod);
            }
        }

        [Test]
        public void ClearingTheRoom_PaysReward_ThenDealsNextDoor()
        {
            director.RestartRun(5);
            director.ChooseDoor(0);
            director.DebugDefeatAllEnemies();

            Assert.AreEqual(RunDirectorState.Reward, director.State);
            Assert.IsNotNull(director.CurrentReward);
            Assert.AreEqual(director.CurrentReward.Currency, director.Run.Currency);

            director.FinishReward();
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.AreEqual(2, director.Run.RoomNumber);
            Assert.AreEqual(3, director.Run.CurrentOffer.Count);
        }

        [Test]
        public void FinishReward_WithUntouchedDraft_CountsAsSkip()
        {
            director.RestartRun(5);
            director.ChooseDoor(0);
            director.DebugDefeatAllEnemies();
            int before = director.Run.Currency;
            director.FinishReward();
            Assert.AreEqual(before + RewardRules.SkipBonus, director.Run.Currency);
            Assert.AreEqual(director.Content.StarterDeck.Count, director.Run.Deck.Count);
        }

        [Test]
        public void DraftedCard_JoinsTheNextFight()
        {
            director.RestartRun(5);
            director.ChooseDoor(0);
            director.DebugDefeatAllEnemies();
            Assert.IsTrue(director.CurrentReward.TryPick(0, director.Run));
            director.FinishReward();
            director.ChooseDoor(IndexOf(c => c.IsCombat));

            Assert.AreEqual(director.Content.StarterDeck.Count + 1, playerDeck.Cycle.CyclingCardCount);
        }

        [Test]
        public void RunDeckInstances_CarryUpgradesIntoFights()
        {
            director.RestartRun(5);
            foreach (var card in director.Run.Deck) card.PermanentRunUpgradeMod = 1;
            director.ChooseDoor(0);

            foreach (var card in playerDeck.Cycle.Hand.Where(c => c != null))
            {
                Assert.IsTrue(director.Run.OwnsCard(card), "The fight uses the run's own instances.");
                Assert.AreEqual(1, card.PermanentRunUpgradeMod);
            }
        }

        [Test]
        public void Hp_CarriesBetweenRooms()
        {
            director.RestartRun(5);
            director.ChooseDoor(0);
            player.SetHp(40);
            director.DebugDefeatAllEnemies();
            Assert.AreEqual(40, director.Run.Hp);

            director.FinishReward();
            director.ChooseDoor(IndexOf(c => c.IsCombat));
            Assert.AreEqual(40, player.Hp);
        }

        [Test]
        public void KindledGround_BuffsOnlyThePlayersElement()
        {
            int index = RestartUntilOffer(c => c.Kind == MapCardKind.KindledGround);
            var room = director.Run.CurrentOffer[index];
            director.ChooseDoor(index);
            int buff = RoomIntensity.PlayerElementBuff(room);

            Assert.Greater(buff, 0);
            foreach (var card in director.Run.Deck)
                Assert.AreEqual(card.Data.Element == room.BuffElement ? buff : 0, card.TemporaryRoomMod, card.ToString());
        }

        [Test]
        public void NonKindledRoom_ClearsLastRoomsBuff()
        {
            // Find a seed: room 1 KindledGround, then a different combat card in room 2.
            for (int seed = 1; seed < 300; seed++)
            {
                director.RestartRun(seed);
                int kindled = IndexOf(c => c.Kind == MapCardKind.KindledGround);
                if (kindled < 0) continue;
                director.ChooseDoor(kindled);
                director.DebugDefeatAllEnemies();
                director.FinishReward();
                int other = IndexOf(c => c.IsCombat && c.Kind != MapCardKind.KindledGround);
                if (other < 0) continue;

                director.ChooseDoor(other);
                Assert.IsTrue(director.Run.Deck.All(c => c.TemporaryRoomMod == 0));
                return;
            }
            Assert.Fail("No seed gave KindledGround followed by another combat room.");
        }

        [Test]
        public void BlueRoom_OpensItsVisit_ThenLeaves()
        {
            int index = RestartUntilOffer(c => c.Family == MapFamily.Blue, ClearRoom1);
            var room = director.Run.CurrentOffer[index];
            director.ChooseDoor(index);

            Assert.AreEqual(RunDirectorState.Utility, director.State);
            Assert.IsEmpty(director.Enemies);
            Assert.AreEqual(room.Kind == MapCardKind.Shop, director.CurrentShop != null);
            Assert.AreEqual(room.Kind == MapCardKind.Forge, director.CurrentForge != null);
            Assert.AreEqual(room.Kind == MapCardKind.Rest, director.CurrentRest != null);

            director.LeaveUtility();
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.AreEqual(3, director.Run.RoomNumber);
            Assert.IsNull(director.CurrentShop ?? (object)director.CurrentForge ?? director.CurrentRest);
        }

        [Test]
        public void RestHeal_ShowsOnThePlayerWhenLeaving()
        {
            RestartUntilOffer(c => c.Kind == MapCardKind.Rest, () =>
            {
                director.ChooseDoor(0);
                player.SetHp(30);
                director.DebugDefeatAllEnemies();
                director.FinishReward();
            });
            director.ChooseDoor(IndexOf(c => c.Kind == MapCardKind.Rest));
            int heal = director.CurrentRest.HealAmount(director.Run);
            Assert.IsTrue(director.CurrentRest.TryHeal(director.Run));
            director.LeaveUtility();
            Assert.AreEqual(Mathf.Min(player.MaxHp, 30 + heal), player.Hp);
        }

        [UnityTest]
        public IEnumerator PlayerDeath_EndsRunInDefeat_WithoutRespawn()
        {
            director.RestartRun(5);
            bool? ended = null;
            director.RunEnded += victory => ended = victory;
            director.ChooseDoor(0);

            var lethal = new AttackPayload(12345, Team.Enemy, 999, false, true, Time.frameCount, CardShape.Strike, Element.Physical);
            player.TakeHit(lethal);
            Assert.AreEqual(RunDirectorState.Defeat, director.State);
            Assert.AreEqual(false, ended);
            Assert.AreEqual(0, director.Run.Hp);

            yield return WaitRealtime(2f); // longer than the player's respawn delay
            Assert.IsTrue(player.IsDead, "No auto-respawn during a run.");

            director.RestartRun(6);
            yield return null;
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.IsFalse(player.IsDead);
            Assert.AreEqual(player.MaxHp, player.Hp);
            Assert.AreEqual(1, director.Run.RoomNumber);
            Assert.IsEmpty(director.Enemies);
        }

        [UnityTest]
        public IEnumerator DeadEnemies_AreRemoved_NotRespawned()
        {
            director.RestartRun(5);
            director.ChooseDoor(0);
            var spawned = director.Enemies.ToArray();
            director.DebugDefeatAllEnemies();

            yield return WaitRealtime(3f); // longer than the dummy's respawn delay
            Assert.IsTrue(spawned.All(e => e == null), "Dead run enemies are destroyed.");
        }

        [Test]
        public void FullRun_FiveRoomsThenBoss_EndsInVictory()
        {
            director.RestartRun(9);
            bool? ended = null;
            BossEnemy spawnedBoss = null;
            director.RunEnded += victory => ended = victory;
            director.BossSpawned += b => spawnedBoss = b;

            for (int guard = 0; guard < 20 && director.State != RunDirectorState.Victory; guard++)
            {
                switch (director.State)
                {
                    case RunDirectorState.ChoosingDoor:
                        director.ChooseDoor(0);
                        if (director.Run.CurrentRoom.Kind == MapCardKind.Boss)
                        {
                            Assert.AreEqual(1, director.Enemies.Count);
                            Assert.Greater(director.Enemies[0].transform.localScale.x, 1f);
                            Assert.AreEqual(RunContent.BossDeckSize, director.Enemies[0].Deck.Cycle.CyclingCardCount);
                            var boss = director.Enemies[0].GetComponent<BossEnemy>();
                            Assert.IsNotNull(boss, "The boss room spawns a BossEnemy.");
                            Assert.AreSame(boss, director.Boss);
                            Assert.AreSame(boss, spawnedBoss);
                            Assert.IsNotNull(director.Enemies[0].Deck.Cycle.SpacingRule, "Spacing set before the deal.");
                        }
                        else if (director.Run.CurrentRoom.IsCombat)
                        {
                            Assert.IsNull(director.Boss);
                            Assert.IsTrue(director.Enemies.All(e => e.GetComponent<BossEnemy>() == null), "Mobs have no boss brain.");
                        }
                        break;
                    case RunDirectorState.Fighting: director.DebugDefeatAllEnemies(); break;
                    case RunDirectorState.Reward: director.FinishReward(); break;
                    case RunDirectorState.Utility: director.LeaveUtility(); break;
                    default: Assert.Fail($"Unexpected state {director.State}"); break;
                }
            }

            Assert.AreEqual(RunDirectorState.Victory, director.State);
            Assert.AreEqual(true, ended);
            Assert.AreEqual(RunPhase.Complete, director.Run.Phase);
            Assert.AreEqual(RunState.BossRoom, director.Run.History.Count);
        }

        [Test]
        public void WrongStateCalls_Throw()
        {
            Assert.Throws<InvalidOperationException>(() => director.FinishReward());
            Assert.Throws<InvalidOperationException>(() => director.LeaveUtility());
            director.ChooseDoor(0);
            Assert.Throws<InvalidOperationException>(() => director.ChooseDoor(0));
        }
    }
}
