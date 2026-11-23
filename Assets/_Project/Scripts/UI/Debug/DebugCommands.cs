#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;

namespace DeckArpg.UI
{
    public enum DebugTab
    {
        Run,
        Player,
        Enemies,
        Time,
        Info
    }

    /// <summary>
    /// One debug-menu row. <see cref="Run"/> is the click (null = a label-only row); <see cref="Step"/>, when set, adds
    /// &lt; and &gt; buttons that call it with -1/+1. <see cref="Available"/> greys the row out instead of letting it throw.
    /// </summary>
    public sealed class DebugCommand
    {
        public readonly DebugTab Tab;
        public readonly Func<string> Label;
        public readonly Action Run;
        public readonly Func<bool> Available;
        public readonly Action<int> Step;

        public DebugCommand(DebugTab tab, Func<string> label, Action run, Func<bool> available = null, Action<int> step = null)
        {
            Tab = tab;
            Label = label;
            Run = run;
            Available = available ?? (() => true);
            Step = step;
        }
    }

    /// <summary>
    /// Every debug-menu command (docs/specs/debug-menu.md). To add a test hook for a new feature, add one row here: it
    /// shows up in its tab, greys out when <c>available</c> is false and is covered by the menu's smoke test.
    /// </summary>
    public static class DebugCommands
    {
        public const int SpawnDistance = 5;
        private static readonly float[] TimeScales = { 0.1f, 0.25f, 0.5f, 1f, 2f };

        public static List<DebugCommand> Build(DebugMenu m)
        {
            var list = new List<DebugCommand>();
            AddRun(list, m);
            AddPlayer(list, m);
            AddEnemies(list, m);
            AddTime(list, m);
            return list;
        }

        // ---- Run ----

        private static void AddRun(List<DebugCommand> list, DebugMenu m)
        {
            const DebugTab T = DebugTab.Run;
            Func<bool> hasRun = () => m.Director != null && m.Director.Run != null;
            var picker = m.Picker;

            list.Add(new DebugCommand(T, () => "Restart run (random seed)", () => m.Director.RestartRun(), () => m.Director != null));
            list.Add(new DebugCommand(T, () => $"Restart same seed ({(m.Director != null ? m.Director.LastSeed : 0)})",
                () => m.Director.RestartRun(m.Director.LastSeed), hasRun));
            list.Add(new DebugCommand(T, () => "Copy seed to clipboard",
                () => GUIUtility.systemCopyBuffer = m.Director.LastSeed.ToString(), hasRun));
            list.Add(new DebugCommand(T, () => picker.KindLabel, null, () => true, picker.StepKind));
            list.Add(new DebugCommand(T, () => picker.NumberLabel, null, () => true, picker.StepNumber));
            list.Add(new DebugCommand(T, () => picker.ExtraLabel, null,
                () => picker.Kind == MapCardKind.EliteTrial || picker.Kind == MapCardKind.KindledGround, picker.StepExtra));
            list.Add(new DebugCommand(T, () => $"> Enter now: {picker.Card}",
                () => m.Director.DebugEnterRoom(picker.Card), () => hasRun() && m.Director.DebugCanEnterRoom(picker.Card)));
            list.Add(new DebugCommand(T, () => "Clear room (kills wave 2 too)", () => m.Director.DebugDefeatAllEnemies(),
                () => hasRun() && m.Director.State == RunDirectorState.Fighting));
            list.Add(new DebugCommand(T, () => "Skip wave-2 delay", () => m.Director.DebugSkipWaveDelay(),
                () => hasRun() && m.Director.IsWaveIncoming));
            list.Add(new DebugCommand(T, () => "Finish room -> next door", () => m.Director.DebugFinishCurrentRoom(),
                () => hasRun() && m.Director.State != RunDirectorState.ChoosingDoor));
            list.Add(new DebugCommand(T, () => "Skip to boss", () => m.Director.DebugSkipToBoss(), () => m.Director != null));
        }

        // ---- Player ----

        private static void AddPlayer(List<DebugCommand> list, DebugMenu m)
        {
            const DebugTab T = DebugTab.Player;
            Func<bool> alive = () => m.PlayerStatus != null && !m.PlayerStatus.IsDead;
            Func<bool> hasRun = () => m.Director != null && m.Director.Run != null;

            list.Add(new DebugCommand(T, () => $"God mode: {OnOff(m.PlayerStatus != null && m.PlayerStatus.GodMode)}",
                () => m.PlayerStatus.GodMode = !m.PlayerStatus.GodMode, () => m.PlayerStatus != null));
            list.Add(new DebugCommand(T, () => "Full heal", () => SetPlayerHp(m, int.MaxValue), alive));
            list.Add(new DebugCommand(T, () => "HP to 50%", () => SetPlayerHp(m, m.PlayerStatus.MaxHp / 2), alive));
            list.Add(new DebugCommand(T, () => "HP to 1", () => SetPlayerHp(m, 1), alive));
            list.Add(new DebugCommand(T, () => "+10 max HP", () => m.Director.DebugIncreaseMaxHp(10), hasRun));
            list.Add(new DebugCommand(T, () => "+100 currency", () => m.Director.Run.AddCurrency(100), hasRun));
            list.Add(new DebugCommand(T, () => "+1 Momentum", () => m.Director.Run.Momentum.AddStack(), hasRun));

            var relics = (RelicId[])Enum.GetValues(typeof(RelicId));
            int relic = 0;
            list.Add(new DebugCommand(T,
                () => $"Give relic: {relics[relic]}" + (hasRun() && m.Director.Run.HasRelic(relics[relic]) ? " (held)" : ""),
                () => m.Director.DebugGrantRelic(relics[relic]),
                () => hasRun() && !m.Director.Run.HasRelic(relics[relic]),
                dir => relic = Wrap(relic + dir, relics.Length)));

            int card = 0;
            Func<CardData> picked = () => { var all = m.AllCards; return all.Count > 0 ? all[Wrap(card, all.Count)] : null; };
            list.Add(new DebugCommand(T,
                () => { var c = picked(); return c != null ? $"Add card: {c.DisplayName} [{c.CardId}]" : "Add card: (no content)"; },
                () => m.Director.DebugAddCard(picked()), () => hasRun() && picked() != null,
                dir => card = Wrap(card + dir, Math.Max(1, m.AllCards.Count))));
            list.Add(new DebugCommand(T, () => "Remove one copy of that card", () => m.Director.DebugRemoveCard(picked()),
                () => hasRun() && picked() != null && m.Director.Run.Deck.Any(c => c.Data == picked())));

            list.Add(new DebugCommand(T, () => "Instant reload (fresh hand)", () =>
            {
                m.PlayerDeck.Reload.Cancel();
                m.PlayerDeck.Cycle.Reload();
            }, () => m.PlayerDeck != null && m.PlayerDeck.Cycle != null));
            list.Add(new DebugCommand(T, () => "Restore burned cards", () => m.PlayerDeck.Cycle.RestoreBurned(),
                () => m.PlayerDeck != null && m.PlayerDeck.Cycle != null && m.PlayerDeck.Cycle.BurnedCount > 0));
            list.Add(new DebugCommand(T, () => "Cancel heal channel", () => m.PlayerInput.HealChannel.Cancel(),
                () => m.PlayerInput != null && m.PlayerInput.HealChannel != null && m.PlayerInput.HealChannel.IsChannelling));
            list.Add(new DebugCommand(T, () => "Burn me", () => Effects(m.PlayerStatus)?.ApplyBurn(15, false, EnemySource()),
                () => alive() && Effects(m.PlayerStatus) != null));
            list.Add(new DebugCommand(T, () => "Chill me", () => Effects(m.PlayerStatus)?.ApplyChill(false),
                () => alive() && Effects(m.PlayerStatus) != null));
            list.Add(new DebugCommand(T, () => "Clear my statuses", () => Effects(m.PlayerStatus)?.Clear(),
                () => Effects(m.PlayerStatus) != null));
        }

        private static void SetPlayerHp(DebugMenu m, int hp)
        {
            if (m.Director != null) m.Director.DebugSetPlayerHp(hp);
            else m.PlayerStatus.SetHp(Mathf.Clamp(hp, 1, m.PlayerStatus.MaxHp));
        }

        // ---- Enemies & combat ----

        private static void AddEnemies(List<DebugCommand> list, DebugMenu m)
        {
            const DebugTab T = DebugTab.Enemies;
            Func<bool> any = () => m.LivingEnemies().Count > 0;

            foreach (var v in new[] { EnemyVariant.Brute, EnemyVariant.Caster, EnemyVariant.Rusher })
                list.Add(new DebugCommand(T, () => $"Spawn {v} ({SpawnDistance} u ahead)",
                    () => m.Director.DebugSpawnEnemy(v, m.SpawnPointAhead(SpawnDistance)),
                    () => m.Director != null && m.Director.DebugCanSpawnEnemy && m.PlayerInput != null));

            list.Add(new DebugCommand(T, () => "Kill all living", () =>
            {
                foreach (var e in m.LivingEnemies()) Kill(e.Status, m);
            }, any));
            list.Add(new DebugCommand(T, () => "Kill nearest", () => Kill(m.NearestEnemy().Status, m), any));
            list.Add(new DebugCommand(T, () => "Damage all 10", () =>
            {
                foreach (var e in m.LivingEnemies()) e.Status.TakeEffectDamage(10, Element.Physical, PlayerSource(m));
            }, any));
            list.Add(new DebugCommand(T, () => $"Freeze AI: {OnOff(TrainingDummyEnemy.DebugFreezeAI)}",
                () => TrainingDummyEnemy.DebugFreezeAI = !TrainingDummyEnemy.DebugFreezeAI));
            list.Add(new DebugCommand(T, () => "Force telegraph (all)", () =>
            {
                foreach (var e in m.LivingEnemies()) e.DebugForceTelegraph();
            }, any));
            list.Add(new DebugCommand(T, () => "Elite: force Overextended", () =>
            {
                foreach (var e in m.LivingEnemies())
                {
                    var opening = e.GetComponent<EliteOpening>();
                    if (opening != null) opening.Open(e.RecoverSeconds + EliteRules.OverextendSeconds);
                }
            }, () => m.LivingEnemies().Any(e => e.GetComponent<EliteOpening>() != null)));
            list.Add(new DebugCommand(T, () => "Stun all 2 s", () =>
            {
                foreach (var e in m.LivingEnemies()) e.Status.ApplyStun(2f);
            }, any));
            list.Add(new DebugCommand(T, () => "Card-break all", () =>
            {
                foreach (var e in m.LivingEnemies()) e.Status.ApplyBreak();
            }, any));
            list.Add(new DebugCommand(T, () => "Burn all", () =>
            {
                foreach (var e in m.LivingEnemies()) Effects(e.Status)?.ApplyBurn(15, false, PlayerSource(m));
            }, any));
            list.Add(new DebugCommand(T, () => "Chill all", () =>
            {
                foreach (var e in m.LivingEnemies()) Effects(e.Status)?.ApplyChill(false);
            }, any));
            list.Add(new DebugCommand(T, () => "Wind shove all (away from you)", () =>
            {
                var wind = new AttackPayload(m.PlayerStatus != null ? m.PlayerStatus.OwnerId : 1, Team.Player, 6, false,
                    false, Time.frameCount, CardShape.Strike, Element.Wind);
                var from = m.PlayerStatus != null ? m.PlayerStatus.transform.position : Vector3.zero;
                foreach (var e in m.LivingEnemies()) e.Status.ApplyKnockback(wind, e.transform.position - from);
            }, any));

            Func<BossEnemy> boss = () => m.Director != null && m.Director.Boss != null && !m.Director.Boss.Status.IsDead
                ? m.Director.Boss : null;
            list.Add(new DebugCommand(T, () => "Boss: force Exhausted", () => boss().ForceExhausted(), () => boss() != null));
            list.Add(new DebugCommand(T, () => "Boss: mill 5 cards", () =>
            {
                var cycle = boss().Deck.Cycle;
                for (int n = 0; n < 5; n++)
                {
                    var card = cycle.Hand.FirstOrDefault(c => c != null);
                    if (card == null || !cycle.DeleteFromCurrentCycle(card)) break;
                }
            }, () => boss() != null));
            list.Add(new DebugCommand(T, () => $"Hitboxes: {HitboxLabel(CombatActionController.DebugShowHitboxes)}",
                () => CombatActionController.DebugShowHitboxes = NextHitboxMode(CombatActionController.DebugShowHitboxes)));
        }

        private static void Kill(CombatantStatus s, DebugMenu m)
        {
            var lethal = new AttackPayload(PlayerSource(m).OwnerId, Team.Player, 999, false, true, Time.frameCount,
                CardShape.Strike, Element.Physical);
            for (int hit = 0; hit < 10 && s != null && !s.IsDead; hit++) s.TakeHit(lethal);
        }

        private static string HitboxLabel(bool? mode) => mode == null ? "default" : OnOff(mode.Value);
        /// <summary>default -> ON -> OFF -> default.</summary>
        public static bool? NextHitboxMode(bool? mode) => mode == null ? true : mode.Value ? false : (bool?)null;

        // ---- Time & juice ----

        private static void AddTime(List<DebugCommand> list, DebugMenu m)
        {
            const DebugTab T = DebugTab.Time;
            list.Add(new DebugCommand(T, () => HitStop.BaseTimeScale == 0f ? "Resume" : "Pause",
                () => m.TogglePause()));
            foreach (var scale in TimeScales)
                list.Add(new DebugCommand(T, () => $"Time scale {scale}x" + (Mathf.Approximately(HitStop.BaseTimeScale, scale) ? "  <" : ""),
                    () => m.SetTimeScale(scale)));
            list.Add(new DebugCommand(T, () => $"Hit-stop: {OnOff(!HitStop.Disabled)}", () => HitStop.Disabled = !HitStop.Disabled));
            list.Add(new DebugCommand(T, () => $"Camera shake: {OnOff(m.Juice.Shake)}", () => { m.Juice.Shake = !m.Juice.Shake; m.JuiceChanged(); }));
            list.Add(new DebugCommand(T, () => $"Hit/clash sparks: {OnOff(m.Juice.Sparks)}", () => { m.Juice.Sparks = !m.Juice.Sparks; m.JuiceChanged(); }));
            list.Add(new DebugCommand(T, () => $"Damage numbers: {OnOff(m.Juice.Numbers)}", () => { m.Juice.Numbers = !m.Juice.Numbers; m.JuiceChanged(); }));
            list.Add(new DebugCommand(T, () => $"Sound: {OnOff(m.Juice.Sound)}", () => { m.Juice.Sound = !m.Juice.Sound; m.JuiceChanged(); }));
        }

        // ---- helpers ----

        private static ElementStatusEffects Effects(CombatantStatus s) => s != null ? s.GetComponent<ElementStatusEffects>() : null;

        private static AttackPayload PlayerSource(DebugMenu m) =>
            new AttackPayload(m.PlayerStatus != null ? m.PlayerStatus.OwnerId : 1, Team.Player, 3, false, false,
                Time.frameCount, CardShape.Strike, Element.Fire);

        private static AttackPayload EnemySource() =>
            new AttackPayload(-1, Team.Enemy, 3, false, false, Time.frameCount, CardShape.Strike, Element.Fire);

        private static string OnOff(bool on) => on ? "ON" : "OFF";
        private static int Wrap(int i, int n) => n <= 0 ? 0 : ((i % n) + n) % n;
    }
}
#endif
