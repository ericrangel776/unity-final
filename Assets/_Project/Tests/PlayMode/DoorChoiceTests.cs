using System;
using System.Collections;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The Map Card door choice screen (docs/specs/m3-ui.md, sub-step 3).</summary>
    public class DoorChoiceTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private RunScreens screens;
        private DoorChoiceView doors;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            screens = Object.FindAnyObjectByType<RunScreens>();
            doors = Object.FindAnyObjectByType<DoorChoiceView>();
            Assert.IsNotNull(doors, "DoorChoiceView missing from M3_Run (rebuild the scene).");
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

        private IEnumerator WalkIntoDoor()
        {
            RunTestHelpers.Place(director.Player, director.Door.transform.position);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator WalkingIntoTheDoor_OpensTheChoice_AndBlocksInput()
        {
            Assert.IsFalse(doors.IsOpen);
            yield return WalkIntoDoor();
            Assert.IsTrue(doors.IsOpen);
            Assert.IsTrue(director.Player.InputBlocked);
        }

        [UnityTest]
        public IEnumerator Tiles_ShowEveryOfferedCard()
        {
            yield return WalkIntoDoor();
            var offer = director.Run.CurrentOffer;
            Assert.AreEqual(offer.Count, doors.VisibleCount);
            for (int i = 0; i < offer.Count; i++)
            {
                var tile = doors.Tile(i);
                Assert.AreEqual(RunUiText.MapCardName(offer[i].Kind), tile.Title.text);
                Assert.AreEqual(offer[i].Number.ToString(), tile.Badge.text);
                Assert.AreEqual(RunUiText.NumberLabel(offer[i]), tile.BadgeLabel.text);
                Assert.AreEqual(RunUiText.TierLine(offer[i]), tile.Footer.text);
                Assert.AreEqual($"[{i + 1}]", tile.Hotkey.text);
            }
        }

        [UnityTest]
        public IEnumerator Select_EntersThatRoom_AndCloses()
        {
            yield return WalkIntoDoor();
            var chosen = director.Run.CurrentOffer[1];
            screens.HandleKey(1); // key `2`
            Assert.AreEqual(chosen, director.Run.CurrentRoom);
            Assert.AreNotEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.IsFalse(doors.IsOpen);
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked, "the player can fight once the frame ends");
        }

        [UnityTest]
        public IEnumerator ClickingATile_EntersThatRoom()
        {
            yield return WalkIntoDoor();
            var chosen = director.Run.CurrentOffer[0];
            doors.Tile(0).Button.onClick.Invoke();
            Assert.AreEqual(chosen, director.Run.CurrentRoom);
        }

        [UnityTest]
        public IEnumerator Back_ClosesWithoutChoosing()
        {
            yield return WalkIntoDoor();
            screens.HandleBack();
            Assert.IsFalse(doors.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked);
        }

        [UnityTest]
        public IEnumerator Select_OutOfRange_IsIgnored()
        {
            yield return WalkIntoDoor();
            screens.HandleKey(3); // key `4`: no fourth card
            Assert.IsTrue(doors.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
        }

        [UnityTest]
        public IEnumerator StandingInTheDoorAfterBack_DoesNotReopen_UntilReEntered()
        {
            yield return WalkIntoDoor();
            screens.HandleBack();
            yield return null;
            yield return null;
            Assert.IsFalse(doors.IsOpen, "still inside: no reopen");

            RunTestHelpers.Place(director.Player, director.Door.transform.position + new Vector3(0f, 0f, -6f));
            yield return null;
            yield return WalkIntoDoor();
            Assert.IsTrue(doors.IsOpen, "stepping back in reopens it");
        }

        [Test]
        public void BossDoor_ShowsOneCentredCard()
        {
            for (int room = 1; room < Core.RunState.BossRoom; room++)
            {
                director.ChooseDoor(0);
                if (director.State == RunDirectorState.Fighting)
                {
                    director.DebugDefeatAllEnemies();
                    director.FinishReward();
                }
                else director.LeaveUtility();
            }
            Assert.IsTrue(director.Run.IsBossRoom);

            screens.Show(doors);
            Assert.AreEqual(1, doors.VisibleCount);
            Assert.AreEqual(0f, doors.Tile(0).Rect.anchoredPosition.x, 0.01f, "a lone card is centred");
            Assert.AreEqual("Final fight", doors.Tile(0).Footer.text);
        }
    }
}
