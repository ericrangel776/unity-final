using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;

namespace DeckArpg.UI
{
    /// <summary>
    /// World-space speed lines that follow the player while the Perfect-reload move-speed boost lasts.
    /// Streaks ring the player and stream out behind its movement direction, fading as the boost runs down.
    /// Nothing shows while the player stands still or when there is no boost.
    /// </summary>
    public sealed class SpeedLinesView : MonoBehaviour
    {
        [SerializeField] private DeckRuntimeManager deck;
        [SerializeField] private int lineCount = 14;
        [SerializeField] private float maxAlpha = 0.6f;
        [Tooltip("Metres per second a streak travels backwards along the trail.")]
        [SerializeField] private float streamSpeed = 9f;
        [Tooltip("Ground speed (m/s) at which the lines reach full strength.")]
        [SerializeField] private float fullSpeed = 3f;

        private const float StartAhead = 0.4f;   // streaks appear slightly ahead of the player's centre
        private const float TrailLength = 2.6f;  // and travel this far behind it
        private const float MinSpeed = 0.2f;     // below this the player counts as standing still
        private const float VelocitySmoothing = 12f;

        private Transform root;
        private LineRenderer[] lines;
        private Vector2[] ringOffsets; // x = sideways, y = height
        private float[] lengths;
        private float[] phases;
        private Transform follow;
        private Vector3 lastPosition;
        private Vector3 smoothedVelocity;
        private Vector3 trailDirection = Vector3.back;
        private float alpha;

        public float Alpha => alpha;
        public bool IsActive => root != null && root.gameObject.activeSelf;
        /// <summary>Unit direction the streaks stream towards (opposite the player's movement).</summary>
        public Vector3 TrailDirection => trailDirection;
        public int LineCount => lines != null ? lines.Length : 0;

        public void Bind(DeckRuntimeManager deckManager)
        {
            deck = deckManager;
            ResetFollow();
        }

        /// <summary>World position of streak <paramref name="index"/>'s midpoint (for tests).</summary>
        public Vector3 LineCenter(int index) =>
            (lines[index].GetPosition(0) + lines[index].GetPosition(1)) * 0.5f;

        private void Awake()
        {
            if (deck == null)
            {
                var player = FindAnyObjectByType<PlayerCombatInput>();
                if (player != null) deck = player.GetComponent<DeckRuntimeManager>();
            }
            Build();
            ResetFollow();
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        private void ResetFollow()
        {
            follow = deck != null ? deck.transform : null;
            if (follow != null) lastPosition = follow.position;
            smoothedVelocity = Vector3.zero;
        }

        private void Build()
        {
            // Unparented so it never inherits the player's rotation; positioned every frame instead.
            root = new GameObject("SpeedLines").transform;
            // The shared FX material: lines are tinted by vertex colour, so no per-view material (it used to leak per scene load).
            var material = ParticleFx.SharedParticleMaterial;
            lines = new LineRenderer[lineCount];
            ringOffsets = new Vector2[lineCount];
            lengths = new float[lineCount];
            phases = new float[lineCount];
            var rng = new System.Random(1234); // fixed layout: same streak pattern every run
            for (int i = 0; i < lineCount; i++)
            {
                float side = (i % 2 == 0 ? 1f : -1f) * (0.45f + (float)rng.NextDouble() * 0.6f);
                ringOffsets[i] = new Vector2(side, 0.15f + (float)rng.NextDouble() * 1.5f);
                lengths[i] = 0.6f + (float)rng.NextDouble() * 0.8f;
                phases[i] = (float)rng.NextDouble();

                var go = new GameObject($"Line{i}");
                go.transform.SetParent(root, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = material;
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                float width = 0.02f + (float)rng.NextDouble() * 0.03f;
                lr.startWidth = width;
                lr.endWidth = width * 0.2f; // tapers towards the tail
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lines[i] = lr;
            }
            root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (follow == null && deck != null) ResetFollow();
            float dt = Time.deltaTime;
            if (follow != null && dt > 0f)
            {
                // Scaled time: hit-stop freezes the streaks with the world.
                var velocity = (follow.position - lastPosition) / dt;
                velocity.y = 0f;
                smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, 1f - Mathf.Exp(-VelocitySmoothing * dt));
                lastPosition = follow.position;
            }

            var reload = deck != null ? deck.Reload : null;
            float remaining = reload != null ? reload.BoostRemaining : 0f;
            float boost = Mathf.Clamp01(remaining / ActiveReloadRules.BoostSeconds);
            float speed = smoothedVelocity.magnitude;
            float moving = speed < MinSpeed ? 0f : Mathf.Clamp01(speed / fullSpeed);
            alpha = follow != null ? maxAlpha * boost * moving : 0f;

            bool active = alpha > 0f;
            if (root.gameObject.activeSelf != active) root.gameObject.SetActive(active);
            if (!active) return;

            var forward = smoothedVelocity / speed;
            trailDirection = -forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var origin = follow.position;
            float travel = StartAhead + TrailLength;

            for (int i = 0; i < lines.Length; i++)
            {
                phases[i] = Mathf.Repeat(phases[i] + streamSpeed * dt / travel, 1f);
                var head = origin + right * ringOffsets[i].x + Vector3.up * ringOffsets[i].y
                           + forward * (StartAhead - phases[i] * travel);
                var lr = lines[i];
                lr.SetPosition(0, head);
                lr.SetPosition(1, head - forward * lengths[i]);
                // Fade in near the player, out at the end of the trail, so streaks never pop.
                float fade = Mathf.SmoothStep(0f, 1f, phases[i] * 4f) * Mathf.SmoothStep(0f, 1f, (1f - phases[i]) * 3f);
                var c = new Color(1f, 1f, 1f, alpha * fade);
                lr.startColor = c;
                lr.endColor = new Color(1f, 1f, 1f, 0f);
            }
        }
    }
}
