using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>Pops "CARD BREAK!" / "ZERO BREAK!" / "CLASH" at the clash point, fading on unscaled time.</summary>
    public sealed class ClashFeedbackView : MonoBehaviour
    {
        [SerializeField] private float showSeconds = 0.7f;
        [SerializeField] private float riseScreenPixels = 60f;

        private RectTransform popup;
        private Text text;
        private Vector3 worldPosition;
        private float remaining;

        private void Awake()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "ClashFeedbackCanvas", sortingOrder: 10);
            popup = UiFactory.CreateRect(canvas.transform, "Popup", Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(400f, 80f));
            text = UiFactory.CreateText(popup, "PopupText", 48, TextAnchor.MiddleCenter, Color.white);
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
                    break;
                case ClashOutcome.AWins:
                case ClashOutcome.BWins:
                    var winner = e.Result.Outcome == ClashOutcome.AWins ? e.A : e.B;
                    text.text = e.Result.BrokeByZero ? "ZERO BREAK!" : "CARD BREAK!";
                    text.color = winner.Team == Team.Player ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f);
                    break;
                default:
                    return;
            }
            worldPosition = e.Position;
            remaining = showSeconds;
            text.enabled = true;
        }

        private void LateUpdate()
        {
            if (remaining <= 0f) return;
            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f)
            {
                text.enabled = false;
                return;
            }

            var cam = Camera.main;
            if (cam == null) return;
            float t = 1f - remaining / showSeconds;
            var canvasRect = (RectTransform)popup.parent;
            var screen = cam.WorldToScreenPoint(worldPosition);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
            popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
            popup.anchoredPosition = local + Vector2.up * (riseScreenPixels * t);
            var c = text.color;
            c.a = 1f - t * t;
            text.color = c;
        }
    }
}
