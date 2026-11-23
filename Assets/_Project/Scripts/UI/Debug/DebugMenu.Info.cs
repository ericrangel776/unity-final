#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using System.Text;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;

namespace DeckArpg.UI
{
    // Info tab, status line, juice switches and the room/run timer binding.
    public sealed partial class DebugMenu
    {
        private string StatusLine()
        {
            string scene = Director != null ? "run scene" : PlayerInput != null ? "arena (no RunDirector)" : "no player";
            return $"{scene}  ·  time x{HitStop.BaseTimeScale:0.##}  ·  {(Pinned ? "pinned: input free" : "input blocked while open")}";
        }

        private void ApplyJuice()
        {
            if (Juice.AllOn && juiceApplied) return; // nothing switched off: leave the scene alone
            foreach (var punch in FindObjectsByType<CameraPunch>(FindObjectsSortMode.None))
            {
                punch.enabled = Juice.Shake;
                foreach (var b in punch.GetComponents<Behaviour>())
                    if (b.GetType().Name == "CinemachineImpulseListener") b.enabled = Juice.Shake;
            }
            foreach (var fx in FindObjectsByType<HitImpactFx>(FindObjectsSortMode.None)) fx.enabled = Juice.Sparks;
            foreach (var fx in FindObjectsByType<ClashImpactFx>(FindObjectsSortMode.None)) fx.enabled = Juice.Sparks;
            foreach (var view in FindObjectsByType<DamageNumberView>(FindObjectsSortMode.None)) view.enabled = Juice.Numbers;
            AudioListener.volume = Juice.Sound ? 1f : 0f;
            juiceApplied = Juice.AllOn; // all back on: applied once, then left alone
        }

        /// <summary>Marks the juice switches for re-application (call after changing <see cref="Juice"/>).</summary>
        public void JuiceChanged() => juiceApplied = false;

        private string BuildInfo()
        {
            float now = Time.realtimeSinceStartup;
            var sb = new StringBuilder();
            sb.AppendLine($"FPS {fps:0}   time x{HitStop.BaseTimeScale:0.##}   Freeze AI {(TrainingDummyEnemy.DebugFreezeAI ? "ON" : "off")}");

            var d = Director;
            var run = d != null ? d.Run : null;
            if (run != null)
            {
                string room = d.State == RunDirectorState.ChoosingDoor
                    ? $"door ({string.Join(", ", run.CurrentOffer.Select(c => c.ToString()))})"
                    : run.CurrentRoom.ToString();
                sb.AppendLine($"{d.State}   room {run.RoomNumber}/{RunState.BossRoom}: {room}");
                sb.AppendLine($"Seed {d.LastSeed}   currency {run.Currency}   Momentum {run.Momentum.Stacks}");
                sb.AppendLine($"Relics: {(run.Relics.Count > 0 ? string.Join(", ", run.Relics) : "none")}");
                sb.AppendLine($"Room time {Timer.RoomElapsed(now):0.0}s   run time {Timer.RunElapsed(now):0}s");
                if (Timer.History.Count > 0)
                    sb.AppendLine("Last rooms: " + string.Join("  ", Timer.History.Select(h => $"{h.room} {h.seconds:0.0}s")));
                sb.AppendLine($"Enemies {d.Enemies.Count(e => e != null && !e.Status.IsDead)}/{d.Enemies.Count(e => e != null)}" +
                              $"   mix [{string.Join(", ", d.CurrentMix)}]   waves pending {d.WavesPending}" +
                              (d.IsWaveIncoming ? "   WAVE 2 INCOMING" : ""));
            }
            else
            {
                sb.AppendLine(d == null ? "No RunDirector in this scene (Run tab is off)." : "No run yet.");
                sb.AppendLine($"Enemies alive: {LivingEnemies().Count}");
            }

            var p = PlayerStatus;
            if (p != null)
            {
                var fx = p.GetComponent<ElementStatusEffects>();
                string statuses = fx == null ? "" : (fx.IsBurning ? " BURN" : "") + (fx.IsChilled ? " CHILL" : "");
                sb.AppendLine($"Player HP {p.Hp}/{p.MaxHp}   posture {p.Posture:0}/{p.MaxPosture:0}{statuses}" +
                              (p.GodMode ? "   GOD" : "") + (p.IsStunned ? "   STUNNED" : ""));
            }
            var deck = PlayerDeck;
            if (deck != null && deck.Cycle != null)
            {
                var c = deck.Cycle;
                sb.AppendLine($"Hand [{string.Join(", ", c.Hand.Select(h => h == null ? "-" : h.GetEffectiveValue().ToString()))}]" +
                              $"   draw {c.DrawCount}  discard {c.DiscardCount}  burned {c.BurnedCount}  stock {c.StockCount}");
            }
            return sb.ToString();
        }

        private void BindTimer()
        {
            if (Director == boundDirector) return;
            UnbindTimer();
            boundDirector = Director;
            if (boundDirector == null) return;
            boundDirector.RoomStarted += HandleRoomStarted;
            boundDirector.StateChanged += HandleStateChanged;
            boundRun = boundDirector.Run;
            Timer.StartRun(Time.realtimeSinceStartup);
        }

        private void UnbindTimer()
        {
            if (ReferenceEquals(boundDirector, null)) return;
            boundDirector.RoomStarted -= HandleRoomStarted;
            boundDirector.StateChanged -= HandleStateChanged;
            boundDirector = null;
        }

        private void HandleRoomStarted(MapCard room)
        {
            CheckNewRun();
            Timer.RoomStarted(Time.realtimeSinceStartup, room.ToString());
        }

        private void HandleStateChanged(RunDirectorState state)
        {
            CheckNewRun();
            if (state != RunDirectorState.Fighting && state != RunDirectorState.Utility) Timer.RoomEnded(Time.realtimeSinceStartup);
        }

        private void CheckNewRun()
        {
            if (boundDirector == null || boundDirector.Run == boundRun) return;
            boundRun = boundDirector.Run;
            Timer.StartRun(Time.realtimeSinceStartup);
        }
    }
}
#endif
