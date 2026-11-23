using System;
using System.Collections;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Base fixture for tests that drive the M3 run scene: loads <c>M3_Run</c> before each test (after its Start ran)
    /// and finds the <see cref="RunDirector"/>. Afterwards it unloads the scene into a fresh empty one, so live enemies
    /// can't affect later fixtures. Derived fixtures grab their extra objects in <see cref="OnRunLoaded"/>.
    /// </summary>
    public abstract class RunSceneTest
    {
        protected const string SceneName = "M3_Run";
        protected RunDirector director;

        [UnitySetUp]
        public IEnumerator LoadRunScene()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
            OnRunLoaded();
        }

        /// <summary>Runs right after the scene loaded and <see cref="director"/> was found.</summary>
        protected virtual void OnRunLoaded() { }

        [UnityTearDown]
        public IEnumerator UnloadRunScene()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
