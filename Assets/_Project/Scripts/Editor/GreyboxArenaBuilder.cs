using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DeckArpg.Editor
{
    /// <summary>
    /// Generates the M1 greybox arena (scene, prefabs, materials) from code so it is reproducible
    /// and can run in batchmode: -executeMethod DeckArpg.Editor.GreyboxArenaBuilder.Build
    /// Rebuilding overwrites the scene, prefabs and materials it owns (not the card assets).
    /// </summary>
    public static class GreyboxArenaBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/M1_Arena.unity";
        public const string PrefabFolder = "Assets/_Project/Prefabs";
        public const string MaterialFolder = "Assets/_Project/Art/Materials";
        public const string PlayerPrefabPath = PrefabFolder + "/Player.prefab";
        public const string DummyPrefabPath = PrefabFolder + "/TrainingDummy.prefab";

        private const float ArenaHalfSize = 12f;
        private static readonly Quaternion IsoRotation = Quaternion.Euler(50f, 45f, 0f);
        private const float CameraDistance = 22f;
        public const float DummyParryWindow = 0.3f;
        /// <summary>Player flinch is shorter than the dummy's default so getting hit stays fair.</summary>
        public const float PlayerHitFlinch = 0.15f;

        [MenuItem("DeckArpg/Build M1 Arena")]
        public static void Build()
        {
            var playerDeck = StarterContentBuilder.BuildPlayerStarterDeck();
            var dummyDeck = StarterContentBuilder.BuildDummyDeck();

            StarterContentBuilder.EnsureFolder(PrefabFolder);
            StarterContentBuilder.EnsureFolder(MaterialFolder);
            StarterContentBuilder.EnsureFolder("Assets/_Project/Scenes");

            var floorMat = GetOrCreateMaterial("Floor", new Color(0.32f, 0.34f, 0.38f));
            var wallMat = GetOrCreateMaterial("Wall", new Color(0.2f, 0.21f, 0.25f));
            var playerMat = GetOrCreateMaterial("Player", new Color(0.25f, 0.55f, 0.95f));
            var enemyMat = GetOrCreateMaterial("Enemy", new Color(0.85f, 0.3f, 0.25f));
            var accentMat = GetOrCreateMaterial("Accent", new Color(0.95f, 0.95f, 0.95f));

            var playerPrefab = BuildPlayerPrefab(playerDeck, playerMat, accentMat);
            var dummyPrefab = BuildDummyPrefab(dummyDeck, enemyMat, accentMat);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildEnvironment(floorMat, wallMat);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.position = new Vector3(0f, 0f, -4f);
            var dummy = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab, scene);
            dummy.transform.position = new Vector3(0f, 0f, 4f);
            dummy.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            BuildCameras(player.transform);
            BuildClashFx();

            var hud = new GameObject("HUD");
            hud.AddComponent<HandHudView>();
            hud.AddComponent<ClashFeedbackView>();
            hud.AddComponent<CardShatterView>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"M1 arena built: {ScenePath}");
        }

        private static void BuildEnvironment(Material floorMat, Material wallMat)
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.37f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.22f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(ArenaHalfSize * 0.2f, 1f, ArenaHalfSize * 0.2f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            floor.isStatic = true;

            var walls = new GameObject("Walls").transform;
            float len = ArenaHalfSize * 2f + 1f;
            CreateWall(walls, "North", new Vector3(0f, 1f, ArenaHalfSize), new Vector3(len, 2f, 1f), wallMat);
            CreateWall(walls, "South", new Vector3(0f, 1f, -ArenaHalfSize), new Vector3(len, 2f, 1f), wallMat);
            CreateWall(walls, "East", new Vector3(ArenaHalfSize, 1f, 0f), new Vector3(1f, 2f, len), wallMat);
            CreateWall(walls, "West", new Vector3(-ArenaHalfSize, 1f, 0f), new Vector3(1f, 2f, len), wallMat);
        }

        private static void CreateWall(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.position = position;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = mat;
            wall.isStatic = true;
        }

        private static void BuildCameras(Transform followTarget)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            cam.fieldOfView = 30f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CinemachineBrain>();
            var offset = IsoRotation * (Vector3.back * CameraDistance);
            camGo.transform.SetPositionAndRotation(followTarget.position + offset, IsoRotation);

            // Fixed isometric angle: world-space follow offset and no aim component, so rotation never changes.
            var vcamGo = new GameObject("CM Iso Camera");
            vcamGo.transform.SetPositionAndRotation(camGo.transform.position, IsoRotation);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Follow = followTarget;
            var lens = vcam.Lens;
            lens.FieldOfView = 30f;
            vcam.Lens = lens;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = offset;
            var tracker = follow.TrackerSettings;
            tracker.BindingMode = BindingMode.WorldSpace;
            tracker.PositionDamping = new Vector3(0.4f, 0.4f, 0.4f);
            follow.TrackerSettings = tracker;
            // Receives clash camera shake from ClashImpactFx's impulse source.
            vcamGo.AddComponent<CinemachineImpulseListener>();
            // Zooms in on Card Breaks and eases back out after the freeze.
            vcamGo.AddComponent<CameraPunch>();
        }

        private static GameObject BuildPlayerPrefab(List<CardData> deck, Material bodyMat, Material accentMat)
        {
            var root = CreateCharacterRoot("Player", bodyMat, accentMat);
            root.tag = "Player";
            var status = root.AddComponent<CombatantStatus>();
            status.Team = Team.Player;
            status.HitFlinchSeconds = PlayerHitFlinch;
            root.AddComponent<HitFlash>();
            root.AddComponent<CombatActionController>();
            var deckManager = root.AddComponent<DeckRuntimeManager>();
            AssignDeck(deckManager, deck);
            root.AddComponent<PlayerMotor>();
            root.AddComponent<PlayerCombatInput>();
            return SavePrefab(root, PlayerPrefabPath);
        }

        private static GameObject BuildDummyPrefab(List<CardData> deck, Material bodyMat, Material accentMat)
        {
            var root = CreateCharacterRoot("TrainingDummy", bodyMat, accentMat);
            var status = root.AddComponent<CombatantStatus>();
            status.Team = Team.Enemy;
            SetInt(status, "maxHp", 60);
            root.AddComponent<HitFlash>();
            // Windup between the swing activating and it hurting: the window to answer with a 0-Parry.
            root.AddComponent<CombatActionController>().BodyHitDelay = DummyParryWindow;
            var deckManager = root.AddComponent<DeckRuntimeManager>();
            AssignDeck(deckManager, deck);
            root.AddComponent<TrainingDummyEnemy>();
            root.AddComponent<EnemyTelegraphView>();
            return SavePrefab(root, DummyPrefabPath);
        }

        private static GameObject CreateCharacterRoot(string name, Material bodyMat, Material accentMat)
        {
            var root = new GameObject(name);
            var cc = root.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 1f, 0f);
            cc.height = 2f;
            cc.radius = 0.45f;
            // Default 0.001 silently drops small per-frame moves at high frame rates (slow walkers stall).
            cc.minMoveDistance = 0f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;

            // "Nose" shows facing direction in the greybox.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.4f, 0.45f);
            nose.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
            nose.GetComponent<Renderer>().sharedMaterial = accentMat;
            return root;
        }

        private static void AssignDeck(DeckRuntimeManager manager, List<CardData> deck)
        {
            var so = new SerializedObject(manager);
            var list = so.FindProperty("startingDeck");
            list.arraySize = deck.Count;
            for (int i = 0; i < deck.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = deck[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Additive particle material + soft-dot texture saved as assets so builds keep them.</summary>
        private static Material GetOrCreateSparkMaterial()
        {
            string texPath = $"{MaterialFolder}/ClashSparkDot.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = ClashImpactFx.CreateSoftDotTexture();
                AssetDatabase.CreateAsset(tex, texPath);
            }

            string path = $"{MaterialFolder}/ClashSpark.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            ClashImpactFx.ConfigureAdditive(mat, tex);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildClashFx()
        {
            var fx = new GameObject("ClashFx");
            fx.AddComponent<HitStop>();
            fx.AddComponent<ClashImpactFx>().ParticleMaterial = GetOrCreateSparkMaterial();
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
