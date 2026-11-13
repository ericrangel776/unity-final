using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Loads the generated M1 arena and lets it run. Any error logged during play fails the test,
    /// so this catches broken scene wiring that unit tests cannot see.
    /// </summary>
    public class ArenaSmokeTests
    {
        private const string SceneName = "M1_Arena";

        /// <summary>
        /// Unload the arena after every test. Left loaded, its live dummy, player and HitStop keep
        /// reacting to hitboxes and clashes spawned by later fixtures (e.g. freezing time mid-test),
        /// which made unrelated physics tests flaky.
        /// </summary>
        [UnityTearDown]
        public IEnumerator UnloadArena()
        {
            Time.timeScale = 1f;
            var arena = SceneManager.GetSceneByName(SceneName);
            if (!arena.IsValid() || !arena.isLoaded) yield break;

            // A scene can't be unloaded while it's the only one loaded, so switch to an empty one first.
            var empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(arena);
            Assert.AreEqual(1f, Time.timeScale, "Unloading mid-freeze must restore the time scale.");
        }

        [UnityTest]
        public IEnumerator Arena_LoadsAndRuns_DummyTelegraphsAndSwings()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);

            var player = Object.FindAnyObjectByType<PlayerCombatInput>();
            var dummy = Object.FindAnyObjectByType<TrainingDummyEnemy>();
            Assert.IsNotNull(player, "Player missing from arena.");
            Assert.IsNotNull(dummy, "Dummy missing from arena.");
            Assert.IsNotNull(Camera.main, "Main camera missing.");

            bool telegraphed = false;
            dummy.TelegraphStarted += _ => telegraphed = true;

            float end = Time.time + 8f;
            while (!telegraphed && Time.time < end) yield return null;
            Assert.IsTrue(telegraphed,
                $"Dummy never approached and telegraphed a card. dummy={dummy.transform.position} " +
                $"player={player.transform.position} target={(dummy.Target != null ? dummy.Target.name : "null")} " +
                $"stunned={dummy.Status.IsStunned} hand0={dummy.Deck.Cycle.Hand[0]} t={Time.time:0.0}");
        }

        [UnityTest]
        public IEnumerator Arena_HitDuringDummyTelegraph_CancelsIt()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);

            var dummy = Object.FindAnyObjectByType<TrainingDummyEnemy>();
            Assert.IsNotNull(dummy.GetComponent<HitFlash>(), "Dummy should flash on hits.");
            Assert.IsNotNull(Object.FindAnyObjectByType<PlayerMotor>().GetComponent<HitFlash>(), "Player should flash on hits.");

            float end = Time.time + 8f;
            while (dummy.CurrentTelegraph == null && Time.time < end) yield return null;
            Assert.IsNotNull(dummy.CurrentTelegraph, "Dummy never telegraphed.");

            bool ended = false;
            dummy.TelegraphEnded += () => ended = true;
            var hit = new AttackPayload(1, Team.Player, 3, false, false, 0, CardShape.Strike, Element.Physical);
            Assert.IsTrue(dummy.Status.TakeHit(hit, dummy.transform.forward * -1f));

            Assert.IsTrue(ended, "A flinching hit should interrupt the wind-up.");
            Assert.IsNull(dummy.CurrentTelegraph);
        }

        [UnityTest]
        public IEnumerator Arena_HasClashJuiceWired()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;

            Assert.IsNotNull(Object.FindAnyObjectByType<HitStop>(), "HitStop missing from arena.");
            Assert.IsNotNull(Object.FindAnyObjectByType<ClashImpactFx>(), "ClashImpactFx missing from arena.");
            Assert.IsNotNull(Object.FindAnyObjectByType<CinemachineImpulseListener>(), "Camera can't receive clash shake.");
            Assert.IsNotNull(Object.FindAnyObjectByType<CinemachineCamera>().GetComponent<CameraPunch>(),
                "Camera should punch in on breaks.");
            Assert.IsNotNull(Object.FindAnyObjectByType<DeckArpg.UI.ClashFeedbackView>());
            Assert.IsNotNull(Object.FindAnyObjectByType<DeckArpg.UI.CardShatterView>(), "Broken cards should shatter.");
            Assert.IsNotNull(Object.FindAnyObjectByType<DeadlockController>(), "Ties should go to a Deadlock.");
            Assert.IsNotNull(Object.FindAnyObjectByType<DeckArpg.UI.DeadlockView>(), "Deadlock needs its HUD.");
        }

        [UnityTest]
        public IEnumerator Arena_PlayerCanPlayCardAndPoke()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;

            var player = Object.FindAnyObjectByType<PlayerCombatInput>();
            var combat = player.GetComponent<CombatActionController>();
            var deck = player.GetComponent<DeckRuntimeManager>();
            int drawBefore = deck.Cycle.DrawCount;

            Assert.IsTrue(player.Execute(CombatCommand.PlaySlot1));
            Assert.AreEqual(1, combat.ActiveHitboxCount);
            Assert.AreEqual(1, deck.Cycle.DiscardCount);
            Assert.AreEqual(drawBefore - 1, deck.Cycle.DrawCount);

            Assert.IsTrue(player.Execute(CombatCommand.Poke), "Poke is always available.");
            Assert.IsFalse(player.Execute(CombatCommand.Reload), "Reload is unavailable while the draw queue has cards.");
        }
    }
}
