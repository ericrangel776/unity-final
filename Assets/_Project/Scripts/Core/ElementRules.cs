using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeckArpg.Core
{
    /// <summary>
    /// Pure element rules and every element tuning constant (docs/specs/element-mechanics.md). Element decides how
    /// an attack is delivered and which status it applies; Physical keeps the card's authored shape.
    /// </summary>
    public static class ElementRules
    {
        // Burn (Fire)
        /// <summary>Total burn damage = ceil(hitDamage * 0.4), at least 1.</summary>
        public const float BurnFraction = 0.4f;
        public const float BurnSeconds = 3f;
        public const float SleightBurnSeconds = 4f;
        public const float BurnTickSeconds = 0.5f;

        // Chill (Ice)
        /// <summary>x0.7 move speed; enemies also tick their wind-up/recover at x0.7.</summary>
        public const float ChillSlow = 0.3f;
        public const float ChillSeconds = 2.5f;
        public const float SleightChillSeconds = 4f;

        // Chain (Lightning)
        public const int ChainTargets = 3;
        /// <summary>Each hop is measured from the previous link (XZ plane).</summary>
        public const float ChainHopRadius = 4f;
        /// <summary>Chain damage = ceil(hitDamage * 0.5), at least 1.</summary>
        public const float ChainDamageFraction = 0.5f;

        // Physical
        public const float PhysicalBreakPostureMultiplier = 1.5f;

        // Wind
        public const float WindKnockbackMultiplier = 2.5f;
        /// <summary>Posture added by a slam, as a fraction of the victim's MaxPosture; once per gust hit.</summary>
        public const float WindSlamPostureFraction = 0.3f;
        /// <summary>A wall/body bump this soon after a Wind hit counts as a slam.</summary>
        public const float WindSlamWindowSeconds = 0.5f;
        /// <summary>Knockback speed (m/s) that must still be left at the bump.</summary>
        public const float WindSlamMinSpeed = 2f;

        // Resistances
        public const float EliteBossDurationScale = 0.5f;
        public const float BossWindKnockbackScale = 0.5f;

        // Same-element finisher shockwave (player Sleights only)
        public const float FinisherRadius = 3.5f;
        /// <summary>Of the Sleight's total (unsplit) damage, rounded up.</summary>
        public const float FinisherDamageFraction = 0.3f;
        /// <summary>Scaled time after the Sleight fires, so hit-stop pauses it.</summary>
        public const float FinisherDelaySeconds = 0.35f;
        public const float PhysicalFinisherPostureFraction = 0.2f;

        /// <summary>
        /// How an attack of this element/shape travels. Physical keeps its shape (Strike/Throw/Arcana ->
        /// Strike/Projectile/Burst); every other element decides delivery on its own.
        /// </summary>
        public static Delivery DeliveryFor(Element element, CardShape shape)
        {
            switch (element)
            {
                case Element.Fire: return Delivery.Projectile;
                case Element.Ice: return Delivery.FrostBlast;
                case Element.Lightning: return Delivery.ChainArc;
                case Element.Wind: return Delivery.Gust;
                default:
                    switch (shape)
                    {
                        case CardShape.Throw: return Delivery.Projectile;
                        case CardShape.Arcana: return Delivery.Burst;
                        default: return Delivery.Strike;
                    }
            }
        }

        /// <summary>Total burn from a hit: ceil(hitDamage * 0.4), at least 1. 0 when the hit dealt no damage.</summary>
        public static int BurnTotal(int hitDamage) => CeilFraction(hitDamage, 4);

        /// <summary>Damage each chain link takes: ceil(hitDamage * 0.5), at least 1. 0 for no damage.</summary>
        public static int ChainDamage(int hitDamage) => CeilFraction(hitDamage, 5);

        /// <summary>Finisher shockwave damage: ceil(sleightTotal * 0.3), at least 1. 0 for no damage.</summary>
        public static int FinisherDamage(int sleightTotalDamage) => CeilFraction(sleightTotalDamage, 3);

        /// <summary>
        /// Status duration for an element: burn 3 s (Sleight 4 s), chill 2.5 s (Sleight 4 s), scaled by
        /// <paramref name="durationScale"/> (0.5 on boss/Elites). 0 for elements with no timed status.
        /// </summary>
        public static float StatusSeconds(Element element, bool isSleight, float durationScale)
        {
            float scale = durationScale > 0f ? durationScale : 0f;
            switch (element)
            {
                case Element.Fire: return (isSleight ? SleightBurnSeconds : BurnSeconds) * scale;
                case Element.Ice: return (isSleight ? SleightChillSeconds : ChillSeconds) * scale;
                default: return 0f;
            }
        }

        /// <summary>Knockback multiplier for an element: 2.5 for Wind, else 1.</summary>
        public static float KnockbackScale(Element element) =>
            element == Element.Wind ? WindKnockbackMultiplier : 1f;

        /// <summary>
        /// Picks up to <see cref="ChainTargets"/> links: each is the nearest unvisited candidate within
        /// <see cref="ChainHopRadius"/> of the previous link (the first hop from <paramref name="origin"/>). Distances
        /// are on the XZ plane; ties go to the lower index. Returns candidate indices in hop order.
        /// </summary>
        public static List<int> PickChain(Vector3 origin, IReadOnlyList<Vector3> candidates)
        {
            var links = new List<int>(ChainTargets);
            if (candidates == null || candidates.Count == 0) return links;
            var visited = new bool[candidates.Count];
            var from = origin;
            float maxSqr = ChainHopRadius * ChainHopRadius;
            while (links.Count < ChainTargets)
            {
                int best = -1;
                float bestSqr = float.MaxValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (visited[i]) continue;
                    float dx = candidates[i].x - from.x, dz = candidates[i].z - from.z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr <= maxSqr && sqr < bestSqr)
                    {
                        best = i;
                        bestSqr = sqr;
                    }
                }
                if (best < 0) break;
                visited[best] = true;
                links.Add(best);
                from = candidates[best];
            }
            return links;
        }

        /// <summary>ceil(amount * tenths / 10) in integer math, at least 1; 0 for non-positive amounts.</summary>
        private static int CeilFraction(int amount, int tenths)
        {
            if (amount <= 0) return 0;
            return Math.Max(1, (amount * tenths + 9) / 10);
        }
    }
}
