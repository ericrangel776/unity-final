using DeckArpg.Core;

namespace DeckArpg.UI
{
    /// <summary>
    /// Ember Forge (docs/specs/m3-blue-polish.md): `1` Upgrade +N or `2` Make Premium opens the deck list; select a card
    /// to preview the change, then Confirm. Below Quality 7 it's one or the other; at 7-9 one of each on different cards
    /// (<see cref="ForgeVisit"/> enforces it). The prompt always says what's left.
    /// </summary>
    public sealed class ForgeView : UtilityView
    {
        public enum Mode { Actions, Upgrade, Premium }

        private Mode cardMode = Mode.Upgrade;

        public Mode CurrentMode => InCardList ? cardMode : Mode.Actions;

        private ForgeVisit Forge => Director != null ? Director.CurrentForge : null;
        protected override bool HasVisit => Forge != null;
        protected override int TileCount => 2;
        protected override string RoomNoun => "the Forge";
        protected override bool Used => Forge.UpgradedCard != null || Forge.PremiumCard != null;
        protected override string TopHint => "1: upgrade   ·   2: make Premium   ·   Enter: leave";

        protected override string TopPrompt
        {
            get
            {
                var forge = Forge;
                if (forge.IsDone) return "Forge spent. Leave when ready.";
                if (!ForgeRules.GrantsBoth(forge.Quality))
                    return $"Choose one: upgrade a card (+{forge.UpgradeAmount}) or make one Premium";
                if (forge.CanStillUpgrade && forge.CanStillMakePremium)
                    return $"Upgrade one card (+{forge.UpgradeAmount}) and make a different one Premium";
                return forge.CanStillUpgrade
                    ? $"Now upgrade a different card (+{forge.UpgradeAmount})"
                    : "Now make a different card Premium";
            }
        }

        protected override string CardPrompt => cardMode == Mode.Upgrade
            ? $"Choose a card to upgrade (+{Forge.UpgradeAmount})"
            : $"Choose a card to make Premium (+{RuntimeCardInstance.PremiumBonus}, burns when played)";

        protected override void RedrawTop()
        {
            var forge = Forge;
            var deck = Director.Run.Deck;
            ShowTiles(2);
            bool anyUpgradable = false, anyPremiumable = false;
            foreach (var card in deck)
            {
                anyUpgradable |= UpgradeReason(card) == null;
                anyPremiumable |= PremiumReason(card) == null;
            }

            var upgrade = Tiles[0];
            upgrade.Button.interactable = forge.CanStillUpgrade && anyUpgradable;
            upgrade.Badge.text = $"+{forge.UpgradeAmount}";
            upgrade.BadgeLabel.text = "VALUE";
            upgrade.Title.text = "Upgrade";
            upgrade.Body.text = $"One card gets +{forge.UpgradeAmount} value\nfor the rest of the run.";
            upgrade.Footer.text = !forge.CanStillUpgrade ? (forge.UpgradedCard != null ? "DONE" : "NOT THIS VISIT")
                : anyUpgradable ? "" : "NO CARD CAN TAKE IT";

            var premium = Tiles[1];
            premium.Button.interactable = forge.CanStillMakePremium && anyPremiumable;
            premium.Badge.text = $"+{RuntimeCardInstance.PremiumBonus}";
            premium.BadgeLabel.text = "PREMIUM";
            premium.Title.text = "Make Premium";
            premium.Body.text = $"One card gets +{RuntimeCardInstance.PremiumBonus}\nbut burns when played.";
            premium.Footer.text = !forge.CanStillMakePremium ? (forge.PremiumCard != null ? "DONE" : "NOT THIS VISIT")
                : anyPremiumable ? "" : "NO CARD CAN TAKE IT";
        }

        /// <summary>`1` opens the upgrade list, `2` the Premium list (if still available). Ignored in the card list.</summary>
        public override void Select(int index)
        {
            if (!HasVisit || InCardList || index < 0 || index >= Tiles.Length) return;
            if (!Tiles[index].Interactable)
            {
                SetMessage(Forge.IsDone ? "The forge is spent" : "That option isn't available", warning: true);
                Refresh();
                return;
            }
            cardMode = index == 0 ? Mode.Upgrade : Mode.Premium;
            OpenCardList();
        }

        protected override string CardBlockedReason(RuntimeCardInstance card) =>
            cardMode == Mode.Upgrade ? UpgradeReason(card) : PremiumReason(card);

        private string UpgradeReason(RuntimeCardInstance card) =>
            card == Forge.PremiumCard ? "made Premium this visit" : RunUiText.UpgradeBlockedReason(card);

        private string PremiumReason(RuntimeCardInstance card) =>
            card == Forge.UpgradedCard ? "upgraded this visit" : RunUiText.PremiumBlockedReason(card);

        protected override string CardPreview(RuntimeCardInstance card) => cardMode == Mode.Upgrade
            ? RunUiText.UpgradePreview(card, Forge.UpgradeAmount)
            : RunUiText.PremiumPreview(card);

        protected override string ApplyCard(RuntimeCardInstance card)
        {
            var run = Director.Run;
            if (cardMode == Mode.Upgrade)
                return Forge.TryUpgrade(card, run) ? $"{card.Data.DisplayName} is now {RunUiText.DeckValue(card)}" : null;
            return Forge.TryMakePremium(card, run)
                ? $"{card.Data.DisplayName} is now Premium ({RunUiText.DeckValue(card)}, burns when played)"
                : null;
        }
    }
}
