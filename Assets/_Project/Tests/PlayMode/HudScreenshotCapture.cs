using System.Collections;
using System.IO;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Visual check, not a regular test: loads the arena, puts the M2 HUD into each state and writes
    /// 1920x1080 PNGs to TestResults/screens. Batchmode has no window, so the main camera renders into a
    /// texture with every HUD canvas temporarily switched to Screen Space - Camera.
    /// Run without -nographics:
    ///   -runTests -testPlatform PlayMode -testFilter DeckArpg.Tests.PlayMode.HudScreenshotCapture
    /// </summary>
    [Explicit("Writes screenshots for manual review; run on its own without -nographics.")]
    public class HudScreenshotCapture
    {
        private const string SceneName = "M1_Arena";
        private RenderTexture rt;
        private Camera cam;

        [UnityTearDown]
        public IEnumerator UnloadArena()
        {
            Time.timeScale = 1f;
            if (cam != null) cam.targetTexture = null;
            if (rt != null) rt.Release();
            var arena = SceneManager.GetSceneByName(SceneName);
            if (!arena.IsValid() || !arena.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(arena);
        }

        [UnityTest]
        public IEnumerator CaptureM2HudStates()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;

            var player = Object.FindAnyObjectByType<PlayerCombatInput>();
            var deck = player.GetComponent<DeckRuntimeManager>();
            var status = player.GetComponent<CombatantStatus>();
            cam = Camera.main;
            Assert.IsNotNull(cam, "Main camera missing.");

            rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = cam.nearClipPlane + 0.5f + canvas.sortingOrder * -0.01f;
            }

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "screens"));
            Directory.CreateDirectory(dir);
            var cycle = deck.Cycle;
            var reload = deck.Reload;

            // 1) A Sleight already burned a card; queue empty; a card and the 0 stocked -> stock row + prompts.
            for (int s = 0; s < DeckCycle.HandSize; s++) cycle.TryStock(s, out _);
            while (cycle.DrawCount > 0)
            {
                int slot = cycle.Hand[0] != null ? 0 : cycle.Hand[1] != null ? 1 : 2;
                cycle.TryPlay(slot, out _);
            }
            int first = cycle.Hand[0] != null ? 0 : cycle.Hand[1] != null ? 1 : 2;
            cycle.TryStock(first, out _);
            cycle.TryStockParry(out _);
            cycle.AdvanceStockTimer(1.5f);
            yield return Capture(dir, "1_stock_and_prompts");

            // 2) Running bar at its rolled anchor, marker partway.
            Assert.IsTrue(reload.Begin(), "Reload should be possible.");
            reload.Tick(0.3f * reload.BarDuration);
            yield return Capture(dir, $"2_bar_running_{reload.Anchor}");

            // 3) Jammed.
            reload.Cancel();
            Assert.IsTrue(reload.Begin());
            reload.Press();
            reload.Tick(0.6f);
            yield return Capture(dir, $"3_bar_jammed_{reload.Anchor}");

            // 4) Perfect: popup + speed lines.
            reload.Cancel();
            Assert.IsTrue(reload.Begin());
            reload.Tick(reload.Target01 * reload.BarDuration);
            Assert.AreEqual(ReloadGrade.Perfect, reload.Press());
            yield return Capture(dir, "4_perfect_speed_lines");

            // 5) Overburn bar (ember tint).
            while (cycle.DrawCount > 0)
            {
                int slot = cycle.Hand[0] != null ? 0 : cycle.Hand[1] != null ? 1 : 2;
                cycle.TryPlay(slot, out _);
            }
            if (cycle.BurnedCount == 0)
            {
                for (int s = 0; s < DeckCycle.HandSize; s++) cycle.TryStock(s, out _);
                while (cycle.DrawCount > 0) cycle.TryPlay(cycle.Hand[0] != null ? 0 : 1, out _);
            }
            Assert.IsTrue(reload.BeginOverburn(status), "Overburn should be possible.");
            reload.Tick(0.45f * reload.BarDuration);
            yield return Capture(dir, $"5_overburn_{reload.Anchor}");

            Debug.Log($"HUD screenshots written to {dir}");
        }

        /// <summary>Freezes the world, lets the views lay out for two frames, then saves the camera's texture.</summary>
        private IEnumerator Capture(string dir, string name)
        {
            Time.timeScale = 0f;
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Time.timeScale = 1f;
        }
    }
}
