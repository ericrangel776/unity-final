using DeckArpg.Core;

namespace DeckArpg.UI
{
    /// <summary>Still Waters: `1` heal or `2` Meditate (+1 Momentum), one choice. `Esc`/`Enter` leaves.</summary>
    public sealed class RestView : UtilityView
    {
        private RestVisit Rest => Director != null ? Director.CurrentRest : null;
        protected override bool HasVisit => Rest != null;
        protected override int TileCount => 2;
        protected override string HintText => "1: heal   ·   2: meditate   ·   Esc/Enter: leave";

        /// <summary>Which option was taken this visit (-1 = none yet).</summary>
        public int Chosen { get; private set; } = -1;

        protected override void ResetMode() => Chosen = -1;

        protected override void Redraw()
        {
            var rest = Rest;
            var run = Director.Run;
            ShowTiles(2);

            var heal = Tiles[0];
            int amount = rest.HealAmount(run);
            heal.Button.interactable = !rest.IsDone;
            heal.Badge.text = $"{RestRules.HealPercent(rest.Quality)}%";
            heal.BadgeLabel.text = "HEAL";
            heal.Title.text = "Rest";
            heal.Body.text = $"Heal {amount} HP.\nHP {run.Hp}/{run.MaxHp}" +
                             (rest.IsDone ? "" : $" -> {System.Math.Min(run.MaxHp, run.Hp + amount)}/{run.MaxHp}");
            heal.Footer.text = Chosen == 0 ? "CHOSEN" : "";

            var meditate = Tiles[1];
            int stacks = run.Momentum.Stacks;
            meditate.Button.interactable = !rest.IsDone;
            meditate.Badge.text = "+1";
            meditate.BadgeLabel.text = "MOMENTUM";
            meditate.Title.text = "Meditate";
            meditate.Body.text = $"+1 Momentum: more currency,\nbetter Blue rooms.\n{RunUiText.Momentum(stacks)}" +
                                 (stacks >= Momentum.MaxStacks ? "\n(already at max)" : "");
            meditate.Footer.text = Chosen == 1 ? "CHOSEN" : "";
        }

        public override void Select(int index)
        {
            if (!HasVisit || Rest.IsDone) return;
            bool done = index == 0 ? Rest.TryHeal(Director.Run) : index == 1 && Rest.TryMeditate(Director.Run);
            if (done) Chosen = index;
            Refresh();
        }

        public override void Back() => Confirm();

        public override void Confirm()
        {
            Chosen = -1;
            base.Confirm();
        }
    }
}
