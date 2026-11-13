using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Pops "CARD BREAK!" / "ZERO BREAK!" / "CLASH" at the clash point with a scale punch, and flashes
    /// the whole screen on breaks (red when the player's card was the one broken). Runs on unscaled
    /// time so it animates through the hit-stop freeze.
    /// </summary>
    public sealed class ClashFeedbackView : MonoBehaviour
    {
        [SerializeField] private float showSeconds = 0.7f;
        [SerializeField] private float riseScreenPixels = 60f;
        [SerializeField] private float punchSeconds = 0.18f;
        [SerializeField] private float screenFlashSeconds = 0.25f;

        private const int NormalFontSize = 48;
        private const int ZeroBreakFontSize = 64;
        private const float CardBreakPunch = 1.6f;
        private const float ZeroBreakPunch = 2f;
        private const float CardBreakFlashAlpha = 0.25f;
        private const float ZeroBreakFlashAlpha = 0.45f;
        private static readonly Color PlayerWinColor = new Color(0.5f, 1f, 0.6f);
        private static readonly Color PlayerLoseColor = new Color(1f, 0.4f, 0.35f);
        private static readonly Color LoseFlashColor = new Color(1f, 0.15f, 0.1f);

        private RectTransform popup;
        private Text text;
        private Image screenFlash;
        private Vector3 worldPosition;
        private float remaining;
        private float punchFrom = 1f;
        private float flashRemaining;
        private float flashPeak;

        public string ShownText => text != null && text.enabled ? text.text : null;
        public float PopupScale => popup != null ? popup.localScale.x : 1f;
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

            popup = UiFactory.CreateRect(canvas.transform, "Popup", Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(400f, 80f));
            text = UiFactory.CreateText(popup, "PopupText", NormalFontSize, TextAnchor.MiddleCenter, Color.white);
            text.enabled = false;
        }

        private void OnEnable() => ClashSystem.ClashOccurred += Show;
        private void OnDisable() => ClashSystem.ClashOccurred -= Show;

        private void Show(ClashEvent e)
        {
            switch (e.Result.Outcome)
            {
                case ClashOutcome.Neutral:
                    text.text = "CLASH";
                    text.color = Color.white;
                    text.fontSize = NormalFontSize;
                    punchFrom = 1.2f;
                    break;
                case ClashOutcome.AWins:
                case ClashOutcome.BWins:
                    var winner = e.Result.Outcome == ClashOutcome.AWins ? e.A : e.B;
                    bool playerWon = winner.Team == Team.Player;
                    bool zero = e.Result.BrokeByZero;
                    text.text = zero ? "ZERO BREAK!" : "CARD BREAK!";
                    text.color = playerWon ? PlayerWinColor : PlayerLoseColor;
                    text.fontSize = zero ? ZeroBreakFontSize : NormalFontSize;
                    punchFrom = zero ? ZeroBreakPunch : CardBreakPunch;
                    StartScreenFlash(playerWon ? Color.white : LoseFlashColor, zero ? ZeroBreakFlashAlpha : CardBreakFlashAlpha);
                    break;
                default:
                    return;
            }
            worldPosition = e.Position;
            remaining = showSeconds;
            text.enabled = true;
            popup.localScale = Vector3.one * punchFrom;
            PositionPopup(0f);
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

            if (remaining <= 0f) return;
            remaining -= dt;
            if (remaining <= 0f)
            {
                text.enabled = false;
                popup.localScale = Vector3.one;
                return;
            }

            float t = 1f - remaining / showSeconds;
            // Ease-out punch: starts big and snaps down to normal size.
            float elapsed = showSeconds - remaining;
            float p = Mathf.Clamp01(elapsed / punchSeconds);
            float ease = 1f - (1f - p) * (1f - p) * (1f - p);
            popup.localScale = Vector3.one * Mathf.Lerp(punchFrom, 1f, ease);

            PositionPopup(t);
            var c = text.color;
            c.a = 1f - t * t;
            text.color = c;
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

        private void PositionPopup(float t)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var canvasRect = (RectTransform)popup.parent;
            var screen = cam.WorldToScreenPoint(worldPosition);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
            popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
            popup.anchoredPosition = local + Vector2.up * (riseScreenPixels * t);
        }
    }
}
