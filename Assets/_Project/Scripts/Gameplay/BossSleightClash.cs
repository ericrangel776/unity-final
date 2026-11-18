using System;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// The global HUD clash for boss Sleights (docs/specs/m4-overview.md). Range doesn't matter:
    /// <list type="bullet">
    /// <item>During the wind-up, a player Sleight clashes the boss's pending sum: higher breaks the wind-up, lower is
    /// broken, equal fires the boss Sleight on the spot and starts the Big Deadlock.</item>
    /// <item>Within <see cref="BossRules.GlobalZeroWindowSeconds"/> after the boss Sleight activates, a player 0 breaks
    /// it and 0-deletes all three of its cards; a player Sleight clashes it (equal = Big Deadlock).</item>
    /// <item>A single card reaching the body mid wind-up clashes the sum like a telegraph (it usually loses).</item>
    /// </list>
    /// Dashing through it always works (no clash). Only the first answer to each boss Sleight counts.
    /// </summary>
    [RequireComponent(typeof(BossEnemy))]
    public sealed class BossSleightClash : MonoBehaviour
    {
        private BossEnemy boss;
        private CombatActionController player;
        private float answeredSleightTime = float.NaN;

        /// <summary>A Big Deadlock finished and its outcome was applied (AWins = player).</summary>
        public event Action<ClashOutcome, Vector3> BigDeadlockApplied;

        public CombatActionController Player => player;

        private void Awake()
        {
            boss = GetComponent<BossEnemy>();
            boss.BusyClashResolver = ResolveBusyClash;
        }

        private void OnDestroy()
        {
            Watch(null);
            if (boss != null) boss.BusyClashResolver = null;
        }

        /// <summary>Starts listening to <paramref name="playerCombat"/>'s attacks (null stops).</summary>
        public void Watch(CombatActionController playerCombat)
        {
            if (player != null) player.HitboxSpawned -= HandlePlayerHitbox;
            player = playerCombat;
            if (player != null) player.HitboxSpawned += HandlePlayerHitbox;
        }

        private void HandlePlayerHitbox(AttackHitbox hb)
        {
            if (hb == null || !hb.IsLive || boss.Status.IsDead) return;
            var payload = hb.Payload;
            if (payload.Team != Team.Player || payload.IsPoke) return;

            if (boss.State == BossState.SleightWindup)
            {
                if (payload.IsSleight) ClashWindup(hb);
                return; // single cards clash on the body; 0s never clash a telegraph
            }

            if (!payload.IsSleight && !payload.IsZeroBreaker) return;
            if (Time.time - boss.LastSleightTime > BossRules.GlobalZeroWindowSeconds) return;
            if (answeredSleightTime.Equals(boss.LastSleightTime)) return;
            var bossHb = LiveBossSleight();
            if (bossHb == null) return;

            answeredSleightTime = boss.LastSleightTime;
            ClashLive(hb, bossHb);
        }

        private AttackHitbox LiveBossSleight()
        {
            foreach (var b in boss.LastSleightHitboxes) if (b != null && b.IsLive) return b;
            return null;
        }

        private void ClashWindup(AttackHitbox playerHb)
        {
            var anticipated = Anticipated();
            var result = ClashResolver.Resolve(playerHb.Payload, anticipated);
            if (result.Outcome == ClashOutcome.Neutral)
            {
                // Both Sleights go off at once: the tie becomes the Big Deadlock.
                var bossHb = boss.FireNow();
                answeredSleightTime = boss.LastSleightTime;
                if (bossHb != null) BeginBig(playerHb, bossHb);
                return;
            }
            // Higher: the boss is broken (its wind-up cracks via Broken). Lower: the player's Sleight is broken.
            ClashSystem.ResolveTelegraph(playerHb, boss.Combat, anticipated);
        }

        private void ClashLive(AttackHitbox playerHb, AttackHitbox bossHb)
        {
            var result = ClashResolver.Resolve(playerHb.Payload, bossHb.Payload);
            if (result.Outcome == ClashOutcome.Neutral && playerHb.Payload.IsSleight)
            {
                BeginBig(playerHb, bossHb);
                return;
            }

            var cards = new System.Collections.Generic.List<RuntimeCardInstance>(boss.LastSleightCards);
            result = ClashSystem.Resolve(playerHb, bossHb);
            if (result.Outcome == ClashOutcome.AWins && result.BrokeByZero)
                boss.Deck.Cycle.DeleteSleight(cards); // all three set aside until the reload (counts toward Exhausted)
        }

        /// <summary>A single card (or a late Sleight) reached the body mid wind-up: it clashes the pending sum.</summary>
        private bool ResolveBusyClash(AttackHitbox incoming)
        {
            if (boss.State != BossState.SleightWindup || incoming == null || !incoming.IsLive) return false;
            var payload = incoming.Payload;
            if (payload.Team != Team.Player || payload.IsPoke || payload.IsZeroBreaker) return false;
            var result = ClashSystem.ResolveTelegraph(incoming, boss.Combat, Anticipated());
            return result.Outcome != ClashOutcome.None;
        }

        private AttackPayload Anticipated() =>
            AttackPayload.FromSleight(boss.PendingSleight.Value, boss.Status.OwnerId, Team.Enemy, Time.frameCount,
                SleightRules.HitCount(boss.PendingSleight.Value.Shape));

        // ---- Big Deadlock ("winner takes all") ----

        private void BeginBig(AttackHitbox playerHb, AttackHitbox bossHb)
        {
            var playerPayload = playerHb.Payload;
            var bossPayload = bossHb.Payload;
            var position = (playerHb.Owner.transform.position + boss.transform.position) * 0.5f + Vector3.up * 0.5f;
            var deadlock = DeadlockController.Current;
            void Apply(ClashOutcome outcome) => ApplyBig(outcome, playerHb, playerPayload, bossPayload, position);

            if (deadlock == null || !deadlock.TryBeginBig(playerHb, bossHb, position, Apply))
                Apply(ClashOutcome.Neutral); // no minigame available: both shatter
        }

        private void ApplyBig(ClashOutcome outcome, AttackHitbox playerHb, AttackPayload playerPayload,
            AttackPayload bossPayload, Vector3 position)
        {
            // The Deadlock can finish after we're gone (it aborts when its controller is disabled, e.g. on scene unload).
            if (this == null || boss == null) return;
            var playerOwner = playerHb != null ? playerHb.Owner : player;
            switch (outcome)
            {
                case ClashOutcome.AWins:
                {
                    // The player's Sleight lands with both sums combined, as one hit, then the boss is Exhausted.
                    boss.Combat.CancelActiveAction();
                    var combined = new AttackPayload(playerPayload.OwnerId, Team.Player,
                        playerPayload.Value + bossPayload.Value, false, true, Time.frameCount, playerPayload.Shape,
                        playerPayload.Element, playerPayload.FinisherValue, 1);
                    var away = playerOwner != null ? boss.transform.position - playerOwner.transform.position : Vector3.zero;
                    // Winner takes all: the combined hit ignores the boss's guard.
                    float guard = boss.Status.DamageTakenMultiplier;
                    boss.Status.DamageTakenMultiplier = Mathf.Max(1f, guard);
                    boss.Status.TakeHit(combined, away, canCounter: false);
                    boss.Status.DamageTakenMultiplier = guard;
                    if (!boss.Status.IsDead) boss.ForceExhausted();
                    break;
                }
                case ClashOutcome.BWins:
                {
                    // A heavy hit that never kills, plus knockback.
                    if (playerOwner != null)
                    {
                        playerOwner.CancelActiveAction();
                        var full = new AttackPayload(bossPayload.OwnerId, Team.Enemy, bossPayload.Value, false, true,
                            Time.frameCount, bossPayload.Shape, bossPayload.Element, bossPayload.FinisherValue, 1)
                            .WithDamageScale(bossPayload.DamageScale);
                        var away = playerOwner.transform.position - boss.transform.position;
                        playerOwner.Status.TakeHit(full, away, canCounter: false, nonLethal: true);
                    }
                    boss.Combat.CancelActiveAction();
                    break;
                }
                default:
                    outcome = ClashOutcome.Neutral;
                    if (playerOwner != null) playerOwner.CancelActiveAction();
                    boss.Combat.CancelActiveAction();
                    break;
            }

            ClashSystem.ReportBig(outcome, playerPayload, bossPayload, position);
            BigDeadlockApplied?.Invoke(outcome, position);
        }
    }
}
