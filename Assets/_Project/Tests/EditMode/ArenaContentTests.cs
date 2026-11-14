using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Guards the generated M1 arena (run DeckArpg/Build M1 Arena to regenerate it).</summary>
    public class ArenaContentTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/M1_Arena.unity";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player.prefab";
        private const string DummyPrefab = "Assets/_Project/Prefabs/TrainingDummy.prefab";

        [Test]
        public void ArenaScene_IsFirstInBuildSettings()
        {
            var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            Assert.IsNotNull(first, "No enabled scenes in Build Settings.");
            Assert.AreEqual(ScenePath, first.path);
        }

        [Test]
        public void PlayerPrefab_HasCombatComponents_AndStarterDeck()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<PlayerMotor>());
            Assert.IsNotNull(prefab.GetComponent<PlayerCombatInput>());
            Assert.IsNotNull(prefab.GetComponent<CombatActionController>());
            Assert.AreEqual(Team.Player, prefab.GetComponent<CombatantStatus>().Team);

            var deck = StartingDeck(prefab);
            Assert.AreEqual(10, deck.Length);
            Assert.AreEqual(1, deck.Count(c => c.IsZeroBreaker), "Starter deck has exactly one 0-Parry.");
            Assert.IsTrue(deck.All(c => c.IsZeroBreaker || (c.BaseValue >= CardValue.Min && c.BaseValue <= CardValue.Max)));
        }

        [Test]
        public void DummyPrefab_IsEnemyWithDeck()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefab);
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<TrainingDummyEnemy>());
            Assert.AreEqual(Team.Enemy, prefab.GetComponent<CombatantStatus>().Team);
            Assert.IsNotEmpty(StartingDeck(prefab));
        }

        [TestCase(PlayerPrefab)]
        [TestCase(DummyPrefab)]
        public void CharacterPrefabs_DoNotDropSmallMoves(string path)
        {
            // minMoveDistance > 0 makes slow walkers stall at high frame rates.
            var cc = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<CharacterController>();
            Assert.AreEqual(0f, cc.minMoveDistance);
        }

        [Test]
        public void ArenaScene_ContainsPlayerDummyHudAndMainCamera()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.IsTrue(roots.Any(r => r.GetComponent<PlayerCombatInput>() != null), "Player missing.");
                Assert.IsTrue(roots.Any(r => r.GetComponent<TrainingDummyEnemy>() != null), "Dummy missing.");
                Assert.IsTrue(roots.Any(r => r.GetComponent("HandHudView") != null), "HUD missing.");
                foreach (var view in new[] { "StockView", "ReloadBarView", "SpeedLinesView" })
                    Assert.IsTrue(roots.Any(r => r.GetComponent(view) != null), $"HUD {view} missing.");
                Assert.IsTrue(roots.Any(r => r.CompareTag("MainCamera") && r.GetComponent<Camera>() != null), "Main camera missing.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static CardData[] StartingDeck(GameObject prefab)
        {
            var so = new SerializedObject(prefab.GetComponent<DeckRuntimeManager>());
            var list = so.FindProperty("startingDeck");
            return Enumerable.Range(0, list.arraySize)
                .Select(i => (CardData)list.GetArrayElementAtIndex(i).objectReferenceValue)
                .ToArray();
        }
    }
}
