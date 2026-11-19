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
    /// <summary>Element wiring in the real run scene: resistances per room kind and the player's HUD chips.</summary>
    public class ElementRunTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
        }

        /// <summary>Unload so live enemies can't affect later fixtures (see CLAUDE.md lessons).</summary>
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

        [Test]
        public void MobRoom_EnemiesTakeFullStatusDurations()
        {
            RunTestHelpers.EnterRoom(director, c => c.Family == MapFamily.Red);
            Assert.IsNotEmpty(director.Enemies);
            foreach (var e in director.Enemies)
            {
                Assert.AreEqual(1f, e.GetComponent<ElementStatusEffects>().DurationScale);
                Assert.AreEqual(1f, e.Status.KnockbackTakenMultiplier);
            }
        }

        [Test]
        public void EliteRoom_HalvesDurations_ButNotWindKnockback()
        {
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.EliteTrial);
            Assert.IsNotEmpty(director.Enemies);
            foreach (var e in director.Enemies)
            {
                Assert.AreEqual(ElementRules.EliteBossDurationScale, e.GetComponent<ElementStatusEffects>().DurationScale);
                Assert.AreEqual(1f, e.Status.KnockbackTakenMultiplier);
            }
        }

        [Test]
        public void Boss_HalvesDurations_AndWindKnockback()
        {
            director.RestartRun(5);
            Assert.IsTrue(director.DebugSkipToBoss());
            var boss = director.Boss;
            Assert.IsNotNull(boss);
            Assert.AreEqual(ElementRules.EliteBossDurationScale, boss.GetComponent<ElementStatusEffects>().DurationScale);
            Assert.AreEqual(ElementRules.BossWindKnockbackScale, boss.GetComponent<CombatantStatus>().KnockbackTakenMultiplier);
        }

        [UnityTest]
        public IEnumerator BurningPlayer_ShowsTheBurnChip_UntilItEnds()
        {
            director.RestartRun(5);
            var strip = Object.FindAnyObjectByType<RunHudStripView>();
            var fx = director.Player.GetComponent<ElementStatusEffects>();
            Assert.IsNotNull(fx, "player prefab needs ElementStatusEffects");
            yield return null;
            yield return null;
            Assert.IsFalse(strip.IsBurnChipVisible);

            fx.ApplyBurn(3, false, new AttackPayload(1, Team.Enemy, 3, false, false, 0, CardShape.Throw, Element.Fire));
            fx.ApplyChill(false);
            yield return null;
            yield return null; // LateUpdate refresh
            Assert.IsTrue(strip.IsBurnChipVisible);
            Assert.IsTrue(strip.IsChillChipVisible);
            StringAssert.StartsWith("BURN ", strip.BurnChipText);
            StringAssert.StartsWith("CHILL ", strip.ChillChipText);

            fx.Clear();
            yield return null;
            yield return null;
            Assert.IsFalse(strip.IsBurnChipVisible);
            Assert.IsFalse(strip.IsChillChipVisible);
        }
    }
}
