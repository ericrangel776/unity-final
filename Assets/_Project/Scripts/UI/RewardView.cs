using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// The room-clear reward (docs/specs/m3-ui.md). Opens on <see cref="RunDirector.RewardReady"/>.
    /// Phase 1, only when relics are offered: pick one (forced: Continue stays disabled until then).
    /// Phase 2: the card draft. Pick up to <see cref="CombatReward.PicksAllowed"/>, or skip for the skip bonus.
    /// `1`-`4`/click picks, `Esc` skips the draft, `Enter`/Continue closes the reward and deals the next door.
    /// </summary>
    public sealed class RewardView : RunScreen
    {
        public enum Phase { Relic, Draft, Done }

        public const int MaxOptions = 4;
        private static readonly Vector2 TileSize = new Vector2(340f, 440f);
        private const float Gap = 30f;
        private static readonly Color RelicColor = new Color(0.45f, 0.24f, 0.6f);
        private static readonly Color TakenColor = new Color(0.25f, 0.45f, 0.3f);

        private readonly OptionTile[] tiles = new OptionTile[MaxOptions];
        private Text header, subheader;
        private Button skipButton, continueButton;
        private Text skipLabel;

        public OptionTile Tile(int i) => tiles[i];
        public string HeaderText => header.text;
        public string SubheaderText => subheader.text;
        public bool CanSkip => skipButton.gameObject.activeSelf && skipButton.interactable;
        public string SkipText => skipLabel.text;
        public bool CanContinue => continueButton.interactable;
        public int VisibleCount
        {
            get
            {
                int n = 0;
                foreach (var t in tiles) if (t.Visible) n++;
                return n;
            }
        }

        private CombatReward Reward => Director != null ? Director.CurrentReward : null;

        /// <summary>Relic first (if offered and unclaimed), then the draft, then nothing left to choose.</summary>
        public Phase CurrentPhase
        {
            get
            {
                var reward = Reward;
                if (reward == null) return Phase.Done;
                if (reward.RelicOptions.Count > 0 && !reward.IsRelicClaimed) return Phase.Relic;
                return reward.IsDraftClosed ? Phase.Done : Phase.Draft;
            }
        }

        protected override void Build(RectTransform root)
        {
            header = RunWidgets.Header(root, "Header");
            subheader = RunWidgets.Label(root, "Subheader", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -160f), new Vector2(1600f, 40f), 28, TextAnchor.MiddleCenter, RunWidgets.Gold);
            RunWidgets.Hint(root).text = "1-4 or click: take   ·   Esc: skip the draft   ·   Enter: continue";
            for (int i = 0; i < MaxOptions; i++)
            {
                int index = i;
                tiles[i] = RunWidgets.Tile(root, $"Option{i + 1}", Vector2.zero, TileSize, () => Select(index));
                tiles[i].Hotkey.text = $"[{i + 1}]";
            }
            skipButton = RunWidgets.TextButton(root, "Skip", new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(-20f, 100f), new Vector2(300f, 60f), "Skip", Back, out skipLabel);
            continueButton = RunWidgets.TextButton(root, "Continue", new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(20f, 100f), new Vector2(300f, 60f), "Continue", Confirm, out _);
            if (Director != null) Director.RewardReady += Open;
        }

        private void OnDestroy()
        {
            if (Director != null) Director.RewardReady -= Open;
        }

        private void Open(CombatReward reward) => Screens.Show(this);

        public override bool ValidIn(RunDirectorState state) => state == RunDirectorState.Reward;

        public override void Refresh()
        {
            var reward = Reward;
            if (reward == null) return;
            header.text = $"Room cleared  ·  +{reward.Currency} currency";

            var phase = CurrentPhase;
            switch (phase)
            {
                case Phase.Relic:
                    subheader.text = "Choose a relic";
                    ShowRelics(reward);
                    break;
                default:
                    subheader.text = DraftLine(reward, phase);
                    ShowDraft(reward, phase);
                    break;
            }

            skipButton.gameObject.SetActive(phase == Phase.Draft);
            skipLabel.text = reward.PicksMade == 0 ? $"Skip (+{reward.SkipBonus})" : "Done picking";
            continueButton.interactable = phase != Phase.Relic;
        }

        private static string DraftLine(CombatReward reward, Phase phase)
        {
            string relicNote = reward.RelicFallbackPaid ? $"All relics owned: +{RelicRules.FallbackCurrency} currency  ·  " : "";
            if (reward.Options.Count == 0) return relicNote + "No cards to draft";
            if (phase == Phase.Done) return relicNote + (reward.PicksMade > 0 ? "Card added to your deck" : "Draft skipped");
            int left = reward.PicksAllowed - reward.PicksMade;
            return relicNote + $"Take {left} of {reward.Options.Count}";
        }

        private void ShowRelics(CombatReward reward)
        {
            int count = Mathf.Min(reward.RelicOptions.Count, MaxOptions);
            Layout(count);
            for (int i = 0; i < count; i++)
            {
                var tile = tiles[i];
                var relic = reward.RelicOptions[i];
                RunWidgets.Paint(tile, RelicColor);
                tile.Button.interactable = true;
                tile.Badge.text = "";
                tile.BadgeLabel.text = "RELIC";
                tile.Title.text = RunUiText.RelicName(relic);
                tile.Body.text = RunUiText.RelicDescription(relic);
                tile.Footer.text = "";
            }
        }

        private void ShowDraft(CombatReward reward, Phase phase)
        {
            int count = Mathf.Min(reward.Options.Count, MaxOptions);
            Layout(count);
            for (int i = 0; i < count; i++)
            {
                var tile = tiles[i];
                var card = reward.Options[i];
                bool taken = reward.IsTaken(i);
                RunWidgets.Paint(tile, taken ? TakenColor : RarityColor(card.Rarity));
                tile.Button.interactable = phase == Phase.Draft && !taken;
                tile.Badge.text = RunUiText.CardValue(card, reward.DraftUpgrade);
                tile.BadgeLabel.text = card.Rarity.ToString().ToUpperInvariant();
                tile.Title.text = card.DisplayName;
                tile.Body.text = RunUiText.CardDetails(card, reward.DraftUpgrade);
                tile.Footer.text = taken ? "TAKEN" : "";
            }
        }

        /// <summary>Shows the first <paramref name="count"/> tiles, centred in a row.</summary>
        private void Layout(int count)
        {
            float start = -(count - 1) * (TileSize.x + Gap) / 2f;
            for (int i = 0; i < MaxOptions; i++)
            {
                tiles[i].SetVisible(i < count);
                tiles[i].Rect.anchoredPosition = new Vector2(start + i * (TileSize.x + Gap), 20f);
            }
        }

        /// <summary>Phase 1: takes relic <paramref name="index"/>. Phase 2: drafts card <paramref name="index"/>.</summary>
        public override void Select(int index)
        {
            var reward = Reward;
            if (reward == null || Director.State != RunDirectorState.Reward) return;
            switch (CurrentPhase)
            {
                case Phase.Relic:
                    Director.ChooseRelic(index);
                    break;
                case Phase.Draft:
                    reward.TryPick(index, Director.Run);
                    break;
            }
            Refresh();
        }

        /// <summary>Skips the draft (the skip bonus is paid only if nothing was taken). Not during the relic pick.</summary>
        public override void Back()
        {
            var reward = Reward;
            if (reward == null || CurrentPhase != Phase.Draft) return;
            reward.Skip(Director.Run);
            Refresh();
        }

        /// <summary>Closes the reward and deals the next door. Blocked while a relic must still be picked.</summary>
        public override void Confirm()
        {
            if (Director == null || Director.State != RunDirectorState.Reward || CurrentPhase == Phase.Relic) return;
            Screens.Hide();
            Director.FinishReward();
        }

        public static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return new Color(0.2f, 0.4f, 0.62f);
                case Rarity.Rare: return new Color(0.66f, 0.5f, 0.15f);
                default: return new Color(0.32f, 0.34f, 0.4f);
            }
        }
    }
}
