using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// A grid of the run deck's cards as buttons (Shop purge, Forge upgrade/Premium). Mouse-driven: a deck can be
    /// longer than the number keys. <see cref="Pick"/> is the same call a click makes, for tests.
    /// </summary>
    public sealed class DeckListView
    {
        public const int Columns = 5;
        public const int MaxCards = 30;
        private static readonly Vector2 CellSize = new Vector2(300f, 52f);
        private const float Gap = 10f;

        private readonly RectTransform root;
        private readonly Button[] buttons = new Button[MaxCards];
        private readonly Text[] labels = new Text[MaxCards];
        private IReadOnlyList<RuntimeCardInstance> cards;
        private Action<RuntimeCardInstance> onPick;

        public bool Visible => root.gameObject.activeSelf;
        public int Count => cards != null ? Mathf.Min(cards.Count, MaxCards) : 0;
        public string Label(int i) => labels[i].text;
        public bool IsEnabled(int i) => i >= 0 && i < Count && buttons[i].interactable;

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
                    CellSize, "", () => Pick(index), out labels[i]);
                labels[i].fontSize = 22;
            }
            root.gameObject.SetActive(false);
        }

        /// <summary>Shows <paramref name="deck"/>; cards failing <paramref name="enabled"/> are greyed out.</summary>
        public void Show(IReadOnlyList<RuntimeCardInstance> deck, Func<RuntimeCardInstance, bool> enabled,
            Action<RuntimeCardInstance> pick)
        {
            cards = deck;
            onPick = pick;
            root.gameObject.SetActive(true);
            for (int i = 0; i < MaxCards; i++)
            {
                bool used = i < Count;
                buttons[i].gameObject.SetActive(used);
                if (!used) continue;
                labels[i].text = RunUiText.DeckCardLabel(deck[i]);
                buttons[i].interactable = enabled == null || enabled(deck[i]);
            }
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
            cards = null;
            onPick = null;
        }

        /// <summary>Picks deck card <paramref name="index"/> if it's shown and enabled.</summary>
        public void Pick(int index)
        {
            if (!Visible || !IsEnabled(index)) return;
            onPick?.Invoke(cards[index]);
        }
    }
}
