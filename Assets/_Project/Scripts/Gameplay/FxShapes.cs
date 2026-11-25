using System.Collections.Generic;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>Small helpers for world FX: lines (rings, arcs) on a shared additive material, and collider-less primitives.</summary>
    public static class FxShapes
    {
        private static Material material;
        private static readonly Dictionary<Color, Material> solidMaterials = new Dictionary<Color, Material>();

        // Domain reload may be disabled: don't keep destroyed materials across play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            material = null;
            solidMaterials.Clear();
        }

        /// <summary>Additive, untextured URP particle material (shared; tint per renderer with colors).</summary>
        public static Material Material
        {
            get
            {
                if (material != null) return material;
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "FxLine" };
                ParticleFx.ConfigureAdditive(material, Texture2D.whiteTexture);
                return material;
            }
        }

        /// <summary>
        /// A collider-less primitive for visuals. Its colour comes from a material shared by every visual of that colour,
        /// so spawning one (a hitbox, a status pip) never clones a material that outlives the object.
        /// </summary>
        public static GameObject CreateVisual(PrimitiveType type, string name, Transform parent, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            // Immediate: a deferred Destroy would leave a solid collider on the parent's rigidbody for a frame.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = SolidMaterial(r.sharedMaterial, color);
            return go;
        }

        /// <summary>URP Lit: always in builds, because the scene's own materials use it.</summary>
        public const string SolidShaderName = "Universal Render Pipeline/Lit";

        private static Material SolidMaterial(Material template, Color color)
        {
            if (solidMaterials.TryGetValue(color, out var m) && m != null) return m;
            // Not a copy of the primitive's material: in a player build CreatePrimitive hands out the built-in
            // Standard material, which URP can't draw (magenta). The Editor hides this by using the pipeline default.
            var lit = Shader.Find(SolidShaderName);
            m = lit != null ? new Material(lit) : new Material(template);
            m.name = "FxSolid";
            m.color = color;
            solidMaterials[color] = m;
            return m;
        }

        public static LineRenderer CreateLine(Transform parent, string name, int points, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Material;
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.startWidth = lr.endWidth = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>
        /// Lays <paramref name="line"/> out as a flat arc around <paramref name="center"/>: <paramref name="fraction"/>
        /// of a full circle (1 = closed ring), starting at the front and going clockwise.
        /// </summary>
        public static void SetArc(LineRenderer line, Vector3 center, float radius, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            int n = line.positionCount;
            line.loop = fraction >= 0.999f;
            int last = line.loop ? n : n - 1;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)Mathf.Max(1, last)) * fraction * Mathf.PI * 2f;
                line.SetPosition(i, center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius);
            }
        }

        public static void SetColor(LineRenderer line, Color color)
        {
            line.startColor = color;
            line.endColor = color;
        }
    }
}
