using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Sleight stock row above the hand: the stocked cards (a 0 shows what it mirrors), a live partial
    /// preview ("SLEIGHT 12+ · Throw · Fire"), and a bar draining toward the stock timeout.
    /// Hidden while nothing is stocked. Polls every frame because the timeout changes continuously.
    /// </summary>
    public sealed class StockView : MonoBehaviour
    {
        [SerializeField] private DeckRuntimeManager deck;

        private const float TimeoutWidth = 420f;
        private static readonly Color EmptySlot = new Color(0.1f, 0.1f, 0.1f, 0.55f);
        private static readonly Color TimeoutColor = new Color(1f, 0.85f, 0.3f, 0.9f);

        private readonly Image[] slots = new Image[SleightRules.StockSize];
        private readonly Text[] slotTexts = new Text[SleightRules.StockSize];
        private RectTransform row;
        private Text preview;
        private Image timeoutFill;

        public bool IsVisible => row != null && row.gameObject.activeSelf;
        public string SlotText(int i) => slotTexts[i].text;
        public string PreviewText => preview.text;
        public float TimeoutFill01 => timeoutFill.rectTransform.sizeDelta.x / TimeoutWidth;

        public void Bind(DeckRuntimeManager deckManager)
        {
            deck = deckManager;
            Refresh();
        }

        private void Awake()
        {
            if (deck == null)
            {
                var player = FindAnyObjectByType<PlayerCombatInput>();
                if (player != null) deck = player.GetComponent<DeckRuntimeManager>();
            }
            Build();
        }

        private void LateUpdate() => Refresh();

        private void Build()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "StockCanvas", sortingOrder: 1);
            row = UiFactory.CreateRect(canvas.transform, "Stock", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 300f), new Vector2(760f, 110f));

            var label = UiFactory.CreateRect(row, "Label", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(-10f, 10f), new Vector2(120f, 40f));
            UiFactory.CreateText(label, "LabelText", 24, TextAnchor.MiddleLeft, TimeoutColor).text = "STOCK";

            for (int i = 0; i < SleightRules.StockSize; i++)
            {
                slots[i] = UiFactory.CreateImage(row, $"StockSlot{i + 1}", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(100f + i * 92f, 10f), new Vector2(84f, 84f), EmptySlot);
                slotTexts[i] = UiFactory.CreateText(slots[i].transform, "Value", 34, TextAnchor.MiddleCenter, Color.white);
            }

            var previewRect = UiFactory.CreateRect(row, "Preview", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(390f, 10f), new Vector2(380f, 84f));
            preview = UiFactory.CreateText(previewRect, "PreviewText", 24, TextAnchor.MiddleLeft, Color.white);

            UiFactory.CreateImage(row, "TimeoutBack", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(100f, 0f), new Vector2(TimeoutWidth, 6f), EmptySlot);
            timeoutFill = UiFactory.CreateImage(row, "TimeoutFill", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(100f, 0f), new Vector2(TimeoutWidth, 6f), TimeoutColor);
            row.gameObject.SetActive(false);
        }

        private void Refresh()
        {
            if (row == null) return;
            var stock = deck != null ? deck.Cycle.Stock : null;
            bool visible = stock != null && stock.Count > 0;
            if (row.gameObject.activeSelf != visible) row.gameObject.SetActive(visible);
            if (!visible) return;

            for (int i = 0; i < SleightRules.StockSize; i++)
            {
                if (i >= stock.Count)
                {
                    slots[i].color = EmptySlot;
                    slotTexts[i].text = "";
                    continue;
                }
                var card = stock[i];
                int shown = SleightRules.PreviewSlotValue(stock, i);
                slots[i].color = card.IsZeroBreaker ? ElementColors.Zero : Opaque(ElementColors.For(card.Data.Element));
                slotTexts[i].text = card.IsZeroBreaker ? $"0→{shown}" : shown.ToString();
            }

            preview.text = PreviewLine(stock);
            float remaining = 1f - Mathf.Clamp01(deck.Cycle.StockIdleSeconds / SleightRules.StockTimeoutSeconds);
            timeoutFill.rectTransform.sizeDelta = new Vector2(TimeoutWidth * remaining, 6f);
        }

        /// <summary>"SLEIGHT 12+ · Throw · Fire" (+ = partial; the 3rd card fires it and sets the finisher multiplier).</summary>
        private static string PreviewLine(IReadOnlyList<RuntimeCardInstance> stock)
        {
            int total = SleightRules.PreviewTotal(stock);
            string plus = stock.Count < SleightRules.StockSize ? "+" : "";
            string line = $"SLEIGHT {total}{plus} · {Resolve(stock, 0).Data.Shape}";
            if (stock.Count > 1) line += $" · {Resolve(stock, 1).Data.Element}";
            if (stock.Count == SleightRules.StockSize - 1) line += "\n3rd card = finisher x(1 + v/10)";
            return line;
        }

        /// <summary>The card whose shape/element a slot shows: itself, or the best numbered card for a 0.</summary>
        private static RuntimeCardInstance Resolve(IReadOnlyList<RuntimeCardInstance> stock, int slot)
        {
            var card = stock[slot];
            if (!card.IsZeroBreaker) return card;
            RuntimeCardInstance best = null;
            foreach (var c in stock)
                if (!c.IsZeroBreaker && (best == null || c.GetEffectiveValue() > best.GetEffectiveValue())) best = c;
            return best ?? card;
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 0.9f);
    }
}
