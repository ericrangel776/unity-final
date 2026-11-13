using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// A trigger volume carrying an immutable <see cref="AttackPayload"/> snapshot. Touching an
    /// opposing hitbox starts a local clash; touching an opposing body deals damage once.
    /// </summary>
    public sealed class AttackHitbox : MonoBehaviour
    {
        private readonly HashSet<CombatantStatus> alreadyHit = new HashSet<CombatantStatus>();
        private Rigidbody body;
        private Collider trigger;
        private Vector3 velocity;
        private float lifetime;
        private bool initialized;
        private bool cancelled;

        public AttackPayload Payload { get; private set; }
        public CombatActionController Owner { get; private set; }
        public bool IsLive => initialized && !cancelled;

        internal void InitBox(CombatActionController owner, in AttackPayload payload, Vector3 size, float duration, bool visible)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
            Setup(owner, payload, box, duration, Vector3.zero);
            if (visible) AddVisual(PrimitiveType.Cube, size);
        }

        internal void InitSphere(CombatActionController owner, in AttackPayload payload, float radius, float duration,
            Vector3 moveVelocity, bool visible)
        {
            var sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            Setup(owner, payload, sphere, duration, moveVelocity);
            if (visible) AddVisual(PrimitiveType.Sphere, Vector3.one * radius * 2f);
        }

        private void Setup(CombatActionController owner, in AttackPayload payload, Collider col, float duration, Vector3 moveVelocity)
        {
            Owner = owner;
            Payload = payload;
            trigger = col;
            trigger.isTrigger = true;
            lifetime = duration;
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
            if (velocity != Vector3.zero) body.MovePosition(body.position + velocity * Time.fixedDeltaTime);
            lifetime -= Time.fixedDeltaTime;
            if (lifetime <= 0f) Cancel();
        }

        private void OnTriggerEnter(Collider other) => HandleContact(other);

        private void HandleContact(Collider other)
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

            var status = other.GetComponentInParent<CombatantStatus>();
            if (status == null || status.Team == Payload.Team || !alreadyHit.Add(status)) return;
            status.TakeHit(Payload);
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
