using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Floating damage numbers (docs/specs/hit-juice.md). Every damaging body hit pops its final damage over the victim.
    /// On enemies the number takes the attack's element colour; on the player it is red. Bigger hits get bigger numbers.
    /// Each label punches in, rises with a sideways drift and fades out, on unscaled time so it moves during the hit-stop.
    /// Labels come from a fixed pool; when every label is busy the oldest one is reused. Heals pop a green "+N" on the
    /// same pool (docs/specs/heal-rework.md).
    /// </summary>
    public sealed class DamageNumberView : MonoBehaviour
    {
        public const int PoolSize = 24;
        public const int BaseFontSize = 36;
        /// <summary>Effect damage (burn ticks, chain jumps, finisher) shows smaller than a card hit.</summary>
        public const float EffectNumberScale = 0.75f;
        public static readonly Color PlayerHurtColor = new Color(1f, 0.3f, 0.25f, 1f);
        /// <summary>Heal numbers: the heal green, opaque.</summary>
        public static readonly Color HealColor = Opaque(ElementColors.Heal);

        [SerializeField] private float showSeconds = 0.7f;
        [SerializeField] private float riseScreenPixels = 70f;
        [SerializeField] private float driftScreenPixels = 25f;
        [SerializeField] private float jitterScreenPixels = 30f;
        [SerializeField] private float punchSeconds = 0.12f;
        [SerializeField] private float punchFrom = 1.4f;
        [Tooltip("Hits on the same victim within this window stack upward instead of overlapping (multi-hit Sleights).")]
        [SerializeField] private float stackWindowSeconds = 0.25f;
        [SerializeField] private float stackStepPixels = 38f;
        /// <summary>Numbers start a little above the hit point so they clear the body.</summary>
        private static readonly Vector3 AnchorOffset = new Vector3(0f, 0.6f, 0f);

        /// <summary>What a live label is showing, for tests.</summary>
        public readonly struct Shown
        {
            public readonly string Text;
            public readonly Color Color;
            public readonly float Scale;
            /// <summary>0 for the first recent number on a victim, 1 for the next one stacked above it, and so on.</summary>
            public readonly int StackIndex;

            public Shown(string text, Color color, float scale, int stackIndex)
            {
                Text = text;
                Color = color;
                Scale = scale;
                StackIndex = stackIndex;
            }
        }

        private Canvas canvas;
        private readonly List<Label> pool = new List<Label>(PoolSize);
        private readonly List<Shown> activeScratch = new List<Shown>(PoolSize);
        private int spawnSerial;

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var l in pool) if (l.IsActive) n++;
                return n;
            }
        }

        /// <summary>The labels currently showing, newest last.</summary>
        public IReadOnlyList<Shown> Active
        {
            get
            {
                activeScratch.Clear();
                var live = new List<Label>(pool.Count);
                foreach (var l in pool) if (l.IsActive) live.Add(l);
                live.Sort((a, b) => a.Serial.CompareTo(b.Serial));
                foreach (var l in live) activeScratch.Add(new Shown(l.Text, l.Color, l.DamageScale, l.StackIndex));
                return activeScratch;
            }
        }

        private void Awake()
        {
            // Below ClashFeedbackView (10) so "COUNTER!" and "CARD BREAK!" draw on top of the numbers.
            canvas = UiFactory.CreateOverlayCanvas(transform, "DamageNumberCanvas", sortingOrder: 9);
            for (int i = 0; i < PoolSize; i++) pool.Add(new Label(canvas.transform, i));
        }

        private void OnEnable()
        {
            CombatantStatus.AnyHitLanded += HandleHit;
            CombatantStatus.AnyHealed += HandleHealed;
        }

        private void OnDisable()
        {
            CombatantStatus.AnyHitLanded -= HandleHit;
            CombatantStatus.AnyHealed -= HandleHealed;
            foreach (var l in pool) l.Hide();
        }

        private void HandleHit(HitInfo hit)
        {
            bool onPlayer = hit.Victim != null && hit.Victim.Team == Team.Player;
            var color = onPlayer ? PlayerHurtColor : Opaque(ElementColors.For(hit.Payload));
            float scale = HitJuiceRules.DamageNumberScale(hit.Damage) * (hit.IsEffect ? EffectNumberScale : 1f);
            Pop(hit.Damage.ToString(), color, scale, hit.Victim, hit.Position);
        }

        /// <summary>A heal pops a green "+N" over whoever healed (only raised when more than 0 was restored).</summary>
        private void HandleHealed(CombatantStatus who, int amount)
        {
            if (who == null || amount <= 0) return;
            Pop($"+{amount}", HealColor, HitJuiceRules.DamageNumberScale(amount), who,
                who.transform.position + Vector3.up * CombatantStatus.HitFeedbackHeight);
        }

        private void Pop(string message, Color color, float scale, CombatantStatus victim, Vector3 position)
        {
            var jitter = new Vector2(Random.Range(-jitterScreenPixels, jitterScreenPixels), 0f);
            float drift = Random.Range(-driftScreenPixels, driftScreenPixels);

            var label = TakeLabel();
            int stack = RecentOn(victim, label, out float stackPixels);
            var offset = jitter + new Vector2(0f, stackPixels);
            label.Show(message, color, scale, position + AnchorOffset, offset, drift, showSeconds,
                spawnSerial++, victim, stack);
            label.Tick(0f, Camera.main, CanvasCamera, this);
        }

        /// <summary>
        /// How many live labels on <paramref name="victim"/> are younger than the stack window, and how far up a new one
        /// must start to clear them (each counts its own text height, so a big number pushes the next one further).
        /// </summary>
        private int RecentOn(CombatantStatus victim, Label except, out float pixels)
        {
            pixels = 0f;
            if (victim == null) return 0;
            int n = 0;
            foreach (var l in pool)
            {
                if (l == except || !l.IsActive || l.Victim != victim || l.Age >= stackWindowSeconds) continue;
                n++;
                pixels += Mathf.Max(stackStepPixels, BaseFontSize * l.DamageScale * 0.9f);
            }
            return n;
        }

        /// <summary>A free label, or the oldest busy one when the pool is exhausted.</summary>
        private Label TakeLabel()
        {
            Label oldest = null;
            foreach (var l in pool)
            {
                if (!l.IsActive) return l;
                if (oldest == null || l.Serial < oldest.Serial) oldest = l;
            }
            return oldest;
        }

        // Camera-space canvases (the screenshot capture) need their camera for the conversion; overlay needs null.
        private Camera CanvasCamera => UiFactory.CanvasCamera(canvas);

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var cam = Camera.main;
            var canvasCam = CanvasCamera;
            foreach (var l in pool) l.Tick(dt, cam, canvasCam, this);
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        private sealed class Label
        {
            private readonly RectTransform rect;
            private readonly Text text;
            private Vector3 worldPosition;
            private Vector2 jitter;
            private float drift;
            private float duration;
            private float remaining;
            private Color baseColor;

            public bool IsActive => remaining > 0f;
            public int Serial { get; private set; }
            public string Text => text.text;
            public Color Color => baseColor;
            public float DamageScale { get; private set; } = 1f;
            public CombatantStatus Victim { get; private set; }
            public int StackIndex { get; private set; }
            public float Age => duration - remaining;

            public Label(Transform canvas, int index)
            {
                rect = UiFactory.CreateRect(canvas, "DamageNumber" + index, Vector2.zero, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(200f, 90f));
                text = UiFactory.CreateText(rect, "Text", BaseFontSize, TextAnchor.MiddleCenter, Color.white);
                text.raycastTarget = false;
                UiFactory.ReadableOutline(text, 2f);
                text.enabled = false;
            }

            public void Show(string message, Color color, float damageScale, Vector3 position, Vector2 jitterPixels,
                float driftPixels, float seconds, int serial, CombatantStatus victim, int stackIndex)
            {
                Victim = victim;
                StackIndex = stackIndex;
                text.text = message;
                baseColor = color;
                text.color = color;
                DamageScale = damageScale;
                text.fontSize = Mathf.RoundToInt(BaseFontSize * damageScale);
                worldPosition = position;
                jitter = jitterPixels;
                drift = driftPixels;
                duration = remaining = Mathf.Max(0.01f, seconds);
                Serial = serial;
                text.enabled = true;
            }

            public void Hide()
            {
                remaining = 0f;
                Victim = null;
                text.enabled = false;
                rect.localScale = Vector3.one;
            }

            public void Tick(float dt, Camera cam, Camera canvasCam, DamageNumberView view)
            {
                if (remaining <= 0f) return;
                remaining -= dt;
                if (remaining <= 0f)
                {
                    Hide();
                    return;
                }

                float elapsed = duration - remaining;
                float t = elapsed / duration;
                // Ease-out punch from big down to normal size.
                float p = view.punchSeconds > 0f ? Mathf.Clamp01(elapsed / view.punchSeconds) : 1f;
                float ease = UiFactory.EaseOutCubic(p);
                rect.localScale = Vector3.one * Mathf.Lerp(view.punchFrom, 1f, ease);

                var c = baseColor;
                c.a = 1f - t * t;
                text.color = c;

                if (cam == null) return;
                var screen = cam.WorldToScreenPoint(worldPosition);
                // Behind the camera: keep it hidden rather than mirrored onto the screen.
                text.enabled = screen.z > 0f;
                var canvasRect = (RectTransform)rect.parent;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, canvasCam, out var local);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                float rise = 1f - (1f - t) * (1f - t); // ease-out rise
                rect.anchoredPosition = local + jitter + new Vector2(drift * t, view.riseScreenPixels * rise);
            }
        }
    }
}
