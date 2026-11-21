using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Ground marker where a second-wave enemy is about to appear: a flat disc that grows over the delay and a ring whose
    /// alpha pulses. Destroys itself after its duration (scaled time, like the wave delay). No assets.
    /// See docs/specs/enemy-variants.md.
    /// </summary>
    public sealed class SpawnRingFx : MonoBehaviour
    {
        public const float Radius = 0.8f;
        public const float Width = 0.08f;
        /// <summary>Just above the ground so it doesn't z-fight it.</summary>
        public const float Height = 0.05f;
        public const int Points = 40;
        public const float PulsesPerSecond = 2f;
        public static readonly Color RingColor = new Color(1f, 0.3f, 0.4f, 0.9f);
        public static readonly Color DiscColor = new Color(0.35f, 0.08f, 0.12f);

        private LineRenderer ring;
        private Transform disc;
        private float duration;
        private float age;

        public float Duration => duration;
        public float ShownAlpha { get; private set; }

        public static SpawnRingFx Create(Vector3 groundPosition, float seconds, Transform parent = null)
        {
            var go = new GameObject("SpawnRingFx");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPosition + Vector3.up * Height;
            var fx = go.AddComponent<SpawnRingFx>();
            fx.duration = Mathf.Max(0.01f, seconds);
            fx.Build();
            return fx;
        }

        private void Build()
        {
            disc = FxShapes.CreateVisual(PrimitiveType.Cylinder, "SpawnDisc", transform, DiscColor).transform;
            disc.localScale = new Vector3(0.2f, 0.005f, 0.2f);
            ring = FxShapes.CreateLine(transform, "SpawnRing", Points, Width);
            FxShapes.SetArc(ring, transform.position, Radius, 1f);
            Pulse();
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= duration)
            {
                Destroy(gameObject);
                return;
            }
            Pulse();
        }

        private void Pulse()
        {
            float t = Mathf.Clamp01(age / duration);
            float d = Mathf.Lerp(0.2f, Radius * 2f, t);
            disc.localScale = new Vector3(d, 0.005f, d);
            ShownAlpha = RingColor.a * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(age * Mathf.PI * PulsesPerSecond)));
            FxShapes.SetColor(ring, new Color(RingColor.r, RingColor.g, RingColor.b, ShownAlpha));
        }
    }
}
