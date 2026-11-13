using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Draws the Deadlock minigame at the clash point: a ring shrinking onto a target ring, the input
    /// hint with your current hand values, your grade once you press, then a "YOU x vs y FOE" reveal.
    /// Polls <see cref="DeadlockController.Current"/> and runs on unscaled time (the world is frozen).
    /// </summary>
    public sealed class DeadlockView : MonoBehaviour
    {
        [SerializeField] private float resultSeconds = 0.9f;

        private const float TargetSize = 120f;
        private static readonly Color TargetColor = new Color(1f, 1f, 1f, 0.75f);
        private static readonly Color RingColor = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color PerfectColor = new Color(0.55f, 1f, 0.65f);
        private static readonly Color GoodColor = new Color(0.6f, 0.85f, 1f);
        private static readonly Color MissColor = new Color(1f, 0.45f, 0.4f);

        private RectTransform root;
        private Image target;
        private Image ring;
        private Text title;
        private Text hint;
        private Text hand;
        private Text grade;
        private Text result;
        private Vector3 worldPosition;
        private float resultRemaining;

        public bool IsRingVisible => ring != null && ring.enabled;
        public string GradeText => grade != null && grade.enabled ? grade.text : null;
        public string ResultText => result != null && result.enabled ? result.text : null;
        public string HandText => hand != null && hand.enabled ? hand.text : null;

        private void Awake()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "DeadlockCanvas", sortingOrder: 11);
            root = UiFactory.CreateRect(canvas.transform, "Deadlock", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(TargetSize, TargetSize));
            var sprite = CreateRingSprite();
            target = UiFactory.CreateImage(root, "Target", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.one * TargetSize, TargetColor);
            target.sprite = sprite;
            ring = UiFactory.CreateImage(root, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.one * TargetSize, RingColor);
            ring.sprite = sprite;

            title = Label("Title", 44, new Vector2(0f, 120f), RingColor);
            title.text = "DEADLOCK!";
            hint = Label("Hint", 22, new Vector2(0f, -100f), Color.white);
            hint.text = "POKE: push    1 / 2 / 3: commit that card";
            hand = Label("Hand", 26, new Vector2(0f, -132f), new Color(0.85f, 0.9f, 1f));
            grade = Label("Grade", 34, Vector2.zero, Color.white);
            result = Label("Result", 40, new Vector2(0f, 70f), Color.white);
            SetDeadlockVisible(false);
            result.enabled = false;
        }

        private Text Label(string name, int size, Vector2 offset, Color color)
        {
            var rect = UiFactory.CreateRect(root, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), offset,
                new Vector2(600f, 50f));
            return UiFactory.CreateText(rect, name + "Text", size, TextAnchor.MiddleCenter, color);
        }

        private void OnEnable() => DeadlockController.Resolved += ShowResult;
        private void OnDisable() => DeadlockController.Resolved -= ShowResult;

        private void LateUpdate()
        {
            var deadlock = DeadlockController.Current;
            bool active = deadlock != null && deadlock.IsActive;
            SetDeadlockVisible(active);

            if (active)
            {
                worldPosition = deadlock.Position;
                float elapsed = deadlock.Elapsed;
                // 3x the target at the start, exactly the target at TargetTime, still shrinking after.
                float scale = 1f + (DeadlockRules.TargetTime - elapsed) / DeadlockRules.TargetTime * 2f;
                ring.rectTransform.sizeDelta = Vector2.one * (TargetSize * Mathf.Max(0.2f, scale));
                hand.text = HandLine(deadlock.PlayerCycle);

                grade.enabled = deadlock.HasPlayerSubmitted;
                if (deadlock.HasPlayerSubmitted)
                {
                    grade.text = GradeLine(deadlock.PlayerGrade, deadlock.PlayerCommittedValue);
                    grade.color = GradeColor(deadlock.PlayerGrade);
                }
            }

            if (resultRemaining > 0f)
            {
                resultRemaining -= Time.unscaledDeltaTime;
                if (resultRemaining <= 0f) result.enabled = false;
            }

            if (active || result.enabled) Place();
        }

        private void ShowResult(DeadlockResult r)
        {
            worldPosition = r.Position;
            result.text = $"YOU {r.PlayerPower}  vs  {r.EnemyPower} FOE";
            result.color = r.Outcome == ClashOutcome.AWins ? PerfectColor
                : r.Outcome == ClashOutcome.BWins ? MissColor : Color.white;
            result.enabled = true;
            resultRemaining = resultSeconds;
        }

        private void SetDeadlockVisible(bool visible)
        {
            target.enabled = ring.enabled = title.enabled = hint.enabled = hand.enabled = visible;
            if (!visible) grade.enabled = false;
        }

        private void Place()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var screen = cam.WorldToScreenPoint(worldPosition);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)root.parent, screen, null, out var local);
            root.anchoredPosition = local;
        }

        private static string HandLine(DeckCycle cycle)
        {
            if (cycle == null) return "";
            var parts = new string[DeckCycle.HandSize];
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var card = cycle.Hand[i];
                parts[i] = card == null ? $"{i + 1}:-" : $"{i + 1}:[{(card.IsZeroBreaker ? 0 : card.GetEffectiveValue())}]";
            }
            return string.Join("   ", parts);
        }

        private static string GradeLine(DeadlockGrade grade, int committed)
        {
            string g = grade == DeadlockGrade.Perfect ? $"PERFECT +{DeadlockRules.PerfectBonus}"
                : grade == DeadlockGrade.Good ? $"GOOD +{DeadlockRules.GoodBonus}" : "MISS";
            return committed > 0 ? $"{g}  +{committed} card" : g;
        }

        private static Color GradeColor(DeadlockGrade grade) =>
            grade == DeadlockGrade.Perfect ? PerfectColor : grade == DeadlockGrade.Good ? GoodColor : MissColor;

        /// <summary>A thin anti-aliased ring, generated so no art asset is needed.</summary>
        private static Sprite CreateRingSprite()
        {
            const int size = 128;
            const float thickness = 6f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DeadlockRing", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                float edge = Mathf.Abs(d - (radius - thickness * 0.5f)) - thickness * 0.5f;
                byte a = (byte)(Mathf.Clamp01(1f - edge) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
