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

        public ClashEvent(ClashResult result, AttackPayload a, AttackPayload b, Vector3 position)
        {
            Result = result;
            A = a;
            B = b;
            Position = position;
        }
    }

    /// <summary>Applies <see cref="ClashResolver"/> results to the scene (local hitbox-vs-hitbox clashes).</summary>
    public static class ClashSystem
    {
        public static event Action<ClashEvent> ClashOccurred;

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
                    b.Owner.OnClashLost(b, result.BrokeByZero);
                    break;
                case ClashOutcome.BWins:
                    a.Owner.OnClashLost(a, result.BrokeByZero);
                    break;
                case ClashOutcome.Neutral:
                    a.Owner.CancelActiveAction();
                    b.Owner.CancelActiveAction();
                    break;
            }

            ClashOccurred?.Invoke(new ClashEvent(result, payloadA, payloadB, position));
            return result;
        }
    }
}
