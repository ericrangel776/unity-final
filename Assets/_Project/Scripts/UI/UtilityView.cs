using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Shared frame for the Blue rooms (Shop/Forge/Rest): opens when the run enters a Utility room whose visit is
    /// this screen's, has a header + status line, a row of option tiles, a deck list and a Leave button.
    /// `Enter`/Leave calls <see cref="RunDirector.LeaveUtility"/>; `Esc` steps back out of a sub-mode first, then leaves.
    /// </summary>
    public abstract class UtilityView : RunScreen
    {
        protected static readonly Vector2 TileSize = new Vector2(300f, 400f);
        protected const float Gap = 26f;
        protected static readonly Color BlueColor = new Color(0.2f, 0.36f, 0.66f);

        protected OptionTile[] Tiles;
        protected DeckListView DeckList;
        private Text header, status;
        private Button leaveButton;

        public OptionTile Tile(int i) => Tiles[i];
        public DeckListView Deck => DeckList;
        public string HeaderText => header.text;
        public string StatusText => status.text;

        /// <summary>True when the run's current Blue visit belongs to this screen.</summary>
        protected abstract bool HasVisit { get; }
        protected abstract int TileCount { get; }
        protected abstract string HintText { get; }

        protected override void Build(RectTransform root)
        {
            header = RunWidgets.Header(root, "Header");
            status = RunWidgets.Label(root, "Status", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -160f), new Vector2(1600f, 40f), 28, TextAnchor.MiddleCenter, RunWidgets.Gold);
            RunWidgets.Hint(root).text = HintText;

            Tiles = new OptionTile[TileCount];
            for (int i = 0; i < TileCount; i++)
            {
                int index = i;
                Tiles[i] = RunWidgets.Tile(root, $"Option{i + 1}", Vector2.zero, TileSize, () => Select(index));
                Tiles[i].Hotkey.text = $"[{i + 1}]";
                RunWidgets.Paint(Tiles[i], BlueColor);
            }
            DeckList = new DeckListView(root, new Vector2(0f, 10f));
            leaveButton = RunWidgets.TextButton(root, "Leave", new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(20f, 100f), new Vector2(300f, 60f), "Leave", Confirm, out _);
            BuildExtras(root);
            if (Director != null) Director.StateChanged += HandleStateChanged;
        }

        /// <summary>Extra buttons for a subclass (the Shop's Purge button).</summary>
        protected virtual void BuildExtras(RectTransform root) { }

        private void OnDestroy()
        {
            if (Director != null) Director.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(RunDirectorState state)
        {
            if (state != RunDirectorState.Utility || !HasVisit) return;
            ResetMode(); // a visit left via the debug menu could have left a sub-mode open
            Screens.Show(this);
        }

        /// <summary>Back to the screen's first mode for a fresh visit.</summary>
        protected virtual void ResetMode() { }

        public override bool ValidIn(RunDirectorState state) => state == RunDirectorState.Utility && HasVisit;

        public override void Refresh()
        {
            if (!HasVisit) return;
            header.text = RunUiText.MapCardName(Director.Run.CurrentRoom.Kind) +
                          $"  ·  Quality {Director.Run.CurrentRoom.Number}";
            status.text = $"Currency {Director.Run.Currency}  ·  HP {Director.Run.Hp}/{Director.Run.MaxHp}  ·  Deck {Director.Run.Deck.Count}";
            Redraw();
        }

        /// <summary>Draws the tiles / deck list for the current mode.</summary>
        protected abstract void Redraw();

        /// <summary>Leaves the room and deals the next door.</summary>
        public override void Confirm()
        {
            if (Director == null || Director.State != RunDirectorState.Utility) return;
            Screens.Hide();
            Director.LeaveUtility();
        }

        /// <summary>Shows the first <paramref name="count"/> tiles in a centred row and hides the deck list.</summary>
        protected void ShowTiles(int count)
        {
            DeckList.Hide();
            float start = -(count - 1) * (TileSize.x + Gap) / 2f;
            for (int i = 0; i < Tiles.Length; i++)
            {
                Tiles[i].SetVisible(i < count);
                Tiles[i].Rect.anchoredPosition = new Vector2(start + i * (TileSize.x + Gap), 20f);
            }
        }

        protected void HideTiles()
        {
            foreach (var tile in Tiles) tile.SetVisible(false);
        }
    }
}
