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

        /// <summary>The M4 boss fight (docs/specs/m4-overview.md): boss HUD, FX states and the draw-order strip, as m4_*.png.</summary>
        [UnityTest]
        public IEnumerator CaptureM4Boss()
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            RenderOverlaysToTexture();
            string dir = ScreensDir();
            director.RestartRun(3);

            // Walk the run to the boss room (debug-clearing every fight).
            for (int guard = 0; guard < 20 && director.Boss == null; guard++)
            {
                switch (director.State)
                {
                    case RunDirectorState.ChoosingDoor: director.ChooseDoor(0); break;
                    case RunDirectorState.Fighting: director.DebugDefeatAllEnemies(); break;
                    case RunDirectorState.Reward: director.FinishReward(); break;
                    case RunDirectorState.Utility: director.LeaveUtility(); break;
                }
                yield return null;
            }
            var boss = director.Boss;
            Assert.IsNotNull(boss, "Never reached the boss room.");
            // Frame the boss: stand just in front of it (the camera follows the player).
            RunTestHelpers.Place(director.Player, boss.transform.position + new Vector3(0f, 0f, -3.5f));
            yield return null;
            yield return null;
            yield return Capture(dir, "m4_1_boss_hud_and_queue_strip");

            Assert.IsTrue(boss.ForceSleight(), "Setup: the boss needs a full hand.");
            yield return Capture(dir, "m4_2_boss_sleight_windup");

            boss.ForceStateEnd();
            yield return null; // fires
            var cycle = boss.Deck.Cycle;
            while (cycle.OldestSlot >= 0) cycle.TryPlay(cycle.OldestSlot, out _);
            for (int i = 0; i < 2 && cycle.DiscardCount > 0; i++) cycle.DeleteFromCurrentCycle(cycle.DiscardPile[0]);
            boss.ForceCycleEnd();
            yield return Capture(dir, "m4_3_boss_reload_window");

            boss.ForceExhausted();
            yield return Capture(dir, "m4_4_boss_exhausted");
            Debug.Log($"M4 screenshots written to {dir}");
        }

        /// <summary>Hit juice (docs/specs/hit-juice.md): damage numbers in each element colour, the red hurt number, a death pop.</summary>
        [UnityTest]
        public IEnumerator CaptureHitJuice()
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            RenderOverlaysToTexture();
            string dir = ScreensDir();
            director.RestartRun(5);

            // Find a fight with at least two enemies.
            for (int guard = 0; guard < 20; guard++)
            {
                if (director.State == RunDirectorState.Fighting && director.Enemies.Count >= 2) break;
                switch (director.State)
                {
                    case RunDirectorState.ChoosingDoor: director.ChooseDoor(0); break;
                    case RunDirectorState.Fighting: director.DebugDefeatAllEnemies(); break;
                    case RunDirectorState.Reward: director.FinishReward(); break;
                    case RunDirectorState.Utility: director.LeaveUtility(); break;
                }
                yield return null;
            }
            Assert.AreEqual(RunDirectorState.Fighting, director.State, "Never reached a fight.");
            var enemies = director.Enemies;
            Assert.GreaterOrEqual(enemies.Count, 2, "Need two enemies for the shot.");
            foreach (var e in enemies) e.Target = null; // hold their AI still
            // Stand between the two targets, a little in front, so both sit mid-screen above the HUD.
            var middle = (enemies[0].transform.position + enemies[1].transform.position) * 0.5f;
            RunTestHelpers.Place(director.Player, middle + new Vector3(0f, 0f, -1.5f));
            float settle = Time.realtimeSinceStartup + 1.5f; // let the follow camera's damping catch up
            while (Time.realtimeSinceStartup < settle) yield return null;

            var player = director.Player.GetComponent<CombatantStatus>();
            int tick = Time.frameCount;
            AttackPayload Card(int value, Element element) =>
                new AttackPayload(player.OwnerId, Team.Player, value, false, false, tick, CardShape.Strike, element);

            // 1) Numbers: small Fire, big Ice Sleight, Lightning, a poke, and a red hurt number over the player.
            enemies[0].Status.TakeHit(Card(2, Element.Fire));
            enemies[0].Status.TakeHit(new AttackPayload(player.OwnerId, Team.Player, 18, false, true, tick,
                CardShape.Strike, Element.Ice, 9));
            enemies[1].Status.TakeHit(Card(7, Element.Lightning));
            enemies[1].Status.TakeHit(AttackPayload.Poke(player.OwnerId, Team.Player, tick));
            player.TakeHit(new AttackPayload(enemies[1].Status.OwnerId, Team.Enemy, 4, false, false, tick,
                CardShape.Strike, Element.Physical));
            yield return Capture(dir, "juice_1_damage_numbers");

            // 2) Death pop mid-squash: a lethal Fire hit, captured a few frames in.
            Time.timeScale = 1f;
            var victim = enemies[1].Status;
            victim.TakeHit(new AttackPayload(player.OwnerId, Team.Player, 99, false, true, tick, CardShape.Strike,
                Element.Fire, 9));
            float popEnd = Time.realtimeSinceStartup + 0.1f;
            while (Time.realtimeSinceStartup < popEnd) yield return null;
            yield return Capture(dir, "juice_2_death_pop");
            Debug.Log($"Hit-juice screenshots written to {dir}");
        }

        /// <summary>
        /// Element mechanics (docs/specs/element-mechanics.md): a burning, chilled cluster mid-chain with pips and the
        /// chain line, and the BURN/CHILL chips on the player, as elements_*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureElements()
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            RenderOverlaysToTexture();
            string dir = ScreensDir();

            // Find a fight with at least three enemies (a chain needs a cluster).
            for (int seed = 1; seed < 40; seed++)
            {
                director.RestartRun(seed);
                for (int guard = 0; guard < 12; guard++)
                {
                    if (director.State == RunDirectorState.Fighting && director.Enemies.Count >= 3) break;
                    switch (director.State)
                    {
                        case RunDirectorState.ChoosingDoor: director.ChooseDoor(0); break;
                        case RunDirectorState.Fighting: director.DebugDefeatAllEnemies(); break;
                        case RunDirectorState.Reward: director.FinishReward(); break;
                        case RunDirectorState.Utility: director.LeaveUtility(); break;
                    }
                    yield return null;
                }
                if (director.State == RunDirectorState.Fighting && director.Enemies.Count >= 3) break;
            }
            var enemies = director.Enemies;
            Assert.GreaterOrEqual(enemies.Count, 3, "Need three enemies for the chain shot.");
            foreach (var e in enemies) e.Target = null; // hold their AI still
            // Pull them into a tight row (hops under 4 u) and stand the player in front of it.
            var centre = enemies[0].transform.position;
            for (int i = 0; i < enemies.Count; i++)
                RunTestHelpers.Place(enemies[i], centre + new Vector3((i - 1) * 2f, 0f, 0f));
            RunTestHelpers.Place(director.Player, centre + new Vector3(0f, 0f, -3f));
            float settle = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < settle) yield return null;

            var player = director.Player.GetComponent<CombatantStatus>();
            int tick = Time.frameCount;
            AttackPayload Card(int value, Element element, Team team = Team.Player, int owner = 0) =>
                new AttackPayload(owner != 0 ? owner : player.OwnerId, team, value, false, false, tick, CardShape.Strike, element);

            // Burn and chill the cluster, burn and chill the player, then fire a Lightning hit and shoot mid-chain.
            enemies[1].Status.TakeHit(Card(5, Element.Fire));
            enemies[1].Status.TakeHit(Card(3, Element.Ice));
            enemies[2].Status.TakeHit(Card(4, Element.Ice));
            player.TakeHit(Card(3, Element.Fire, Team.Enemy, enemies[1].Status.OwnerId));
            player.TakeHit(Card(2, Element.Ice, Team.Enemy, enemies[2].Status.OwnerId));
            float wait = Time.realtimeSinceStartup + 0.25f; // let the hit flashes and hit-stop pass
            while (Time.realtimeSinceStartup < wait) yield return null;
            Time.timeScale = 1f;
            enemies[0].Status.TakeHit(Card(6, Element.Lightning));
            yield return Capture(dir, "elements_1_chain_burn_chill");
            Debug.Log($"Element screenshots written to {dir}");
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
        /// <summary>
        /// Heal rework (docs/specs/heal-rework.md): the green "+3" Mend card in the hand, the channel ring mid-fill and
        /// the green heal number, as heal_*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureHeal()
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            RenderOverlaysToTexture();
            string dir = ScreensDir();
            director.RestartRun(5);
            director.ChooseDoor(0); // room 1 is always a fight
            Assert.AreEqual(RunDirectorState.Fighting, director.State);
            foreach (var e in director.Enemies) e.Target = null; // hold their AI still
            // Well out of swing reach: a dummy already in range keeps swinging even without a target.
            RunTestHelpers.Place(director.Player, director.Enemies[0].transform.position + new Vector3(0f, 0f, -8f));
            float settle = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < settle) yield return null;

            var player = director.Player.GetComponent<CombatantStatus>();
            int hitsTaken = 0;
            player.HitTaken += (d, dir2) => hitsTaken++;
            var cycle = director.Player.GetComponent<DeckRuntimeManager>().Cycle;
            player.SetHp(40);
            // Cycle non-heal cards out until Mend is in the hand.
            int healSlot = -1;
            for (int guard = 0; guard < 12 && healSlot < 0; guard++)
            {
                for (int i = 0; i < DeckCycle.HandSize; i++)
                    if (cycle.Hand[i] != null && cycle.Hand[i].IsHeal) healSlot = i;
                if (healSlot >= 0) break;
                for (int i = 0; i < DeckCycle.HandSize; i++)
                    if (cycle.Hand[i] != null && cycle.TryPlay(i, out _)) break;
            }
            Assert.GreaterOrEqual(healSlot, 0, "Mend never reached the hand.");
            yield return Capture(dir, "heal_1_hand_card");

            var channel = director.Player.HealChannel;
            int healed = -1;
            channel.Completed += a => healed = a;
            Assert.IsTrue(director.Player.Execute(CombatCommand.PlaySlot1 + healSlot));
            Assert.IsTrue(channel.IsChannelling);
            // Frame-driven, not wall-clock: batchmode frames are long, so a real-time wait can overshoot the 0.5 s channel.
            while (channel.IsChannelling && channel.Progress01 < 0.4f) yield return null;
            Assert.IsTrue(channel.IsChannelling, "the channel ended before the mid-fill shot");
            yield return Capture(dir, "heal_2_channel_ring");

            while (channel.IsChannelling) yield return null;
            Assert.AreEqual(0, hitsTaken, "nothing hit the player during the shot");
            Assert.AreEqual(9, healed, "Mend 3 heals 9");
            Assert.AreEqual(49, player.Hp);
            float pop = Time.realtimeSinceStartup + 0.1f;
            while (Time.realtimeSinceStartup < pop) yield return null;
            yield return Capture(dir, "heal_3_heal_number");
            Debug.Log($"Heal screenshots written to {dir}");
        }

        /// <summary>
        /// Enemy variants (docs/specs/enemy-variants.md): door blurbs with a wave line, a High Hexfire room with Casters
        /// and Brutes winding up (telegraph above the tall Caster), Rushers in a Mid Vanguard room and the wave-2 rings,
        /// as variants_*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureVariants()
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            var screens = Object.FindAnyObjectByType<RunScreens>();
            RenderOverlaysToTexture();
            string dir = ScreensDir();

            // 1) A door offer that includes a wave room (3-line blurb).
            bool found = false;
            for (int seed = 1; seed < 200 && !found; seed++)
            {
                director.RestartRun(seed);
                for (int room = 1; room <= 5 && !found; room++)
                {
                    foreach (var card in director.Run.CurrentOffer)
                        if (card.IsCombat && EnemyVariantRules.WaveTwoCount(card) > 0) found = true;
                    if (found) break;
                    director.ChooseDoor(0);
                    if (director.State == RunDirectorState.Fighting) { director.DebugDefeatAllEnemies(); director.FinishReward(); }
                    else director.LeaveUtility();
                }
            }
            Assert.IsTrue(found, "No offer with a wave room.");
            screens.Show(Object.FindAnyObjectByType<DoorChoiceView>());
            yield return Capture(dir, "variants_1_door_blurbs");
            screens.Hide();

            // 2) High Hexfire: Casters and Brutes, all winding up so the overhead telegraph shows.
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.Hexfire && RoomIntensity.Band(c.Number) == IntensityBand.High);
            yield return null; // Start() runs first, then the forced wind-up sticks
            foreach (var e in director.Enemies) { e.Target = null; e.ForceTelegraph(); }
            RunTestHelpers.Place(director.Player, FrontOf(director.Enemies));
            float settle = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            yield return Capture(dir, "variants_2_hexfire_casters");

            // 3) Mid Vanguard: Rushers next to Brutes.
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.Vanguard && RoomIntensity.Band(c.Number) != IntensityBand.Low);
            yield return null;
            foreach (var e in director.Enemies) e.Target = null;
            RunTestHelpers.Place(director.Player, FrontOf(director.Enemies));
            settle = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            yield return Capture(dir, "variants_3_vanguard_rushers");

            // 4) Wave 1 down: the wave-2 rings mid-delay.
            var lethal = new AttackPayload(director.Player.GetComponent<CombatantStatus>().OwnerId, Team.Player, 999,
                false, true, Time.frameCount, CardShape.Strike, Element.Physical);
            foreach (var e in director.Enemies)
                for (int hit = 0; hit < 10 && e != null && !e.Status.IsDead; hit++) e.Status.TakeHit(lethal);
            Assert.IsTrue(director.IsWaveIncoming);
            Time.timeScale = 1f;
            float ringWait = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < ringWait && director.IsWaveIncoming) yield return null;
            yield return Capture(dir, "variants_4_wave_rings");
            Debug.Log($"Variant screenshots written to {dir}");
        }

        /// <summary>The F1 debug menu (docs/specs/debug-menu.md): one shot per tab over a live fight, as debug_*.png.</summary>
        [UnityTest]
        public IEnumerator CaptureDebugMenu()
        {
            yield return SceneManager.LoadSceneAsync(RunSceneName);
            yield return null;
            var director = Object.FindAnyObjectByType<RunDirector>();
            var menu = DeckArpg.UI.DebugMenu.Instance;
            Assert.IsNotNull(menu, "The debug menu should create itself on play.");
            director.RestartRun(5);
            director.DebugEnterRoom(new MapCard(MapCardKind.Hexfire, 7));
            foreach (var e in director.Enemies) e.Target = null; // hold their AI still
            RunTestHelpers.Place(director.Player, FrontOf(director.Enemies));
            menu.FindTargets();
            menu.Open(); // before the canvas switch below, so the menu's canvas is active and gets moved too
            RenderOverlaysToTexture();
            string dir = ScreensDir();
            var menuCanvas = menu.GetComponentInChildren<Canvas>(true);
            // RenderOverlaysToTexture spaces canvases by sorting order (0.01 per step); at order 100 that lands behind
            // the near plane. Put the menu just in front of every other canvas instead.
            menuCanvas.planeDistance = cam.nearClipPlane + 0.02f;
            try
            {
                float settle = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < settle) yield return null;
                foreach (DeckArpg.UI.DebugTab tab in System.Enum.GetValues(typeof(DeckArpg.UI.DebugTab)))
                {
                    menu.SelectTab(tab);
                    yield return Capture(dir, $"debug_{(int)tab + 1}_{tab.ToString().ToLowerInvariant()}");
                }
            }
            finally
            {
                // The menu outlives this scene: put its canvas back on the overlay before the camera goes away.
                menu.SelectTab(DeckArpg.UI.DebugTab.Run);
                menu.Close();
                menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                menuCanvas.worldCamera = null;
            }
            Debug.Log($"Debug menu screenshots written to {dir}");
        }

        /// <summary>A spot just in front of the enemies' centre, so the follow camera frames the whole group.</summary>
        private static Vector3 FrontOf(System.Collections.Generic.IReadOnlyList<TrainingDummyEnemy> enemies)
        {
            var c = Vector3.zero;
            foreach (var e in enemies) c += e.transform.position;
            c /= Mathf.Max(1, enemies.Count);
            return new Vector3(c.x, 0f, c.z - 1.5f);
        }

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
