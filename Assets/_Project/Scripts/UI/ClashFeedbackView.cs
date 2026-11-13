using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Pops "CARD BREAK!" / "ZERO BREAK!" / "CLASH" at the clash point with a scale punch, flashes
    /// the whole screen on breaks (red when the player's card was the one broken), and pops
    /// "COUNTER!" over anyone who takes a counter hit. Runs on unscaled time so it animates through
    /// the hit-stop freeze.
    /// </summary>
    public sealed class ClashFeedbackView : MonoBehaviour
    {
        [SerializeField] private float showSeconds = 0.7f;
        [SerializeField] private float riseScreenPixels = 60f;
        [SerializeField] private float punchSeconds = 0.18f;
        [SerializeField] private float screenFlashSeconds = 0.25f;

        private const int NormalFontSize = 48;
        private const int ZeroBreakFontSize = 64;
        private const int CounterFontSize = 44;
        private const float CardBreakPunch = 1.6f;
        private const float ZeroBreakPunch = 2f;
        private const float CounterPunch = 1.5f;
        private const float CardBreakFlashAlpha = 0.25f;
        private const float ZeroBreakFlashAlpha = 0.45f;
        /// <summary>Counter popup sits above the victim's head, clear of the clash popup.</summary>
        private static readonly Vector3 CounterOffset = new Vector3(0f, 2.4f, 0f);
        private static readonly Color PlayerWinColor = new Color(0.5f, 1f, 0.6f);
        private static readonly Color PlayerLoseColor = new Color(1f, 0.4f, 0.35f);
        private static readonly Color LoseFlashColor = new Color(1f, 0.15f, 0.1f);
        private static readonly Color CounterColor = new Color(1f, 0.82f, 0.25f);

        private Popup clashPopup;
        private Popup counterPopup;
        private Image screenFlash;
        private float flashRemaining;
        private float flashPeak;

        public string ShownText => clashPopup?.ShownText;
        public float PopupScale => clashPopup != null ? clashPopup.Scale : 1f;
        public string CounterText => counterPopup?.ShownText;
        public float ScreenFlashAlpha => screenFlash != null && screenFlash.enabled ? screenFlash.color.a : 0f;
        public Color ScreenFlashColor => screenFlash != null ? screenFlash.color : Color.clear;

        private void Awake()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "ClashFeedbackCanvas", sortingOrder: 10);
            // Created first so it draws behind the popup text.
            screenFlash = UiFactory.CreateImage(canvas.transform, "ScreenFlash", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
            var flashRect = screenFlash.rectTransform;
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.sizeDelta = Vector2.zero;
            screenFlash.enabled = false;

            clashPopup = new Popup(canvas.transform, "Popup");
            counterPopup = new Popup(canvas.transform, "CounterPopup");
        }

        private void OnEnable()
        {
            ClashSystem.ClashOccurred += ShowClash;
            CombatantStatus.CounterHit += ShowCounter;
        }

        private void OnDisable()
        {
            ClashSystem.ClashOccurred -= ShowClash;
            CombatantStatus.CounterHit -= ShowCounter;
        }

        private void ShowClash(ClashEvent e)
        {
            switch (e.Result.Outcome)
            {
                case ClashOutcome.Neutral:
                    clashPopup.Show("CLASH", Color.white, NormalFontSize, 1.2f, e.Position, showSeconds);
                    break;
                case ClashOutcome.AWins:
                case ClashOutcome.BWins:
                    var winner = e.Result.Outcome == ClashOutcome.AWins ? e.A : e.B;
                    bool playerWon = winner.Team == Team.Player;
                    bool zero = e.Result.BrokeByZero;
                    clashPopup.Show(zero ? "ZERO BREAK!" : "CARD BREAK!", playerWon ? PlayerWinColor : PlayerLoseColor,
                        zero ? ZeroBreakFontSize : NormalFontSize, zero ? ZeroBreakPunch : CardBreakPunch, e.Position, showSeconds);
                    StartScreenFlash(playerWon ? Color.white : LoseFlashColor, zero ? ZeroBreakFlashAlpha : CardBreakFlashAlpha);
                    break;
            }
        }

        private void ShowCounter(CombatantStatus victim, int damage)
        {
            if (victim == null) return;
            counterPopup.Show("COUNTER!", CounterColor, CounterFontSize, CounterPunch,
                victim.transform.position + CounterOffset, showSeconds);
        }

        private void StartScreenFlash(Color color, float alpha)
        {
            flashPeak = alpha;
            flashRemaining = screenFlashSeconds;
            color.a = alpha;
            screenFlash.color = color;
            screenFlash.enabled = true;
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateScreenFlash(dt);
            var cam = Camera.main;
            clashPopup.Tick(dt, cam, punchSeconds, riseScreenPixels);
            counterPopup.Tick(dt, cam, punchSeconds, riseScreenPixels);
        }

        private void UpdateScreenFlash(float dt)
        {
            if (flashRemaining <= 0f) return;
            flashRemaining -= dt;
            if (flashRemaining <= 0f)
            {
                screenFlash.enabled = false;
                return;
            }
            var c = screenFlash.color;
            c.a = flashPeak * (flashRemaining / screenFlashSeconds);
            screenFlash.color = c;
        }

        /// <summary>World-anchored text that punches in, rises and fades.</summary>
        private sealed class Popup
        {
            private readonly RectTransform rect;
            private readonly Text text;
            private Vector3 worldPosition;
            private float duration;
            private float remaining;
            private float punchFrom = 1f;

            public string ShownText => text.enabled ? text.text : null;
            public float Scale => rect.localScale.x;

            public Popup(Transform canvas, string name)
            {
                rect = UiFactory.CreateRect(canvas, name, Vector2.zero, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(400f, 80f));
                text = UiFactory.CreateText(rect, name + "Text", NormalFontSize, TextAnchor.MiddleCenter, Color.white);
                text.enabled = false;
            }

            public void Show(string message, Color color, int fontSize, float punch, Vector3 position, float seconds)
            {
                text.text = message;
                text.color = color;
                text.fontSize = fontSize;
                punchFrom = punch;
                worldPosition = position;
                duration = remaining = seconds;
                text.enabled = true;
                rect.localScale = Vector3.one * punch;
                Place(Camera.main, 0f, 0f);
            }

            public void Tick(float dt, Camera cam, float punchSeconds, float risePixels)
            {
                if (remaining <= 0f) return;
                remaining -= dt;
                if (remaining <= 0f)
                {
                    text.enabled = false;
                    rect.localScale = Vector3.one;
                    return;
                }

                float t = 1f - remaining / duration;
                // Ease-out punch: starts big and snaps down to normal size.
                float p = Mathf.Clamp01((duration - remaining) / punchSeconds);
                float ease = 1f - (1f - p) * (1f - p) * (1f - p);
                rect.localScale = Vector3.one * Mathf.Lerp(punchFrom, 1f, ease);

                Place(cam, t, risePixels);
                var c = text.color;
                c.a = 1f - t * t;
                text.color = c;
            }

            private void Place(Camera cam, float t, float risePixels)
            {
                if (cam == null) return;
                var canvasRect = (RectTransform)rect.parent;
                var screen = cam.WorldToScreenPoint(worldPosition);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = local + Vector2.up * (risePixels * t);
            }
        }
    }
}
