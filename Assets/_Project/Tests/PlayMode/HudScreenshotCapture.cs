using System.Collections;
using System.IO;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
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
        private const string RunSceneName = "M3_Run";
        private RenderTexture rt;
        private Camera cam;

        [UnityTearDown]
        public IEnumerator UnloadArena()
        {
            Time.timeScale = 1f;
            if (cam != null) cam.targetTexture = null;
            if (rt != null) rt.Release();
            foreach (var name in new[] { SceneName, RunSceneName })
            {
                var scene = SceneManager.GetSceneByName(name);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                var empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        /// <summary>Points the main camera at a 1920x1080 texture and moves every overlay canvas onto it.</summary>
        private void RenderOverlaysToTexture()
        {
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
        }

        private static string ScreensDir()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "screens"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>The M3 run screens (docs/specs/m3-ui.md): one PNG per screen, written as m3_*.png.</summary>
        [UnityTest]
        public IEnumerator CaptureM3RunScreens()
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            var screens = Object.FindAnyObjectByType<RunScreens>();
            Assert.IsNotNull(screens, "RunScreens missing from M3_Run.");
            RenderOverlaysToTexture();
            string dir = ScreensDir();
            director.RestartRun(11);

            // 1) The arena at a door: the exit door and the HUD strip, no screen open.
            RunTestHelpers.Place(director.Player, director.Door.transform.position + new Vector3(0f, 0f, -5f));
            float settle = Time.realtimeSinceStartup + 1.5f; // let the follow camera's damping catch up
            while (Time.realtimeSinceStartup < settle) yield return null;
            yield return Capture(dir, "m3_1_arena_door");

            // 2) Door choice.
            screens.Show(Object.FindAnyObjectByType<DoorChoiceView>());
            yield return Capture(dir, "m3_2_door_choice");

            // 3) Reward draft after a plain fight (room 1 never offers a relic).
            director.ChooseDoor(0);
            director.DebugDefeatAllEnemies();
            yield return Capture(dir, "m3_3_reward_draft");
            screens.HandleKey(0);
            yield return Capture(dir, "m3_4_reward_picked");
            screens.HandleConfirm();

            // 5) Relic pick after an Elite.
            director.Run.AddCurrency(300); // so the shop below can afford things
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.EliteTrial);
            director.DebugDefeatAllEnemies();
            yield return Capture(dir, "m3_5_reward_relic");

            // 6-8) Blue rooms.
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.Shop);
            director.Run.AddCurrency(300);
            var shop = Object.FindAnyObjectByType<ShopView>();
            shop.Refresh();
            yield return Capture(dir, "m3_6_shop");
            screens.HandleKey(0); // buy: feedback line
            yield return Capture(dir, "m3_6b_shop_bought");
            shop.TogglePurge();
            yield return Capture(dir, "m3_7_shop_purge");
            shop.Deck.Pick(1);
            yield return Capture(dir, "m3_7b_shop_purge_selected");

            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.Forge);
            yield return Capture(dir, "m3_8_forge");
            screens.HandleKey(0);
            yield return Capture(dir, "m3_9_forge_upgrade_list");
            var forge = Object.FindAnyObjectByType<ForgeView>();
            for (int i = 0; i < forge.Deck.Count; i++) if (forge.Deck.IsEnabled(i)) { forge.Deck.Pick(i); break; }
            yield return Capture(dir, "m3_9b_forge_selected");
            screens.HandleConfirm();
            yield return Capture(dir, "m3_9c_forge_done");

            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.Rest);
            yield return Capture(dir, "m3_10_rest");

            // 11) Defeat summary.
            director.LeaveUtility();
            director.ChooseDoor(0);
            if (director.State == RunDirectorState.Fighting)
            {
                var lethal = new AttackPayload(12345, Team.Enemy, 999, false, true, Time.frameCount, CardShape.Strike, Element.Physical);
                director.Player.GetComponent<CombatantStatus>().TakeHit(lethal);
                yield return Capture(dir, "m3_11_run_end_defeat");
            }
            Debug.Log($"M3 screenshots written to {dir}");
        }

        [UnityTest]
        public IEnumerator CaptureM2HudStates()
        {
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null;

            var player = Object.FindAnyObjectByType<PlayerCombatInput>();
            var deck = player.GetComponent<DeckRuntimeManager>();
            var status = player.GetComponent<CombatantStatus>();
            RenderOverlaysToTexture();
            string dir = ScreensDir();
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
