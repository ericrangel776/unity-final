using System;
using System.Collections;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Drives the M3 run scene through the RunDirector API (the step 6 screens will call the same API).</summary>
    public class RunLoopTests : RunSceneTest
    {
        private CombatantStatus player;
        private DeckRuntimeManager playerDeck;

        protected override void OnRunLoaded()
        {
            player = director.Player.GetComponent<CombatantStatus>();
            playerDeck = director.Player.GetComponent<DeckRuntimeManager>();
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
            CollectionAssert.AreEqual(director.CurrentMix, director.Enemies.Select(e => e.Variant));
            foreach (var enemy in director.Enemies)
            {
                // Room scaling, then the variant's percentages on top (docs/specs/enemy-variants.md).
                var stats = EnemyVariantRules.Stats(enemy.Variant);
                int roomHp = RoomIntensity.EnemyMaxHp(director.EnemyBaseHp, room);
                Assert.AreEqual(RewardRules.PercentHalfUp(roomHp, stats.HpPercent), enemy.Status.MaxHp);
                Assert.AreEqual(enemy.Status.MaxHp, enemy.Status.Hp);
                Assert.IsFalse(enemy.RespawnOnDeath);
                Assert.AreSame(director.Player.transform, enemy.Target);
                Assert.AreEqual(prefab.MoveSpeed * tempo * stats.MovePercent / 100f, enemy.MoveSpeed, 1e-4f);
                Assert.AreEqual(prefab.TelegraphSeconds / tempo * stats.TelegraphPercent / 100f, enemy.TelegraphSeconds, 1e-4f);
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

            yield return TestWait.Realtime(2f); // longer than the player's respawn delay
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

            yield return TestWait.Realtime(3f); // longer than the dummy's respawn delay
            Assert.IsTrue(spawned.All(e => e == null), "Dead run enemies are destroyed.");
        }

        [Test]
        public void FullRun_FiveRoomsThenBoss_EndsInVictory()
        {
            director.RestartRun(9);
            director.Run.IncreaseMaxHp(15); // max-HP upgrades must not toughen the boss
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
                            Assert.AreEqual(BossRules.MaxHp(director.Run.Floor, director.Run.StartingMaxHp), boss.Status.MaxHp,
                                "boss HP is relative to the player's starting max HP");
                            Assert.AreEqual(150, boss.Status.MaxHp, "floor-1 boss stays 150 after a +15 max-HP upgrade");
                            Assert.AreSame(boss, spawnedBoss);
                            Assert.IsNotNull(director.Enemies[0].Deck.Cycle.SpacingRule, "Spacing set before the deal.");
                            var hud = Object.FindAnyObjectByType<DeckArpg.UI.BossHudView>();
                            Assert.IsNotNull(hud, "M3_Run has a boss HUD.");
                            Assert.AreSame(boss, hud.Boss, "the boss HUD binds on BossSpawned");
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
        public void SkipToBoss_LandsInTheBossFight()
        {
            director.RestartRun(5);
            Assert.IsTrue(director.DebugSkipToBoss());
            Assert.AreEqual(RunDirectorState.Fighting, director.State);
            Assert.IsTrue(director.Run.IsBossRoom);
            Assert.IsNotNull(director.Boss);
            Assert.Greater(director.Run.Deck.Count, 10, "drafted cards on the way");
        }

        [Test]
        public void SkipToBoss_AfterAFinishedRun_StartsANewOne()
        {
            director.RestartRun(5);
            director.DebugSkipToBoss();
            director.DebugDefeatAllEnemies();
            Assert.AreEqual(RunDirectorState.Victory, director.State);
            Assert.IsTrue(director.DebugSkipToBoss());
            Assert.IsNotNull(director.Boss);
        }

        [Test]
        public void WrongStateCalls_Throw()
        {
            Assert.Throws<InvalidOperationException>(() => director.FinishReward());
            Assert.Throws<InvalidOperationException>(() => director.LeaveUtility());
            director.ChooseDoor(0);
            Assert.Throws<InvalidOperationException>(() => director.ChooseDoor(0));
        }

        // ---- enemy variants and the second wave (docs/specs/enemy-variants.md) ----

        private int waveTwoStarts;

        private MapCard EnterRoom(MapCardKind kind, IntensityBand band)
        {
            var room = RunTestHelpers.EnterRoom(director, c => c.Kind == kind && RoomIntensity.Band(c.Number) == band);
            waveTwoStarts = 0;
            director.WaveStarted += wave => { if (wave == 2) waveTwoStarts++; };
            return room;
        }

        private void KillLivingEnemies()
        {
            var lethal = new AttackPayload(player.OwnerId, Team.Player, 999, false, true, Time.frameCount,
                CardShape.Strike, Element.Physical);
            foreach (var e in director.Enemies.ToArray())
                for (int hit = 0; hit < 10 && e != null && !e.Status.IsDead; hit++)
                    e.Status.TakeHit(lethal);
        }

        private int Living => director.Enemies.Count(e => e != null && !e.Status.IsDead);

        private static int RingsInScene => Object.FindObjectsByType<SpawnRingFx>(FindObjectsSortMode.None).Length;

        /// <summary>Waits (real time, so hit-stop can't stall it) until wave 2 has spawned or the timeout passes.</summary>
        private IEnumerator WaitForWaveTwo(float timeout = 3f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (waveTwoStarts == 0 && Time.realtimeSinceStartup < end) yield return null;
        }

        [Test]
        public void LowHexfire_SpawnsOneCasterAndOneBrute_AndNoWave()
        {
            var room = EnterRoom(MapCardKind.Hexfire, IntensityBand.Low);

            Assert.AreEqual(2, director.Enemies.Count);
            CollectionAssert.AreEqual(new[] { EnemyVariant.Brute, EnemyVariant.Caster },
                director.Enemies.Select(e => e.Variant));
            Assert.AreEqual(0, director.WavesPending);
            Assert.AreEqual("Hexfire Caster Enemy 2", director.Enemies[1].name);

            var brute = director.Enemies[0];
            var caster = director.Enemies[1];
            var casterIds = director.Content.CasterDeck.Select(c => c.CardId).ToList();
            foreach (var card in caster.Deck.Cycle.Hand.Where(c => c != null))
                CollectionAssert.Contains(casterIds, card.Data.CardId, "The Caster plays its own deck.");
            var hexIds = director.Content.HexfireDeck.Select(c => c.CardId).ToList();
            foreach (var card in brute.Deck.Cycle.Hand.Where(c => c != null))
                CollectionAssert.Contains(hexIds, card.Data.CardId, "Hexfire Brutes keep the Hexfire deck.");

            float prefabPosture = director.EnemyPrefab.GetComponent<CombatantStatus>().MaxPosture;
            Assert.AreEqual(prefabPosture * 0.8f, caster.Status.MaxPosture, 1e-3f);
            Assert.AreEqual(prefabPosture, brute.Status.MaxPosture, 1e-3f);
            Assert.Greater(caster.transform.position.z - brute.transform.position.z, 0.5f,
                "Casters stand further from the player.");
            Assert.AreEqual(EnemyVariantLook.CasterScale, caster.transform.localScale);
            Assert.AreEqual(RoomIntensity.EnemyCardValueBonus(room),
                caster.Deck.Cycle.Hand.First(c => c != null).TemporaryRoomMod, "Room value bonus applies to variants.");
        }

        [UnityTest]
        public IEnumerator MidVanguard_WaveOneDeath_SpawnsWaveTwoAfterTheDelay()
        {
            EnterRoom(MapCardKind.Vanguard, IntensityBand.Mid);
            Assert.AreEqual(4, director.Enemies.Count);
            Assert.AreEqual(2, director.CurrentMix.Count(v => v == EnemyVariant.Rusher));
            Assert.AreEqual(2, director.CurrentMix.Count(v => v == EnemyVariant.Brute));
            Assert.AreEqual(1, director.WavesPending);

            KillLivingEnemies();

            Assert.AreEqual(RunDirectorState.Fighting, director.State, "Wave 1 dying doesn't clear the room.");
            Assert.AreEqual(0, director.WavesPending);
            Assert.IsTrue(director.IsWaveIncoming);
            Assert.AreEqual(1, director.SpawnRings.Count, "A ring marks the wave-2 spawn point.");
            Assert.AreEqual(0, waveTwoStarts, "Not before the delay.");

            float t0 = Time.time;
            yield return WaitForWaveTwo();

            Assert.AreEqual(1, waveTwoStarts);
            Assert.GreaterOrEqual(Time.time - t0, EnemyVariantRules.WaveSpawnDelaySeconds - 0.05f);
            Assert.AreEqual(1, Living, "Mid band: one more enemy (a Vanguard wave of 1 is a Brute).");
            CollectionAssert.AreEqual(new[] { EnemyVariant.Brute }, director.CurrentMix);
            Assert.IsFalse(director.IsWaveIncoming);
            Assert.AreEqual(0, director.SpawnRings.Count);
            yield return null; // Destroy is deferred to the end of the frame
            Assert.AreEqual(0, RingsInScene);
        }

        [UnityTest]
        public IEnumerator KillingWaveTwo_ClearsTheRoom_AndPaysTheWaveBonus()
        {
            var room = EnterRoom(MapCardKind.Vanguard, IntensityBand.Mid);
            var before = new Momentum();
            for (int i = 0; i < director.Run.Momentum.Stacks; i++) before.AddStack();
            int currencyBefore = director.Run.Currency;

            KillLivingEnemies();
            yield return WaitForWaveTwo();
            Assert.AreEqual(1, waveTwoStarts);
            KillLivingEnemies();

            Assert.AreEqual(RunDirectorState.Reward, director.State);
            int expected = RewardRules.Currency(room, before);
            Assert.AreEqual(expected, director.CurrentReward.Currency);
            Assert.AreEqual(currencyBefore + expected, director.Run.Currency);
            int withoutWave = before.ApplyCurrencyBonus(RewardRules.PercentHalfUp(
                RewardRules.PercentHalfUp(RewardRules.BaseCurrency(room), RewardRules.CurrencyPercent(RewardRules.Tier(room.Number))),
                RoomIntensity.BountyCurrencyPercent(room)));
            Assert.Greater(expected, withoutWave, "A room with a second wave pays more.");
        }

        [UnityTest]
        public IEnumerator PlayerDeathDuringTheWaveDelay_SpawnsNothing()
        {
            EnterRoom(MapCardKind.Vanguard, IntensityBand.Mid);
            int spawned = director.Enemies.Count;
            KillLivingEnemies();
            Assert.IsTrue(director.IsWaveIncoming);

            player.TakeHit(new AttackPayload(12345, Team.Enemy, 999, false, true, Time.frameCount, CardShape.Strike,
                Element.Physical));
            Assert.AreEqual(RunDirectorState.Defeat, director.State);

            yield return TestWait.Realtime(EnemyVariantRules.WaveSpawnDelaySeconds + 0.6f);
            Assert.AreEqual(0, waveTwoStarts);
            Assert.AreEqual(0, Living);
            Assert.LessOrEqual(director.Enemies.Count, spawned, "No wave-2 enemies were added.");
            Assert.AreEqual(0, RingsInScene, "The rings are gone.");
        }

        [UnityTest]
        public IEnumerator RestartDuringTheWaveDelay_LeavesNoRingsOrExtraEnemies()
        {
            EnterRoom(MapCardKind.Vanguard, IntensityBand.Mid);
            KillLivingEnemies();
            Assert.IsTrue(director.IsWaveIncoming);

            director.RestartRun(7);
            Assert.IsFalse(director.IsWaveIncoming);
            Assert.AreEqual(0, director.WavesPending);
            Assert.AreEqual(0, director.SpawnRings.Count);
            yield return null;
            Assert.AreEqual(0, RingsInScene);

            director.ChooseDoor(0);
            int roomOne = director.Enemies.Count;
            yield return TestWait.Realtime(EnemyVariantRules.WaveSpawnDelaySeconds + 0.6f);
            Assert.AreEqual(0, waveTwoStarts, "The old room's wave never arrives.");
            Assert.AreEqual(roomOne, director.Enemies.Count);
        }

        [Test]
        public void DebugDefeatAllEnemies_AlsoClearsAPendingWave()
        {
            EnterRoom(MapCardKind.Vanguard, IntensityBand.Mid);
            director.DebugDefeatAllEnemies();

            Assert.AreEqual(RunDirectorState.Reward, director.State);
            Assert.AreEqual(1, waveTwoStarts);
            Assert.AreEqual(0, director.SpawnRings.Count);
        }

        [Test]
        public void EliteRoom_IsOneBrute_WithNoWave()
        {
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.EliteTrial);
            Assert.AreEqual(1, director.Enemies.Count);
            Assert.AreEqual(EnemyVariant.Brute, director.Enemies[0].Variant);
            Assert.AreEqual(0, director.WavesPending);
            StringAssert.StartsWith("EliteTrial Brute Enemy", director.Enemies[0].name);
        }
    }
}
