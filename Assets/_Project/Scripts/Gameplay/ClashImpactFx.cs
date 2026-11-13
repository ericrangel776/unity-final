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
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

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
            var def = impulse.ImpulseDefinition;
            def.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
            def.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            def.ImpulseDuration = 0.25f;

            var material = particleMaterial != null ? particleMaterial : CreateParticleMaterial();
            sparks = BuildSparks(material);
            ring = BuildRing(material);
            volume = BuildVolume();
        }

        private void Start()
        {
            // Shake during the freeze, not after it.
            CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
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
                    color = e.Result.BrokeByZero ? ZeroBreakColor : Opaque(ElementColors.For(winner));
                    break;
                default:
                    return;
            }

            var tier = Tiers[tierIndex];
            impulse.GenerateImpulseAtPositionWithVelocity(e.Position, Random.onUnitSphere * tier.Shake);
            Emit(sparks, e.Position, color, tier.Sparks);
            Emit(ring, e.Position, color, tier.RingStreaks);
            LastSparkCount = tier.Sparks;
            if (tier.Pulse > volume.weight) volume.weight = tier.Pulse;
        }

        private static void Emit(ParticleSystem ps, Vector3 position, Color color, int count)
        {
            if (count <= 0) return;
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
                startColor = color
            };
            ps.Emit(p, count);
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        private ParticleSystem BuildSparks(Material material)
        {
            var ps = CreateSystem("Sparks", material, maxParticles: 256);
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
            var ps = CreateSystem("ShockwaveRing", material, maxParticles: 128);
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

        private ParticleSystem CreateSystem(string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.useUnscaledTime = true; // fly while the world is frozen
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;

            var emission = ps.emission;
            emission.enabled = false; // burst manually via Emit

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 1.5f;
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

        /// <summary>Additive URP particle material with a soft round dot, used if none is assigned.</summary>
        public static Material CreateParticleMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader) { name = "ClashSpark" };
            ConfigureAdditive(mat, CreateSoftDotTexture());
            return mat;
        }

        /// <summary>Sets a URP Particles/Unlit material to transparent additive with the given texture.</summary>
        public static void ConfigureAdditive(Material mat, Texture2D texture)
        {
            mat.SetFloat("_Surface", 1f);      // transparent
            mat.SetFloat("_Blend", 2f);        // additive
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // What URP's material validator would otherwise fix up on import (keeps rebuilds stable).
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("SHADOWCASTER", false);
            mat.renderQueue = (int)RenderQueue.Transparent;
            if (texture != null) mat.SetTexture(BaseMapId, texture);
        }

        /// <summary>64x64 white dot with a soft radial falloff in alpha.</summary>
        public static Texture2D CreateSoftDotTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ClashSparkDot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - d);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
