using System;
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
        [Header("Visuals")]
        [SerializeField] private bool showHitboxes = true;

        private readonly List<AttackHitbox> active = new List<AttackHitbox>();
        private readonly Dictionary<AttackHitbox, RuntimeCardInstance> sources = new Dictionary<AttackHitbox, RuntimeCardInstance>();

        public CombatantStatus Status { get; private set; }
        public int ActiveHitboxCount => active.Count;

        /// <summary>Raised when one of our cards loses a clash. Card is null for off-deck attacks (poke).</summary>
        public event Action<RuntimeCardInstance, bool> CardBroken;
        public event Action<AttackHitbox> HitboxSpawned;

        private void Awake()
        {
            Status = GetComponent<CombatantStatus>();
        }

        /// <summary>Spawns the hitbox for <paramref name="payload"/>, shaped by its <see cref="CardShape"/>.</summary>
        public AttackHitbox Spawn(in AttackPayload payload, Vector3 aimDirection, RuntimeCardInstance source = null)
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
                    hb.InitSphere(this, payload, throwRadius, throwLifetime, aimDirection * throwSpeed, showHitboxes);
                    break;
                case CardShape.Arcana:
                    go.transform.SetParent(transform, false);
                    go.transform.position = origin;
                    hb.InitSphere(this, payload, arcanaRadius, arcanaDuration, Vector3.zero, showHitboxes);
                    break;
                default:
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(origin + aimDirection * strikeReach, Quaternion.LookRotation(aimDirection));
                    hb.InitBox(this, payload, strikeSize, strikeDuration, showHitboxes);
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
            // Copy first: Cancel() calls back into Unregister.
            var snapshot = active.ToArray();
            foreach (var hb in snapshot)
                if (hb != null) hb.Cancel();
            active.Clear();
            sources.Clear();
        }

        /// <summary>Our hitbox lost a clash: cancel everything, take the stun, and report the broken card.</summary>
        internal void OnClashLost(AttackHitbox loser, bool brokeByZero)
        {
            sources.TryGetValue(loser, out var card);
            CancelActiveAction();
            Status.ApplyBreak();
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
