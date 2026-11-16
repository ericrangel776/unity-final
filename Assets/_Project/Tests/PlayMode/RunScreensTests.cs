using System;
using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The run-screen shell and HUD strip (docs/specs/m3-ui.md, sub-step 2).</summary>
    public class RunScreensTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private RunScreens screens;
        private RunHudStripView strip;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            screens = Object.FindAnyObjectByType<RunScreens>();
            strip = Object.FindAnyObjectByType<RunHudStripView>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
            Assert.IsNotNull(screens, "RunScreens missing from M3_Run (rebuild the scene).");
            Assert.IsNotNull(strip, "RunHudStripView missing from M3_Run.");
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

        private TestRunScreen AddFake() => TestRunScreen.AddTo(screens);

        // ---- shell ----

        [Test]
        public void Scene_HasAnEventSystem_ForMouseClicks()
        {
            Assert.IsNotNull(Object.FindAnyObjectByType<EventSystem>());
        }

        [Test]
        public void Screens_SortAboveEveryCombatHudCanvas()
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (canvas == screens.Canvas || canvas.GetComponentInParent<RunHudStripView>() != null) continue;
                Assert.Less(canvas.sortingOrder, screens.Canvas.sortingOrder, $"{canvas.name} would draw over the run screens");
            }
            Assert.Greater(RunHudStripView.SortingOrder, screens.Canvas.sortingOrder, "the strip stays readable over the dim");
        }

        [Test]
        public void NoScreenOpen_AtRunStart_InputFree()
        {
            Assert.IsNull(screens.Current);
            Assert.IsFalse(director.Player.InputBlocked);
        }

        [Test]
        public void Show_BlocksInputImmediately_AndRefreshesTheScreen()
        {
            var fake = AddFake();
            screens.Show(fake);
            Assert.AreSame(fake, screens.Current);
            Assert.IsTrue(fake.IsOpen);
            Assert.IsTrue(director.Player.InputBlocked);
            Assert.AreEqual(1, fake.Refreshes);
        }

        [UnityTest]
        public IEnumerator Hide_ReleasesInputOnlyAtTheEndOfTheFrame()
        {
            var fake = AddFake();
            screens.Show(fake);
            screens.HandleKey(1); // closes it, as a number-key choice would
            Assert.IsNull(screens.Current);
            Assert.IsTrue(director.Player.InputBlocked, "same frame: the key must not reach the card slots");
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked, "released after LateUpdate");
        }

        [Test]
        public void Keys_AreRoutedToTheOpenScreen()
        {
            var fake = AddFake();
            screens.Show(fake);
            screens.HandleBack();
            screens.HandleConfirm();
            screens.HandleKey(2);
            Assert.AreEqual(1, fake.Backs);
            Assert.AreEqual(1, fake.Confirms);
            Assert.AreEqual(2, fake.Selected);
        }

        [Test]
        public void Keys_WithNoScreenOpen_DoNothing()
        {
            var fake = AddFake();
            Assert.DoesNotThrow(() =>
            {
                screens.HandleKey(0);
                screens.HandleBack();
                screens.HandleConfirm();
            });
            Assert.AreEqual(-1, fake.Selected);
            Assert.AreEqual(0, fake.Backs + fake.Confirms);
        }

        [Test]
        public void Show_ReplacesTheOpenScreen()
        {
            var first = AddFake();
            var second = AddFake();
            screens.Show(first);
            screens.Show(second);
            Assert.AreSame(second, screens.Current);
            Assert.IsFalse(first.IsOpen);
            Assert.IsTrue(director.Player.InputBlocked);
        }

        // ---- HUD strip ----

        [UnityTest]
        public IEnumerator Strip_ShowsTheRunAtStart()
        {
            yield return FreezeAndRedraw();
            Assert.IsTrue(strip.IsVisible);
            Assert.AreEqual("Room 1/6", strip.RoomText);
            Assert.AreEqual($"Currency {director.Run.Currency}", strip.CurrencyText);
            Assert.AreEqual("Momentum ○○○", strip.MomentumText);
            Assert.AreEqual("No relics", strip.RelicsText);
        }

        [UnityTest]
        public IEnumerator Strip_FollowsCurrencyMomentumAndRelics()
        {
            director.Run.AddCurrency(25);
            director.Run.Momentum.AddStack();
            director.Run.AddRelic(RelicId.CinderLantern);
            yield return FreezeAndRedraw();
            Assert.AreEqual($"Currency {director.Run.Currency}", strip.CurrencyText);
            StringAssert.Contains("25", strip.CurrencyText);
            Assert.AreEqual("Momentum ●○○", strip.MomentumText);
            Assert.AreEqual("Lantern", strip.RelicsText);
        }

        [UnityTest]
        public IEnumerator Strip_FollowsTheRoomNumber()
        {
            director.ChooseDoor(0);
            if (director.State == RunDirectorState.Fighting)
            {
                director.DebugDefeatAllEnemies();
                director.FinishReward();
            }
            else director.LeaveUtility();
            yield return FreezeAndRedraw();
            Assert.AreEqual("Room 2/6", strip.RoomText);
        }

        /// <summary>The strip redraws in LateUpdate: freeze time (frames still run) and yield twice (see the PlayMode rules).</summary>
        private static IEnumerator FreezeAndRedraw()
        {
            Time.timeScale = 0f;
            yield return null;
            yield return null;
        }
    }
}
