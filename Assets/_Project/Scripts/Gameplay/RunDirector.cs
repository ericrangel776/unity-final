using System;
using System.Collections;
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
    public sealed partial class RunDirector : MonoBehaviour
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
        [Tooltip("Casters spawn this much further from the player than their arc point.")]
        [SerializeField] private float casterSpawnBack = 2f;
        [Tooltip("Spawn points are clamped to +-this on X and Z (inside the arena walls).")]
        [SerializeField] private float arenaHalfExtent = 10f;
        [Tooltip("Where the exit door stands (built at runtime).")]
        [SerializeField] private Vector3 doorPosition = new Vector3(0f, 0f, 8f);

        [Header("Enemy tuning")]
        [SerializeField] private int enemyBaseHp = 60;
        [SerializeField] private float eliteScale = 1.4f;
        [SerializeField] private float bossScale = 1.8f;

        private readonly List<TrainingDummyEnemy> enemies = new List<TrainingDummyEnemy>();
        private CombatantStatus playerStatus;
        private DeckRuntimeManager playerDeck;
        private CombatActionController playerCombat;
        private RelicEffects playerRelics;
        private ExitDoor door;
        /// <summary>The player's authored max HP, so a new run starts from it and not from last run's upgrades.</summary>
        private int playerBaseMaxHp;
        /// <summary>Wave 2's mix, rolled with wave 1 so the composition RNG is drawn once per room.</summary>
        private IReadOnlyList<EnemyVariant> waveTwoMix;
        private Coroutine waveRoutine;
        private readonly List<SpawnRingFx> rings = new List<SpawnRingFx>();

        public RunState Run { get; private set; }
        public RunDirectorState State { get; private set; }
        public RunContent Content => content;
        public TrainingDummyEnemy EnemyPrefab => enemyPrefab;
        public int EnemyBaseHp => enemyBaseHp;
        public PlayerCombatInput Player => player;
        public RelicEffects PlayerRelics => playerRelics;
        /// <summary>Open only while <see cref="RunDirectorState.ChoosingDoor"/>; walking in raises <see cref="ExitDoorEntered"/>.</summary>
        public ExitDoor Door => door;
        /// <summary>Spawned enemies of the current room that haven't been removed yet.</summary>
        public IReadOnlyList<TrainingDummyEnemy> Enemies => enemies;
        /// <summary>The boss of the current room, or null outside the boss fight.</summary>
        public BossEnemy Boss { get; private set; }
        /// <summary>Raised once the boss is spawned and dealt (boss HUD/FX bind to it).</summary>
        public event Action<BossEnemy> BossSpawned;
        /// <summary>Waves still to come in this room (0 or 1). Cleared the moment wave 1 dies and the wave-2 delay starts.</summary>
        public int WavesPending { get; private set; }
        /// <summary>True during the delay between wave 1's last death and wave 2's spawn.</summary>
        public bool IsWaveIncoming => waveRoutine != null;
        /// <summary>Variants of the wave currently spawned (empty outside combat).</summary>
        public IReadOnlyList<EnemyVariant> CurrentMix { get; private set; } = Array.Empty<EnemyVariant>();
        /// <summary>Spawn-point markers of the incoming wave (empty otherwise).</summary>
        public IReadOnlyList<SpawnRingFx> SpawnRings => rings;
        /// <summary>A wave spawned: 1 when the room starts, 2 for the second wave.</summary>
        public event Action<int> WaveStarted;
        public CombatReward CurrentReward { get; private set; }
        public ShopVisit CurrentShop { get; private set; }
        public ForgeVisit CurrentForge { get; private set; }
        public RestVisit CurrentRest { get; private set; }

        public event Action<RunDirectorState> StateChanged;
        public event Action<MapCard> RoomStarted;
        public event Action<CombatReward> RewardReady;
        /// <summary>True on victory, false on defeat.</summary>
        public event Action<bool> RunEnded;
        /// <summary>The player walked into the open exit door (the UI opens the Map Card choice).</summary>
        public event Action ExitDoorEntered;

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
            playerBaseMaxHp = playerStatus.MaxHp;
            playerRelics = player.GetComponent<RelicEffects>();
            if (playerRelics == null) playerRelics = player.gameObject.AddComponent<RelicEffects>();
            player.RespawnOnDeath = false;
            playerStatus.Died += HandlePlayerDied;
            // Added after DeckRuntimeManager's own listener, so a 0-broken card is already set aside here.
            playerCombat.CardBroken += HandlePlayerCardBroken;
            door = ExitDoor.Create(doorPosition, transform);
            door.Target = player.transform;
            door.PlayerEntered += HandleDoorEntered;

            if (startOnPlay) StartRun(seed);
        }

        private void OnDestroy()
        {
            if (playerStatus != null) playerStatus.Died -= HandlePlayerDied;
            if (playerCombat != null) playerCombat.CardBroken -= HandlePlayerCardBroken;
            if (door != null) door.PlayerEntered -= HandleDoorEntered;
        }

        // ---- run flow ----

        /// <summary>New run in place: enemies removed, player revived on the starter deck, first offer dealt.</summary>
        public void StartRun(int runSeed = 0)
        {
            ClearEnemies();
            ClearVisits();
            CurrentReward = null;

            LastSeed = runSeed != 0 ? runSeed : Environment.TickCount;
            Run = new RunState(content.StarterDeck, playerBaseMaxHp, LastSeed);
            player.ResetForNewRoom();
            playerStatus.SetMaxHp(Run.MaxHp); // drop last run's max-HP upgrades
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
            // Deliberate: HP is restored to what it was when the last room ended. Anything between rooms (Overburn
            // costs, Ashen Sigil heals from Sleights fired at the door) is undone, so the door isn't a free-heal spot.
            playerStatus.SetMaxHpKeepHp(Run.MaxHp, Run.Hp);
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
            playerStatus.SetMaxHpKeepHp(Run.MaxHp, Run.Hp); // Heartwood Seed shows right away
            return true;
        }

        /// <summary>Leaves a Shop/Forge/Rest room and deals the next offer.</summary>
        public void LeaveUtility()
        {
            RequireState(RunDirectorState.Utility);
            Run.CompleteRoom(null);
            ClearVisits();
            playerStatus.SetMaxHpKeepHp(Run.MaxHp, Run.Hp); // a rest heal or max-HP upgrade shows right away
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

        /// <summary>
        /// Rolls the room's composition (wave 1 and, at Intensity 4+, wave 2) and spawns wave 1. See
        /// docs/specs/enemy-variants.md.
        /// </summary>
        private void SpawnEnemies(in MapCard room)
        {
            var rng = new System.Random(NextSeed());
            var mix = EnemyVariantRules.Composition(room, RoomIntensity.EnemyCount(room), rng);
            int waveTwoCount = EnemyVariantRules.WaveTwoCount(room);
            waveTwoMix = waveTwoCount > 0 ? EnemyVariantRules.Composition(room, waveTwoCount, rng) : null;
            WavesPending = waveTwoCount > 0 ? 1 : 0;
            SpawnWave(room, mix);
            WaveStarted?.Invoke(1);
        }

        /// <summary>Spawns one enemy per entry of <paramref name="mix"/>, each with its variant's deck, stats and look.</summary>
        private void SpawnWave(in MapCard room, IReadOnlyList<EnemyVariant> mix, Vector3? positionOverride = null)
        {
            int count = mix.Count;
            float tempo = RoomIntensity.EnemyTempoPercent(room) / 100f;
            int roomMaxHp = RoomIntensity.EnemyMaxHp(enemyBaseHp, room);
            float roomPostureScale = RoomIntensity.EnemyPosturePercent(room) / 100f;
            int valueBonus = RoomIntensity.EnemyCardValueBonus(room);
            float scale = room.Kind == MapCardKind.EliteTrial ? eliteScale : room.Kind == MapCardKind.Boss ? bossScale : 1f;

            for (int i = 0; i < count; i++)
            {
                var variant = mix[i];
                var stats = EnemyVariantRules.Stats(variant);
                var deck = new List<CardData>(content.EnemyDeckFor(room.Kind, variant));
                if (room.Trial == EliteTrial.Counterpuncher)
                    for (int z = 0; z < RoomIntensity.CounterpuncherZeros(room.Number); z++) deck.Add(content.EnemyZero);

                var position = positionOverride ?? WaveSpawnPoint(i, mix);
                var facing = playerSpawn - position;
                facing.y = 0f;
                var enemy = Instantiate(enemyPrefab, position, Quaternion.LookRotation(facing));
                enemy.name = $"{room.Kind} {variant} Enemy {i + 1}";
                enemy.transform.localScale *= scale;
                var pop = enemy.GetComponent<DeathPop>();
                if (pop != null)
                    pop.Scale = room.Kind == MapCardKind.EliteTrial ? DeathPop.EliteScale
                        : room.Kind == MapCardKind.Boss ? DeathPop.BossScale : 1f;
                enemy.RespawnOnDeath = false;
                enemy.Target = player.transform;
                enemy.ApplyTempo(tempo);
                // Before the boss components: the boss's TurnHook forbids a variant (it is always a Brute).
                enemy.ApplyVariant(variant);
                EnemyVariantLook.Apply(enemy, variant);

                enemy.Status.SetMaxHp(Math.Max(1, RewardRules.PercentHalfUp(roomMaxHp, stats.HpPercent)));
                enemy.Status.SetMaxPosture(enemy.Status.MaxPosture * roomPostureScale * (stats.PosturePercent / 100f));
                // Before the deal: the boss sets its 9-spacing rule on the deck cycle.
                if (room.Kind == MapCardKind.Boss)
                {
                    Boss = enemy.gameObject.AddComponent<BossEnemy>();
                    enemy.gameObject.AddComponent<BossSleightClash>().Watch(playerCombat);
                    enemy.gameObject.AddComponent<BossTelegraphFx>();
                    enemy.gameObject.AddComponent<BossStateFx>();
                }
                enemy.Deck.SetRoomValueMod(valueBonus);
                int deckSeed = NextSeed();
                enemy.Deck.SetStartingDeck(deck, deckSeed);
                // Boss HP is relative to the player's *starting* max HP, per floor (BossRules.MaxHp), so max-HP
                // upgrades don't make the boss tougher.
                if (room.Kind == MapCardKind.Boss) enemy.Status.SetMaxHp(BossRules.MaxHp(Run.Floor, Run.StartingMaxHp));
                // The natural boss AI, seeded from the deck seed (no extra draw from the run's stream).
                if (room.Kind == MapCardKind.Boss) Boss.Policy = new NaturalBossPolicy(new System.Random(unchecked(deckSeed * 31 + 7)));
                ApplyTrial(enemy, room);
                ApplyElementResistance(enemy, room.Kind);

                var captured = enemy;
                enemy.Status.Died += () => HandleEnemyDied(captured);
                enemies.Add(enemy);
            }
            CurrentMix = mix;
            if (Boss != null) BossSpawned?.Invoke(Boss);
        }

        /// <summary>Arc point <paramref name="index"/>; Casters stand further back. Clamped inside the arena.</summary>
        private Vector3 WaveSpawnPoint(int index, IReadOnlyList<EnemyVariant> mix)
        {
            var p = SpawnPoint(index, mix.Count);
            if (mix[index] == EnemyVariant.Caster) p.z += casterSpawnBack;
            p.x = Mathf.Clamp(p.x, -arenaHalfExtent, arenaHalfExtent);
            p.z = Mathf.Clamp(p.z, -arenaHalfExtent, arenaHalfExtent);
            return p;
        }

        /// <summary>Rings at wave 2's spawn points for the delay, then wave 2 (unless the fight ended meanwhile).</summary>
        private IEnumerator WaveTwoRoutine(MapCard room)
        {
            for (int i = 0; i < waveTwoMix.Count; i++)
                rings.Add(SpawnRingFx.Create(WaveSpawnPoint(i, waveTwoMix), EnemyVariantRules.WaveSpawnDelaySeconds));
            yield return new WaitForSeconds(EnemyVariantRules.WaveSpawnDelaySeconds);
            waveRoutine = null; // finished: don't stop ourselves below
            SpawnPendingWaveNow(room);
        }

        /// <summary>Ends the wave-2 delay now: removes the rings and spawns wave 2 if the fight is still on.</summary>
        private void SpawnPendingWaveNow(in MapCard room)
        {
            if (waveRoutine != null) StopCoroutine(waveRoutine);
            waveRoutine = null;
            ClearRings();
            if (State != RunDirectorState.Fighting || waveTwoMix == null) return;
            var mix = waveTwoMix;
            waveTwoMix = null;
            SpawnWave(room, mix);
            WaveStarted?.Invoke(2);
        }

        private void ClearRings()
        {
            foreach (var r in rings) if (r != null) Destroy(r.gameObject);
            rings.Clear();
        }

        /// <summary>
        /// Boss and Elites shrug statuses off twice as fast; only the boss also halves Wind knockback
        /// (docs/specs/element-mechanics.md).
        /// </summary>
        private static void ApplyElementResistance(TrainingDummyEnemy enemy, MapCardKind kind)
        {
            bool tough = kind == MapCardKind.EliteTrial || kind == MapCardKind.Boss;
            var fx = enemy.GetComponent<ElementStatusEffects>();
            if (fx != null) fx.DurationScale = tough ? ElementRules.EliteBossDurationScale : 1f;
            if (kind == MapCardKind.Boss) enemy.Status.KnockbackTakenMultiplier = ElementRules.BossWindKnockbackScale;
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
            if (WavesPending > 0)
            {
                // Cleared first, so two enemies dying on the same frame start only one wave.
                WavesPending = 0;
                waveRoutine = StartCoroutine(WaveTwoRoutine(Run.CurrentRoom));
                return;
            }
            if (waveRoutine != null) return;
            RoomCleared();
        }

        private void RoomCleared()
        {
            player.HealChannel.Cancel(); // a heal still channelling when the room ends doesn't land
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

        private void HandleDoorEntered()
        {
            if (State == RunDirectorState.ChoosingDoor) ExitDoorEntered?.Invoke();
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
            Boss = null;
            if (waveRoutine != null) StopCoroutine(waveRoutine);
            waveRoutine = null;
            ClearRings();
            WavesPending = 0;
            waveTwoMix = null;
            CurrentMix = Array.Empty<EnemyVariant>();
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
            if (door != null) door.SetOpen(state == RunDirectorState.ChoosingDoor);
            StateChanged?.Invoke(state);
        }

        private void RequireState(RunDirectorState state)
        {
            if (State != state) throw new InvalidOperationException($"RunDirector is in {State}, expected {state}.");
        }

        // ---- debug: drive the run by hand from the Inspector ⋮ menu ----

        [ContextMenu("Run/Choose Door 1")] private void DebugDoor1() => ChooseDoor(0);
        [ContextMenu("Run/Choose Door 2")] private void DebugDoor2() => ChooseDoor(Math.Min(1, Run.CurrentOffer.Count - 1));
        [ContextMenu("Run/Choose Door 3")] private void DebugDoor3() => ChooseDoor(Math.Min(2, Run.CurrentOffer.Count - 1));
        [ContextMenu("Run/Choose Relic 1")] private void DebugRelic1() => ChooseRelic(0);
        [ContextMenu("Run/Finish Reward")] private void DebugFinishReward() => FinishReward();
        [ContextMenu("Run/Leave Utility Room")] private void DebugLeaveUtility() => LeaveUtility();
        [ContextMenu("Run/Restart")] private void DebugRestart() => RestartRun();

        /// <summary>
        /// Playtesting: plays the run forward into the boss fight. Earlier fights are debug-cleared, the first draft card
        /// and relic are taken (so the deck looks like a real run's) and Blue rooms are left. Starts a run if needed; a
        /// finished run is restarted. Returns false if the boss wasn't reached.
        /// </summary>
        [ContextMenu("Run/Skip To Boss")]
        public bool DebugSkipToBoss()
        {
            if (Run == null || State == RunDirectorState.Victory || State == RunDirectorState.Defeat) StartRun();
            for (int step = 0; step < 50; step++)
            {
                switch (State)
                {
                    case RunDirectorState.ChoosingDoor:
                        ChooseDoor(0);
                        break;
                    case RunDirectorState.Fighting:
                        if (Boss != null) return true;
                        DebugDefeatAllEnemies();
                        break;
                    case RunDirectorState.Reward:
                        if (CurrentReward.RelicOptions != null && CurrentReward.RelicOptions.Count > 0) ChooseRelic(0);
                        while (CurrentReward.PicksMade < CurrentReward.PicksAllowed && CurrentReward.TryPick(0, Run)) { }
                        FinishReward();
                        break;
                    case RunDirectorState.Utility:
                        LeaveUtility();
                        break;
                    default:
                        StartRun();
                        break;
                }
            }
            return State == RunDirectorState.Fighting && Boss != null;
        }

        [ContextMenu("Run/Defeat All Enemies")]
        public void DebugDefeatAllEnemies()
        {
            // Overwhelming Sleight hits (values aren't clamped to 9), repeated so a guarded boss still goes down.
            var payload = new AttackPayload(playerStatus.OwnerId, Team.Player, 999, false, true, Time.frameCount,
                CardShape.Strike, Element.Physical);
            // A pending second wave is spawned at once and defeated too, so a debug clear still ends the room.
            for (int wave = 0; wave < 2; wave++)
            {
                foreach (var e in enemies.ToArray())
                    for (int hit = 0; hit < 10 && e != null && !e.Status.IsDead; hit++)
                        e.Status.TakeHit(payload);
                if (waveRoutine == null) break;
                SpawnPendingWaveNow(Run.CurrentRoom);
            }
        }
    }
}
