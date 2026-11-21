using System;
using System.Collections;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The M3 exit door and the player input lock (docs/specs/m3-ui.md, sub-step 1).</summary>
    public class ExitDoorTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private int entered;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior savedBackground;
        private InputSettings.EditorInputBehaviorInPlayMode savedEditorInput;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
            entered = 0;
            director.ExitDoorEntered += () => entered++;
        }

        [UnityTearDown]
        public IEnumerator UnloadRun()
        {
            Time.timeScale = 1f;
            if (keyboard != null)
            {
                InputSystem.RemoveDevice(keyboard);
                keyboard = null;
                InputSystem.settings.backgroundBehavior = savedBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorInput;
            }
            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator Realtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private Vector3 DoorPosition => director.Door.transform.position;
        private Vector3 SpawnPosition => DoorPosition + new Vector3(0f, 0f, -10f);

        [Test]
        public void Door_IsOpenOnlyWhileChoosingDoor()
        {
            Assert.IsNotNull(director.Door);
            Assert.IsTrue(director.Door.IsOpen, "open at run start");

            director.ChooseDoor(0);
            Assert.IsFalse(director.Door.IsOpen, "closed while in a room");

            if (director.State == RunDirectorState.Fighting)
            {
                director.DebugDefeatAllEnemies();
                Assert.AreEqual(RunDirectorState.Reward, director.State);
                Assert.IsFalse(director.Door.IsOpen, "closed during the reward");
                director.FinishReward();
            }
            else director.LeaveUtility();

            Assert.IsTrue(director.Door.IsOpen, "open again at the next door");
        }

        [UnityTest]
        public IEnumerator WalkingIntoDoor_RaisesEnteredOncePerEntry()
        {
            RunTestHelpers.Place(director.Player, DoorPosition);
            yield return Frames(2);
            Assert.AreEqual(1, entered);

            yield return Frames(3); // standing inside doesn't repeat it
            Assert.AreEqual(1, entered);

            RunTestHelpers.Place(director.Player, SpawnPosition);
            yield return Frames(2);
            RunTestHelpers.Place(director.Player, DoorPosition);
            yield return Frames(2);
            Assert.AreEqual(2, entered, "stepping out and back in fires again");
        }

        [UnityTest]
        public IEnumerator ClosedDoor_DoesNotFire()
        {
            director.ChooseDoor(0);
            RunTestHelpers.Place(director.Player, DoorPosition);
            yield return Frames(2);
            Assert.AreEqual(0, entered);
        }

        [UnityTest]
        public IEnumerator DoorOpening_WithPlayerInside_DoesNotFireUntilReEntered()
        {
            director.ChooseDoor(0);
            RunTestHelpers.Place(director.Player, DoorPosition);
            yield return null;
            // Leave the room while standing in the doorway: the door reopens around the player.
            if (director.State == RunDirectorState.Fighting)
            {
                director.DebugDefeatAllEnemies();
                director.FinishReward();
            }
            else director.LeaveUtility();
            Assert.IsTrue(director.Door.IsOpen);
            yield return Frames(2);
            Assert.AreEqual(0, entered, "already inside when it opened: no entry");
        }

        [UnityTest]
        public IEnumerator InputBlocked_StopsKeyboardMovement()
        {
            // Batchmode has no focused window, and unfocused input is dropped by default: let the fake keyboard through.
            savedBackground = InputSystem.settings.backgroundBehavior;
            savedEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return Frames(2);

            var player = director.Player.transform;
            var start = player.position;
            yield return Realtime(0.3f); // real time, not frames: batchmode frames are very short
            Assert.Greater(Flat(player.position - start).magnitude, 0.05f, "control: W moves an unblocked player");

            // Block the way the game does: through the lock's only owner, by opening a screen.
            var screens = Object.FindAnyObjectByType<RunScreens>();
            Assert.IsNotNull(screens, "RunScreens missing from M3_Run.");
            screens.Show(TestRunScreen.AddTo(screens));
            Assert.IsTrue(director.Player.InputBlocked);
            yield return null; // the motor may finish the frame it already had input for
            start = player.position;
            yield return Realtime(0.3f);
            Assert.Less(Flat(player.position - start).magnitude, 0.001f, "a blocked player stays put");

            screens.Hide();
        }

        [UnityTest]
        public IEnumerator DebugInputBlocked_StopsKeyboardMovement_WithoutTouchingTheScreensLock()
        {
            savedBackground = InputSystem.settings.backgroundBehavior;
            savedEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return Frames(2);

            director.Player.DebugInputBlocked = true;
            yield return null;
            var player = director.Player.transform;
            var start = player.position;
            yield return Realtime(0.3f);
            Assert.Less(Flat(player.position - start).magnitude, 0.001f, "the debug menu's block holds the player");
            Assert.IsFalse(director.Player.InputBlocked, "RunScreens' lock is left alone");

            director.Player.DebugInputBlocked = false;
            start = player.position;
            yield return Realtime(0.3f);
            Assert.Greater(Flat(player.position - start).magnitude, 0.05f, "released: W moves again");
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
