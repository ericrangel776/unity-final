using DeckArpg.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// World-space clash juice: camera shake, a spark burst, a shockwave ring of streaks, and a
    /// chromatic-aberration/vignette pulse. Everything runs on unscaled time so it keeps moving while
    /// <see cref="HitStop"/> has the world frozen - that contrast is what sells the impact.
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public sealed class ClashImpactFx : MonoBehaviour
    {
        /// <summary>Per-outcome intensity. Index 0 = neutral, 1 = card break, 2 = zero break.</summary>
        private struct Tier
        {
            public float Shake;
            public int Sparks;
            public int RingStreaks;
            public float Pulse;
        }

        private static readonly Tier[] Tiers =
        {
            new Tier { Shake = 0.15f, Sparks = 12, RingStreaks = 0, Pulse = 0f },
            new Tier { Shake = 0.45f, Sparks = 30, RingStreaks = 24, Pulse = 0.6f },
            new Tier { Shake = 0.8f, Sparks = 45, RingStreaks = 40, Pulse = 1f },
        };

        private static readonly Color ZeroBreakColor = new Color(0.75f, 0.9f, 1f, 1f);

        [Tooltip("Additive particle material. Created at runtime if left empty.")]
        [SerializeField] private Material particleMaterial;
        [SerializeField] private float pulseDecaySeconds = 0.35f;

        private CinemachineImpulseSource impulse;
        private ParticleSystem sparks;
        private ParticleSystem ring;
        private Volume volume;

        /// <summary>Current post-FX pulse weight (0..1), for tests and debugging.</summary>
        public float PulseWeight => volume != null ? volume.weight : 0f;
        public int LastSparkCount { get; private set; }

        public Material ParticleMaterial { get => particleMaterial; set => particleMaterial = value; }

        private void Awake()
        {
            impulse = GetComponent<CinemachineImpulseSource>();
            ParticleFx.ConfigureImpulse(impulse, CinemachineImpulseDefinition.ImpulseShapes.Recoil, 0.25f);

            var material = particleMaterial != null ? particleMaterial : ParticleFx.SharedParticleMaterial;
            sparks = BuildSparks(material);
            ring = BuildRing(material);
            volume = BuildVolume();
        }

        // The profile is built at runtime, so nothing else frees it.
        private void OnDestroy()
        {
            if (volume != null && volume.sharedProfile != null) Destroy(volume.sharedProfile);
        }

        private void Start()
        {
            ParticleFx.ShakeDuringHitStop();
            var cam = Camera.main;
            if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        private void OnEnable() => ClashSystem.ClashOccurred += HandleClash;
        private void OnDisable() => ClashSystem.ClashOccurred -= HandleClash;

        private void Update()
        {
            if (volume.weight > 0f)
                volume.weight = Mathf.Max(0f, volume.weight - Time.unscaledDeltaTime / pulseDecaySeconds);
        }

        private void HandleClash(ClashEvent e)
        {
            int tierIndex;
            Color color;
            switch (e.Result.Outcome)
            {
                case ClashOutcome.Neutral:
                    tierIndex = 0;
                    color = Color.white;
                    break;
                case ClashOutcome.AWins:
                case ClashOutcome.BWins:
                    tierIndex = e.Result.BrokeByZero ? 2 : 1;
                    var winner = e.Result.Outcome == ClashOutcome.AWins ? e.A : e.B;
                    color = e.Result.BrokeByZero ? ZeroBreakColor : ParticleFx.Opaque(ElementColors.For(winner));
                    break;
                default:
                    return;
            }

            var tier = Tiers[tierIndex];
            impulse.GenerateImpulseAtPositionWithVelocity(e.Position, Random.onUnitSphere * tier.Shake);
            ParticleFx.Emit(sparks, e.Position, color, tier.Sparks);
            ParticleFx.Emit(ring, e.Position, color, tier.RingStreaks);
            LastSparkCount = tier.Sparks;
            if (tier.Pulse > volume.weight) volume.weight = tier.Pulse;
        }

        private ParticleSystem BuildSparks(Material material)
        {
            var ps = ParticleFx.CreateSystem(transform, "Sparks", material, maxParticles: 256);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = 1.5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            return ps;
        }

        private ParticleSystem BuildRing(Material material)
        {
            var ps = ParticleFx.CreateSystem(transform, "ShockwaveRing", material, maxParticles: 128);
            var main = ps.main;
            main.startLifetime = 0.22f;
            main.startSpeed = 18f;
            main.startSize = 0.22f;

            // Flat ring on the ground plane: streaks fly radially outward from the clash point.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            return ps;
        }

        private Volume BuildVolume()
        {
            var go = new GameObject("ImpactPulseVolume");
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 10f;
            v.weight = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(1f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.6f);
            vignette.color.Override(new Color(0.15f, 0f, 0.05f));
            v.sharedProfile = profile;
            return v;
        }
    }
}
