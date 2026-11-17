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
    /// Measurement, not a regular test: plays seeded runs to the boss (first draft card and relic taken, fights
    /// debug-cleared), then lets <see cref="BossFightBot"/> fight the boss in real time and logs how long it took.
    /// Results go to TestResults/bossfight.txt. Run alone:
    ///   -runTests -testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.BossFightMeasurement
    /// </summary>
    [Explicit("Real-time boss fights for tuning (several minutes); run on its own.")]
    public class BossFightMeasurement
    {
        private const string RunSceneName = "M3_Run";
        private const float CapSeconds = 300f;
        private static readonly int[] Seeds = { 3, 5, 6, 7, 8, 9 };
        private static readonly StringBuilder Report = new StringBuilder();

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
        public IEnumerator FightTheBoss([ValueSource(nameof(Seeds))] int seed)
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            director.RestartRun(seed);

            int drafted = 0;
            for (int guard = 0; guard < 30 && director.Boss == null; guard++)
            {
                switch (director.State)
                {
                    case RunDirectorState.ChoosingDoor: director.ChooseDoor(0); break;
                    case RunDirectorState.Fighting: director.DebugDefeatAllEnemies(); break;
                    case RunDirectorState.Reward:
                        var reward = director.CurrentReward;
                        if (reward.RelicOptions != null && reward.RelicOptions.Count > 0) director.ChooseRelic(0);
                        while (reward.PicksMade < reward.PicksAllowed && reward.TryPick(0, director.Run)) drafted++;
                        director.FinishReward();
                        break;
                    case RunDirectorState.Utility: director.LeaveUtility(); break;
                }
                yield return null;
            }
            var boss = director.Boss;
            Assert.IsNotNull(boss, "Never reached the boss.");

            int exhausted = 0, reloadWindows = 0, bossSleights = 0, bigDeadlocks = 0, zeroBreaks = 0;
            boss.ExhaustedStarted += () => exhausted++;
            boss.CycleEnded += e => { if (e == BossCycleEnd.ReloadWindow) reloadWindows++; };
            boss.SleightFired += _ => bossSleights++;
            void OnDeadlock(DeadlockResult r) { if (r.IsBig) bigDeadlocks++; }
            void OnClash(ClashEvent e) { if (e.Result.BrokeByZero) zeroBreaks++; }
            DeadlockController.Resolved += OnDeadlock;
            ClashSystem.ClashOccurred += OnClash;

            var bot = director.Player.gameObject.AddComponent<BossFightBot>();
            bot.Seed = seed;
            bot.Init(director.Player, boss);
            var playerStatus = director.Player.GetComponent<CombatantStatus>();
            int deckSize = director.Run.Deck.Count;

            float start = Time.time;
            while (!boss.Status.IsDead && !playerStatus.IsDead && Time.time - start < CapSeconds)
                yield return null;
            float seconds = Time.time - start;

            DeadlockController.Resolved -= OnDeadlock;
            ClashSystem.ClashOccurred -= OnClash;
            string outcome = boss.Status.IsDead ? "WIN" : playerStatus.IsDead ? "LOSS" : "TIMEOUT";
            string line = $"seed {seed,2}: {outcome,-7} {seconds,6:0.0}s  bossHP {boss.Status.Hp,3}/{boss.Status.MaxHp}  " +
                          $"playerHP {playerStatus.Hp,3}/{playerStatus.MaxHp}  deck {deckSize} (+{drafted} drafted)  " +
                          $"exhausted {exhausted} reloadWin {reloadWindows} bossSleights {bossSleights} big {bigDeadlocks} " +
                          $"zeroBreaks {zeroBreaks}  bot: cards {bot.CardsPlayed} parries {bot.Parries} dashes {bot.Dashes} " +
                          $"sleights {bot.SleightsFired} reloadPerfect {bot.ReloadsPerfect}/{bot.ReloadsPerfect + bot.ReloadsOther}";
            Debug.Log("[BossFight] " + line);
            Report.AppendLine(line);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "bossfight.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, $"Boss HP percent {RoomIntensity.BossHpPercent}\n" + Report);
            Object.Destroy(bot);
        }
    }
}
