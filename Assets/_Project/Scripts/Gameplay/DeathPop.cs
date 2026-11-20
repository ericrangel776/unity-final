using Unity.Cinemachine;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Enemy death juice (docs/specs/hit-juice.md): a particle burst in the killing hit's element colour, a small camera
    /// kick, and the body squashing to nothing. Only the visual children are scaled, never the root (it carries the
    /// <see cref="CharacterController"/>). The root is still destroyed on the enemy's own timer. The burst is detached on
    /// death so it keeps playing after that. Everything runs on unscaled time so it plays through the hit-stop.
    /// </summary>
    [RequireComponent(typeof(CombatantStatus))]
    public sealed class DeathPop : MonoBehaviour
    {
        public const int BaseBurstCount = 24;
        public const float BaseShake = 0.25f;
        public const float SquashSeconds = 0.15f;
        public const float EliteScale = 1.6f;
        public const float BossScale = 2.5f;
        private const float BurstLifetimeSeconds = 1.5f;

        [Tooltip("Additive particle material. A shared runtime one is created if left empty.")]
        [SerializeField] private Material particleMaterial;

        private CombatantStatus status;
        private Transform[] visuals;
        private Renderer[] visualRenderers;
        private Vector3[] restScales;
        private ParticleSystem burst;
        private CinemachineImpulseSource impulse;
        private float squashElapsed = -1f;

        /// <summary>Size of the pop: 1 for mobs, <see cref="EliteScale"/> for Elites, <see cref="BossScale"/> for the boss.</summary>
        public float Scale { get; set; } = 1f;
        public bool IsPopped { get; private set; }
        public int LastBurstCount { get; private set; }
        public Color LastBurstColor { get; private set; }
        public Material ParticleMaterial { get => particleMaterial; set => particleMaterial = value; }

        private void Awake()
        {
            status = GetComponent<CombatantStatus>();
            CacheVisuals();
            impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            var def = impulse.ImpulseDefinition;
            def.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            def.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            def.ImpulseDuration = 0.25f;
            BuildBurst();
        }

        private void OnEnable()
        {
            status.Died += HandleDied;
            status.Revived += HandleRevived;
        }

        private void OnDisable()
        {
            status.Died -= HandleDied;
            status.Revived -= HandleRevived;
        }

        /// <summary>The direct children that draw the body (captured before any hitbox or FX child exists).</summary>
        private void CacheVisuals()
        {
            int n = 0;
            var found = new Transform[transform.childCount];
            var renderers = new Renderer[transform.childCount];
            foreach (Transform child in transform)
            {
                var r = child.GetComponent<Renderer>();
                if (r == null) continue;
                found[n] = child;
                renderers[n] = r;
                n++;
            }
            visuals = new Transform[n];
            visualRenderers = new Renderer[n];
            restScales = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                visuals[i] = found[i];
                visualRenderers[i] = renderers[i];
                restScales[i] = found[i].localScale;
            }
        }

        private void BuildBurst()
        {
            var material = particleMaterial != null ? particleMaterial : ClashImpactFx.SharedParticleMaterial;
            burst = ClashImpactFx.CreateSystem(transform, "DeathPopFx", material, maxParticles: 128);
            var main = burst.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.gravityModifier = 0.6f;
            var shape = burst.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            // Round puffs, not the clash streaks: a death should read as the body bursting apart.
            burst.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
        }

        private void HandleDied()
        {
            if (IsPopped) return;
            IsPopped = true;

            var position = transform.position + Vector3.up * CombatantStatus.HitFeedbackHeight;
            int count = Mathf.RoundToInt(BaseBurstCount * Mathf.Max(0f, Scale));
            var color = ClashImpactFx.Opaque(ElementColors.For(status.LastHitElement));
            if (burst != null)
            {
                ClashImpactFx.Emit(burst, position, color, count);
                // Detach so the burst outlives the enemy, which is destroyed on its own timer.
                burst.transform.SetParent(null, true);
                Destroy(burst.gameObject, BurstLifetimeSeconds);
                burst = null;
            }
            LastBurstCount = count;
            LastBurstColor = color;
            impulse.GenerateImpulseAtPositionWithVelocity(position, Random.onUnitSphere * (BaseShake * Scale));
            squashElapsed = 0f;
        }

        private void HandleRevived()
        {
            squashElapsed = -1f;
            IsPopped = false;
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] != null) visuals[i].localScale = restScales[i];
                if (visualRenderers[i] != null) visualRenderers[i].enabled = true;
            }
            if (burst == null) BuildBurst();
        }

        private void LateUpdate()
        {
            if (squashElapsed < 0f) return;
            squashElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(squashElapsed / SquashSeconds);
            // Ease-in collapse, with a brief upward stretch before it flattens.
            float shrink = 1f - t * t;
            float stretchY = 1f + 0.4f * Mathf.Sin(t * Mathf.PI) * (1f - t);
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] == null) continue;
                var s = restScales[i];
                visuals[i].localScale = new Vector3(s.x * shrink, s.y * shrink * stretchY, s.z * shrink);
            }
            if (t < 1f) return;
            foreach (var r in visualRenderers) if (r != null) r.enabled = false;
            squashElapsed = -1f;
        }
    }
}
