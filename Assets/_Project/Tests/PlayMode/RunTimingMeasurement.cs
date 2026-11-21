using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Measurement, not a regular test (docs/specs/run-timing.md): plays whole seeded runs with
    /// <see cref="BossFightBot"/> in run mode and logs each room's real time, HP and outcome. Doors are picked by a
    /// seeded RNG; draft card 1 and relic 1 are taken; Rest heals, Shop/Forge are left at once. Menus aren't timed.
    /// Results go to TestResults/runtiming.txt (rewritten after every seed). Run alone:
    ///   -runTests -testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.RunTimingMeasurement
    /// </summary>
    [Explicit("Real-time full runs for pacing (tens of minutes); run on its own.")]
    public class RunTimingMeasurement
    {
        private const string RunSceneName = "M3_Run";
        private const float RoomCapSeconds = 180f;
        private const float BossCapSeconds = 300f;
        private static readonly int[] Seeds = { 1, 2, 3, 4, 5, 6 };
        private static readonly List<RunTimingLog> Runs = new List<RunTimingLog>();
        /// <summary>Bot state dumps for TIMEOUT rooms, appended to the report.</summary>
        private static readonly StringBuilder Stalls = new StringBuilder();

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetSceneByName(RunSceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator PlayARun([ValueSource(nameof(Seeds))] int seed)
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            director.RestartRun(seed);
            yield return null;

            var log = new RunTimingLog(seed);
            var doorRng = new System.Random(seed * 7919 + 17);
            var playerStatus = director.Player.GetComponent<CombatantStatus>();
            var bot = director.Player.gameObject.AddComponent<BossFightBot>();
            bot.Seed = seed;
            bot.Init(director.Player, director);

            for (int guard = 0; guard < 40 && log.Result == RunResult.Incomplete; guard++)
            {
                switch (director.State)
                {
                    case RunDirectorState.ChoosingDoor:
                        director.ChooseDoor(doorRng.Next(director.Run.CurrentOffer.Count));
                        break;

                    case RunDirectorState.Fighting:
                    {
                        var room = director.Run.CurrentRoom;
                        int roomNumber = director.Run.RoomNumber;
                        string mix = MixLabel(director.CurrentMix);
                        int waves = 1 + director.WavesPending;
                        int hpBefore = playerStatus.Hp;
                        float cap = room.Kind == MapCardKind.Boss ? BossCapSeconds : RoomCapSeconds;
                        float start = Time.realtimeSinceStartup;
                        while (director.State == RunDirectorState.Fighting && Time.realtimeSinceStartup - start < cap)
                            yield return null;
                        float seconds = Time.realtimeSinceStartup - start;

                        var outcome = RoomOutcome.Cleared;
                        if (director.State == RunDirectorState.Fighting)
                        {
                            outcome = RoomOutcome.Timeout;
                            string stall = $"seed {seed} room {roomNumber} {room.Kind}: {bot.Describe()}";
                            Debug.Log("[RunTiming] TIMEOUT " + stall);
                            Stalls.AppendLine(stall);
                            director.DebugDefeatAllEnemies();
                        }
                        else if (director.State == RunDirectorState.Defeat)
                        {
                            outcome = RoomOutcome.Loss;
                        }
                        log.Add(new RoomTiming(roomNumber, room, mix, waves, seconds, hpBefore, playerStatus.Hp,
                            playerStatus.MaxHp, outcome));
                        if (director.State == RunDirectorState.Defeat) log.Finish(RunResult.Loss);
                        else if (director.State == RunDirectorState.Victory) log.Finish(RunResult.Victory);
                        break;
                    }

                    case RunDirectorState.Reward:
                        var reward = director.CurrentReward;
                        if (reward.RelicOptions != null && reward.RelicOptions.Count > 0) director.ChooseRelic(0);
                        while (reward.PicksMade < reward.PicksAllowed && reward.TryPick(0, director.Run)) { }
                        director.FinishReward();
                        break;

                    case RunDirectorState.Utility:
                    {
                        var room = director.Run.CurrentRoom;
                        int hpBefore = director.Run.Hp;
                        if (room.Kind == MapCardKind.Rest) director.CurrentRest.TryHeal(director.Run);
                        log.Add(new RoomTiming(director.Run.RoomNumber, room, "", 0, 0f, hpBefore, director.Run.Hp,
                            director.Run.MaxHp, RoomOutcome.Utility));
                        director.LeaveUtility();
                        break;
                    }

                    case RunDirectorState.Victory:
                        log.Finish(RunResult.Victory);
                        break;
                    case RunDirectorState.Defeat:
                        log.Finish(RunResult.Loss);
                        break;
                }
                yield return null;
            }
            Object.Destroy(bot);

            Runs.Add(log);
            string run = log.FormatRun();
            Debug.Log("[RunTiming] " + run);
            var report = new StringBuilder();
            foreach (var r in Runs) report.Append(r.FormatRun()).Append('\n');
            report.Append(RunTimingLog.FormatSummary(Runs));
            if (Stalls.Length > 0) report.Append("\nTIMEOUT bot states:\n").Append(Stalls);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "runtiming.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString());
            Assert.AreNotEqual(RunResult.Incomplete, log.Result, $"Seed {seed}: the run never ended.\n{run}");
        }

        /// <summary>"B2 C1 R1" from the wave's variants (zero counts omitted).</summary>
        private static string MixLabel(IReadOnlyList<EnemyVariant> mix)
        {
            int b = 0, c = 0, r = 0;
            foreach (var v in mix)
            {
                if (v == EnemyVariant.Brute) b++;
                else if (v == EnemyVariant.Caster) c++;
                else r++;
            }
            var parts = new List<string>();
            if (b > 0) parts.Add("B" + b);
            if (c > 0) parts.Add("C" + c);
            if (r > 0) parts.Add("R" + r);
            return parts.Count > 0 ? string.Join(" ", parts) : "-";
        }
    }
}
