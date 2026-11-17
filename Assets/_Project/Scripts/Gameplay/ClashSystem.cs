using System;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>What happened in a clash, for feedback (popups, VFX, SFX).</summary>
    public readonly struct ClashEvent
    {
        public readonly ClashResult Result;
        public readonly AttackPayload A;
        public readonly AttackPayload B;
        public readonly Vector3 Position;
        /// <summary>True when this result came out of a Deadlock tie-break.</summary>
        public readonly bool FromDeadlock;

        public ClashEvent(ClashResult result, AttackPayload a, AttackPayload b, Vector3 position, bool fromDeadlock = false)
        {
            Result = result;
            A = a;
            B = b;
            Position = position;
            FromDeadlock = fromDeadlock;
        }
    }

    /// <summary>Applies <see cref="ClashResolver"/> results to the scene (local hitbox-vs-hitbox clashes).</summary>
    public static class ClashSystem
    {
        public static event Action<ClashEvent> ClashOccurred;

        /// <summary>The scene's Deadlock controller, if any. Equal-value player/enemy clashes go to it.</summary>
        internal static DeadlockController Deadlock { get; set; }

        public static ClashResult Resolve(AttackHitbox a, AttackHitbox b)
        {
            var result = ClashResolver.Resolve(a.Payload, b.Payload);
            var position = (a.transform.position + b.transform.position) * 0.5f;
            var payloadA = a.Payload;
            var payloadB = b.Payload;

            switch (result.Outcome)
            {
                case ClashOutcome.None:
                    return result;
                case ClashOutcome.AWins:
                    Break(winner: a, loser: b, result.BrokeByZero, CombatMath.BreakMargin(payloadA, payloadB));
                    break;
                case ClashOutcome.BWins:
                    Break(winner: b, loser: a, result.BrokeByZero, CombatMath.BreakMargin(payloadB, payloadA));
                    break;
                case ClashOutcome.Neutral:
                    // A tie goes to the Deadlock minigame when one is available; it resolves later.
                    if (Deadlock != null && Deadlock.TryBegin(a, b, position)) return result;
                    a.Owner.CancelActiveAction();
                    b.Owner.CancelActiveAction();
                    break;
            }

            ClashOccurred?.Invoke(new ClashEvent(result, payloadA, payloadB, position));
            return result;
        }

        /// <summary>
        /// Applies a Deadlock's result: the winner's attack goes through as a Card Break with
        /// <paramref name="margin"/>; a tie cancels both. Falls back to a cancel if either hitbox is gone.
        /// </summary>
        internal static void ResolveDeadlock(AttackHitbox player, AttackHitbox enemy, ClashOutcome outcome, int margin,
            Vector3 position)
        {
            bool playerLive = player != null && player.IsLive;
            bool enemyLive = enemy != null && enemy.IsLive;
            if (!playerLive || !enemyLive) outcome = ClashOutcome.Neutral;
            // Snapshot before Break: the loser's hitbox is cancelled (destroyed) by it.
            var playerPayload = playerLive ? player.Payload : default;
            var enemyPayload = enemyLive ? enemy.Payload : default;

            switch (outcome)
            {
                case ClashOutcome.AWins:
                    Break(winner: player, loser: enemy, false, margin);
                    break;
                case ClashOutcome.BWins:
                    Break(winner: enemy, loser: player, false, margin);
                    break;
                default:
                    outcome = ClashOutcome.Neutral;
                    if (playerLive) player.Owner.CancelActiveAction();
                    if (enemyLive) enemy.Owner.CancelActiveAction();
                    if (!playerLive || !enemyLive) return; // nothing left to report on
                    break;
            }

            ClashOccurred?.Invoke(new ClashEvent(new ClashResult(outcome, false), playerPayload, enemyPayload,
                position, fromDeadlock: true));
        }

        /// <summary>
        /// An attack reached <paramref name="defender"/>'s body while it was winding up <paramref name="telegraph"/>:
        /// the two cards clash as if the swing were live. Higher breaks the anticipated attack (the hit still lands,
        /// without a counter bonus); lower breaks the attacker's card; equal cancels the attacker's action. The
        /// defender spends its card on a win or tie. 0s are not passed here (they only answer live swings).
        /// </summary>
        internal static ClashResult ResolveTelegraph(AttackHitbox attacker, CombatActionController defender,
            in AttackPayload telegraph)
        {
            var payload = attacker.Payload;
            var result = ClashResolver.Resolve(payload, telegraph);
            var position = (attacker.transform.position + defender.transform.position + Vector3.up * 0.5f) * 0.5f;
            var attackerOwner = attacker.Owner;

            switch (result.Outcome)
            {
                case ClashOutcome.None:
                    return result;
                case ClashOutcome.AWins:
                {
                    attacker.MarkBroke(defender.Status);
                    var away = defender.transform.position - attackerOwner.transform.position;
                    defender.Status.ApplyBreak(CombatMath.BreakMargin(payload, telegraph), away);
                    attackerOwner.OnClashWon(defender.Status, false);
                    break;
                }
                case ClashOutcome.BWins:
                {
                    var away = attackerOwner.transform.position - defender.transform.position;
                    attackerOwner.OnClashLost(attacker, false, CombatMath.BreakMargin(telegraph, payload), away);
                    defender.OnClashWon(attackerOwner.Status, false);
                    break;
                }
                case ClashOutcome.Neutral:
                    attackerOwner.CancelActiveAction();
                    break;
            }

            ClashOccurred?.Invoke(new ClashEvent(result, payload, telegraph, position));
            return result;
        }

        /// <summary>Reports a Big Deadlock's outcome (already applied by the boss clash) to clash listeners.</summary>
        internal static void ReportBig(ClashOutcome outcome, in AttackPayload player, in AttackPayload boss, Vector3 position) =>
            ClashOccurred?.Invoke(new ClashEvent(new ClashResult(outcome, false), player, boss, position, fromDeadlock: true));

        // The loser's break scales with how decisively it lost, and shoves it away from the winner.
        private static void Break(AttackHitbox winner, AttackHitbox loser, bool brokeByZero, int margin)
        {
            var away = loser.Owner.transform.position - winner.Owner.transform.position;
            // The swing that won can still follow through, but the counter is for a separate follow-up.
            winner.MarkBroke(loser.Owner.Status);
            var winnerOwner = winner.Owner;
            var loserStatus = loser.Owner.Status;
            loser.Owner.OnClashLost(loser, brokeByZero, CombatMath.ClampMargin(margin), away);
            winnerOwner.OnClashWon(loserStatus, brokeByZero);
        }
    }
}
