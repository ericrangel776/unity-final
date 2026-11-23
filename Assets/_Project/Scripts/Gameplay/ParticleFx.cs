using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Shared helpers for the runtime-built FX (clash sparks, hit sparks, death pops, element status FX, speed lines):
    /// one-shot unscaled particle systems, the shared additive particle material, and camera-impulse setup.
    /// </summary>
    public static class ParticleFx
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>A uniform impulse of the given shape and duration (the shake every FX component uses).</summary>
        internal static void ConfigureImpulse(CinemachineImpulseSource source,
            CinemachineImpulseDefinition.ImpulseShapes shape, float duration)
        {
            var def = source.ImpulseDefinition;
            def.ImpulseShape = shape;
            def.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            def.ImpulseDuration = duration;
        }

        /// <summary>Camera shakes run on unscaled time, so they play during the hit-stop freeze, not after it.</summary>
        internal static void ShakeDuringHitStop() => CinemachineImpulseManager.Instance.IgnoreTimeScale = true;

        internal static void Emit(ParticleSystem ps, Vector3 position, Color color, int count)
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

        internal static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        /// <summary>A one-shot, unscaled-time, world-space particle system under <paramref name="parent"/>, burst via Emit.</summary>
        internal static ParticleSystem CreateSystem(Transform parent, string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
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

        private static Material sharedParticleMaterial;

        // Domain reload may be disabled: don't keep a destroyed material across play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => sharedParticleMaterial = null;

        /// <summary>One runtime particle material shared by every FX component whose slot is left empty.</summary>
        public static Material SharedParticleMaterial =>
            sharedParticleMaterial != null ? sharedParticleMaterial : (sharedParticleMaterial = CreateParticleMaterial());

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
