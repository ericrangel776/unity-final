using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// The Active Reload bar, drawn on a screen overlay that follows the player and sits at the reload's
    /// rolled <see cref="ReloadPromptAnchor"/>. Shows the rolled Good/Perfect zones, a marker sweeping at the
    /// rolled speed, a draining red fill while jammed, an ember tint during an Overburn, and a short
    /// PERFECT / GOOD / JAMMED popup.
    /// </summary>
    public sealed class ReloadBarView : MonoBehaviour
    {
        [SerializeField] private DeckRuntimeManager deck;
        [SerializeField] private Transform player;
        [SerializeField] private float popupSeconds = 0.6f;

        public const float BarWidth = 260f;
        private const float BarHeight = 22f;
        private const float EdgeMargin = 40f;
        private static readonly Vector2 BarHalfExtents = new Vector2(BarWidth * 0.5f + 30f, 40f);

        private static readonly Color BackColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        private static readonly Color OverburnBackColor = new Color(0.45f, 0.14f, 0.04f, 0.9f);
        private static readonly Color GoodColor = new Color(0.6f, 0.85f, 1f, 0.75f);
        private static readonly Color PerfectColor = new Color(0.55f, 1f, 0.65f, 1f);
        private static readonly Color DimColor = new Color(0.4f, 0.4f, 0.4f, 0.35f);
        private static readonly Color JamColor = new Color(1f, 0.3f, 0.25f, 0.9f);
        private static readonly Color MissTextColor = new Color(1f, 0.45f, 0.4f);

        private Camera cameraOverride;
        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform root;
        private Image back, good, perfect, marker, jamFill;
        private Text title;
        private Text popup;
        private RectTransform popupRect;
        private float popupRemaining;
        private ActiveReload subscribed;
        private ReloadState lastState;

        public bool IsBarVisible => root != null && root.gameObject.activeSelf;
        public float MarkerProgress => marker.rectTransform.anchoredPosition.x / BarWidth;
        public float ZoneCenter01 => good.rectTransform.anchoredPosition.x / BarWidth;
        public float GoodZoneWidth01 => good.rectTransform.sizeDelta.x / BarWidth;
        public bool IsJammedStyle => jamFill.enabled;
        public bool IsOverburnTint => back.color == OverburnBackColor;
        public string GradeText => popup.enabled ? popup.text : null;
        public string TitleText => title.text;
        /// <summary>Where the bar sits relative to the projected player point (after clamping).</summary>
        public Vector2 BarScreenOffset { get; private set; }

        public void Bind(DeckRuntimeManager deckManager, Transform playerTransform, Camera cam = null)
        {
            deck = deckManager;
            player = playerTransform;
            cameraOverride = cam;
        }

        private void Awake()
        {
            if (deck == null)
            {
                var p = FindAnyObjectByType<PlayerCombatInput>();
                if (p != null)
                {
                    deck = p.GetComponent<DeckRuntimeManager>();
                    player = p.transform;
                }
            }
            Build();
        }

        private void OnDisable() => Subscribe(null);

        private void Build()
        {
            canvas = UiFactory.CreateOverlayCanvas(transform, "ReloadBarCanvas", sortingOrder: 5);
            canvasRect = (RectTransform)canvas.transform;
            root = UiFactory.CreateRect(canvasRect, "ReloadBar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(BarWidth, BarHeight));

            back = Bar("Back", 0.5f, BarWidth, BackColor);
            good = Bar("GoodZone", 0.5f, 0f, GoodColor);
            perfect = Bar("PerfectZone", 0.5f, 0f, PerfectColor);
            jamFill = Bar("JamFill", 0f, BarWidth, JamColor);
            jamFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            marker = UiFactory.CreateImage(root, "Marker", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(5f, BarHeight + 14f), Color.white);

            var key = UiFactory.CreateRect(root, "Key", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f), new Vector2(40f, 30f));
            UiFactory.CreateText(key, "KeyText", 24, TextAnchor.MiddleRight, Color.white).text = "R";
            var titleRect = UiFactory.CreateRect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(BarWidth, 26f));
            title = UiFactory.CreateText(titleRect, "TitleText", 20, TextAnchor.LowerCenter, Color.white);

            popupRect = UiFactory.CreateRect(canvasRect, "ReloadGrade", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(300f, 40f));
            popup = UiFactory.CreateText(popupRect, "GradeText", 34, TextAnchor.MiddleCenter, Color.white);
            popup.enabled = false;
            root.gameObject.SetActive(false);
        }

        private Image Bar(string name, float pivotX, float width, Color color)
        {
            var image = UiFactory.CreateImage(root, name, new Vector2(0f, 0.5f), new Vector2(pivotX, 0.5f),
                new Vector2(pivotX * BarWidth, 0f), new Vector2(width, BarHeight), color);
            return image;
        }

        private void LateUpdate()
        {
            var reload = deck != null ? deck.Reload : null;
            Subscribe(reload); // lazily: the deck may have been created after this view
            TickPopup();
            if (reload == null)
            {
                root.gameObject.SetActive(false);
                return;
            }

            // A new bar clears a lingering popup from the previous reload.
            if (lastState == ReloadState.Idle && reload.State == ReloadState.Running) HidePopup();
            if (lastState != ReloadState.Jammed && reload.State == ReloadState.Jammed)
                ShowPopup("JAMMED", MissTextColor);
            lastState = reload.State;

            bool visible = reload.State != ReloadState.Idle && Place(reload.Anchor);
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            if (!visible) return;

            bool jammed = reload.State == ReloadState.Jammed;
            back.color = reload.IsOverburn ? OverburnBackColor : BackColor;
            // The popup already says JAMMED; the title says what the bar is doing now.
            title.text = jammed ? "RECHARGING" : reload.IsOverburn ? "OVERBURN" : "RELOAD";
            SetZone(good, reload.Target01, ActiveReloadRules.GoodTolerance01, jammed ? DimColor : GoodColor);
            SetZone(perfect, reload.Target01, ActiveReloadRules.PerfectTolerance01, jammed ? DimColor : PerfectColor);

            marker.enabled = !jammed;
            marker.rectTransform.anchoredPosition = new Vector2(reload.Progress01 * BarWidth, 0f);
            jamFill.enabled = jammed;
            if (jammed) jamFill.rectTransform.sizeDelta = new Vector2(BarWidth * (1f - reload.Progress01), BarHeight);
        }

        private static void SetZone(Image zone, float center01, float halfWidth01, Color color)
        {
            zone.rectTransform.anchoredPosition = new Vector2(center01 * BarWidth, 0f);
            zone.rectTransform.sizeDelta = new Vector2(halfWidth01 * 2f * BarWidth, BarHeight);
            zone.color = color;
        }

        /// <summary>Positions the bar at the anchor around the projected player. False if the player is behind the camera.</summary>
        private bool Place(ReloadPromptAnchor anchor)
        {
            var basePos = Vector2.zero;
            var cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (cam != null && player != null)
            {
                var screen = cam.WorldToScreenPoint(player.position + Vector3.up);
                if (screen.z < 0f) return false;
                // Overlay canvases take a null camera; camera-space canvases need theirs.
                var canvasCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, canvasCam, out basePos);
            }

            var limit = canvasRect.rect.size * 0.5f - BarHalfExtents - Vector2.one * EdgeMargin;
            limit = Vector2.Max(limit, Vector2.zero);
            var pos = basePos + AnchorOffset(anchor);
            pos = new Vector2(Mathf.Clamp(pos.x, -limit.x, limit.x), Mathf.Clamp(pos.y, -limit.y, limit.y));
            root.anchoredPosition = pos;
            popupRect.anchoredPosition = pos + new Vector2(0f, 64f);
            BarScreenOffset = pos - basePos;
            return true;
        }

        /// <summary>Canvas-unit offset (1920x1080 reference) from the player for each anchor.</summary>
        public static Vector2 AnchorOffset(ReloadPromptAnchor anchor)
        {
            switch (anchor)
            {
                case ReloadPromptAnchor.Above: return new Vector2(0f, 150f);
                case ReloadPromptAnchor.UpperRight: return new Vector2(200f, 110f);
                case ReloadPromptAnchor.Right: return new Vector2(250f, 0f);
                case ReloadPromptAnchor.Below: return new Vector2(0f, -120f);
                case ReloadPromptAnchor.Left: return new Vector2(-250f, 0f);
                default: return new Vector2(-200f, 110f); // UpperLeft
            }
        }

        private void Subscribe(ActiveReload reload)
        {
            if (subscribed == reload) return;
            if (subscribed != null) subscribed.Completed -= HandleCompleted;
            subscribed = reload;
            if (subscribed != null) subscribed.Completed += HandleCompleted;
        }

        private void HandleCompleted(ReloadGrade grade)
        {
            if (grade == ReloadGrade.Perfect) ShowPopup("PERFECT!", PerfectColor);
            else if (grade == ReloadGrade.Good) ShowPopup("GOOD", GoodColor);
        }

        private void ShowPopup(string text, Color color)
        {
            popup.text = text;
            popup.color = new Color(color.r, color.g, color.b, 1f);
            popup.enabled = true;
            popupRemaining = popupSeconds;
        }

        private void HidePopup()
        {
            popup.enabled = false;
            popupRemaining = 0f;
        }

        private void TickPopup()
        {
            if (popupRemaining <= 0f) return;
            popupRemaining -= Time.unscaledDeltaTime;
            if (popupRemaining <= 0f) popup.enabled = false;
        }
    }
}
