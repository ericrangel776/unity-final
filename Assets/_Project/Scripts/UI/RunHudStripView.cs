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

        public bool IsVisible => strip != null && strip.activeSelf;
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
            strip.SetActive(false);
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
        }
    }
}
