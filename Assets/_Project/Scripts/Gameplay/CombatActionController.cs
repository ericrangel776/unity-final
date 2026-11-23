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
        [Header("FrostBlast (Ice: short blast in front)")]
        [SerializeField] private float frostRadius = 1.4f;
        [SerializeField] private float frostReach = 1f;
        [SerializeField] private float frostDuration = 0.25f;
        [Tooltip("FrostBlast Sleight: grows around self from this radius to frostRadius x the end multiplier.")]
        [SerializeField] private float sleightFrostStartRadius = 0.5f;
        [SerializeField] private float sleightFrostEndMultiplier = 2.1f;
        [SerializeField] private float sleightFrostGrowSeconds = 0.3f;
        [Header("ChainArc (Lightning: fast mid-range bolt, despawns on its first body hit)")]
        [SerializeField] private float arcRadius = 0.3f;
        [SerializeField] private float arcSpeed = 28f;
        [SerializeField] private float arcLifetime = 0.25f;
        [SerializeField] private float sleightArcScale = 1.5f;
        [Header("Gust (Wind: forward box that hits every body inside)")]
        [SerializeField] private Vector3 gustSize = new Vector3(2.4f, 1.2f, 2.5f);
        [SerializeField] private float gustReach = 1.6f;
        [SerializeField] private float gustDuration = 0.2f;
        [SerializeField] private float sleightGustScale = 1.5f;
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
        /// <summary>Debug menu: overrides every controller's <c>showHitboxes</c> while set (null = each controller's own).</summary>
        public static bool? DebugShowHitboxes { get; set; }
        private bool ShowHitboxes => DebugShowHitboxes ?? showHitboxes;

        // Static state survives play sessions when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDebugStatics() => DebugShowHitboxes = null;

        private readonly List<AttackHitbox> active = new List<AttackHitbox>();
        private readonly Dictionary<AttackHitbox, RuntimeCardInstance> sources = new Dictionary<AttackHitbox, RuntimeCardInstance>();
        private readonly List<AttackHitbox> cancelScratch = new List<AttackHitbox>();
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
        /// <summary>Stamped onto every payload we spawn (<see cref="AttackPayload.DamageScale"/>). 1 = normal; the boss uses 0.5.</summary>
        public float OutgoingDamageScale { get; set; } = 1f;

        /// <summary>Raised when one of our cards loses a clash. Card is null for off-deck attacks (poke).</summary>
        public event Action<RuntimeCardInstance, bool> CardBroken;
        public event Action<AttackHitbox> HitboxSpawned;
        /// <summary>Any combatant spawned a hitbox (global feedback such as SFX).</summary>
        public static event Action<AttackHitbox> AnyHitboxSpawned;
        /// <summary>One of our attacks broke an enemy card: the loser, and whether we broke it with a 0.</summary>
        public event Action<CombatantStatus, bool> ClashWon;
        /// <summary>Any combatant fired a Sleight (the caster and its payload). Drives the finisher shockwave.</summary>
        public static event Action<CombatActionController, AttackPayload> SleightSpawned;

        private void Awake()
        {
            Status = GetComponent<CombatantStatus>();
        }

        /// <summary>Spawns the hitbox for <paramref name="payload"/>, shaped by its <see cref="Delivery"/>.</summary>
        public AttackHitbox Spawn(in AttackPayload payload, Vector3 aimDirection, RuntimeCardInstance source = null) =>
            SpawnScaled(payload, aimDirection, source, 1f);

        /// <summary>
        /// Fires a composed Sleight, each delivery with its own behavior. Every hit carries the full Sleight
        /// payload and clashes on its own. Strike: <c>HitCount</c> boxes spaced in time (pair with a motor lunge).
        /// Projectile: a piercing boomerang. Burst: an expanding blast. FrostBlast: a bigger blast growing around
        /// self. ChainArc and Gust: the single-card hitbox, scaled up.
        /// </summary>
        public void SpawnSleight(AttackPayload payload, Vector3 aimDirection)
        {
            switch (payload.Delivery)
            {
                case Delivery.Projectile:
                    SpawnScaled(payload, aimDirection, null, sleightThrowScale)
                        .MakeBoomerang(sleightThrowOutSeconds, transform, sleightThrowMaxLifetime);
                    break;
                case Delivery.Burst:
                    SpawnScaled(payload, aimDirection, null, 1f)
                        .MakeGrowing(sleightArcanaStartRadius, arcanaRadius * sleightArcanaEndMultiplier, sleightArcanaGrowSeconds);
                    break;
                case Delivery.FrostBlast:
                    SpawnScaled(payload, aimDirection, null, 1f, aroundSelf: true)
                        .MakeGrowing(sleightFrostStartRadius, frostRadius * sleightFrostEndMultiplier, sleightFrostGrowSeconds);
                    break;
                case Delivery.ChainArc:
                    SpawnScaled(payload, aimDirection, null, sleightArcScale);
                    break;
                case Delivery.Gust:
                    SpawnScaled(payload, aimDirection, null, sleightGustScale);
                    break;
                default:
                    if (pendingCombo != null) StopCoroutine(pendingCombo);
                    pendingCombo = StartCoroutine(StrikeCombo(payload, aimDirection));
                    break;
            }
            SleightSpawned?.Invoke(this, payload);
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

        private AttackHitbox SpawnScaled(in AttackPayload payload, Vector3 aimDirection, RuntimeCardInstance source, float scale,
            bool aroundSelf = false)
        {
            aimDirection = FlatDirection.Of(aimDirection, transform.forward);

            var stamped = OutgoingDamageScale != 1f && OutgoingDamageScale > 0f
                ? payload.WithDamageScale(OutgoingDamageScale) : payload;
#if UNITY_EDITOR
            var go = new GameObject($"Hitbox_{payload.Delivery}_{payload.Value}"); // readable in the Hierarchy
#else
            var go = new GameObject("Hitbox"); // no per-attack string in builds
#endif
            go.layer = gameObject.layer;
            var hb = go.AddComponent<AttackHitbox>();
            var origin = transform.position + Vector3.up * CombatantStatus.BodyCenterHeight;

            switch (payload.Delivery)
            {
                case Delivery.Projectile:
                    go.transform.SetPositionAndRotation(origin + aimDirection * 0.6f, Quaternion.LookRotation(aimDirection));
                    hb.InitSphere(this, stamped, throwRadius * scale, throwLifetime, bodyHitDelay, aimDirection * throwSpeed, ShowHitboxes);
                    break;
                case Delivery.FrostBlast:
                    go.transform.SetParent(transform, false);
                    go.transform.position = aroundSelf ? origin : origin + aimDirection * frostReach;
                    hb.InitSphere(this, stamped, frostRadius * scale, frostDuration, bodyHitDelay, Vector3.zero, ShowHitboxes);
                    break;
                case Delivery.ChainArc:
                    go.transform.SetPositionAndRotation(origin + aimDirection * 0.6f, Quaternion.LookRotation(aimDirection));
                    hb.InitSphere(this, stamped, arcRadius * scale, arcLifetime, bodyHitDelay, aimDirection * arcSpeed, ShowHitboxes);
                    hb.DespawnOnBodyHit = true;
                    break;
                case Delivery.Gust:
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(origin + aimDirection * gustReach, Quaternion.LookRotation(aimDirection));
                    hb.InitBox(this, stamped, gustSize * scale, gustDuration, bodyHitDelay, ShowHitboxes);
                    break;
                case Delivery.Burst:
                    go.transform.SetParent(transform, false);
                    go.transform.position = origin;
                    hb.InitSphere(this, stamped, arcanaRadius * scale, arcanaDuration, bodyHitDelay, Vector3.zero, ShowHitboxes);
                    break;
                default:
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(origin + aimDirection * strikeReach, Quaternion.LookRotation(aimDirection));
                    hb.InitBox(this, stamped, strikeSize * scale, strikeDuration, bodyHitDelay, ShowHitboxes);
                    break;
            }

            active.Add(hb);
            if (source != null) sources[hb] = source;
            HitboxSpawned?.Invoke(hb);
            AnyHitboxSpawned?.Invoke(hb);
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
            // Copy first: Cancel() calls back into Unregister. Reuses one scratch list (runs on every Card Break).
            cancelScratch.Clear();
            cancelScratch.AddRange(active);
            foreach (var hb in cancelScratch)
                if (hb != null) hb.Cancel();
            cancelScratch.Clear();
            active.Clear();
            sources.Clear();
        }

        /// <summary>
        /// Our hitbox lost a clash: cancel everything, take a break scaled by <paramref name="margin"/>
        /// (shoved along <paramref name="awayFromWinner"/>), and report the broken card.
        /// </summary>
        internal void OnClashLost(AttackHitbox loser, bool brokeByZero, int margin, Vector3 awayFromWinner,
            float postureScale = 1f)
        {
            sources.TryGetValue(loser, out var card);
            CancelActiveAction();
            // The player's stun shrinks as more enemies are alive, so crowds can't stun-lock.
            float stunScale = Status.Team == Team.Player ? CombatMath.CrowdStunScale(TrainingDummyEnemy.LiveCount) : 1f;
            Status.ApplyBreak(margin, awayFromWinner, stunScale, postureScale);
            CardBroken?.Invoke(card, brokeByZero);
        }

        internal void OnClashWon(CombatantStatus loser, bool brokeByZero) => ClashWon?.Invoke(loser, brokeByZero);

        internal void Unregister(AttackHitbox hb)
        {
            active.Remove(hb);
            sources.Remove(hb);
        }

        private void OnDisable() => CancelActiveAction();
    }
}
