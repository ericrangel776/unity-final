using DeckArpg.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Wayfarer's Stall: buy stock cards (`1`-`5`/click), or Purge: pick a deck card to remove for the purge cost.
    /// `Esc` leaves purge mode, then leaves the shop.
    /// </summary>
    public sealed class ShopView : UtilityView
    {
        public const int MaxStock = 5;
        private Button purgeButton;
        private Text purgeLabel;

        public bool PurgeMode { get; private set; }
        public bool CanPurge => purgeButton.gameObject.activeSelf && purgeButton.interactable;
        public string PurgeText => purgeLabel.text;

        private ShopVisit Shop => Director != null ? Director.CurrentShop : null;
        protected override bool HasVisit => Shop != null;
        protected override int TileCount => MaxStock;
        protected override string HintText => "1-5 or click: buy   ·   Purge: remove a card   ·   Esc/Enter: leave";

        protected override void BuildExtras(RectTransform root)
        {
            purgeButton = RunWidgets.TextButton(root, "Purge", new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(-20f, 100f), new Vector2(360f, 60f), "Purge", TogglePurge, out purgeLabel);
        }

        protected override void ResetMode() => PurgeMode = false;

        protected override void Redraw()
        {
            var shop = Shop;
            var run = Director.Run;
            int cost = shop.PurgeCost(run);
            bool purgeAllowed = ShopRules.CanPurge(run.Deck.Count) && run.Currency >= cost;
            purgeLabel.text = PurgeMode ? "Back to stock" : $"Purge a card ({cost})";
            purgeButton.interactable = PurgeMode || purgeAllowed;

            if (PurgeMode)
            {
                HideTiles();
                DeckList.Show(run.Deck, _ => purgeAllowed, Purge);
                return;
            }

            int count = Mathf.Min(shop.Stock.Count, MaxStock);
            ShowTiles(count);
            for (int i = 0; i < count; i++)
            {
                var tile = Tiles[i];
                var card = shop.Stock[i];
                int price = ShopRules.Price(card.Rarity);
                bool sold = shop.IsSold(i);
                RunWidgets.Paint(tile, sold ? RunWidgets.ButtonColor : RewardView.RarityColor(card.Rarity));
                tile.Button.interactable = !sold && run.Currency >= price;
                tile.Badge.text = RunUiText.CardValue(card);
                tile.BadgeLabel.text = card.Rarity.ToString().ToUpperInvariant();
                tile.Title.text = card.DisplayName;
                tile.Body.text = RunUiText.CardDetails(card);
                tile.Footer.text = sold ? "SOLD" : $"{price} currency";
            }
        }

        /// <summary>Buys stock card <paramref name="index"/> (ignored in purge mode, when sold or unaffordable).</summary>
        public override void Select(int index)
        {
            if (!HasVisit || PurgeMode) return;
            Shop.TryBuy(index, Director.Run);
            Refresh();
        }

        public void TogglePurge()
        {
            if (!HasVisit) return;
            PurgeMode = !PurgeMode;
            Refresh();
        }

        private void Purge(RuntimeCardInstance card)
        {
            if (Shop.TryPurge(card, Director.Run)) PurgeMode = false;
            Refresh();
        }

        public override void Back()
        {
            if (PurgeMode) TogglePurge();
            else Confirm();
        }

        public override void Confirm()
        {
            PurgeMode = false;
            base.Confirm();
        }
    }
}
