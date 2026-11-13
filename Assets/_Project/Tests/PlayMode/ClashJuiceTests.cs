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
    /// <summary>Hit-stop and clash feedback driven by real hitbox clashes.</summary>
    public class ClashJuiceTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        private T Spawn<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go.AddComponent<T>();
        }

        private CombatActionController MakeCombatant(Team team, Vector3 position)
        {
            var go = new GameObject($"{team}_combatant");
            spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CombatantStatus>().Team = team;
            return go.AddComponent<CombatActionController>();
        }

        private static AttackPayload Payload(CombatActionController c, int value, int tick, bool zero = false) =>
            new AttackPayload(c.Status.OwnerId, c.Status.Team, value, zero, false, tick, CardShape.Strike, Element.Fire);

        /// <summary>Player at origin facing +Z, enemy 2.2 m ahead facing -Z: strike boxes overlap.</summary>
        private (CombatActionController player, CombatActionController enemy) FacingPair() =>
            (MakeCombatant(Team.Player, Vector3.zero), MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 2.2f)));

        /// <summary>Waits on frames + real time (WaitForFixedUpdate would stall while timeScale is 0).</summary>
        private static IEnumerator WaitUntilRealtime(System.Func<bool> done, float timeout = 1f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        private static IEnumerator Clash(CombatActionController winner, int winValue, CombatActionController loser, int loseValue,
            bool zero = false)
        {
            loser.Spawn(Payload(loser, loseValue, tick: 1), loser.Status.Team == Team.Enemy ? Vector3.back : Vector3.forward);
            winner.Spawn(Payload(winner, winValue, tick: 2, zero), winner.Status.Team == Team.Player ? Vector3.forward : Vector3.back);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private struct ViewSnapshot
        {
            public string Text;
            public float Scale;
            public float FlashAlpha;
            public Color FlashColor;
        }

        /// <summary>
        /// Captures the view right after it handles the clash (it subscribed first, so it runs first),
        /// so frame-time fades can't make the assertions flaky.
        /// </summary>
        private static IEnumerator ClashAndSnapshot(ClashFeedbackView view, CombatActionController winner, int winValue,
            CombatActionController loser, int loseValue, bool zero, System.Action<ViewSnapshot> result)
        {
            ViewSnapshot? snap = null;
            System.Action<ClashEvent> probe = _ => snap = new ViewSnapshot
            {
                Text = view.ShownText, Scale = view.PopupScale,
                FlashAlpha = view.ScreenFlashAlpha, FlashColor = view.ScreenFlashColor
            };
            ClashSystem.ClashOccurred += probe;
            try { yield return Clash(winner, winValue, loser, loseValue, zero); }
            finally { ClashSystem.ClashOccurred -= probe; }
            Assert.IsTrue(snap.HasValue, "No clash happened.");
            result(snap.Value);
        }

        [UnityTest]
        public IEnumerator CardBreak_FreezesTime_ThenRestoresIt()
        {
            Spawn<HitStop>("HitStop");
            var (player, enemy) = FacingPair();
            float scaleDuringClash = -1f;
            float clashRealtime = 0f;
            System.Action<ClashEvent> probe = _ => { scaleDuringClash = Time.timeScale; clashRealtime = Time.realtimeSinceStartup; };
            ClashSystem.ClashOccurred += probe;
            try
            {
                enemy.Spawn(Payload(enemy, 3, tick: 1), Vector3.back);
                player.Spawn(Payload(player, 8, tick: 2), Vector3.forward);
                yield return WaitUntilRealtime(() => scaleDuringClash >= 0f);
                Assert.AreEqual(0f, scaleDuringClash, "Time should freeze on Card Break.");

                yield return WaitUntilRealtime(() => Time.timeScale > 0f);
                Assert.AreEqual(1f, Time.timeScale, "Time scale must be restored after the freeze.");
                Assert.GreaterOrEqual(Time.realtimeSinceStartup - clashRealtime, CombatMath.CardBreakHitStop * 0.9f);
            }
            finally
            {
                ClashSystem.ClashOccurred -= probe;
            }
        }

        [UnityTest]
        public IEnumerator TwoHitStops_SameClash_RestoreTimeScale_InsteadOfLockingAtZero()
        {
            // Regression: the second instance saved timeScale 0 as "previous" and restored it, freezing forever.
            Spawn<HitStop>("HitStopA");
            Spawn<HitStop>("HitStopB");
            var (player, enemy) = FacingPair();
            enemy.Spawn(Payload(enemy, 3, tick: 1), Vector3.back);
            player.Spawn(Payload(player, 8, tick: 2), Vector3.forward);

            yield return WaitUntilRealtime(() => Time.timeScale == 0f);
            Assert.AreEqual(0f, Time.timeScale, "Clash should have frozen time.");
            yield return WaitUntilRealtime(() => Time.timeScale > 0f, timeout: 1f);

            Assert.AreEqual(1f, Time.timeScale, "Overlapping HitStops must restore the original time scale.");
        }

        [UnityTest]
        public IEnumerator DisablingHitStop_MidFreeze_RestoresTimeScale()
        {
            var hitStop = Spawn<HitStop>("HitStop");
            hitStop.Freeze(5f);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(hitStop.IsFrozen);

            hitStop.enabled = false;

            Assert.AreEqual(1f, Time.timeScale, "A disabled HitStop must never leave the game frozen.");
            Assert.IsFalse(hitStop.IsFrozen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Freeze_RestoresPreviousTimeScale_NotAlwaysOne()
        {
            Time.timeScale = 0.5f;
            var hitStop = Spawn<HitStop>("HitStop");
            hitStop.Freeze(0.05f);

            yield return WaitUntilRealtime(() => !hitStop.IsFrozen);

            Assert.AreEqual(0.5f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator Freeze_NonPositiveDuration_DoesNothing()
        {
            var hitStop = Spawn<HitStop>("HitStop");
            hitStop.Freeze(0f);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(hitStop.IsFrozen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CardBreak_PulsesPostFx_AndBurstsSparks()
        {
            var fx = Spawn<ClashImpactFx>("Fx");
            var (player, enemy) = FacingPair();

            yield return Clash(player, 8, enemy, 3);

            Assert.Greater(fx.PulseWeight, 0f, "A Card Break should pulse the post-FX volume.");
            Assert.Greater(fx.LastSparkCount, 0);
        }

        [UnityTest]
        public IEnumerator NeutralClash_SparksButNoPostFxPulse()
        {
            var fx = Spawn<ClashImpactFx>("Fx");
            var (player, enemy) = FacingPair();
            enemy.Spawn(Payload(enemy, 5, tick: 1), Vector3.back);
            player.Spawn(Payload(player, 5, tick: 1), Vector3.forward);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.Greater(fx.LastSparkCount, 0);
            Assert.AreEqual(0f, fx.PulseWeight, "Neutral clashes are kept small: no post-FX pulse.");
        }

        [UnityTest]
        public IEnumerator PlayerBreaksEnemy_ShowsCardBreak_WithPunch_AndWhiteFlash()
        {
            var view = Spawn<ClashFeedbackView>("Feedback");
            var (player, enemy) = FacingPair();

            ViewSnapshot s = default;
            yield return ClashAndSnapshot(view, player, 8, enemy, 3, false, r => s = r);

            Assert.AreEqual("CARD BREAK!", s.Text);
            Assert.Greater(s.Scale, 1f, "Popup should start punched-up.");
            Assert.Greater(s.FlashAlpha, 0f);
            Assert.AreEqual(1f, s.FlashColor.g, 0.01f, "Flash is white when the player wins.");
        }

        [UnityTest]
        public IEnumerator EnemyBreaksPlayer_FlashesRed()
        {
            var view = Spawn<ClashFeedbackView>("Feedback");
            var (player, enemy) = FacingPair();

            ViewSnapshot s = default;
            yield return ClashAndSnapshot(view, enemy, 8, player, 3, false, r => s = r);

            Assert.AreEqual("CARD BREAK!", s.Text);
            Assert.Greater(s.FlashColor.r, s.FlashColor.g, "Being broken should flash red.");
        }

        [UnityTest]
        public IEnumerator ZeroBreak_ShowsZeroBreak_WithStrongerFlashThanCardBreak()
        {
            var view = Spawn<ClashFeedbackView>("Feedback");
            var (player, enemy) = FacingPair();

            ViewSnapshot zeroSnap = default;
            yield return ClashAndSnapshot(view, player, 0, enemy, 9, true, r => zeroSnap = r);

            Assert.AreEqual("ZERO BREAK!", zeroSnap.Text);
            Assert.Greater(zeroSnap.FlashAlpha, 0.3f, "Zero Break flashes harder than a Card Break (0.25).");
        }

        [UnityTest]
        public IEnumerator BrokenCombatant_FlashesBreakColor()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spawned.Add(body);
            var status = body.AddComponent<CombatantStatus>();
            var flash = body.AddComponent<HitFlash>();

            status.ApplyBreak();

            Assert.IsTrue(flash.IsFlashing);
            Assert.AreNotEqual(Color.white, flash.CurrentColor, "Break flash should differ from the hit flash.");
            yield return null;
        }
    }
}
