using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Debug-menu entry points (docs/specs/debug-menu.md). They reuse the normal run flow (finish the room, deal, enter,
    /// spawn through <see cref="SpawnWave"/>) so a debug action leaves the run in a state the game can continue from.
    /// </summary>
    public sealed partial class RunDirector
    {
        /// <summary>The seed the current run started with (shown and copied by the debug menu).</summary>
        public int LastSeed { get; private set; }

        /// <summary>The room number of the next door: the current one at a door or reward, the next one mid-room, 1 after the run.</summary>
        public int DebugNextDoorRoomNumber
        {
            get
            {
                if (Run == null) return 1;
                switch (State)
                {
                    case RunDirectorState.ChoosingDoor:
                    case RunDirectorState.Reward: return Run.RoomNumber;
                    case RunDirectorState.Fighting:
                        return Run.CurrentRoom.Kind == MapCardKind.Boss ? 1 : Run.RoomNumber + 1;
                    case RunDirectorState.Utility: return Run.RoomNumber + 1;
                    default: return 1;
                }
            }
        }

        /// <summary>Room 6 is the boss: only the Boss card can be forced there (there's no room 7).</summary>
        public bool DebugCanEnterRoom(in MapCard card) =>
            Run != null && (card.Kind == MapCardKind.Boss || DebugNextDoorRoomNumber != RunState.BossRoom);

        /// <summary>Finishes whatever is going on (fight, reward, Blue room, finished run) and enters <paramref name="card"/>.</summary>
        public void DebugEnterRoom(MapCard card)
        {
            if (!DebugCanEnterRoom(card))
                throw new InvalidOperationException($"Can't force {card} at room {DebugNextDoorRoomNumber}.");
            DebugFinishCurrentRoom();
            Run.DealOffer();
            Run.DebugSetOffer(new[] { card });
            ChooseDoor(0);
        }

        /// <summary>Gets to the next door: debug-clears a fight, closes a reward, leaves a Blue room, restarts a finished run.</summary>
        public void DebugFinishCurrentRoom()
        {
            if (Run == null)
            {
                StartRun();
                return;
            }
            if (State == RunDirectorState.Fighting) DebugDefeatAllEnemies();
            switch (State)
            {
                case RunDirectorState.Reward: FinishReward(); break;
                case RunDirectorState.Utility: LeaveUtility(); break;
                case RunDirectorState.Victory:
                case RunDirectorState.Defeat: StartRun(); break;
            }
        }

        /// <summary>Spawning works in a running mob fight (not Elite/Boss: their trial and boss wiring is one-per-room).</summary>
        public bool DebugCanSpawnEnemy =>
            State == RunDirectorState.Fighting && Run != null && !Run.CurrentRoom.IsBigFight;

        /// <summary>
        /// Spawns one <paramref name="variant"/> at <paramref name="position"/> with the room's scaling. It joins
        /// <see cref="Enemies"/> and has to die before the room clears. Returns it, or null if not allowed now.
        /// </summary>
        public TrainingDummyEnemy DebugSpawnEnemy(EnemyVariant variant, Vector3 position)
        {
            if (!DebugCanSpawnEnemy) return null;
            var before = CurrentMix;
            SpawnWave(Run.CurrentRoom, new[] { variant }, position);
            var mix = new List<EnemyVariant>(before) { variant };
            CurrentMix = mix;
            return enemies[enemies.Count - 1];
        }

        /// <summary>Ends a running wave-2 delay now.</summary>
        public void DebugSkipWaveDelay()
        {
            if (waveRoutine != null) SpawnPendingWaveNow(Run.CurrentRoom);
        }

        /// <summary>Sets the player's HP now and in the run, so it survives the room carry-over. Clamped to [1, max].</summary>
        public void DebugSetPlayerHp(int hp)
        {
            if (playerStatus == null || playerStatus.IsDead) return;
            hp = Mathf.Clamp(hp, 1, playerStatus.MaxHp);
            playerStatus.SetHp(hp);
            Run?.SetHp(hp);
        }

        public void DebugIncreaseMaxHp(int amount)
        {
            if (Run == null || amount <= 0) return;
            SyncRunHpFromPlayer();
            Run.IncreaseMaxHp(amount);
            ShowRunMaxHp();
        }

        /// <summary>Grants <paramref name="relic"/> as <see cref="ChooseRelic"/> does. False if already held.</summary>
        public bool DebugGrantRelic(RelicId relic)
        {
            if (Run == null) return false;
            SyncRunHpFromPlayer();
            if (!Run.AddRelic(relic)) return false;
            playerRelics.SetRelics(Run.Relics);
            ShowRunMaxHp();
            return true;
        }

        /// <summary>Adds a copy of <paramref name="card"/> to the run deck and re-deals the player's cycle.</summary>
        public void DebugAddCard(CardData card)
        {
            if (Run == null || card == null) return;
            Run.AddCard(card);
            RedealPlayerDeck();
        }

        /// <summary>Removes one copy of <paramref name="card"/> from the run deck (if any) and re-deals. False if none.</summary>
        public bool DebugRemoveCard(CardData card)
        {
            if (Run == null || card == null) return false;
            foreach (var c in Run.Deck)
            {
                if (c.Data != card) continue;
                Run.RemoveCard(c);
                RedealPlayerDeck();
                return true;
            }
            return false;
        }

        // A fight's live HP isn't in the run until the room ends; copy it first so max-HP changes don't undo damage.
        private void SyncRunHpFromPlayer()
        {
            if (State == RunDirectorState.Fighting && playerStatus != null && !playerStatus.IsDead)
                Run.SetHp(playerStatus.Hp);
        }

        private void ShowRunMaxHp()
        {
            if (playerStatus != null && !playerStatus.IsDead) playerStatus.SetMaxHpKeepHp(Run.MaxHp, Run.Hp);
        }

        // Like PreparePlayerDeck, but without drawing from the run's seed stream (debug use mustn't shift seeded runs).
        private void RedealPlayerDeck()
        {
            playerDeck.UseRunDeck(Run.Deck, UnityEngine.Random.Range(1, int.MaxValue));
            playerRelics.SetRelics(Run.Relics);
            playerRelics.BeginRoom();
        }
    }
}
