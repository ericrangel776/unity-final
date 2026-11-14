using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    public enum RunDirectorState
    {
        ChoosingDoor,
        Fighting,
        Reward,
        Utility,
        Victory,
        Defeat
    }

    /// <summary>
    /// Drives one run in a single reused arena: deals Map Card offers, sets up each room (scaled dummies or a
    /// Blue visit), detects the clear, pays the reward and ends the run on boss kill or player death.
    /// All rules live in <see cref="RunState"/>; this only applies them to the scene. See docs/specs/m3-room-loop.md.
    /// </summary>
    public sealed class RunDirector : MonoBehaviour
    {
        [SerializeField] private RunContent content;
        [SerializeField] private TrainingDummyEnemy enemyPrefab;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerCombatInput player;
        [Tooltip("0 = random seed each run.")]
        [SerializeField] private int seed;
        [SerializeField] private bool startOnPlay = true;

        [Header("Layout")]
        [SerializeField] private Vector3 playerSpawn = new Vector3(0f, 0f, -6f);
        [SerializeField] private Vector3 enemyCenter = new Vector3(0f, 0f, 4f);
        [SerializeField] private float enemySpread = 3.5f;

        [Header("Enemy tuning")]
        [SerializeField] private int enemyBaseHp = 60;
        [SerializeField] private float eliteScale = 1.4f;
        [SerializeField] private float bossScale = 1.8f;

        private readonly List<TrainingDummyEnemy> enemies = new List<TrainingDummyEnemy>();
        private CombatantStatus playerStatus;
        private DeckRuntimeManager playerDeck;
        private CombatActionController playerCombat;
        private RelicEffects playerRelics;

        public RunState Run { get; private set; }
        public RunDirectorState State { get; private set; }
        public RunContent Content => content;
        public TrainingDummyEnemy EnemyPrefab => enemyPrefab;
        public int EnemyBaseHp => enemyBaseHp;
        public PlayerCombatInput Player => player;
        public RelicEffects PlayerRelics => playerRelics;
        /// <summary>Spawned enemies of the current room that haven't been removed yet.</summary>
        public IReadOnlyList<TrainingDummyEnemy> Enemies => enemies;
        public CombatReward CurrentReward { get; private set; }
        public ShopVisit CurrentShop { get; private set; }
        public ForgeVisit CurrentForge { get; private set; }
        public RestVisit CurrentRest { get; private set; }

        public event Action<RunDirectorState> StateChanged;
        public event Action<MapCard> RoomStarted;
        public event Action<CombatReward> RewardReady;
        /// <summary>True on victory, false on defeat.</summary>
        public event Action<bool> RunEnded;

        private void Start()
        {
            if (player == null) player = FindAnyObjectByType<PlayerCombatInput>();
            if (content == null || enemyPrefab == null || player == null)
            {
                Debug.LogError("RunDirector needs RunContent, an enemy prefab and a player.", this);
                enabled = false;
                return;
            }
            playerStatus = player.GetComponent<CombatantStatus>();
            playerDeck = player.GetComponent<DeckRuntimeManager>();
            playerCombat = player.GetComponent<CombatActionController>();
            playerRelics = player.GetComponent<RelicEffects>();
            if (playerRelics == null) playerRelics = player.gameObject.AddComponent<RelicEffects>();
            player.RespawnOnDeath = false;
            playerStatus.Died += HandlePlayerDied;
            // Added after DeckRuntimeManager's own listener, so a 0-broken card is already set aside here.
            playerCombat.CardBroken += HandlePlayerCardBroken;

            if (startOnPlay) StartRun(seed);
        }

        private void OnDestroy()
        {
            if (playerStatus != null) playerStatus.Died -= HandlePlayerDied;
            if (playerCombat != null) playerCombat.CardBroken -= HandlePlayerCardBroken;
        }

        // ---- run flow ----

        /// <summary>New run in place: enemies removed, player revived on the starter deck, first offer dealt.</summary>
        public void StartRun(int runSeed = 0)
        {
            ClearEnemies();
            ClearVisits();
            CurrentReward = null;

            Run = new RunState(content.StarterDeck, playerStatus.MaxHp, runSeed != 0 ? runSeed : Environment.TickCount);
            player.ResetForNewRoom();
            playerStatus.Revive();
            MovePlayerToSpawn();
            PreparePlayerDeck(null);
            Run.DealOffer();
            SetState(RunDirectorState.ChoosingDoor);
        }

        public void RestartRun(int runSeed = 0) => StartRun(runSeed);

        /// <summary>Enters the room on offer card <paramref name="index"/>.</summary>
        public void ChooseDoor(int index)
        {
            RequireState(RunDirectorState.ChoosingDoor);
            var room = Run.EnterRoom(index);

            ClearEnemies(); // last room's corpses may still be waiting out their removal delay
            player.ResetForNewRoom();
            MovePlayerToSpawn();
            playerStatus.SetHp(Run.Hp);
            PreparePlayerDeck(room);
            RoomStarted?.Invoke(room);

            if (room.IsCombat)
            {
                SpawnEnemies(room);
                SetState(RunDirectorState.Fighting);
                return;
            }

            switch (room.Kind)
            {
                case MapCardKind.Shop: CurrentShop = new ShopVisit(room.Number, content.DraftPool, Run.Rng); break;
                case MapCardKind.Forge: CurrentForge = new ForgeVisit(room.Number); break;
                case MapCardKind.Rest: CurrentRest = new RestVisit(room.Number); break;
            }
            SetState(RunDirectorState.Utility);
        }

        /// <summary>Closes the reward (an untouched draft counts as a skip) and deals the next offer.</summary>
        public void FinishReward()
        {
            RequireState(RunDirectorState.Reward);
            if (CurrentReward != null && !CurrentReward.IsDraftClosed) CurrentReward.Skip(Run);
            CurrentReward = null;
            Run.DealOffer();
            SetState(RunDirectorState.ChoosingDoor);
        }

        /// <summary>Takes relic option <paramref name="index"/> from the current reward. False if none is open or already taken.</summary>
        public bool ChooseRelic(int index)
        {
            RequireState(RunDirectorState.Reward);
            if (CurrentReward == null || !CurrentReward.TryPickRelic(index, Run)) return false;
            playerRelics.SetRelics(Run.Relics);
            return true;
        }

        /// <summary>Leaves a Shop/Forge/Rest room and deals the next offer.</summary>
        public void LeaveUtility()
        {
            RequireState(RunDirectorState.Utility);
            Run.CompleteRoom(null);
            ClearVisits();
            playerStatus.SetHp(Run.Hp); // a rest heal shows right away
            Run.DealOffer();
            SetState(RunDirectorState.ChoosingDoor);
        }

        // ---- rooms ----

        private void PreparePlayerDeck(MapCard? room)
        {
            int buff = room.HasValue ? RoomIntensity.PlayerElementBuff(room.Value) : 0;
            Element? element = buff != 0 ? room.Value.BuffElement : (Element?)null;
            playerDeck.SetRoomValueMod(buff, element);
            playerDeck.UseRunDeck(Run.Deck, NextSeed());
            // After the rebuild: Reinitialize resets the Overburn price, and relics set this room's freebies.
            playerRelics.SetRelics(Run.Relics);
            playerRelics.BeginRoom();
        }

        private void SpawnEnemies(in MapCard room)
        {
            int count = RoomIntensity.EnemyCount(room);
            var deck = new List<CardData>(content.EnemyDeckFor(room.Kind));
            if (room.Trial == EliteTrial.Counterpuncher)
                for (int z = 0; z < RoomIntensity.CounterpuncherZeros(room.Number); z++) deck.Add(content.EnemyZero);
            float tempo = RoomIntensity.EnemyTempoPercent(room) / 100f;
            int maxHp = RoomIntensity.EnemyMaxHp(enemyBaseHp, room);
            float postureScale = RoomIntensity.EnemyPosturePercent(room) / 100f;
            int valueBonus = RoomIntensity.EnemyCardValueBonus(room);
            float scale = room.Kind == MapCardKind.EliteTrial ? eliteScale : room.Kind == MapCardKind.Boss ? bossScale : 1f;

            for (int i = 0; i < count; i++)
            {
                var position = SpawnPoint(i, count);
                var facing = playerSpawn - position;
                facing.y = 0f;
                var enemy = Instantiate(enemyPrefab, position, Quaternion.LookRotation(facing));
                enemy.name = $"{room.Kind} Enemy {i + 1}";
                enemy.transform.localScale *= scale;
                enemy.RespawnOnDeath = false;
                enemy.Target = player.transform;
                enemy.ApplyTempo(tempo);

                enemy.Status.SetMaxHp(maxHp);
                enemy.Status.SetMaxPosture(enemy.Status.MaxPosture * postureScale);
                enemy.Deck.SetRoomValueMod(valueBonus);
                enemy.Deck.SetStartingDeck(deck, NextSeed());
                ApplyTrial(enemy, room);

                var captured = enemy;
                enemy.Status.Died += () => HandleEnemyDied(captured);
                enemies.Add(enemy);
            }
        }

        /// <summary>Purple Elite trial hooks (docs/specs/m3-elite-trials.md). Hexer lives in <see cref="HandlePlayerCardBroken"/>.</summary>
        private void ApplyTrial(TrainingDummyEnemy enemy, in MapCard room)
        {
            switch (room.Trial)
            {
                case EliteTrial.Veiled:
                    enemy.VeiledRevealSeconds = RoomIntensity.VeiledRevealSeconds(room.Number);
                    break;
                case EliteTrial.Counterpuncher:
                    enemy.gameObject.AddComponent<CounterpuncherTrial>().Watch(playerCombat);
                    break;
            }
        }

        /// <summary>Hexer: each Card Break the player suffers burns that card (Floor of 3 permitting) and costs HP.</summary>
        private void HandlePlayerCardBroken(RuntimeCardInstance card, bool brokeByZero)
        {
            if (State != RunDirectorState.Fighting || Run.CurrentRoom.Trial != EliteTrial.Hexer) return;
            if (card != null) playerDeck.Cycle.BurnFromCycle(card);
            playerStatus.SpendHp(RoomIntensity.HexerHpPerBreak(Run.CurrentRoom.Number)); // non-lethal
        }

        /// <summary>Spreads <paramref name="count"/> enemies on an arc around <see cref="enemyCenter"/>.</summary>
        private Vector3 SpawnPoint(int index, int count)
        {
            if (count == 1) return enemyCenter;
            float t = index / (float)(count - 1);              // 0..1
            float angle = Mathf.Lerp(-60f, 60f, t) * Mathf.Deg2Rad;
            return enemyCenter + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle) - 1f) * enemySpread;
        }

        private void HandleEnemyDied(TrainingDummyEnemy enemy)
        {
            if (State != RunDirectorState.Fighting) return;
            foreach (var e in enemies)
                if (e != null && !e.Status.IsDead) return;
            RoomCleared();
        }

        private void RoomCleared()
        {
            Run.SetHp(playerStatus.Hp);
            bool boss = Run.CurrentRoom.Kind == MapCardKind.Boss;
            CurrentReward = Run.CompleteRoom(content.DraftPool);

            if (boss)
            {
                SetState(RunDirectorState.Victory);
                RunEnded?.Invoke(true);
                return;
            }
            SetState(RunDirectorState.Reward);
            RewardReady?.Invoke(CurrentReward);
        }

        private void HandlePlayerDied()
        {
            if (State == RunDirectorState.Victory || State == RunDirectorState.Defeat) return;
            Run.SetHp(0);
            SetState(RunDirectorState.Defeat);
            RunEnded?.Invoke(false);
        }

        // ---- helpers ----

        private void MovePlayerToSpawn()
        {
            // CharacterController overrides direct position writes while enabled.
            var body = player.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            player.transform.SetPositionAndRotation(playerSpawn, Quaternion.identity);
            if (body != null) body.enabled = true;
        }

        private void ClearEnemies()
        {
            foreach (var e in enemies) if (e != null) Destroy(e.gameObject);
            enemies.Clear();
        }

        private void ClearVisits()
        {
            CurrentShop = null;
            CurrentForge = null;
            CurrentRest = null;
        }

        private int NextSeed() => Run.Rng.Next(1, int.MaxValue);

        private void SetState(RunDirectorState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        private void RequireState(RunDirectorState state)
        {
            if (State != state) throw new InvalidOperationException($"RunDirector is in {State}, expected {state}.");
        }

        // ---- manual driving until the step 6 screens exist (Inspector ⋮ menu) ----

        [ContextMenu("Run/Choose Door 1")] private void DebugDoor1() => ChooseDoor(0);
        [ContextMenu("Run/Choose Door 2")] private void DebugDoor2() => ChooseDoor(Math.Min(1, Run.CurrentOffer.Count - 1));
        [ContextMenu("Run/Choose Door 3")] private void DebugDoor3() => ChooseDoor(Math.Min(2, Run.CurrentOffer.Count - 1));
        [ContextMenu("Run/Choose Relic 1")] private void DebugRelic1() => ChooseRelic(0);
        [ContextMenu("Run/Finish Reward")] private void DebugFinishReward() => FinishReward();
        [ContextMenu("Run/Leave Utility Room")] private void DebugLeaveUtility() => LeaveUtility();
        [ContextMenu("Run/Restart")] private void DebugRestart() => RestartRun();

        [ContextMenu("Run/Defeat All Enemies")]
        public void DebugDefeatAllEnemies()
        {
            // One overwhelming Sleight hit each (Sleight values aren't clamped to 9).
            var payload = new AttackPayload(playerStatus.OwnerId, Team.Player, 999, false, true, Time.frameCount,
                CardShape.Strike, Element.Physical);
            foreach (var e in enemies.ToArray())
                if (e != null && !e.Status.IsDead) e.Status.TakeHit(payload);
        }
    }
}
