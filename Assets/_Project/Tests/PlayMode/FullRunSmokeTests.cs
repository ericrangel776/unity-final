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
    /// <summary>
    /// A whole run driven the way a player drives it (docs/specs/m3-smoke-test.md): walk into the exit door, then
    /// only the screens' key routing (`1`-`9`, Esc, Enter). Combat rooms are cleared with the debug kill.
    /// </summary>
    public class FullRunSmokeTests
    {
        private const string SceneName = "M3_Run";
        /// <summary>A seed whose run visits both an Elite and a Blue room under <see cref="PickDoor"/>.</summary>
        private const int VictorySeed = 3;
        private const int MaxFrames = 600;

        private RunDirector director;
        private RunScreens screens;
        private CombatantStatus playerStatus;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            screens = Object.FindAnyObjectByType<RunScreens>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
            Assert.IsNotNull(screens, "RunScreens missing from M3_Run (rebuild the scene).");
            playerStatus = director.Player.GetComponent<CombatantStatus>();
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

        // ---- tests ----

        [UnityTest]
        public IEnumerator FullRun_ThroughTheScreens_ReachesVictory_ThenRestarts()
        {
            director.RestartRun(VictorySeed);
            yield return Settle();
            bool tookRelic = false, visitedBlue = false;

            for (int room = 1; room <= RunState.BossRoom; room++)
            {
                Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State, $"Room {room}: not at a door.");
                Assert.AreEqual(room, director.Run.RoomNumber);
                if (room == RunState.BossRoom)
                    Assert.AreEqual(1, director.Run.CurrentOffer.Count, "The boss door offers a single card.");

                yield return WalkIntoDoor<DoorChoiceView>();
                int door = PickDoor(director.Run.CurrentOffer, visitedBlue);
                screens.HandleKey(door);
                yield return Settle();
                Assert.IsFalse(screens.Current is DoorChoiceView, $"Room {room}: the door choice should close on entering.");

                var current = director.Run.CurrentRoom;
                if (current.IsCombat)
                {
                    AssertNoScreen($"Room {room}: a fight starts with no screen open.");
                    Assert.AreEqual(RunDirectorState.Fighting, director.State);
                    CollectionAssert.IsNotEmpty(director.Enemies, $"Room {room}: no enemies spawned.");
                    Assert.AreEqual(director.Run.Hp, playerStatus.Hp, "HP carries into the room.");

                    director.DebugDefeatAllEnemies();
                    yield return null;
                    if (current.Kind == MapCardKind.Boss) break;
                    tookRelic |= TakeReward(room);
                }
                else
                {
                    visitedBlue = true;
                    UseBlueRoom(room, current.Kind);
                }

                yield return Settle();
                AssertNoScreen($"Room {room}: the screen should close when leaving.");
            }

            Assert.IsTrue(tookRelic, $"Seed {VictorySeed} never reached an Elite relic; pick another seed.");
            Assert.IsTrue(visitedBlue, $"Seed {VictorySeed} never offered a Blue room; pick another seed.");

            // Victory summary.
            Assert.AreEqual(RunDirectorState.Victory, director.State);
            var end = AssertOpen<RunEndView>("Victory");
            Assert.AreEqual("VICTORY", end.TitleText);
            StringAssert.Contains($"Rooms cleared  {RunState.BossRoom}/{RunState.BossRoom}", end.StatsText);
            screens.HandleBack();
            Assert.IsTrue(end.IsOpen, "Esc must not close the run summary.");

            // Enter restarts at a fresh first door.
            screens.HandleConfirm();
            yield return Settle();
            AssertFreshRun();
        }

        [UnityTest]
        public IEnumerator Death_MidRun_ShowsDefeat_ThenRestarts()
        {
            director.RestartRun(VictorySeed);
            yield return Settle();
            yield return WalkIntoDoor<DoorChoiceView>();
            screens.HandleKey(0); // room 1 is always a fight
            yield return Settle();
            Assert.AreEqual(RunDirectorState.Fighting, director.State);

            var lethal = new AttackPayload(12345, Team.Enemy, 999, false, true, Time.frameCount, CardShape.Strike, Element.Physical);
            playerStatus.TakeHit(lethal);
            yield return null;

            Assert.AreEqual(RunDirectorState.Defeat, director.State);
            var end = AssertOpen<RunEndView>("Defeat");
            Assert.AreEqual("DEFEAT", end.TitleText);

            screens.HandleConfirm();
            yield return Settle();
            AssertFreshRun();
        }

        // ---- run steps ----

        /// <summary>An Elite if offered, else a Blue room (until one is visited), else the first card.</summary>
        private static int PickDoor(System.Collections.Generic.IReadOnlyList<MapCard> offer, bool visitedBlue)
        {
            for (int i = 0; i < offer.Count; i++)
                if (offer[i].Kind == MapCardKind.EliteTrial) return i;
            if (!visitedBlue)
                for (int i = 0; i < offer.Count; i++)
                    if (offer[i].Family == MapFamily.Blue) return i;
            return 0;
        }

        /// <summary>Works the reward screen: relic first (forced), then one card, then Continue. True if a relic was taken.</summary>
        private bool TakeReward(int room)
        {
            Assert.AreEqual(RunDirectorState.Reward, director.State, $"Room {room}: clearing should open the reward.");
            var reward = AssertOpen<RewardView>($"Room {room} reward");
            var data = director.CurrentReward;
            bool relic = false;

            if (reward.CurrentPhase == RewardView.Phase.Relic)
            {
                screens.HandleConfirm();
                Assert.IsTrue(reward.IsOpen, "Continue must wait for the relic pick.");
                int relicsBefore = director.Run.Relics.Count;
                screens.HandleKey(0);
                Assert.AreEqual(relicsBefore + 1, director.Run.Relics.Count, "`1` should take the first relic.");
                relic = true;
            }

            Assert.AreNotEqual(RewardView.Phase.Relic, reward.CurrentPhase);
            int deckBefore = director.Run.Deck.Count;
            if (data.Options.Count > 0)
            {
                screens.HandleKey(0);
                Assert.AreEqual(deckBefore + 1, director.Run.Deck.Count, "`1` should draft the first card.");
            }
            screens.HandleBack(); // close the draft (no-op once the picks are used up)
            screens.HandleConfirm();
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State, $"Room {room}: Enter should continue.");
            return relic;
        }

        private void UseBlueRoom(int room, MapCardKind kind)
        {
            Assert.AreEqual(RunDirectorState.Utility, director.State);
            CollectionAssert.IsEmpty(director.Enemies, "A Blue room has no enemies.");
            switch (kind)
            {
                case MapCardKind.Rest:
                    AssertOpen<RestView>($"Room {room} rest");
                    int hpBefore = director.Run.Hp;
                    screens.HandleKey(0); // heal
                    Assert.GreaterOrEqual(director.Run.Hp, hpBefore);
                    Assert.IsTrue(director.CurrentRest.IsDone, "`1` should take the heal.");
                    break;
                case MapCardKind.Shop:
                    AssertOpen<ShopView>($"Room {room} shop");
                    screens.HandleKey(0); // buy if affordable; either way the screen stays up
                    break;
                case MapCardKind.Forge:
                    var forge = AssertOpen<ForgeView>($"Room {room} forge");
                    screens.HandleKey(0); // upgrade: opens the card list
                    Assert.IsTrue(forge.InCardList);
                    int pick = -1;
                    for (int i = 0; i < forge.Deck.Count && pick < 0; i++) if (forge.Deck.IsEnabled(i)) pick = i;
                    Assert.GreaterOrEqual(pick, 0, "No upgradable card in the deck list.");
                    var card = director.Run.Deck[pick];
                    int before = card.PermanentRunUpgradeMod;
                    forge.Deck.Pick(pick);  // select (a click)
                    screens.HandleConfirm(); // apply
                    Assert.Greater(card.PermanentRunUpgradeMod, before, "Select + Enter should upgrade the card.");
                    Assert.IsFalse(forge.InCardList);
                    break;
                default:
                    Assert.Fail($"Unexpected Blue room {kind}.");
                    break;
            }
            var view = (UtilityView)screens.Current;
            screens.HandleBack();
            Assert.AreEqual(RunDirectorState.Utility, director.State, $"Room {room}: Esc must never leave a room.");
            screens.HandleConfirm();
            if (director.State == RunDirectorState.Utility)
            {
                // Only an unused room (a shop we couldn't afford) asks before leaving.
                Assert.AreEqual(MapCardKind.Shop, kind, $"Room {room}: a used room should leave on the first Enter.");
                Assert.IsTrue(view.LeaveWarned);
                screens.HandleConfirm();
            }
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State, $"Room {room}: Enter should leave.");
        }

        // ---- helpers ----

        /// <summary>Puts the player on the exit door and waits until <typeparamref name="T"/> opens.</summary>
        private IEnumerator WalkIntoDoor<T>() where T : RunScreen
        {
            AssertNoScreen("A screen was already open before walking to the door.");
            RunTestHelpers.Place(director.Player, director.Door.transform.position);
            for (int f = 0; f < MaxFrames && !(screens.Current is T); f++) yield return null;
            AssertOpen<T>("Door");
        }

        private T AssertOpen<T>(string context) where T : RunScreen
        {
            Assert.IsInstanceOf<T>(screens.Current, $"{context}: expected {typeof(T).Name} open, got " +
                                                    (screens.Current != null ? screens.Current.GetType().Name : "none") + ".");
            Assert.IsTrue(director.Player.InputBlocked, $"{context}: an open screen must block player input.");
            return (T)screens.Current;
        }

        private void AssertNoScreen(string message)
        {
            Assert.IsNull(screens.Current, message + " Open: " + (screens.Current != null ? screens.Current.GetType().Name : ""));
            Assert.IsFalse(director.Player.InputBlocked, "Player input should be free with no screen open.");
        }

        /// <summary>Two frames: the screen lock is released in LateUpdate, and the door needs a frame to see the player leave.</summary>
        private static IEnumerator Settle()
        {
            yield return null;
            yield return null;
        }

        private void AssertFreshRun()
        {
            AssertNoScreen("The run summary should close on restart.");
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.AreEqual(1, director.Run.RoomNumber);
            Assert.AreEqual(director.Content.StarterDeck.Count, director.Run.Deck.Count);
            Assert.AreEqual(0, director.Run.Currency);
            Assert.AreEqual(0, director.Run.Relics.Count);
            Assert.AreEqual(director.Run.MaxHp, director.Run.Hp);
            Assert.AreEqual(playerStatus.MaxHp, playerStatus.Hp);
            Assert.IsFalse(playerStatus.IsDead);
            CollectionAssert.IsEmpty(director.Enemies);
        }
    }
}
