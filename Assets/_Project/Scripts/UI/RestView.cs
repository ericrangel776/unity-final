using DeckArpg.Core;

namespace DeckArpg.UI
{
    /// <summary>
    /// Still Waters (docs/specs/m3-blue-polish.md, docs/specs/heal-rework.md): `1` heal, `2` Fortify (+max HP) or
    /// `3` Meditate (+1 Momentum), one choice, with a message saying what it did. `Enter`/Leave leaves (an unused rest asks once); `Esc` does nothing here.
    /// </summary>
    public sealed class RestView : UtilityView
    {
        private RestVisit Rest => Director != null ? Director.CurrentRest : null;
        protected override bool HasVisit => Rest != null;
        protected override int TileCount => 3;
        protected override string RoomNoun => "the rest";
        protected override bool Used => Rest.IsDone;
        protected override string TopPrompt => Rest.IsDone ? "Rested. Leave when ready." : "Choose one: rest, fortify or meditate";
        protected override string TopHint => "1: rest   ·   2: fortify   ·   3: meditate   ·   Enter: leave";

        /// <summary>Which option was taken this visit (-1 = none yet).</summary>
        public int Chosen { get; private set; } = -1;

        protected override void ResetMode()
        {
            base.ResetMode();
            Chosen = -1;
        }

        protected override void RedrawTop()
        {
            var rest = Rest;
            var run = Director.Run;
            ShowTiles(3);

            var heal = Tiles[0];
            int amount = rest.HealAmount(run);
            heal.Button.interactable = !rest.IsDone;
            heal.Badge.text = $"{RestRules.HealPercent(rest.Quality)}%";
            heal.BadgeLabel.text = "HEAL";
            heal.Title.text = "Rest";
            heal.Body.text = $"Heal {amount} HP.\nHP {run.Hp}/{run.MaxHp}" +
                             (rest.IsDone ? "" : $" -> {System.Math.Min(run.MaxHp, run.Hp + amount)}/{run.MaxHp}");
            heal.Footer.text = Chosen == 0 ? "CHOSEN" : "";

            var fortify = Tiles[1];
            int gain = rest.FortifyAmount;
            fortify.Button.interactable = !rest.IsDone;
            fortify.Badge.text = $"+{gain}";
            fortify.BadgeLabel.text = "MAX HP";
            fortify.Title.text = "Fortify";
            fortify.Body.text = $"Max HP +{gain} (and heal {gain}).\nHP {run.Hp}/{run.MaxHp}" +
                                (rest.IsDone ? "" : $" -> {System.Math.Min(run.MaxHp + gain, run.Hp + gain)}/{run.MaxHp + gain}");
            fortify.Footer.text = Chosen == 1 ? "CHOSEN" : "";

            var meditate = Tiles[2];
            int stacks = run.Momentum.Stacks;
            meditate.Button.interactable = !rest.IsDone;
            meditate.Badge.text = "+1";
            meditate.BadgeLabel.text = "MOMENTUM";
            meditate.Title.text = "Meditate";
            meditate.Body.text = $"+1 Momentum: more currency,\nbetter Blue rooms.\n{RunUiText.Momentum(stacks)}" +
                                 (stacks >= Momentum.MaxStacks && !rest.IsDone ? "\n(already at max)" : "");
            meditate.Footer.text = Chosen == 2 ? "CHOSEN" : "";
        }

        public override void Select(int index)
        {
            if (!HasVisit || index < 0 || index > 2) return;
            var run = Director.Run;
            if (Rest.IsDone)
            {
                SetMessage("You've already chosen. Leave when ready.", warning: true);
            }
            else if (index == 0)
            {
                int before = run.Hp;
                Rest.TryHeal(run);
                Chosen = 0;
                SetMessage($"Healed {run.Hp - before} HP  ·  HP {run.Hp}/{run.MaxHp}");
            }
            else if (index == 1)
            {
                int gain = Rest.FortifyAmount;
                Rest.TryFortify(run);
                Chosen = 1;
                SetMessage($"Max HP +{gain}  ·  HP {run.Hp}/{run.MaxHp}");
            }
            else
            {
                int before = run.Momentum.Stacks;
                Rest.TryMeditate(run);
                Chosen = 2;
                SetMessage(run.Momentum.Stacks > before
                    ? $"+1 Momentum  ·  {RunUiText.Momentum(run.Momentum.Stacks)}"
                    : $"Momentum already at max  ·  {RunUiText.Momentum(run.Momentum.Stacks)}");
            }
            Refresh();
        }
    }
}
