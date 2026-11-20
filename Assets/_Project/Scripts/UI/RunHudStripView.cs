using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Always-on strip along the top edge during a run: room, currency, Momentum pips and relics (with Ashen stacks).
    /// Polls in LateUpdate: the values change from many places (rewards, shop, relic effects) and the strip is tiny.
    /// Drawn above the run screens' dim (higher sorting order) so currency stays readable while shopping; it has no
    /// raycast targets, so it never blocks a click.
    /// </summary>
    public sealed class RunHudStripView : MonoBehaviour
    {
        public const int SortingOrder = RunScreens.SortingOrder + 10;
        private static readonly Color Back = new Color(0.05f, 0.06f, 0.08f, 0.75f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
        private static readonly Color Momentum = new Color(1f, 0.55f, 0.25f);

        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private RunDirector director;

        private GameObject strip;
        private Text room, currency, momentum, relics;
        private GameObject burnChip, chillChip;
        private Text burnText, chillText;
        private ElementStatusEffects playerEffects;

        public bool IsVisible => strip != null && strip.activeSelf;
        /// <summary>The player's BURN / CHILL chips, shown only while the status lasts.</summary>
        public bool IsBurnChipVisible => burnChip != null && burnChip.activeInHierarchy;
        public bool IsChillChipVisible => chillChip != null && chillChip.activeInHierarchy;
        public string BurnChipText => burnText.text;
        public string ChillChipText => chillText.text;
        public string RoomText => room.text;
        public string CurrencyText => currency.text;
        public string MomentumText => momentum.text;
        public string RelicsText => relics.text;

        private void Awake()
        {
            if (director == null) director = FindAnyObjectByType<RunDirector>();
            Build();
        }

        private void Build()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "RunHudCanvas", SortingOrder);
            var rect = UiFactory.CreateImage(canvas.transform, "Strip", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -84f), new Vector2(1400f, 56f), Back).rectTransform;
            strip = rect.gameObject;

            room = Field(rect, "Room", 0f, 220f, Color.white);
            currency = Field(rect, "Currency", 220f, 200f, Gold);
            momentum = Field(rect, "Momentum", 420f, 300f, Momentum);
            relics = Field(rect, "Relics", 720f, 680f, Color.white);
            burnChip = Chip(rect, "BurnChip", 0f, ElementColors.For(Core.Element.Fire), out burnText);
            chillChip = Chip(rect, "ChillChip", 170f, ElementColors.For(Core.Element.Ice), out chillText);
            strip.SetActive(false);
        }

        /// <summary>A small status chip hanging under the strip's left edge.</summary>
        private static GameObject Chip(RectTransform parent, string name, float x, Color color, out Text label)
        {
            var image = UiFactory.CreateImage(parent, name, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(x + 20f, -6f), new Vector2(150f, 40f), new Color(color.r, color.g, color.b, 0.85f));
            label = UiFactory.CreateText(image.transform, name + "Text", 22, TextAnchor.MiddleCenter, Color.black);
            image.gameObject.SetActive(false);
            return image.gameObject;
        }

        private static Text Field(RectTransform parent, string name, float x, float width, Color color)
        {
            var cell = UiFactory.CreateRect(parent, name, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(x + 20f, 0f), new Vector2(width - 20f, 56f));
            return UiFactory.CreateText(cell, name + "Text", 26, TextAnchor.MiddleLeft, color);
        }

        private void LateUpdate() => Refresh();

        public void Refresh()
        {
            var run = director != null ? director.Run : null;
            bool visible = run != null;
            if (strip.activeSelf != visible) strip.SetActive(visible);
            if (!visible) return;

            room.text = RunUiText.Room(run.RoomNumber);
            currency.text = $"Currency {run.Currency}";
            momentum.text = RunUiText.Momentum(run.Momentum.Stacks);
            int ashen = director.PlayerRelics != null ? director.PlayerRelics.AshenStacks : 0;
            relics.text = RunUiText.Relics(run.Relics, ashen);

            if (playerEffects == null && director.Player != null)
                playerEffects = director.Player.GetComponent<ElementStatusEffects>();
            bool burning = playerEffects != null && playerEffects.IsBurning;
            bool chilled = playerEffects != null && playerEffects.IsChilled;
            if (burnChip.activeSelf != burning) burnChip.SetActive(burning);
            if (chillChip.activeSelf != chilled) chillChip.SetActive(chilled);
            if (burning) burnText.text = RunUiText.StatusChip("BURN", playerEffects.BurnRemaining);
            if (chilled) chillText.text = RunUiText.StatusChip("CHILL", playerEffects.ChillRemaining);
        }
    }
}
