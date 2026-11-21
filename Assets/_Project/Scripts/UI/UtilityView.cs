using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Shared frame for the Blue rooms (Shop/Forge/Rest), so all three work the same way (docs/specs/m3-blue-polish.md):
    /// header, status line, a <b>prompt</b> saying what to do now, option tiles or a deck list, a <b>message</b> line
    /// (the selected card's preview, or the last action's result), the buttons and a key hint that matches the mode.
    /// <list type="bullet">
    /// <item>Deck-changing actions open the card list: a click selects a card and previews it; Confirm/`Enter` applies.</item>
    /// <item>`Esc`/Back steps out of the card list. It never leaves the room.</item>
    /// <item>Leave/`Enter` at the top level leaves. Leaving an unused room asks once.</item>
    /// </list>
    /// </summary>
    public abstract class UtilityView : RunScreen
    {
        protected static readonly Vector2 TileSize = new Vector2(300f, 400f);
        protected const float Gap = 26f;
        protected static readonly Color BlueColor = DoorChoiceView.FamilyColor(MapFamily.Blue);
        /// <summary>Button slots: left (Back / the Shop's Purge), centre (Confirm), right (Leave).</summary>
        protected const float SlotX = 380f;
        protected static readonly Vector2 ButtonSize = new Vector2(340f, 60f);
        private const string CardListHint = "Click a card to select it   ·   Enter: confirm   ·   Esc: back";

        protected OptionTile[] Tiles;
        protected DeckListView DeckList;
        private Text header, status, prompt, message, hint;
        private Button backButton, confirmButton, leaveButton;
        private string messageText = "";
        private bool messageIsWarning;

        public OptionTile Tile(int i) => Tiles[i];
        public DeckListView Deck => DeckList;
        public string HeaderText => header.text;
        public string StatusText => status.text;
        public string PromptText => prompt.text;
        /// <summary>The selection preview, the last action's result, or a warning.</summary>
        public string MessageText => message.text;
        public bool MessageIsWarning => messageIsWarning;
        public string HintText => hint.text;
        /// <summary>Choosing a card from the deck list (Shop purge, Forge upgrade/Premium).</summary>
        public bool InCardList { get; private set; }
        public bool CanConfirmCard => confirmButton.gameObject.activeSelf && confirmButton.interactable;
        /// <summary>The "leave without using?" warning is showing; the next Leave leaves.</summary>
        public bool LeaveWarned { get; private set; }

        /// <summary>True when the run's current Blue visit belongs to this screen.</summary>
        protected abstract bool HasVisit { get; }
        protected abstract int TileCount { get; }
        /// <summary>"the Shop", for the leave warning.</summary>
        protected abstract string RoomNoun { get; }
        /// <summary>Something was bought / forged / chosen this visit (no leave warning then).</summary>
        protected abstract bool Used { get; }
        protected abstract string TopPrompt { get; }
        protected abstract string TopHint { get; }
        /// <summary>Draws the option tiles (top level).</summary>
        protected abstract void RedrawTop();

        // The card list (only Shop and Forge open it).
        protected virtual string CardPrompt => "";
        /// <summary>Why <paramref name="card"/> can't be chosen, or null if it can.</summary>
        protected virtual string CardBlockedReason(RuntimeCardInstance card) => null;
        protected virtual string CardPreview(RuntimeCardInstance card) => "";
        /// <summary>Applies the action to <paramref name="card"/>. Returns the result message, or null if it failed.</summary>
        protected virtual string ApplyCard(RuntimeCardInstance card) => null;
        /// <summary>The subclass's extra top-level buttons (the Shop's Purge, Heal, Vital Draught), placed left of Leave.</summary>
        protected virtual IReadOnlyList<Button> ExtraButtons => System.Array.Empty<Button>();

        protected override void Build(RectTransform root)
        {
            header = RunWidgets.Header(root, "Header");
            status = RunWidgets.Label(root, "Status", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, RunWidgets.SubheaderY), new Vector2(1600f, 40f), 28, TextAnchor.MiddleCenter, RunWidgets.Gold);
            prompt = RunWidgets.Label(root, "Prompt", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, RunWidgets.PromptY), new Vector2(1600f, 40f), 30, TextAnchor.MiddleCenter, Color.white);
            message = RunWidgets.Label(root, "Message", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, RunWidgets.FeedbackY), new Vector2(1600f, 40f), 28, TextAnchor.MiddleCenter, RunWidgets.Gold);
            hint = RunWidgets.Hint(root);

            Tiles = new OptionTile[TileCount];
            for (int i = 0; i < TileCount; i++)
            {
                int index = i;
                Tiles[i] = RunWidgets.Tile(root, $"Option{i + 1}", Vector2.zero, TileSize, () => Select(index));
                Tiles[i].Hotkey.text = $"[{i + 1}]";
                RunWidgets.Paint(Tiles[i], BlueColor);
            }
            DeckList = new DeckListView(root, new Vector2(0f, -60f)); // clear of the prompt line

            backButton = RunWidgets.TextButton(root, "Back", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-SlotX, RunWidgets.ButtonY), ButtonSize, "Back", Back, out _);
            confirmButton = RunWidgets.TextButton(root, "Confirm", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, RunWidgets.ButtonY), ButtonSize, "Confirm", ConfirmCard, out _, new Color(0.2f, 0.5f, 0.3f));
            leaveButton = RunWidgets.TextButton(root, "Leave", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(SlotX, RunWidgets.ButtonY), ButtonSize, "Leave", TryLeave, out _);
            BuildExtras(root);
            if (Director != null) Director.StateChanged += HandleStateChanged;
        }

        /// <summary>Extra buttons for a subclass (the Shop's Purge button, in the left slot).</summary>
        protected virtual void BuildExtras(RectTransform root) { }

        private void OnDestroy()
        {
            if (Director != null) Director.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(RunDirectorState state)
        {
            if (state != RunDirectorState.Utility || !HasVisit) return;
            ResetMode(); // a fresh visit starts at the top level with no message
            Screens.Show(this);
        }

        /// <summary>Back to the top level for a fresh visit. Overrides must call the base.</summary>
        protected virtual void ResetMode()
        {
            InCardList = false;
            LeaveWarned = false;
            SetMessage("");
            if (DeckList != null) DeckList.Hide();
        }

        public override bool ValidIn(RunDirectorState state) => state == RunDirectorState.Utility && HasVisit;

        public override void Refresh()
        {
            if (!HasVisit) return;
            var run = Director.Run;
            header.text = RunUiText.MapCardName(run.CurrentRoom.Kind) + $"  ·  Quality {run.CurrentRoom.Number}";
            status.text = $"Currency {run.Currency}  ·  HP {run.Hp}/{run.MaxHp}  ·  Deck {run.Deck.Count}";

            if (InCardList)
            {
                HideTiles();
                prompt.text = CardPrompt;
                hint.text = CardListHint;
                confirmButton.interactable = DeckList.Selected != null;
            }
            else
            {
                prompt.text = TopPrompt;
                hint.text = TopHint;
                RedrawTop();
            }
            backButton.gameObject.SetActive(InCardList);
            confirmButton.gameObject.SetActive(InCardList);
            foreach (var extra in ExtraButtons)
                if (extra != null) extra.gameObject.SetActive(!InCardList);
            LayoutButtons();
            message.text = messageText;
            message.color = messageIsWarning ? RunWidgets.Warning : RunWidgets.Gold;
        }

        /// <summary>`Esc`: out of the card list. Never leaves the room.</summary>
        public override void Back()
        {
            if (!InCardList) return;
            CloseCardList();
            SetMessage("");
            Refresh();
        }

        /// <summary>`Enter`: applies the selected card in the card list, otherwise leaves.</summary>
        public override void Confirm()
        {
            if (InCardList) ConfirmCard();
            else TryLeave();
        }

        /// <summary>The Leave button (and `Enter` at the top level). An unused room asks once first.</summary>
        public void TryLeave()
        {
            if (Director == null || Director.State != RunDirectorState.Utility) return;
            if (!Used && !LeaveWarned)
            {
                LeaveWarned = true;
                SetMessage($"Leave without using {RoomNoun}? Press Leave again.", warning: true);
                Refresh();
                return;
            }
            ResetMode();
            Screens.Hide();
            Director.LeaveUtility();
        }

        /// <summary>Opens the deck list with nothing selected.</summary>
        protected void OpenCardList()
        {
            InCardList = true;
            LeaveWarned = false;
            SetMessage("Nothing selected yet");
            DeckList.Show(Director.Run.Deck, CardBlockedReason, card =>
            {
                SetMessage(CardPreview(card));
                Refresh();
            });
            Refresh();
        }

        private void CloseCardList()
        {
            InCardList = false;
            DeckList.Hide();
        }

        private void ConfirmCard()
        {
            if (!InCardList || !HasVisit) return;
            var card = DeckList.Selected;
            if (card == null)
            {
                SetMessage("Select a card first", warning: true);
                Refresh();
                return;
            }
            string result = ApplyCard(card);
            if (result == null)
            {
                SetMessage("That card can't be used for this", warning: true);
            }
            else
            {
                CloseCardList();
                SetMessage(result);
            }
            Refresh();
        }

        /// <summary>Centres the visible buttons as a row, left to right: Back / extras, Confirm, Leave.</summary>
        private void LayoutButtons()
        {
            var row = new List<RectTransform>(4);
            AddIfShown(row, backButton);
            foreach (var extra in ExtraButtons) AddIfShown(row, extra);
            AddIfShown(row, confirmButton);
            AddIfShown(row, leaveButton);
            float start = -(row.Count - 1) * SlotX / 2f;
            for (int i = 0; i < row.Count; i++)
                row[i].anchoredPosition = new Vector2(start + i * SlotX, RunWidgets.ButtonY);
        }

        private static void AddIfShown(List<RectTransform> row, Button button)
        {
            if (button != null && button.gameObject.activeSelf) row.Add((RectTransform)button.transform);
        }

        protected void SetMessage(string text, bool warning = false)
        {
            messageText = text ?? "";
            messageIsWarning = warning;
        }

        /// <summary>Shows the first <paramref name="count"/> tiles in a centred row and hides the deck list.</summary>
        protected void ShowTiles(int count)
        {
            DeckList.Hide();
            float start = -(count - 1) * (TileSize.x + Gap) / 2f;
            for (int i = 0; i < Tiles.Length; i++)
            {
                Tiles[i].SetVisible(i < count);
                Tiles[i].Rect.anchoredPosition = new Vector2(start + i * (TileSize.x + Gap), 0f);
            }
        }

        protected void HideTiles()
        {
            foreach (var tile in Tiles) tile.SetVisible(false);
        }
    }
}
