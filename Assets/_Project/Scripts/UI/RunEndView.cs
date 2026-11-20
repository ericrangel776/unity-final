using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Run summary on victory or defeat (docs/specs/m3-ui.md): rooms cleared, average Intensity ("how hard did you
    /// play it"), currency, relics, deck size and the path taken. `Enter`/Restart starts a new run.
    /// </summary>
    public sealed class RunEndView : RunScreen
    {
        private static readonly Color VictoryColor = RunWidgets.Gold;
        private static readonly Color DefeatColor = new Color(0.95f, 0.4f, 0.35f);

        private Text title, stats, path;

        public bool Won { get; private set; }
        public string TitleText => title.text;
        public string StatsText => stats.text;
        public string PathText => path.text;

        protected override void Build(RectTransform root)
        {
            title = RunWidgets.Label(root, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 220f),
                new Vector2(1200f, 110f), 96, TextAnchor.MiddleCenter, Color.white);
            stats = RunWidgets.Label(root, "Stats", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f),
                new Vector2(1000f, 240f), 34, TextAnchor.MiddleCenter, Color.white);
            path = RunWidgets.Label(root, "Path", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f),
                new Vector2(1600f, 80f), 24, TextAnchor.MiddleCenter, RunWidgets.Muted, wrap: true);
            RunWidgets.TextButton(root, "Restart", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -230f),
                new Vector2(340f, 70f), "New run", Confirm, out _);
            RunWidgets.Hint(root).text = "Enter or click: start a new run";
            if (Director != null) Director.RunEnded += Open;
        }

        private void OnDestroy()
        {
            if (Director != null) Director.RunEnded -= Open;
        }

        private void Open(bool won)
        {
            Won = won;
            Screens.Show(this);
        }

        public override bool ValidIn(RunDirectorState state) =>
            state == RunDirectorState.Victory || state == RunDirectorState.Defeat;

        public override void Refresh()
        {
            var run = Director != null ? Director.Run : null;
            if (run == null) return;
            title.text = Won ? "VICTORY" : "DEFEAT";
            title.color = Won ? VictoryColor : DefeatColor;
            stats.text =
                $"Rooms cleared  {RunUiText.RoomsCleared(run)}/{Core.RunState.BossRoom}\n" +
                $"Average Intensity  {RunUiText.AverageIntensity(run)}\n" +
                $"Currency  {run.Currency}   ·   Deck  {run.Deck.Count} cards\n" +
                RunUiText.Relics(run.Relics, 0);
            path.text = RunUiText.Path(run);
        }

        /// <summary>Starts a new run (fresh seed) at the first door.</summary>
        public override void Confirm()
        {
            if (Director == null || !ValidIn(Director.State)) return;
            Screens.Hide();
            Director.RestartRun();
        }

        public override void Back() { } // no backing out of the summary
    }
}
