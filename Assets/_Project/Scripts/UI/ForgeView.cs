using DeckArpg.Core;

namespace DeckArpg.UI
{
    /// <summary>
    /// Ember Forge: `1` Upgrade +N or `2` Make Premium, then click a deck card. Below Quality 7 it's one or the other;
    /// at 7-9 one of each on different cards (<see cref="ForgeVisit"/> enforces it). `Esc` backs out of the card
    /// list, then leaves.
    /// </summary>
    public sealed class ForgeView : UtilityView
    {
        public enum Mode { Actions, Upgrade, Premium }

        public Mode CurrentMode { get; private set; }

        private ForgeVisit Forge => Director != null ? Director.CurrentForge : null;
        protected override bool HasVisit => Forge != null;
        protected override int TileCount => 2;
        protected override string HintText => "1: upgrade   ·   2: make Premium   ·   then click a card   ·   Esc/Enter: leave";

        protected override void ResetMode() => CurrentMode = Mode.Actions;

        protected override void Redraw()
        {
            var forge = Forge;
            var deck = Director.Run.Deck;
            switch (CurrentMode)
            {
                case Mode.Upgrade:
                    HideTiles();
                    DeckList.Show(deck, ForgeRules.CanUpgrade, Apply);
                    return;
                case Mode.Premium:
                    HideTiles();
                    DeckList.Show(deck, ForgeRules.CanMakePremium, Apply);
                    return;
            }

            ShowTiles(2);
            bool anyUpgradable = false, anyPremiumable = false;
            foreach (var card in deck)
            {
                anyUpgradable |= ForgeRules.CanUpgrade(card);
                anyPremiumable |= ForgeRules.CanMakePremium(card);
            }

            var upgrade = Tiles[0];
            upgrade.Button.interactable = forge.CanStillUpgrade && anyUpgradable;
            upgrade.Badge.text = $"+{forge.UpgradeAmount}";
            upgrade.BadgeLabel.text = "VALUE";
            upgrade.Title.text = "Upgrade";
            upgrade.Body.text = $"One card gets +{forge.UpgradeAmount} value\nfor the rest of the run.";
            upgrade.Footer.text = forge.CanStillUpgrade ? "" : "DONE";

            var premium = Tiles[1];
            premium.Button.interactable = forge.CanStillMakePremium && anyPremiumable;
            premium.Badge.text = $"+{RuntimeCardInstance.PremiumBonus}";
            premium.BadgeLabel.text = "PREMIUM";
            premium.Title.text = "Make Premium";
            premium.Body.text = $"One card gets +{RuntimeCardInstance.PremiumBonus}\nbut burns when played.";
            premium.Footer.text = forge.CanStillMakePremium ? "" : "DONE";

            if (forge.IsDone) upgrade.Footer.text = premium.Footer.text = "FORGE SPENT";
            else if (!ForgeRules.GrantsBoth(forge.Quality)) upgrade.Body.text += "\n(or Premium, not both)";
        }

        /// <summary>`1` opens the upgrade list, `2` the Premium list (if still available).</summary>
        public override void Select(int index)
        {
            if (!HasVisit || CurrentMode != Mode.Actions || index < 0 || index >= Tiles.Length) return;
            if (!Tiles[index].Interactable) return;
            CurrentMode = index == 0 ? Mode.Upgrade : Mode.Premium;
            Refresh();
        }

        private void Apply(RuntimeCardInstance card)
        {
            bool done = CurrentMode == Mode.Upgrade
                ? Forge.TryUpgrade(card, Director.Run)
                : Forge.TryMakePremium(card, Director.Run);
            if (done) CurrentMode = Mode.Actions;
            Refresh();
        }

        public override void Back()
        {
            if (CurrentMode != Mode.Actions)
            {
                CurrentMode = Mode.Actions;
                Refresh();
            }
            else Confirm();
        }

        public override void Confirm()
        {
            CurrentMode = Mode.Actions;
            base.Confirm();
        }
    }
}
