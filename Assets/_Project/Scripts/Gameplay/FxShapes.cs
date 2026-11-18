using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>Small helpers for line-based world FX (rings, arcs) on the shared additive particle material.</summary>
    public static class FxShapes
    {
        private static Material material;

        /// <summary>Additive, untextured URP particle material (shared; tint per renderer with colors).</summary>
        public static Material Material
        {
            get
            {
                if (material != null) return material;
                material = ClashImpactFx.CreateParticleMaterial();
                material.name = "BossFx";
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                return material;
            }
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
