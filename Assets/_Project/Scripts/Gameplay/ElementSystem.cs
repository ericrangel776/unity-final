using System;
using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Applies element statuses on landed hits and fires the same-element finisher shockwave (one per scene;
    /// docs/specs/element-mechanics.md). Fire burns, Ice chills, Lightning chains to up to 3 of the victim's
    /// teammates. Physical (break posture) and Wind (knockback, slam) live in the clash and knockback code.
    /// Effect damage never applies a status or chains again. Also draws the chain line and the finisher ring.
    /// </summary>
    public sealed class ElementSystem : MonoBehaviour
    {
        public const float ChainLineSeconds = 0.12f;
        public const float FinisherRingSeconds = 0.25f;

        private readonly List<CombatantStatus> candidates = new List<CombatantStatus>();
        private readonly List<Vector3> candidatePositions = new List<Vector3>();
        private readonly List<Vector3> chainPoints = new List<Vector3>();
        private LineRenderer chainLine;
        private LineRenderer finisherRing;
        private float chainLineRemaining;
        private float ringElapsed = -1f;
        private Vector3 ringCenter;

        /// <summary>A chain fired: the victim's position, then each link in hop order (2+ points).</summary>
        public static event Action<Vector3[]> ChainFired;
        /// <summary>A player same-element Sleight's shockwave went off at the caster's position.</summary>
        public static event Action<Vector3, Element> FinisherFired;

        public bool IsChainLineVisible => chainLine != null && chainLine.enabled;
        public bool IsFinisherRingVisible => finisherRing != null && finisherRing.enabled;

        private void Awake()
        {
            chainLine = FxShapes.CreateLine(transform, "ChainLine", 2, 0.08f);
            chainLine.enabled = false;
            finisherRing = FxShapes.CreateLine(transform, "FinisherRing", 48, 0.12f);
            finisherRing.enabled = false;
        }

        private void OnEnable()
        {
            CombatantStatus.AnyHitLanded += HandleHit;
            CombatActionController.SleightSpawned += HandleSleight;
        }

        private void OnDisable()
        {
            CombatantStatus.AnyHitLanded -= HandleHit;
            CombatActionController.SleightSpawned -= HandleSleight;
            StopAllCoroutines();
        }

        private void HandleHit(HitInfo hit)
        {
            var victim = hit.Victim;
            if (hit.IsEffect || victim == null) return;
            var payload = hit.Payload;
            if (payload.IsPoke || payload.IsZeroBreaker) return;

            switch (payload.Element)
            {
                case Element.Fire:
                    if (!victim.IsDead) EffectsOf(victim)?.ApplyBurn(ElementRules.BurnTotal(hit.Damage), payload.IsSleight, payload);
                    break;
                case Element.Ice:
                    if (!victim.IsDead) EffectsOf(victim)?.ApplyChill(payload.IsSleight);
                    break;
                case Element.Lightning:
                    // Chains even from a victim that just died: the links start at its position.
                    Chain(victim, ElementRules.ChainDamage(hit.Damage), payload);
                    break;
            }
        }

        /// <summary>
        /// Jumps from <paramref name="from"/> to up to 3 of its living teammates (nearest first, 4 u per hop), dealing
        /// <paramref name="damage"/> effect damage to each. Returns how many links landed.
        /// </summary>
        public int Chain(CombatantStatus from, int damage, in AttackPayload source)
        {
            if (from == null || damage <= 0) return 0;
            candidates.Clear();
            candidatePositions.Clear();
            foreach (var c in CombatantStatus.All)
            {
                if (c == null || c == from || c.IsDead || c.Team != from.Team) continue;
                candidates.Add(c);
                candidatePositions.Add(c.transform.position);
            }
            var origin = from.transform.position;
            var links = ElementRules.PickChain(origin, candidatePositions);
            if (links.Count == 0) return 0;

            chainPoints.Clear();
            chainPoints.Add(origin + Vector3.up * CombatantStatus.HitFeedbackHeight);
            foreach (int i in links)
            {
                var target = candidates[i];
                chainPoints.Add(candidatePositions[i] + Vector3.up * CombatantStatus.HitFeedbackHeight);
                if (target != null) target.TakeEffectDamage(damage, Element.Lightning, source);
            }
            ShowChain(chainPoints);
            ChainFired?.Invoke(chainPoints.ToArray());
            return links.Count;
        }

        private void HandleSleight(CombatActionController caster, AttackPayload payload)
        {
            if (!payload.IsFinisherSource || payload.Team != Team.Player || caster == null) return;
            StartCoroutine(Finisher(caster, payload));
        }

        private IEnumerator Finisher(CombatActionController caster, AttackPayload payload)
        {
            // Scaled time on purpose: hit-stop pauses the shockwave along with everything else.
            yield return new WaitForSeconds(ElementRules.FinisherDelaySeconds);
            if (caster == null) yield break;
            FireFinisher(caster.transform.position, payload);
        }

        /// <summary>
        /// The shockwave: every living opponent within <see cref="ElementRules.FinisherRadius"/> (XZ) of
        /// <paramref name="center"/> takes 30% of the Sleight's total damage as effect damage, plus the element's
        /// Sleight-strength status.
        /// </summary>
        internal void FireFinisher(Vector3 center, in AttackPayload payload)
        {
            int damage = ElementRules.FinisherDamage(CombatMath.TotalDamage(payload));
            float maxSqr = ElementRules.FinisherRadius * ElementRules.FinisherRadius;
            candidates.Clear();
            foreach (var c in CombatantStatus.All)
            {
                if (c == null || c.IsDead || c.Team == payload.Team) continue;
                var d = c.transform.position - center;
                if (d.x * d.x + d.z * d.z <= maxSqr) candidates.Add(c);
            }
            // Copy: chains below reuse the candidate list.
            var targets = candidates.ToArray();
            foreach (var target in targets)
            {
                if (target == null || !target.TakeEffectDamage(damage, payload.Element, payload) || target.IsDead) continue;
                ApplyFinisherStatus(target, center, damage, payload);
            }
            ShowFinisherRing(center, payload.Element);
            FinisherFired?.Invoke(center, payload.Element);
        }

        private void ApplyFinisherStatus(CombatantStatus target, Vector3 center, int damage, in AttackPayload payload)
        {
            switch (payload.Element)
            {
                case Element.Fire:
                    EffectsOf(target)?.ApplyBurn(ElementRules.BurnTotal(damage), true, payload);
                    break;
                case Element.Ice:
                    EffectsOf(target)?.ApplyChill(true);
                    break;
                case Element.Lightning:
                    Chain(target, ElementRules.ChainDamage(damage), payload);
                    break;
                case Element.Wind:
                    target.ApplyKnockback(payload, target.transform.position - center);
                    break;
                default:
                    target.AddPostureDamage(ElementRules.PhysicalFinisherPostureFraction * target.MaxPosture);
                    break;
            }
        }

        private static ElementStatusEffects EffectsOf(CombatantStatus status) =>
            status != null ? status.GetComponent<ElementStatusEffects>() : null;

        private void ShowChain(List<Vector3> points)
        {
            chainLine.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) chainLine.SetPosition(i, points[i]);
            FxShapes.SetColor(chainLine, ClashImpactFx.Opaque(ElementColors.For(Element.Lightning)));
            chainLine.enabled = true;
            chainLineRemaining = ChainLineSeconds;
        }

        private void ShowFinisherRing(Vector3 center, Element element)
        {
            ringCenter = center + Vector3.up * 0.1f;
            ringElapsed = 0f;
            FxShapes.SetColor(finisherRing, ClashImpactFx.Opaque(ElementColors.For(element)));
            FxShapes.SetArc(finisherRing, ringCenter, 0.3f, 1f);
            finisherRing.enabled = true;
        }

        private void LateUpdate()
        {
            // Unscaled: the FX keep playing through the hit-stop freeze.
            float dt = Time.unscaledDeltaTime;
            if (chainLineRemaining > 0f)
            {
                chainLineRemaining -= dt;
                if (chainLineRemaining <= 0f) chainLine.enabled = false;
            }
            if (ringElapsed >= 0f)
            {
                ringElapsed += dt;
                float t = Mathf.Clamp01(ringElapsed / FinisherRingSeconds);
                FxShapes.SetArc(finisherRing, ringCenter, Mathf.Lerp(0.3f, ElementRules.FinisherRadius, t), 1f);
                if (t >= 1f)
                {
                    ringElapsed = -1f;
                    finisherRing.enabled = false;
                }
            }
        }
    }
}
