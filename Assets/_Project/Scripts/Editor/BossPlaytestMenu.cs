using DeckArpg.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeckArpg.Editor
{
    /// <summary>
    /// DeckArpg ▸ Play Boss Fight (Ctrl+Shift+B): opens M3_Run, enters Play mode and skips the run straight into the boss
    /// fight (<see cref="RunDirector.DebugSkipToBoss"/>). Already playing M3_Run: skips the current run to the boss.
    /// </summary>
    public static class BossPlaytestMenu
    {
        private const string PendingKey = "DeckArpg.SkipToBossOnPlay";

        [MenuItem("DeckArpg/Play Boss Fight %#b")]
        public static void PlayBossFight()
        {
            if (EditorApplication.isPlaying)
            {
                SkipWhenReady();
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(GreyboxArenaBuilder.RunScenePath);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            SkipWhenReady();
        };

        /// <summary>Waits until the RunDirector has started its run (it does so in Start), then skips to the boss.</summary>
        private static void SkipWhenReady()
        {
            int frames = 0;
            void Tick()
            {
                if (!EditorApplication.isPlaying || ++frames > 600)
                {
                    EditorApplication.update -= Tick;
                    return;
                }
                var director = Object.FindAnyObjectByType<RunDirector>();
                if (director == null || director.Run == null) return;
                EditorApplication.update -= Tick;
                if (director.DebugSkipToBoss()) Debug.Log("Boss fight ready: good luck.");
                else Debug.LogWarning("Couldn't reach the boss room; use the RunDirector ⋮ menu instead.");
            }
            EditorApplication.update += Tick;
        }
    }
}
