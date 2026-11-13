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

        // The loser's break scales with how decisively it lost, and shoves it away from the winner.
        private static void Break(AttackHitbox winner, AttackHitbox loser, bool brokeByZero, int margin)
        {
            var away = loser.Owner.transform.position - winner.Owner.transform.position;
            // The swing that won can still follow through, but the counter is for a separate follow-up.
            winner.MarkBroke(loser.Owner.Status);
            loser.Owner.OnClashLost(loser, brokeByZero, CombatMath.ClampMargin(margin), away);
        }
    }
}
