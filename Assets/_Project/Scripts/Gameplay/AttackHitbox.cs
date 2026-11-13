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
        // Combatants whose card this hitbox broke. Its own follow-through on them isn't a counter.
        private readonly HashSet<CombatantStatus> brokeInClash = new HashSet<CombatantStatus>();
        private Rigidbody body;
        private Collider trigger;
        private Vector3 velocity;
        private float lifetime;
        private float bodyHitDelay;
        private bool initialized;
        private bool cancelled;
        private Transform visual;
        // Boomerang (Throw Sleight): fly out, then home back to returnTo; each pass may hit a body once.
        private Transform returnTo;
        private float outRemaining;
        private bool returning;
        // Growing blast (Arcana Sleight): sphere radius lerps fromRadius -> toRadius over growSeconds.
        private SphereCollider growSphere;
        private float growFrom, growTo, growSeconds, growElapsed;

        private const float ReturnCatchDistance = 0.6f;

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

            if (growSphere != null) Grow(Time.fixedDeltaTime);
            if (returnTo != null && !SteerBoomerang(Time.fixedDeltaTime)) return;

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
                bool canCounter = !brokeInClash.Contains(status);
                if (status.TakeHit(Payload, status.transform.position - source, canCounter)) alreadyHit.Add(status);
            }
        }

        /// <summary>
        /// Throw Sleight: after <paramref name="outSeconds"/> the projectile turns and homes back to
        /// <paramref name="target"/> at the same speed, forgetting who it hit (second pass), and despawns on arrival.
        /// </summary>
        internal void MakeBoomerang(float outSeconds, Transform target, float safetyLifetime)
        {
            returnTo = target;
            outRemaining = Mathf.Max(0f, outSeconds);
            returning = false;
            lifetime = Mathf.Max(lifetime, safetyLifetime);
        }

        /// <summary>Arcana Sleight: the sphere grows from <paramref name="fromRadius"/> to <paramref name="toRadius"/>.</summary>
        internal void MakeGrowing(float fromRadius, float toRadius, float seconds)
        {
            growSphere = trigger as SphereCollider;
            if (growSphere == null) return;
            growFrom = Mathf.Max(0.01f, fromRadius);
            growTo = Mathf.Max(growFrom, toRadius);
            growSeconds = Mathf.Max(0.01f, seconds);
            growElapsed = 0f;
            lifetime = Mathf.Max(lifetime, bodyHitDelay + growSeconds); // live long enough to reach full size
            SetRadius(growFrom);
        }

        public bool IsReturning => returning;
        public float Radius => trigger is SphereCollider s ? s.radius : 0f;

        private void Grow(float dt)
        {
            growElapsed += dt;
            SetRadius(Mathf.Lerp(growFrom, growTo, Mathf.Clamp01(growElapsed / growSeconds)));
        }

        private void SetRadius(float radius)
        {
            growSphere.radius = radius;
            if (visual != null) visual.localScale = Vector3.one * radius * 2f;
        }

        /// <summary>Returns false if the boomerang was caught (and cancelled) this step.</summary>
        private bool SteerBoomerang(float dt)
        {
            if (!returning)
            {
                outRemaining -= dt;
                if (outRemaining > 0f) return true;
                returning = true;
                alreadyHit.Clear(); // second pass may hit again
            }

            var target = returnTo.position + Vector3.up * 0.5f;
            var toTarget = target - body.position;
            if (toTarget.magnitude <= ReturnCatchDistance)
            {
                Cancel();
                return false;
            }
            velocity = toTarget.normalized * velocity.magnitude;
            return true;
        }

        /// <summary>This hitbox won a clash against <paramref name="loser"/>'s card.</summary>
        internal void MarkBroke(CombatantStatus loser)
        {
            if (loser != null) brokeInClash.Add(loser);
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
            this.visual = visual.transform;
        }
    }
}
