using System;
using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The run summary and restart (docs/specs/m3-ui.md, sub-step 6).</summary>
    public class RunEndViewTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private RunScreens screens;
        private RunEndView end;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            screens = Object.FindAnyObjectByType<RunScreens>();
            end = Object.FindAnyObjectByType<RunEndView>();
            Assert.IsNotNull(end, "RunEndView missing from M3_Run (rebuild the scene).");
        }

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

        private void KillPlayer()
        {
            var lethal = new AttackPayload(12345, Team.Enemy, 999, false, true, Time.frameCount, CardShape.Strike, Element.Physical);
            director.Player.GetComponent<CombatantStatus>().TakeHit(lethal);
            Assert.AreEqual(RunDirectorState.Defeat, director.State);
        }

        private void ClearCurrentRoom()
        {
            if (director.State == RunDirectorState.Fighting)
            {
                director.DebugDefeatAllEnemies();
                if (director.State == RunDirectorState.Reward) director.FinishReward();
            }
            else director.LeaveUtility();
        }

        [Test]
        public void Defeat_ShowsTheSummary()
        {
            director.ChooseDoor(0);
            KillPlayer();

            Assert.IsTrue(end.IsOpen);
            Assert.IsFalse(end.Won);
            Assert.AreEqual("DEFEAT", end.TitleText);
            StringAssert.Contains($"Rooms cleared  0/{RunState.BossRoom}", end.StatsText);
            StringAssert.Contains($"Average Intensity  {RunUiText.AverageIntensity(director.Run)}", end.StatsText);
            StringAssert.Contains(RunUiText.MapCardName(director.Run.CurrentRoom.Kind), end.PathText);
        }

        [Test]
        public void Victory_ShowsTheSummary_WithAllRoomsCleared()
        {
            for (int room = 1; room <= RunState.BossRoom; room++)
            {
                director.ChooseDoor(0);
                ClearCurrentRoom();
            }
            Assert.AreEqual(RunDirectorState.Victory, director.State);
            Assert.IsTrue(end.IsOpen);
            Assert.IsTrue(end.Won);
            Assert.AreEqual("VICTORY", end.TitleText);
            StringAssert.Contains($"Rooms cleared  {RunState.BossRoom}/{RunState.BossRoom}", end.StatsText);
        }

        [Test]
        public void Back_DoesNotCloseTheSummary()
        {
            director.ChooseDoor(0);
            KillPlayer();
            screens.HandleBack();
            Assert.IsTrue(end.IsOpen);
        }

        [UnityTest]
        public IEnumerator Restart_StartsAFreshRun_AtTheFirstDoor()
        {
            director.ChooseDoor(0);
            KillPlayer();

            screens.HandleConfirm();

            Assert.IsFalse(end.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.AreEqual(1, director.Run.RoomNumber);
            Assert.IsFalse(director.Player.GetComponent<CombatantStatus>().IsDead);
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked);
        }
    }
}
