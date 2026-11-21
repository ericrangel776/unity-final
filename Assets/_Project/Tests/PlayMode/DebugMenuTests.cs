using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>The F1 debug menu on the run scene (docs/specs/debug-menu.md).</summary>
    public class DebugMenuTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private DebugMenu menu;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;
            director = Object.FindAnyObjectByType<RunDirector>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
            menu = DebugMenu.Instance;
            Assert.IsNotNull(menu, "The debug menu should create itself on play.");
            menu.FindTargets();
            director.RestartRun(5);
        }

        [UnityTearDown]
        public IEnumerator UnloadRun()
        {
            ResetDebugState();
            if (menu != null)
            {
                menu.Close();
                menu.SetPinned(false);
                menu.SelectTab(DebugTab.Run);
            }
            Time.timeScale = 1f;
            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private void ResetDebugState()
        {
            HitStop.Disabled = false;
            HitStop.SetBaseTimeScale(1f);
            TrainingDummyEnemy.DebugFreezeAI = false;
            CombatActionController.DebugShowHitboxes = null;
            if (menu == null) return;
            menu.Juice.Shake = menu.Juice.Sparks = menu.Juice.Numbers = menu.Juice.Sound = true;
            menu.JuiceChanged();
            menu.Refresh();
            if (menu.PlayerStatus != null) menu.PlayerStatus.GodMode = false;
        }

        private DebugCommand Cmd(string prefix) => menu.Commands.First(c => c.Label().StartsWith(prefix));

        [Test]
        public void Bootstrap_StartsClosed()
        {
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(menu.PanelVisible);
            Assert.IsFalse(director.Player.DebugInputBlocked);
        }

        [UnityTest]
        public IEnumerator Open_BlocksInput_PinFreesIt_CloseReleases()
        {
            menu.Open();
            yield return null;
            Assert.IsTrue(menu.PanelVisible);
            Assert.IsTrue(director.Player.DebugInputBlocked);
            Assert.IsFalse(director.Player.InputBlocked, "RunScreens' lock is untouched");

            menu.SetPinned(true);
            yield return null;
            Assert.IsTrue(menu.PanelVisible, "pinned stays up");
            Assert.IsFalse(director.Player.DebugInputBlocked, "pinned frees the input");

            menu.SetPinned(false);
            menu.Toggle();
            yield return null;
            Assert.IsFalse(menu.PanelVisible);
            Assert.IsFalse(director.Player.DebugInputBlocked);
        }

        [Test]
        public void EachTab_DrawsItsCommands()
        {
            menu.Open();
            foreach (DebugTab tab in Enum.GetValues(typeof(DebugTab)))
            {
                menu.SelectTab(tab);
                Assert.AreEqual(menu.Commands.Count(c => c.Tab == tab), menu.RowCount, tab.ToString());
            }
            Assert.Greater(menu.Commands.Count, 40, "every group from the spec is there");
        }

        [Test]
        public void EnterNow_UsesThePicker()
        {
            while (menu.Picker.Kind != MapCardKind.Hexfire) menu.Picker.StepKind(1);
            while (menu.Picker.Number < 7) menu.Picker.StepNumber(1);
            while (menu.Picker.Number > 7) menu.Picker.StepNumber(-1);

            Assert.IsTrue(menu.RunCommand(Cmd("> Enter now")));

            Assert.AreEqual(new MapCard(MapCardKind.Hexfire, 7), director.Run.CurrentRoom);
            Assert.AreEqual(RunDirectorState.Fighting, director.State);
        }

        [Test]
        public void Pause_TogglesTheBaseScale_AndKeepsTheChosenSlowMo()
        {
            Assert.IsTrue(menu.RunCommand(Cmd("Time scale 0.25x")));
            Assert.AreEqual(0.25f, Time.timeScale);
            Assert.IsTrue(menu.RunCommand(Cmd("Pause")));
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(menu.RunCommand(Cmd("Resume")));
            Assert.AreEqual(0.25f, Time.timeScale, "resumes at the chosen slow-mo");
        }

        [Test]
        public void JuiceSwitches_TurnComponentsOffAndBackOn()
        {
            var numbers = Object.FindAnyObjectByType<DamageNumberView>();
            Assert.IsNotNull(numbers, "DamageNumberView missing from M3_Run.");

            menu.RunCommand(Cmd("Damage numbers"));
            Assert.IsFalse(numbers.enabled);
            menu.RunCommand(Cmd("Damage numbers"));
            Assert.IsTrue(numbers.enabled);
        }

        [UnityTest]
        public IEnumerator Info_TimesTheRoom()
        {
            menu.Open();
            menu.SelectTab(DebugTab.Info);
            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 2));
            float wait = Time.realtimeSinceStartup + 0.2f;
            while (Time.realtimeSinceStartup < wait) yield return null;
            director.DebugDefeatAllEnemies();
            menu.Refresh();

            Assert.AreEqual(1, menu.Timer.History.Count);
            Assert.AreEqual("Vanguard 2", menu.Timer.History[0].room);
            Assert.GreaterOrEqual(menu.Timer.History[0].seconds, 0.15f);
            StringAssert.Contains("Last rooms: Vanguard 2", menu.InfoText);
            StringAssert.Contains("Seed 5", menu.InfoText);
        }

        // ---- smoke: every available command runs cleanly in each kind of moment ----

        private static IEnumerable<(string name, Action<RunDirector> setup)> Contexts()
        {
            yield return ("door", d => d.RestartRun(5));
            yield return ("mob fight with wave", d => { d.RestartRun(5); d.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 5)); });
            yield return ("shop", d => { d.RestartRun(5); d.DebugEnterRoom(new MapCard(MapCardKind.Shop, 5)); });
            yield return ("elite", d => { d.RestartRun(5); d.DebugEnterRoom(new MapCard(MapCardKind.EliteTrial, 4, EliteTrial.Counterpuncher)); });
            yield return ("boss", d => { d.RestartRun(5); d.DebugSkipToBoss(); });
        }

        [UnityTest]
        public IEnumerator EveryAvailableCommand_RunsWithoutErrors()
        {
            menu.Open();
            int ran = 0;
            foreach (var (name, setup) in Contexts())
            {
                for (int i = 0; i < menu.Commands.Count; i++)
                {
                    setup(director);
                    menu.FindTargets();
                    var command = menu.Commands[i];
                    if (command.Step != null)
                    {
                        Assert.IsTrue(menu.StepCommand(command, 1), $"{name}: step {command.Label()} -> {menu.LastError}");
                        Assert.IsTrue(menu.StepCommand(command, -1), $"{name}: step {command.Label()} -> {menu.LastError}");
                    }
                    if (command.Run != null && command.Available())
                    {
                        string label = command.Label();
                        Assert.IsTrue(menu.RunCommand(command), $"{name}: {label} -> {menu.LastError}");
                        ran++;
                    }
                    ResetDebugState();
                    yield return null; // let deferred Destroy calls land between setups
                }
            }
            Assert.Greater(ran, 100, "most commands were available somewhere");
        }
    }
}
