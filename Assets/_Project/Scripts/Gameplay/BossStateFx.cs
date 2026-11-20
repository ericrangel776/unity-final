using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Makes the boss's punish windows readable without the HUD (docs/specs/m4-overview.md): during the reload window
    /// (cyan) and Exhausted (orange) the boss is tinted, cards orbit it and a ground ring counts down. When the window
    /// ends a ring bursts outward. The FX root is unparented (unaffected by the boss's scale) and destroyed with it.
    /// </summary>
    [RequireComponent(typeof(BossEnemy))]
    public sealed class BossStateFx : MonoBehaviour
    {
        public static readonly Color ReloadColor = new Color(0.4f, 0.85f, 1f, 1f);
        public static readonly Color ExhaustedColor = new Color(1f, 0.45f, 0.2f, 1f);
        public const int OrbitCards = 6;
        public const float BurstSeconds = 0.35f;
        private const float RingRadius = 2.4f;
        private const int Segments = 64;

        private BossEnemy boss;
        private BodyTint tint;
        private Transform root;
        private LineRenderer countdown, burst;
        private readonly LineRenderer[] cards = new LineRenderer[OrbitCards];
        private float burstRemaining;
        private Color burstColor;
        private bool tinted;

        public bool IsShowing => root != null && root.gameObject.activeSelf && (countdown.gameObject.activeSelf || IsBursting);
        public bool IsBursting => burstRemaining > 0f;
        public bool IsTinted => tinted;
        /// <summary>0..1 of the countdown ring still drawn.</summary>
        public float CountdownFraction { get; private set; }
        public GameObject Root => root != null ? root.gameObject : null;

        private void Awake()
        {
            boss = GetComponent<BossEnemy>();
            tint = BodyTint.For(gameObject);

            root = new GameObject("BossStateFx").transform;
            countdown = FxShapes.CreateLine(root, "Countdown", Segments, 0.16f);
            burst = FxShapes.CreateLine(root, "Burst", Segments, 0.25f);
            // Each orbiting "card" is a short thick line: a glowing upright sliver.
            for (int i = 0; i < OrbitCards; i++)
            {
                cards[i] = FxShapes.CreateLine(root, $"Card{i + 1}", 2, 0.35f);
                cards[i].numCapVertices = 0;
            }
            countdown.gameObject.SetActive(false);
            burst.gameObject.SetActive(false);
            SetCardsActive(false);
        }

        private void OnEnable() => boss.RecoveredFromStagger += HandleRecovered;

        private void OnDisable()
        {
            boss.RecoveredFromStagger -= HandleRecovered;
            ClearTint();
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        private void HandleRecovered()
        {
            burstRemaining = BurstSeconds;
            burstColor = countdown.startColor;
            burst.gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            bool dead = boss.Status.IsDead;
            bool window = !dead && (boss.State == BossState.ReloadWindow || boss.State == BossState.Exhausted);
            var color = boss.State == BossState.Exhausted ? ExhaustedColor : ReloadColor;
            var center = transform.position + Vector3.up * 0.06f;

            countdown.gameObject.SetActive(window);
            SetCardsActive(window);
            if (window)
            {
                CountdownFraction = Mathf.Clamp01(boss.StateRemaining / Mathf.Max(0.0001f, boss.StateDuration));
                FxShapes.SetColor(countdown, color);
                FxShapes.SetArc(countdown, center, RingRadius, CountdownFraction);
                // Orbit on scaled time: hit-stop freezes them with the world (the timer is scaled too).
                float spin = Time.time * (boss.State == BossState.Exhausted ? 0.6f : 1.8f);
                for (int i = 0; i < OrbitCards; i++)
                {
                    float a = spin + i * Mathf.PI * 2f / OrbitCards;
                    var p = transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.6f + Vector3.up * 1.6f;
                    cards[i].SetPosition(0, p - Vector3.up * 0.25f);
                    cards[i].SetPosition(1, p + Vector3.up * 0.25f);
                    FxShapes.SetColor(cards[i], color);
                }
                ApplyTint(color);
            }
            else
            {
                CountdownFraction = 0f;
                ClearTint();
            }

            if (burstRemaining > 0f)
            {
                burstRemaining -= Time.unscaledDeltaTime;
                float t = 1f - Mathf.Clamp01(burstRemaining / BurstSeconds);
                FxShapes.SetColor(burst, burstColor * new Color(1f, 1f, 1f, 1f - t));
                FxShapes.SetArc(burst, center, Mathf.Lerp(RingRadius, RingRadius * 2.5f, t), 1f);
                if (burstRemaining <= 0f || dead) burst.gameObject.SetActive(false);
                if (dead) burstRemaining = 0f;
            }
            root.gameObject.SetActive(window || burstRemaining > 0f);
        }

        private void SetCardsActive(bool active)
        {
            foreach (var c in cards) if (c.gameObject.activeSelf != active) c.gameObject.SetActive(active);
        }

        private void ApplyTint(Color color)
        {
            // The hit flash (a higher BodyTint layer) wins while it plays; the tint comes back right after.
            float pulse = 0.55f + 0.2f * Mathf.Sin(Time.unscaledTime * 8f);
            tint.Set(BodyTint.Layer.State, color, pulse, lerpFromWhite: true);
            tinted = true;
        }

        private void ClearTint()
        {
            if (!tinted) return;
            tinted = false;
            tint.Clear(BodyTint.Layer.State);
        }
    }
}
