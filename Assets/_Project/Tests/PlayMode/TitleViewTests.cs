using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The early-build title screen and the run summary's Quit button.</summary>
    public class TitleViewTests : RunSceneTest
    {
        private RunScreens screens;
        private TitleView title;
        private int quits;

        protected override void OnRunLoaded()
        {
            screens = Object.FindAnyObjectByType<RunScreens>();
            Assert.IsNull(Object.FindAnyObjectByType<TitleView>(), "The title must not open itself in batchmode.");
            quits = 0;
            GameQuit.Override = () => quits++;
            title = screens.gameObject.AddComponent<TitleView>();
            screens.Show(title);
        }

        [TearDown]
        public void ClearQuitOverride() => GameQuit.Override = null;

        [Test]
        public void Title_BlocksInput_OverTheFirstDoor()
        {
            Assert.IsTrue(title.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.IsTrue(director.Player.InputBlocked);
            Assert.IsTrue(screens.CombatHudHidden);
        }

        [UnityTest]
        public IEnumerator Start_ClosesTheTitle_AndReleasesInput()
        {
            screens.HandleKey(0);
            Assert.IsFalse(title.IsOpen);
            Assert.IsTrue(director.Player.InputBlocked, "Released only in LateUpdate, so the `1` isn't read as a card.");
            yield return null;
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
        }

        [Test]
        public void Enter_Starts()
        {
            screens.HandleConfirm();
            Assert.IsFalse(title.IsOpen);
            Assert.AreEqual(0, quits);
        }

        [Test]
        public void Controls_TogglesThePanel_WithoutClosing()
        {
            Assert.IsFalse(title.ControlsShown);
            screens.HandleKey(1);
            Assert.IsTrue(title.ControlsShown);
            Assert.IsTrue(title.IsOpen);
            screens.HandleKey(1);
            Assert.IsFalse(title.ControlsShown);
        }

        [Test]
        public void Quit_KeyAndEsc_Quit()
        {
            screens.HandleKey(2);
            Assert.AreEqual(1, quits);
            screens.HandleBack();
            Assert.AreEqual(2, quits);
        }

        [Test]
        public void Title_ClosesWhenTheRunLeavesTheDoorChoice()
        {
            director.ChooseDoor(0);
            Assert.IsFalse(title.IsOpen);
        }

        [Test]
        public void RunEnd_QuitButton_Quits_AndKeysDoNot()
        {
            screens.Hide();
            director.ChooseDoor(0);
            var lethal = new AttackPayload(12345, Team.Enemy, 999, false, true, Time.frameCount, CardShape.Strike, Element.Physical);
            director.Player.GetComponent<CombatantStatus>().TakeHit(lethal);
            var end = Object.FindAnyObjectByType<RunEndView>();
            Assert.IsTrue(end.IsOpen);

            for (int i = 0; i < 3; i++) screens.HandleKey(i); // card keys mashed at death
            Assert.AreEqual(0, quits);
            Assert.IsTrue(end.IsOpen);

            Button quit = null;
            foreach (var button in screens.Canvas.GetComponentsInChildren<Button>())
                if (button.name == "Quit" && button.transform.parent.name == nameof(RunEndView)) quit = button;
            Assert.IsNotNull(quit, "RunEndView has no Quit button.");
            quit.onClick.Invoke();
            Assert.AreEqual(1, quits);
        }
    }
}
