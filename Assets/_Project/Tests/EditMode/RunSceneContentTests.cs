using System.Linq;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Guards the generated M3 run scene (run DeckArpg/Build M3 Run Scene to regenerate it).</summary>
    public class RunSceneContentTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/M3_Run.unity";

        [Test]
        public void RunScene_IsFirstInBuildSettings_AndTheTemplateSceneIsOut()
        {
            var paths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            Assert.AreEqual(ScenePath, paths.FirstOrDefault(), "A build opens straight into the run.");
            CollectionAssert.DoesNotContain(paths, "Assets/Scenes/SampleScene.unity");
            CollectionAssert.AreEqual(new[] { ScenePath, "Assets/_Project/Scenes/M1_Arena.unity" }, paths,
                "Must match GreyboxArenaBuilder.BuildOrder.");
        }

        [Test]
        public void RunScene_HasWiredDirector_PlayerHudCamera_AndNoFixedDummy()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var director = roots.Select(r => r.GetComponent<RunDirector>()).FirstOrDefault(d => d != null);
                Assert.IsNotNull(director, "RunDirector missing.");

                var so = new SerializedObject(director);
                Assert.IsNotNull(so.FindProperty("content").objectReferenceValue, "RunContent not assigned.");
                Assert.IsNotNull(so.FindProperty("enemyPrefab").objectReferenceValue, "Enemy prefab not assigned.");
                Assert.IsNotNull(so.FindProperty("player").objectReferenceValue, "Player not assigned.");
                CollectionAssert.IsEmpty(director.Content.Validate());

                Assert.IsTrue(roots.Any(r => r.GetComponent<PlayerCombatInput>() != null), "Player missing.");
                Assert.IsFalse(roots.Any(r => r.GetComponent<TrainingDummyEnemy>() != null), "Enemies come from the director.");
                Assert.IsTrue(roots.Any(r => r.GetComponent("HandHudView") != null), "HUD missing.");
                var screens = roots.Select(r => r.GetComponent<RunScreens>()).FirstOrDefault(s => s != null);
                Assert.IsNotNull(screens, "RunScreens missing (rebuild the scene).");
                Assert.IsNotNull(screens.GetComponent<RunHudStripView>(), "HUD strip missing.");
                Assert.IsNotNull(screens.GetComponent<DoorChoiceView>(), "Door choice screen missing.");
                Assert.IsNotNull(screens.GetComponent<RewardView>(), "Reward screen missing.");
                Assert.IsNotNull(screens.GetComponent<ShopView>(), "Shop screen missing.");
                Assert.IsNotNull(screens.GetComponent<ForgeView>(), "Forge screen missing.");
                Assert.IsNotNull(screens.GetComponent<RestView>(), "Rest screen missing.");
                Assert.IsNotNull(screens.GetComponent<RunEndView>(), "Run-end screen missing.");
                Assert.AreSame(director, new SerializedObject(screens).FindProperty("director").objectReferenceValue,
                    "RunScreens not wired to the director.");
                Assert.IsTrue(roots.Any(r => r.CompareTag("MainCamera") && r.GetComponent<Camera>() != null), "Main camera missing.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
