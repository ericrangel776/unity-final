using System;
using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Keeps the measurement bot's run mode (docs/specs/run-timing.md) working in the regular suite: it must clear a
    /// low-Intensity room on its own. God mode keeps survival out of the assertion; only clearing is tested.
    /// </summary>
    public class MeasurementBotTests
    {
        private const string SceneName = "M3_Run";
        private const float CapSeconds = 90f;

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator Bot_ClearsAForcedLowIntensityRoom()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            director.RestartRun(1);
            yield return null;

            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 1));
            Assert.AreEqual(RunDirectorState.Fighting, director.State);
            director.Player.GetComponent<CombatantStatus>().GodMode = true;
            var bot = director.Player.gameObject.AddComponent<BossFightBot>();
            bot.Init(director.Player, director);

            float start = Time.realtimeSinceStartup;
            while (director.State == RunDirectorState.Fighting && Time.realtimeSinceStartup - start < CapSeconds)
                yield return null;

            Assert.AreEqual(RunDirectorState.Reward, director.State,
                $"The bot didn't clear a Vanguard I1 room in {CapSeconds} s (cards {bot.CardsPlayed}, parries {bot.Parries}).");
            Assert.Greater(bot.CardsPlayed, 0, "The room cleared without the bot playing a card.");
            Object.Destroy(bot);
        }
    }
}
