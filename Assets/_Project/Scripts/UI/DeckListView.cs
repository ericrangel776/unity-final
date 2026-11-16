using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// A grid of the run deck's cards as buttons (Shop purge, Forge upgrade/Premium). Each cell shows the value, name,
    /// shape and element, and why it can't be chosen. A click <b>selects</b> a card (highlighted); the screen applies it
    /// on Confirm (docs/specs/m3-blue-polish.md). Mouse-driven: a deck can be longer than the number keys.
    /// <see cref="Pick"/> is the same call a click makes, for tests.
    /// </summary>
    public sealed class DeckListView
    {
        public const int Columns = 3;
        public const int MaxCards = 30;
        private static readonly Vector2 CellSize = new Vector2(560f, 42f);
        private const float Gap = 6f;
        private static readonly Color SelectedColor = new Color(0.85f, 0.62f, 0.2f, 1f);

        private readonly RectTransform root;
        private readonly Button[] buttons = new Button[MaxCards];
        private readonly Text[] labels = new Text[MaxCards];
        private IReadOnlyList<RuntimeCardInstance> cards;
        private Action<RuntimeCardInstance> onSelect;

        public bool Visible => root.gameObject.activeSelf;
        public int Count => cards != null ? Mathf.Min(cards.Count, MaxCards) : 0;
        public string Label(int i) => labels[i].text;
        public bool IsEnabled(int i) => i >= 0 && i < Count && buttons[i].interactable;
        public bool IsSelected(int i) => i >= 0 && i < Count && Selected != null && cards[i] == Selected;
        /// <summary>The selected card, or null.</summary>
        public RuntimeCardInstance Selected { get; private set; }

        public DeckListView(Transform parent, Vector2 position)
        {
            int rows = Mathf.CeilToInt(MaxCards / (float)Columns);
            var size = new Vector2(Columns * (CellSize.x + Gap) - Gap, rows * (CellSize.y + Gap) - Gap);
            root = UiFactory.CreateRect(parent, "DeckList", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            for (int i = 0; i < MaxCards; i++)
            {
                int index = i;
                int col = i % Columns, row = i / Columns;
                var pos = new Vector2(col * (CellSize.x + Gap), -row * (CellSize.y + Gap));
                buttons[i] = RunWidgets.TextButton(root, $"Card{i + 1}", new Vector2(0f, 1f), new Vector2(0f, 1f), pos,
                    CellSize, "", () => Pick(index), out labels[i], RunWidgets.CellColor);
                labels[i].fontSize = 20;
                labels[i].alignment = TextAnchor.MiddleLeft;
                labels[i].rectTransform.offsetMin = new Vector2(16f, 0f); // left padding
            }
            root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows <paramref name="deck"/> with nothing selected. <paramref name="blockedReason"/> returns why a card can't
        /// be chosen (greyed out, reason in its cell) or null if it can. <paramref name="select"/> runs on each selection.
        /// </summary>
        public void Show(IReadOnlyList<RuntimeCardInstance> deck, Func<RuntimeCardInstance, string> blockedReason,
            Action<RuntimeCardInstance> select)
        {
            cards = deck;
            onSelect = select;
            Selected = null;
            root.gameObject.SetActive(true);
            for (int i = 0; i < MaxCards; i++)
            {
                bool used = i < Count;
                buttons[i].gameObject.SetActive(used);
                if (!used) continue;
                string reason = blockedReason != null ? blockedReason(deck[i]) : null;
                labels[i].text = RunUiText.DeckCell(deck[i], reason);
                buttons[i].interactable = reason == null;
            }
            Highlight();
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
            cards = null;
            onSelect = null;
            Selected = null;
        }

        /// <summary>Selects deck card <paramref name="index"/> if it's shown and enabled.</summary>
        public void Pick(int index)
        {
            if (!Visible || !IsEnabled(index)) return;
            Selected = cards[index];
            Highlight();
            onSelect?.Invoke(Selected);
        }

        private void Highlight()
        {
            for (int i = 0; i < Count; i++)
                buttons[i].colors = RunWidgets.Tint(cards[i] == Selected ? SelectedColor : RunWidgets.CellColor);
        }
    }
}
