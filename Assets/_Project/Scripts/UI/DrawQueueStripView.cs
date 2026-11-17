using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// The player's full draw order as a compact chip row along the bottom edge, next card leftmost
    /// (docs/specs/m4-overview.md). Redrawn on every deck change (play, draw, reload, shuffle). Chips shrink to a
    /// minimum width; past that the tail collapses into a <c>+N</c> chip. An empty queue shows <c>Reload</c>.
    /// </summary>
    public sealed class DrawQueueStripView : MonoBehaviour
    {
        [SerializeField] private DeckRuntimeManager deck;

        public const float StripWidth = 640f;
        public const float MaxChipWidth = 40f;
        public const float MinChipWidth = 22f;
        public const float Gap = 3f;
        private const float ChipHeight = 24f;
        private const float LabelWidth = 64f;

        private static readonly Color PremiumOutline = new Color(1f, 0.85f, 0.3f, 1f);
        private static readonly Color EmptyColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);

        private readonly List<Image> chips = new List<Image>();
        private readonly List<Text> chipTexts = new List<Text>();
        private readonly List<Outline> chipOutlines = new List<Outline>();
        private readonly List<string> shown = new List<string>();
        private RectTransform row;
        private Text emptyText;

        /// <summary>Visible chip labels, left to right (a trailing <c>+N</c> chip included). Empty when the queue is.</summary>
        public IReadOnlyList<string> ChipLabels => shown;
        public bool ShowsReload => emptyText != null && emptyText.gameObject.activeSelf;

        public void Bind(DeckRuntimeManager deckManager)
        {
            Unsubscribe();
            deck = deckManager;
            Subscribe();
            Refresh();
        }

        /// <summary>How many chips fit at the minimum width.</summary>
        public static int Capacity => Mathf.FloorToInt((StripWidth + Gap) / (MinChipWidth + Gap));

        /// <summary>Chip width for <paramref name="count"/> chips (clamped to [min, max]).</summary>
        public static float ChipWidth(int count) =>
            count <= 0 ? MaxChipWidth : Mathf.Clamp((StripWidth + Gap) / count - Gap, MinChipWidth, MaxChipWidth);

        private void Awake()
        {
            if (deck == null)
            {
                var player = FindAnyObjectByType<PlayerCombatInput>();
                if (player != null) deck = player.GetComponent<DeckRuntimeManager>();
            }
            Build();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (deck != null && isActiveAndEnabled) deck.Changed += Refresh;
        }

        private void Unsubscribe()
        {
            if (deck != null) deck.Changed -= Refresh;
        }

        private void Build()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "DrawQueueCanvas", sortingOrder: 1);
            var root = UiFactory.CreateRect(canvas.transform, "DrawQueue", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(LabelWidth + StripWidth, ChipHeight));

            var label = UiFactory.CreateRect(root, "Label", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(LabelWidth, ChipHeight));
            UiFactory.CreateText(label, "LabelText", 16, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.75f)).text = "NEXT";

            row = UiFactory.CreateRect(root, "Chips", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(LabelWidth, 0f), new Vector2(StripWidth, ChipHeight));
            var empty = UiFactory.CreateRect(row, "Empty", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(StripWidth, ChipHeight));
            emptyText = UiFactory.CreateText(empty, "EmptyText", 16, TextAnchor.MiddleLeft, EmptyColor);
            emptyText.text = "Reload";
        }

        private Image Chip(int i)
        {
            while (chips.Count <= i)
            {
                var image = UiFactory.CreateImage(row, $"Chip{chips.Count + 1}", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    Vector2.zero, new Vector2(MaxChipWidth, ChipHeight), Color.white);
                chips.Add(image);
                chipTexts.Add(UiFactory.CreateText(image.transform, "Value", 16, TextAnchor.MiddleCenter, Color.white));
                var outline = image.gameObject.AddComponent<Outline>();
                outline.effectColor = PremiumOutline;
                outline.effectDistance = new Vector2(2f, 2f);
                chipOutlines.Add(outline);
            }
            return chips[i];
        }

        private void Refresh()
        {
            if (row == null) return;
            shown.Clear();
            var cards = new List<RuntimeCardInstance>();
            if (deck != null) cards.AddRange(deck.Cycle.DrawOrder);

            // Over capacity: the last chip becomes "+N" for everything that doesn't fit.
            bool overflow = cards.Count > Capacity;
            int cardChips = overflow ? Capacity - 1 : cards.Count;
            int total = overflow ? Capacity : cards.Count;
            float width = ChipWidth(total);

            for (int i = 0; i < total; i++)
            {
                var chip = Chip(i);
                chip.gameObject.SetActive(true);
                chip.rectTransform.anchoredPosition = new Vector2(i * (width + Gap), 0f);
                chip.rectTransform.sizeDelta = new Vector2(width, ChipHeight);
                if (i < cardChips)
                {
                    var card = cards[i];
                    chip.color = ElementColors.For(card.Data.Element) * new Color(1f, 1f, 1f, 1.8f);
                    chipTexts[i].text = card.GetEffectiveValue().ToString();
                    chipOutlines[i].enabled = card.IsPremium;
                }
                else
                {
                    chip.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
                    chipTexts[i].text = $"+{cards.Count - cardChips}";
                    chipOutlines[i].enabled = false;
                }
                shown.Add(chipTexts[i].text);
            }
            for (int i = total; i < chips.Count; i++) chips[i].gameObject.SetActive(false);
            emptyText.gameObject.SetActive(cards.Count == 0);
        }
    }
}
