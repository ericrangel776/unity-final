using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Bottom-center HUD: the 3-card hand, the reserved 0-Parry slot, pile counts, reload prompt,
    /// and the player's HP / status. Readable at a glance: big number + element color.
    /// </summary>
    public sealed class HandHudView : MonoBehaviour
    {
        [SerializeField] private DeckRuntimeManager deck;
        [SerializeField] private CombatantStatus status;
        [SerializeField] private PlayerMotor motor;

        private static readonly string[] SlotKeys = { "1", "2", "3" };
        private static readonly Color EmptyColor = new Color(0.15f, 0.15f, 0.15f, 0.6f);

        private readonly Image[] slotImages = new Image[DeckCycle.HandSize];
        private readonly Text[] slotValues = new Text[DeckCycle.HandSize];
        private readonly Text[] slotNames = new Text[DeckCycle.HandSize];
        private Image parryImage;
        private Text parryValue;
        private Text pilesText;
        private Text reloadText;
        private Text statusText;

        public void Bind(DeckRuntimeManager deckManager, CombatantStatus playerStatus, PlayerMotor playerMotor)
        {
            Unsubscribe();
            deck = deckManager;
            status = playerStatus;
            motor = playerMotor;
            Subscribe();
            Refresh();
        }

        private void Awake()
        {
            if (deck == null)
            {
                var player = FindAnyObjectByType<PlayerCombatInput>();
                if (player != null)
                {
                    deck = player.GetComponent<DeckRuntimeManager>();
                    status = player.GetComponent<CombatantStatus>();
                    motor = player.GetComponent<PlayerMotor>();
                }
            }
            Build();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (deck != null && isActiveAndEnabled) deck.Changed += Refresh;
        }

        private void Unsubscribe()
        {
            if (deck != null) deck.Changed -= Refresh;
        }

        private void Update()
        {
            // HP / stun / dash change every frame; the hand only changes via the Changed event.
            if (status == null || statusText == null) return;
            string state = status.IsDead ? "  DOWN" : status.IsStunned ? "  STUNNED" : "";
            string dash = motor != null && motor.DashCooldownRemaining > 0f ? "  dash..." : "  [Space] dash";
            statusText.text = $"HP {status.Hp}/{status.MaxHp}   Posture {status.Posture:0}{state}{dash}";
        }

        private void Build()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "HandHudCanvas");
            var root = UiFactory.CreateRect(canvas.transform, "Hand", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 30f), new Vector2(760f, 260f)).transform;

            var cardSize = new Vector2(130f, 180f);
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                float x = -230f + i * 150f;
                slotImages[i] = UiFactory.CreateImage(root, $"Slot{i + 1}", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(x, 20f), cardSize, EmptyColor);
                slotValues[i] = UiFactory.CreateText(slotImages[i].transform, "Value", 84, TextAnchor.MiddleCenter, Color.white);
                slotNames[i] = UiFactory.CreateText(slotImages[i].transform, "Name", 18, TextAnchor.LowerCenter, Color.white);
                var key = UiFactory.CreateText(slotImages[i].transform, "Key", 22, TextAnchor.UpperLeft, Color.white);
                key.text = $" {SlotKeys[i]}";
            }

            parryImage = UiFactory.CreateImage(root, "ParrySlot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(250f, 20f), new Vector2(100f, 140f), EmptyColor);
            parryValue = UiFactory.CreateText(parryImage.transform, "Value", 64, TextAnchor.MiddleCenter, Color.white);
            var parryKey = UiFactory.CreateText(parryImage.transform, "Key", 22, TextAnchor.UpperLeft, Color.white);
            parryKey.text = " Q";

            var info = UiFactory.CreateRect(root, "Info", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 205f), new Vector2(760f, 30f));
            pilesText = UiFactory.CreateText(info, "Piles", 22, TextAnchor.MiddleCenter, Color.white);

            var reload = UiFactory.CreateRect(root, "Reload", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 235f), new Vector2(760f, 30f));
            reloadText = UiFactory.CreateText(reload, "ReloadPrompt", 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f));

            var statusRect = UiFactory.CreateRect(canvas.transform, "Status", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, -30f), new Vector2(900f, 40f));
            statusText = UiFactory.CreateText(statusRect, "StatusText", 28, TextAnchor.UpperLeft, Color.white);

            var help = UiFactory.CreateRect(canvas.transform, "Help", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-30f, -30f), new Vector2(700f, 40f));
            UiFactory.CreateText(help, "HelpText", 20, TextAnchor.UpperRight, new Color(1f, 1f, 1f, 0.7f)).text =
                "WASD move · Mouse aim · 1/2/3 play · Q parry (after enemy swings) · LMB poke · R reload";
        }

        private void Refresh()
        {
            if (deck == null || pilesText == null) return;
            var cycle = deck.Cycle;

            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var card = cycle.Hand[i];
                slotImages[i].color = card != null ? ElementColors.For(card.Data.Element) * new Color(1f, 1f, 1f, 1.8f) : EmptyColor;
                slotValues[i].text = card != null ? card.GetEffectiveValue().ToString() : "";
                slotNames[i].text = card != null ? $"{card.Data.DisplayName}\n{card.Data.Shape}\n" : "";
            }

            var parry = cycle.ParrySlot;
            parryImage.color = parry != null ? ElementColors.Zero : EmptyColor;
            parryValue.text = parry != null ? "0" : "";

            string reserve = cycle.ZeroReserveCount > 0 ? $" (+{cycle.ZeroReserveCount})" : "";
            pilesText.text = $"Deck {cycle.DrawCount}   Discard {cycle.DiscardCount}   Parry{reserve}";
            reloadText.text = cycle.CanReload ? "[R] RELOAD" : "";
        }
    }
}
