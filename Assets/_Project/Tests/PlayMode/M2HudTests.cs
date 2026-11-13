using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Stock row, reload bar, speed lines and the reload/Overburn prompt.</summary>
    public class M2HudTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private DeckRuntimeManager deck;
        private CombatantStatus status;
        private Camera cam;
        private StockView stockView;
        private ReloadBarView barView;
        private SpeedLinesView speedView;
        private HandHudView handView;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var player = new GameObject("HudPlayer");
            spawned.Add(player);
            status = player.AddComponent<CombatantStatus>();
            status.Team = Team.Player;
            player.AddComponent<CombatActionController>();
            deck = player.AddComponent<DeckRuntimeManager>();

            var cards = new List<CardData>();
            for (int v = 1; v <= 7; v++)
            {
                var data = CardData.Create($"hud_{v}", v, CardShape.Throw, Element.Fire);
                cards.Add(data);
                spawned.Add(data);
            }
            var zero = CardData.Create("hud_zero", 1, zeroBreaker: true);
            cards.Add(zero);
            spawned.Add(zero);
            deck.SetStartingDeck(cards, newSeed: 5);

            var camGo = new GameObject("HudCamera");
            spawned.Add(camGo);
            cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0f, 10f, -10f);
            camGo.transform.LookAt(Vector3.zero);

            var hud = new GameObject("Hud");
            spawned.Add(hud);
            stockView = hud.AddComponent<StockView>();
            stockView.Bind(deck);
            barView = hud.AddComponent<ReloadBarView>();
            barView.Bind(deck, player.transform, cam);
            speedView = hud.AddComponent<SpeedLinesView>();
            speedView.Bind(deck);
            handView = hud.AddComponent<HandHudView>();
            handView.Bind(deck, status, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private void EmptyDrawQueue()
        {
            while (deck.Cycle.DrawCount > 0) deck.Cycle.TryPlay(0, out _);
        }

        // ---- stock ----

        [UnityTest]
        public IEnumerator Stock_HiddenWhenEmpty_ShowsSlotAndPartialPreview()
        {
            yield return null;
            Assert.IsFalse(stockView.IsVisible);

            int value = deck.Cycle.Hand[0].GetEffectiveValue();
            deck.Cycle.TryStock(0, out _);
            yield return null;

            Assert.IsTrue(stockView.IsVisible);
            Assert.AreEqual(value.ToString(), stockView.SlotText(0));
            Assert.AreEqual("", stockView.SlotText(1));
            StringAssert.StartsWith($"SLEIGHT {value}+ · Throw", stockView.PreviewText);
        }

        [UnityTest]
        public IEnumerator Stock_ZeroShowsMirroredValue()
        {
            int value = deck.Cycle.Hand[0].GetEffectiveValue();
            deck.Cycle.TryStock(0, out _);
            deck.Cycle.TryStockParry(out _);
            yield return null;

            Assert.AreEqual($"0→{value}", stockView.SlotText(1));
            StringAssert.StartsWith($"SLEIGHT {value * 2}+ · Throw · Fire", stockView.PreviewText);
            StringAssert.Contains("finisher", stockView.PreviewText);
        }

        [UnityTest]
        public IEnumerator Stock_TimeoutBarDrains()
        {
            deck.Cycle.TryStock(0, out _);
            yield return null;
            Assert.Greater(stockView.TimeoutFill01, 0.9f);

            deck.Cycle.AdvanceStockTimer(SleightRules.StockTimeoutSeconds * 0.5f);
            yield return null;
            Assert.AreEqual(0.5f, stockView.TimeoutFill01, 0.05f);
        }

        [UnityTest]
        public IEnumerator Stock_HidesAgainAfterInterrupt()
        {
            deck.Cycle.TryStock(0, out _);
            yield return null;
            deck.Cycle.InterruptStock();
            yield return null;
            Assert.IsFalse(stockView.IsVisible);
        }

        // ---- reload bar ----

        [UnityTest]
        public IEnumerator Bar_HiddenWhileIdle_ShowsRolledZoneAndMarker()
        {
            yield return null;
            Assert.IsFalse(barView.IsBarVisible);

            EmptyDrawQueue();
            Assert.IsTrue(deck.Reload.Begin());
            deck.Reload.Tick(0.3f * deck.Reload.BarDuration);
            // Freeze game time so progress can't advance between the view's LateUpdate and our read
            // (test coroutines resume after Update but before LateUpdate). TearDown restores it.
            Time.timeScale = 0f;
            yield return null;
            yield return null;

            Assert.IsTrue(barView.IsBarVisible);
            Assert.AreEqual(deck.Reload.Target01, barView.ZoneCenter01, 1e-3f);
            Assert.AreEqual(ActiveReloadRules.GoodTolerance01 * 2f, barView.GoodZoneWidth01, 1e-3f);
            Assert.AreEqual(deck.Reload.Progress01, barView.MarkerProgress, 0.05f);
            Assert.AreEqual("RELOAD", barView.TitleText);
            Assert.IsFalse(barView.IsOverburnTint);
        }

        [UnityTest]
        public IEnumerator Bar_SitsAtTheRolledAnchor()
        {
            EmptyDrawQueue();
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(deck.Reload.Begin());
                yield return null;
                var expected = ReloadBarView.AnchorOffset(deck.Reload.Anchor);
                var actual = barView.BarScreenOffset;
                // Same direction as the anchor (clamping can shorten it near screen edges).
                Assert.Greater(Vector2.Dot(expected.normalized, actual.normalized), 0.9f,
                    $"{deck.Reload.Anchor}: expected {expected}, got {actual}");
                deck.Reload.Cancel();
            }
        }

        [UnityTest]
        public IEnumerator Bar_Jam_ShowsJamStyleAndPopup()
        {
            EmptyDrawQueue();
            deck.Reload.Begin();
            Assert.AreEqual(ReloadGrade.Miss, deck.Reload.Press()); // at 0 s: always outside the zone
            yield return null;

            Assert.IsTrue(barView.IsJammedStyle);
            Assert.AreEqual("JAMMED", barView.GradeText);
            Assert.AreEqual("RECHARGING", barView.TitleText, "Title doesn't repeat the popup.");
        }

        [UnityTest]
        public IEnumerator Bar_NewBar_ClearsLingeringPopup()
        {
            EmptyDrawQueue();
            deck.Reload.Begin();
            deck.Reload.Tick(deck.Reload.Target01 * deck.Reload.BarDuration);
            deck.Reload.Press(); // Perfect: popup up, deck reshuffled
            yield return null;
            Assert.AreEqual("PERFECT!", barView.GradeText);

            EmptyDrawQueue();
            Assert.IsTrue(deck.Reload.Begin());
            yield return null;
            Assert.IsNull(barView.GradeText);
        }

        [UnityTest]
        public IEnumerator Bar_Perfect_HidesBar_PopsUp_AndStartsSpeedLines()
        {
            yield return null;
            Assert.IsFalse(speedView.IsActive);

            EmptyDrawQueue();
            deck.Reload.Begin();
            deck.Reload.Tick(deck.Reload.Target01 * deck.Reload.BarDuration);
            Assert.AreEqual(ReloadGrade.Perfect, deck.Reload.Press());
            yield return null;

            Assert.IsFalse(barView.IsBarVisible);
            Assert.AreEqual("PERFECT!", barView.GradeText);
            Assert.IsTrue(speedView.IsActive);
            Assert.AreEqual(0.6f, speedView.Alpha, 0.05f);
        }

        [UnityTest]
        public IEnumerator Bar_Overburn_IsTinted()
        {
            deck.Cycle.TryStock(0, out _);
            deck.Cycle.TryStock(1, out _);
            deck.Cycle.TryStock(2, out _); // burns one
            EmptyDrawQueue();
            Assert.IsTrue(deck.Reload.BeginOverburn(status));
            yield return null;

            Assert.IsTrue(barView.IsOverburnTint);
            Assert.AreEqual("OVERBURN", barView.TitleText);
        }

        // ---- prompt ----

        [UnityTest]
        public IEnumerator Prompt_ShowsReload_AndOverburnCost()
        {
            yield return null;
            Assert.AreEqual("", handView.PromptText, "Nothing to reload yet.");

            deck.Cycle.TryStock(0, out _);
            deck.Cycle.TryStock(1, out _);
            deck.Cycle.TryStock(2, out _);
            EmptyDrawQueue();
            yield return null;

            StringAssert.Contains("[R] RELOAD", handView.PromptText);
            StringAssert.Contains($"OVERBURN  -{OverburnRules.Cost(status.MaxHp, 0)} HP  (restores 1)", handView.PromptText);
        }

        [UnityTest]
        public IEnumerator Prompt_UnaffordableOverburn_SaysWhatItNeeds()
        {
            deck.Cycle.TryStock(0, out _);
            deck.Cycle.TryStock(1, out _);
            deck.Cycle.TryStock(2, out _);
            EmptyDrawQueue();
            status.SpendHp(status.MaxHp - 5); // 5 HP left; costs 10
            yield return null;

            StringAssert.Contains("OVERBURN needs 11+ HP", handView.PromptText);
        }

        [UnityTest]
        public IEnumerator Prompt_HiddenWhileBarRuns()
        {
            EmptyDrawQueue();
            deck.Reload.Begin();
            yield return null;
            Assert.AreEqual("", handView.PromptText);
        }
    }
}
