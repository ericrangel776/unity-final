using System;
using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Spawns and tracks every live hitbox for one combatant so a Card Break can shut them all
    /// down at once (no orphaned colliders left behind by an interrupted animation).
    /// </summary>
    [RequireComponent(typeof(CombatantStatus))]
    public sealed class CombatActionController : MonoBehaviour
    {
        [Tooltip("Seconds after a card activates before its hitbox can damage bodies. It can still clash " +
                 "during this windup, which is the window for answering it with a 0-card.")]
        [SerializeField] private float bodyHitDelay;
        [Header("Strike (melee box in front)")]
        [SerializeField] private float strikeReach = 1.1f;
        [SerializeField] private Vector3 strikeSize = new Vector3(1.6f, 1.2f, 1.2f);
        [SerializeField] private float strikeDuration = 0.3f;
        [Header("Throw (projectile)")]
        [SerializeField] private float throwSpeed = 12f;
        [SerializeField] private float throwRadius = 0.35f;
        [SerializeField] private float throwLifetime = 1.2f;
        [Header("Arcana (burst around self)")]
        [SerializeField] private float arcanaRadius = 2.2f;
        [SerializeField] private float arcanaDuration = 0.3f;
        [Header("Sleights")]
        [Tooltip("Strike Sleight: seconds between the hits of the lunge combo.")]
        [SerializeField] private float sleightStrikeInterval = 0.12f;
        [SerializeField] private float sleightStrikeScale = 1.3f;
        [Tooltip("Throw Sleight: boomerang size multiplier, outbound time and safety lifetime.")]
        [SerializeField] private float sleightThrowScale = 1.5f;
        [SerializeField] private float sleightThrowOutSeconds = 0.45f;
        [SerializeField] private float sleightThrowMaxLifetime = 2f;
        [Tooltip("Arcana Sleight: blast grows from this radius to arcanaRadius x the end multiplier.")]
        [SerializeField] private float sleightArcanaStartRadius = 0.5f;
        [SerializeField] private float sleightArcanaEndMultiplier = 2f;
        [SerializeField] private float sleightArcanaGrowSeconds = 0.35f;
        [Header("Visuals")]
        [SerializeField] private bool showHitboxes = true;

        private readonly List<AttackHitbox> active = new List<AttackHitbox>();
        private readonly Dictionary<AttackHitbox, RuntimeCardInstance> sources = new Dictionary<AttackHitbox, RuntimeCardInstance>();
        private Coroutine pendingCombo;

        public CombatantStatus Status { get; private set; }
        public int ActiveHitboxCount => active.Count;

        /// <summary>A Sleight combo is still spawning hits, or a Sleight hitbox is alive.</summary>
        public bool IsSleightActive
        {
            get
            {
                if (pendingCombo != null) return true;
                foreach (var hb in active) if (hb != null && hb.Payload.IsSleight) return true;
                return false;
            }
        }
        public float BodyHitDelay { get => bodyHitDelay; set => bodyHitDelay = Mathf.Max(0f, value); }

        /// <summary>Raised when one of our cards loses a clash. Card is null for off-deck attacks (poke).</summary>
        public event Action<RuntimeCardInstance, bool> CardBroken;
        public event Action<AttackHitbox> HitboxSpawned;

        private void Awake()
        {
            Status = GetComponent<CombatantStatus>();
        }

        /// <summary>Spawns the hitbox for <paramref name="payload"/>, shaped by its <see cref="CardShape"/>.</summary>
        public AttackHitbox Spawn(in AttackPayload payload, Vector3 aimDirection, RuntimeCardInstance source = null) =>
            SpawnScaled(payload, aimDirection, source, 1f);

        /// <summary>
        /// Fires a composed Sleight, each shape with its own behavior. Every hit carries the full Sleight
        /// payload and clashes on its own. Strike: <c>HitCount</c> boxes spaced in time (pair with a motor lunge).
        /// Throw: a piercing boomerang. Arcana: an expanding blast.
        /// </summary>
        public void SpawnSleight(AttackPayload payload, Vector3 aimDirection)
        {
            switch (payload.Shape)
            {
                case CardShape.Throw:
                    SpawnScaled(payload, aimDirection, null, sleightThrowScale)
                        .MakeBoomerang(sleightThrowOutSeconds, transform, sleightThrowMaxLifetime);
                    break;
                case CardShape.Arcana:
                    SpawnScaled(payload, aimDirection, null, 1f)
                        .MakeGrowing(sleightArcanaStartRadius, arcanaRadius * sleightArcanaEndMultiplier, sleightArcanaGrowSeconds);
                    break;
                default:
                    if (pendingCombo != null) StopCoroutine(pendingCombo);
                    pendingCombo = StartCoroutine(StrikeCombo(payload, aimDirection));
                    break;
            }
        }

        private IEnumerator StrikeCombo(AttackPayload payload, Vector3 aimDirection)
        {
            // Scaled time on purpose: hit-stop freezes the combo along with everything else.
            var wait = new WaitForSeconds(sleightStrikeInterval);
            for (int i = 0; i < payload.HitCount; i++)
            {
                if (i > 0) yield return wait;
                SpawnScaled(payload, aimDirection, null, sleightStrikeScale);
            }
            pendingCombo = null;
        }

        private AttackHitbox SpawnScaled(in AttackPayload payload, Vector3 aimDirection, RuntimeCardInstance source, float scale)
        {
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude < 0.0001f) aimDirection = transform.forward;
            aimDirection.Normalize();

            var go = new GameObject($"Hitbox_{payload.Shape}_{payload.Value}");
            go.layer = gameObject.layer;
            var hb = go.AddComponent<AttackHitbox>();
            var origin = transform.position + Vector3.up * 0.5f;

            switch (payload.Shape)
            {
                case CardShape.Throw:
                    go.transform.SetPositionAndRotation(origin + aimDirection * 0.6f, Quaternion.LookRotation(aimDirection));
                    hb.InitSphere(this, payload, throwRadius * scale, throwLifetime, bodyHitDelay, aimDirection * throwSpeed, showHitboxes);
                    break;
                case CardShape.Arcana:
                    go.transform.SetParent(transform, false);
                    go.transform.position = origin;
                    hb.InitSphere(this, payload, arcanaRadius * scale, arcanaDuration, bodyHitDelay, Vector3.zero, showHitboxes);
                    break;
                default:
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(origin + aimDirection * strikeReach, Quaternion.LookRotation(aimDirection));
                    hb.InitBox(this, payload, strikeSize * scale, strikeDuration, bodyHitDelay, showHitboxes);
                    break;
            }

            active.Add(hb);
            if (source != null) sources[hb] = source;
            HitboxSpawned?.Invoke(hb);
            return hb;
        }

        /// <summary>Disables and destroys every live hitbox we own. Called on Card Break.</summary>
        public void CancelActiveAction()
        {
            if (pendingCombo != null)
            {
                StopCoroutine(pendingCombo);
                pendingCombo = null;
            }
            // Copy first: Cancel() calls back into Unregister.
            var snapshot = active.ToArray();
            foreach (var hb in snapshot)
                if (hb != null) hb.Cancel();
            active.Clear();
            sources.Clear();
        }

        /// <summary>
        /// Our hitbox lost a clash: cancel everything, take a break scaled by <paramref name="margin"/>
        /// (shoved along <paramref name="awayFromWinner"/>), and report the broken card.
        /// </summary>
        internal void OnClashLost(AttackHitbox loser, bool brokeByZero, int margin, Vector3 awayFromWinner)
        {
            sources.TryGetValue(loser, out var card);
            CancelActiveAction();
            Status.ApplyBreak(margin, awayFromWinner);
            CardBroken?.Invoke(card, brokeByZero);
        }

        internal void Unregister(AttackHitbox hb)
        {
            active.Remove(hb);
            sources.Remove(hb);
        }

        private void OnDisable() => CancelActiveAction();
    }
}
