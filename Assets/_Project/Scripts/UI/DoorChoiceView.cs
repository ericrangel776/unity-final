using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// The Map Card choice (docs/specs/m3-ui.md). Opens when the player walks into the exit door; one tile per offered
    /// card with its family colour, Intensity/Quality badge, a description and the reward tier.
    /// `1`-`3`/click enters that room, `Esc` closes it so the player can walk around again.
    /// </summary>
    public sealed class DoorChoiceView : RunScreen
    {
        public const int MaxCards = 3;
        private static readonly Vector2 TileSize = new Vector2(400f, 500f);

        private readonly OptionTile[] tiles = new OptionTile[MaxCards];
        private Text header;

        public OptionTile Tile(int i) => tiles[i];
        public int VisibleCount
        {
            get
            {
                int n = 0;
                foreach (var t in tiles) if (t.Visible) n++;
                return n;
            }
        }

        protected override void Build(RectTransform root)
        {
            header = RunWidgets.Header(root, "Header");
            RunWidgets.Hint(root).text = "1-3 or click: enter the room   ·   Esc: step back";
            for (int i = 0; i < MaxCards; i++)
            {
                int index = i;
                tiles[i] = RunWidgets.Tile(root, $"MapCard{i + 1}", new Vector2((i - 1) * (TileSize.x + 40f), 0f),
                    TileSize, () => Select(index));
                tiles[i].Hotkey.text = $"[{i + 1}]";
            }
            if (Director != null) Director.ExitDoorEntered += Open;
        }

        private void OnDestroy()
        {
            if (Director != null) Director.ExitDoorEntered -= Open;
        }

        private void Open() => Screens.Show(this);

        public override void Refresh()
        {
            var run = Director != null ? Director.Run : null;
            var offer = run != null ? run.CurrentOffer : null;
            int count = offer != null ? offer.Count : 0;
            header.text = $"Choose a door  ·  {RunUiText.Room(run != null ? run.RoomNumber : 1)}";

            // Centre whatever is offered (the boss door has a single card).
            float start = -(count - 1) * (TileSize.x + 40f) / 2f;
            for (int i = 0; i < MaxCards; i++)
            {
                var tile = tiles[i];
                tile.SetVisible(i < count);
                if (i >= count) continue;
                var card = offer[i];
                tile.Rect.anchoredPosition = new Vector2(start + i * (TileSize.x + 40f), 0f);
                RunWidgets.Paint(tile, FamilyColor(card.Family));
                tile.Badge.text = card.Number.ToString();
                tile.BadgeLabel.text = RunUiText.NumberLabel(card);
                tile.Title.text = RunUiText.MapCardName(card.Kind);
                tile.Body.text = RunUiText.MapCardBlurb(card);
                tile.Footer.text = RunUiText.TierLine(card);
            }
        }

        /// <summary>Enters offer card <paramref name="index"/>. Ignored if out of range or the run isn't at a door.</summary>
        public override void Select(int index)
        {
            if (Director == null || Director.State != RunDirectorState.ChoosingDoor) return;
            var offer = Director.Run.CurrentOffer;
            if (offer == null || index < 0 || index >= offer.Count) return;
            Screens.Hide();
            Director.ChooseDoor(index);
        }

        public override void Back() => Screens.Hide();

        public static Color FamilyColor(MapFamily family)
        {
            switch (family)
            {
                case MapFamily.Red: return new Color(0.62f, 0.2f, 0.18f);
                case MapFamily.Green: return new Color(0.2f, 0.5f, 0.26f);
                case MapFamily.Blue: return new Color(0.2f, 0.36f, 0.66f);
                default: return new Color(0.45f, 0.24f, 0.6f);
            }
        }
    }
}
