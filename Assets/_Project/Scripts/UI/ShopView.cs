using DeckArpg.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Wayfarer's Stall (docs/specs/m3-blue-polish.md): buy a stock card in one step (`1`-`5`/click). Cards you can't
    /// afford are dimmed and say how much is missing. Purge opens the deck list: select a card, see the cost, Confirm.
    /// </summary>
    public sealed class ShopView : UtilityView
    {
        public const int MaxStock = 5;
        private Button purgeButton;
        private Text purgeLabel;
        private bool usedThisVisit;

        /// <summary>Choosing a card to remove.</summary>
        public bool PurgeMode => InCardList;
        public bool CanPurge => purgeButton.gameObject.activeSelf && purgeButton.interactable;
        public string PurgeText => purgeLabel.text;

        private ShopVisit Shop => Director != null ? Director.CurrentShop : null;
        protected override bool HasVisit => Shop != null;
        protected override int TileCount => MaxStock;
        protected override string RoomNoun => "the Shop";
        protected override bool Used => usedThisVisit;
        protected override string TopPrompt => "Buy a card, or remove one from your deck";
        protected override string TopHint => "1-5 or click: buy   ·   Purge: remove a card   ·   Enter: leave";
        protected override string CardPrompt => $"Choose a card to remove ({Shop.PurgeCost(Director.Run)} currency)";

        protected override void BuildExtras(RectTransform root)
        {
            purgeButton = RunWidgets.TextButton(root, "Purge", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-SlotX, RunWidgets.ButtonY), ButtonSize, "Purge", TogglePurge, out purgeLabel);
        }

        protected override void ResetMode()
        {
            base.ResetMode();
            usedThisVisit = false;
        }

        protected override Button ExtraButton => purgeButton;

        protected override void RedrawTop()
        {
            var shop = Shop;
            var run = Director.Run;
            int cost = shop.PurgeCost(run);
            purgeButton.interactable = ShopRules.CanPurge(run.Deck.Count) && run.Currency >= cost;
            // A disabled Purge says why, so the button never just "doesn't work".
            if (!ShopRules.CanPurge(run.Deck.Count)) purgeLabel.text = $"Purge (deck at minimum {ShopRules.MinDeckSize})";
            else if (run.Currency < cost) purgeLabel.text = $"Purge {cost} (need {cost - run.Currency} more)";
            else purgeLabel.text = $"Purge a card ({cost})";

            int count = Mathf.Min(shop.Stock.Count, MaxStock);
            ShowTiles(count);
            for (int i = 0; i < count; i++)
            {
                var tile = Tiles[i];
                var card = shop.Stock[i];
                int price = ShopRules.Price(card.Rarity);
                bool sold = shop.IsSold(i);
                bool affordable = run.Currency >= price;
                var color = sold ? RunWidgets.ButtonColor : RewardView.RarityColor(card.Rarity);
                // Unaffordable cards stay clickable (the click explains the shortfall) but look dimmed.
                RunWidgets.Paint(tile, sold || affordable ? color : Color.Lerp(color, new Color(0.1f, 0.1f, 0.12f), 0.6f));
                tile.Button.interactable = !sold;
                RunWidgets.ShowCard(tile, card);
                tile.Footer.text = sold ? "SOLD" : affordable ? $"{price} currency" : $"{price}  ·  need {price - run.Currency} more";
                tile.Footer.color = sold || affordable ? RunWidgets.Gold : RunWidgets.Warning;
            }
        }

        /// <summary>Buys stock card <paramref name="index"/>, or says why it can't. Ignored in the card list.</summary>
        public override void Select(int index)
        {
            var shop = Shop;
            if (!HasVisit || InCardList || index < 0 || index >= Mathf.Min(shop.Stock.Count, MaxStock)) return;
            var card = shop.Stock[index];
            int price = ShopRules.Price(card.Rarity);
            if (shop.IsSold(index)) SetMessage($"{card.DisplayName} is already sold", warning: true);
            else if (shop.TryBuy(index, Director.Run))
            {
                usedThisVisit = true;
                SetMessage($"Bought {card.DisplayName} (-{price})");
            }
            else SetMessage(RunUiText.Shortfall(price, Director.Run.Currency), warning: true);
            Refresh();
        }

        /// <summary>The Purge button: opens the card list (or closes it).</summary>
        public void TogglePurge()
        {
            if (!HasVisit) return;
            if (InCardList) Back();
            else if (purgeButton.interactable) OpenCardList();
        }

        protected override string CardPreview(RuntimeCardInstance card) =>
            RunUiText.PurgePreview(card, Shop.PurgeCost(Director.Run), Director.Run.Deck.Count);

        protected override string ApplyCard(RuntimeCardInstance card)
        {
            int cost = Shop.PurgeCost(Director.Run);
            if (!Shop.TryPurge(card, Director.Run)) return null;
            usedThisVisit = true;
            return $"Removed {card.Data.DisplayName} (-{cost})";
        }
    }
}
