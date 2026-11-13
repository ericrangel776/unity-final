using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// A trigger volume carrying an immutable <see cref="AttackPayload"/> snapshot. Touching an
    /// opposing hitbox starts a local clash immediately; touching an opposing body deals damage once,
    /// but only from <see cref="FixedUpdate"/> after the windup (<c>bodyHitDelay</c>) has passed.
    /// Deferring body hits guarantees clashes resolve first (trigger callback order is arbitrary)
    /// and gives a window in which a 0-card played second can still cancel the attack.
    /// </summary>
    public sealed class AttackHitbox : MonoBehaviour
    {
        private readonly HashSet<CombatantStatus> alreadyHit = new HashSet<CombatantStatus>();
        private readonly Dictionary<CombatantStatus, int> touching = new Dictionary<CombatantStatus, int>();
        private readonly List<CombatantStatus> hitScratch = new List<CombatantStatus>();
        private Rigidbody body;
        private Collider trigger;
        private Vector3 velocity;
        private float lifetime;
        private float bodyHitDelay;
        private bool initialized;
        private bool cancelled;

        public AttackPayload Payload { get; private set; }
        public CombatActionController Owner { get; private set; }
        public bool IsLive => initialized && !cancelled;
        /// <summary>True once the windup has passed and body contacts deal damage.</summary>
        public bool CanHitBodies => IsLive && bodyHitDelay <= 0f;

        internal void InitBox(CombatActionController owner, in AttackPayload payload, Vector3 size, float duration,
            float windup, bool visible)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
            Setup(owner, payload, box, duration, windup, Vector3.zero);
            if (visible) AddVisual(PrimitiveType.Cube, size);
        }

        internal void InitSphere(CombatActionController owner, in AttackPayload payload, float radius, float duration,
            float windup, Vector3 moveVelocity, bool visible)
        {
            var sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            Setup(owner, payload, sphere, duration, windup, moveVelocity);
            if (visible) AddVisual(PrimitiveType.Sphere, Vector3.one * radius * 2f);
        }

        private void Setup(CombatActionController owner, in AttackPayload payload, Collider col, float duration,
            float windup, Vector3 moveVelocity)
        {
            Owner = owner;
            Payload = payload;
            trigger = col;
            trigger.isTrigger = true;
            bodyHitDelay = Mathf.Max(0f, windup);
            lifetime = bodyHitDelay + duration; // active time starts after the windup
            velocity = moveVelocity;

            // Kinematic body so trigger events fire against static and CharacterController colliders.
            body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            initialized = true;
        }

        private void FixedUpdate()
        {
            if (!IsLive) return;

            // Runs before this step's simulation, i.e. after every trigger callback of the previous
            // step, so any clash that cancelled us has already happened.
            if (bodyHitDelay > 0f) bodyHitDelay -= Time.fixedDeltaTime;
            if (bodyHitDelay <= 0f) ApplyBodyHits();
            if (!IsLive) return;

            if (velocity != Vector3.zero) body.MovePosition(body.position + velocity * Time.fixedDeltaTime);
            lifetime -= Time.fixedDeltaTime;
            if (lifetime <= 0f) Cancel();
        }

        private void ApplyBodyHits()
        {
            if (touching.Count == 0) return;
            hitScratch.Clear();
            foreach (var pair in touching)
                if (pair.Key != null && !alreadyHit.Contains(pair.Key)) hitScratch.Add(pair.Key);

            // Push away from the attacker, not the hitbox, so Arcana bursts and projectiles push sensibly.
            var source = Owner != null ? Owner.transform.position : transform.position;
            foreach (var status in hitScratch)
            {
                // Hits during i-frames are not consumed, so dashing through only protects while dashing.
                if (status.TakeHit(Payload, status.transform.position - source)) alreadyHit.Add(status);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsLive) return;

            var otherHitbox = other.GetComponent<AttackHitbox>();
            if (otherHitbox != null)
            {
                // Both hitboxes get the callback; only the one with the lower id resolves the pair.
                if (otherHitbox.IsLive && GetInstanceID() < otherHitbox.GetInstanceID())
                    ClashSystem.Resolve(this, otherHitbox);
                return;
            }

            var status = OpposingBody(other);
            if (status != null) touching[status] = touching.TryGetValue(status, out int n) ? n + 1 : 1;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponent<AttackHitbox>() != null) return;
            var status = OpposingBody(other);
            if (status == null || !touching.TryGetValue(status, out int n)) return;
            if (n <= 1) touching.Remove(status);
            else touching[status] = n - 1;
        }

        private CombatantStatus OpposingBody(Collider other)
        {
            var status = other.GetComponentInParent<CombatantStatus>();
            return status != null && status.Team != Payload.Team ? status : null;
        }

        /// <summary>Immediately disables the collider and destroys the hitbox.</summary>
        public void Cancel()
        {
            if (cancelled) return;
            cancelled = true;
            if (trigger != null) trigger.enabled = false;
            if (Owner != null) Owner.Unregister(this);
            Destroy(gameObject);
        }

        private void AddVisual(PrimitiveType type, Vector3 scale)
        {
            var visual = GameObject.CreatePrimitive(type);
            // Immediate: a deferred Destroy would leave a solid collider on our rigidbody for a frame.
            DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "Visual";
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = scale;
            var r = visual.GetComponent<Renderer>();
            r.material.color = ElementColors.For(Payload);
        }
    }
}
