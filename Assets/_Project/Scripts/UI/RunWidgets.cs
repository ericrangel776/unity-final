using System;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// A clickable card-shaped option on a run screen: hotkey hint, a corner badge (number + label), title, body and
    /// footer. Every run screen uses these, so the screens look alike and tests read the same fields everywhere.
    /// </summary>
    public sealed class OptionTile
    {
        public RectTransform Rect;
        public Button Button;
        public Image Back;
        public Text Hotkey, Badge, BadgeLabel, Title, Body, Footer;

        public bool Visible => Rect.gameObject.activeSelf;
        public bool Interactable => Button.interactable;

        public void SetVisible(bool visible)
        {
            if (Rect.gameObject.activeSelf != visible) Rect.gameObject.SetActive(visible);
        }
    }

    /// <summary>Greybox widget builders for the run screens (on top of <see cref="UiFactory"/>).</summary>
    internal static class RunWidgets
    {
        public static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.3f, 1f);
        public static readonly Color Muted = new Color(0.75f, 0.78f, 0.85f);
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
        public static readonly Color CellColor = new Color(0.32f, 0.35f, 0.45f, 1f);

        /// <summary>
        /// Layout bands in the 1920x1080 reference. The HUD strip owns the top ~140 px; the combat HUD is hidden while a
        /// screen is open, so the bottom is free. Top-anchored: header, subheader, prompt. Bottom-anchored: feedback,
        /// buttons, key hint.
        /// </summary>
        public const float HeaderY = -175f, SubheaderY = -232f, PromptY = -280f;
        public const float FeedbackY = 205f, ButtonY = 115f, HintY = 60f;
        public static readonly Color Warning = new Color(1f, 0.45f, 0.4f);

        /// <summary>A text label in its own rect, so it can be placed inside a parent.</summary>
        public static Text Label(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position,
            Vector2 size, int fontSize, TextAnchor alignment, Color color, bool wrap = false)
        {
            var rect = UiFactory.CreateRect(parent, name, anchor, pivot, position, size);
            var text = UiFactory.CreateText(rect, name + "Text", fontSize, alignment, color);
            if (wrap) text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        /// <summary>A plain text button (Continue, Leave, Skip...).</summary>
        public static Button TextButton(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position,
            Vector2 size, string label, Action onClick, out Text labelText) =>
            TextButton(parent, name, anchor, pivot, position, size, label, onClick, out labelText, ButtonColor);

        public static Button TextButton(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position,
            Vector2 size, string label, Action onClick, out Text labelText, Color color)
        {
            var image = UiFactory.CreateImage(parent, name, anchor, pivot, position, size, Color.white); // tinted below
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = Tint(color);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            labelText = UiFactory.CreateText(image.transform, "Label", 26, TextAnchor.MiddleCenter, Color.white);
            labelText.text = label;
            return button;
        }

        /// <summary>A card-shaped option. <paramref name="onClick"/> gets the same call as the matching number key.</summary>
        public static OptionTile Tile(Transform parent, string name, Vector2 position, Vector2 size, Action onClick)
        {
            var tile = new OptionTile();
            tile.Back = UiFactory.CreateImage(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position, size, Color.white); // tinted by the ColorBlock (Paint)
            tile.Back.raycastTarget = true;
            tile.Rect = tile.Back.rectTransform;
            tile.Button = tile.Back.gameObject.AddComponent<Button>();
            tile.Button.targetGraphic = tile.Back;
            tile.Button.colors = Tint(ButtonColor);
            if (onClick != null) tile.Button.onClick.AddListener(() => onClick());

            var top = new Vector2(0.5f, 1f);
            float w = size.x - 32f;
            tile.Hotkey = Label(tile.Rect, "Hotkey", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -10f),
                new Vector2(80f, 40f), 26, TextAnchor.UpperLeft, Muted);
            tile.Badge = Label(tile.Rect, "Badge", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -6f),
                new Vector2(120f, 70f), 60, TextAnchor.UpperRight, Color.white);
            tile.BadgeLabel = Label(tile.Rect, "BadgeLabel", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -76f),
                new Vector2(160f, 24f), 16, TextAnchor.UpperRight, Muted);
            tile.Title = Label(tile.Rect, "Title", top, top, new Vector2(0f, -112f), new Vector2(w, 44f), 32,
                TextAnchor.MiddleCenter, Color.white);
            tile.Body = Label(tile.Rect, "Body", top, top, new Vector2(0f, -162f), new Vector2(w, size.y - 230f), 22,
                TextAnchor.UpperCenter, Color.white, wrap: true);
            tile.Footer = Label(tile.Rect, "Footer", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f),
                new Vector2(w, 40f), 24, TextAnchor.MiddleCenter, Gold);
            return tile;
        }

        /// <summary>Sets a tile's colour; the button tint darkens it when disabled.</summary>
        public static void Paint(OptionTile tile, Color color)
        {
            tile.Back.color = Color.white; // the ColorBlock tints this
            tile.Button.colors = Tint(color);
        }

        internal static ColorBlock Tint(Color color)
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
            colors.selectedColor = color;
            colors.disabledColor = Color.Lerp(color, new Color(0.15f, 0.15f, 0.15f, 1f), 0.7f);
            colors.fadeDuration = 0.05f;
            return colors;
        }

        /// <summary>The screen title band at the top of a run screen (below the HUD strip).</summary>
        public static Text Header(Transform root, string name)
        {
            return Label(root, name, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, HeaderY),
                new Vector2(1600f, 60f), 44, TextAnchor.MiddleCenter, Color.white);
        }

        /// <summary>The key-hint line at the bottom of a run screen.</summary>
        public static Text Hint(Transform root)
        {
            return Label(root, "Hint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, HintY),
                new Vector2(1600f, 36f), 22, TextAnchor.MiddleCenter, Muted);
        }
    }
}
