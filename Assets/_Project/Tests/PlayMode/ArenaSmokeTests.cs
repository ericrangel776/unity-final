using System.Collections;
using DeckArpg.Gameplay;
using NUnit.Framework;
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
