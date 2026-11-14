using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Manga-style speed lines along the left and right screen edges while the Perfect-reload move-speed
    /// boost lasts. Streaks rush outward and fade as the boost runs down; hidden when there is no boost.
    /// </summary>
    public sealed class SpeedLinesView : MonoBehaviour
    {
        [SerializeField] private DeckRuntimeManager deck;
        [SerializeField] private int lineCount = 12;
        [SerializeField] private float maxAlpha = 0.6f;
        [SerializeField] private float speed = 1400f;

        // Streaks live in the outer band of each half of the 1920-wide reference canvas.
        private const float InnerX = 620f;
        private const float OuterX = 1000f;

        private RectTransform root;
        private Image[] lines;
        private float[] offsets;
        private float alpha;

        public float Alpha => alpha;
        public bool IsActive => root != null && root.gameObject.activeSelf;

        public void Bind(DeckRuntimeManager deckManager) => deck = deckManager;

        private void Awake()
        {
            if (deck == null)
            {
                var player = FindAnyObjectByType<PlayerCombatInput>();
                if (player != null) deck = player.GetComponent<DeckRuntimeManager>();
            }
            Build();
        }

        private void Build()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "SpeedLinesCanvas", sortingOrder: -1);
            root = UiFactory.CreateRect(canvas.transform, "SpeedLines", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            lines = new Image[lineCount];
            offsets = new float[lineCount];
            var rng = new System.Random(1234); // fixed layout: same streak pattern every run
            for (int i = 0; i < lineCount; i++)
            {
                float y = -450f + (float)rng.NextDouble() * 900f;
                float length = 120f + (float)rng.NextDouble() * 180f;
                float thickness = 2f + (float)rng.NextDouble() * 3f;
                lines[i] = UiFactory.CreateImage(root, $"Line{i}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(length, thickness), Color.white);
                offsets[i] = (float)rng.NextDouble();
            }
            root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            var reload = deck != null ? deck.Reload : null;
            float remaining = reload != null ? reload.BoostRemaining : 0f;
            alpha = maxAlpha * Mathf.Clamp01(remaining / ActiveReloadRules.BoostSeconds);
            bool active = alpha > 0f;
            if (root.gameObject.activeSelf != active) root.gameObject.SetActive(active);
            if (!active) return;

            float band = OuterX - InnerX;
            for (int i = 0; i < lines.Length; i++)
            {
                // Scaled time: hit-stop freezes the streaks with the world.
                offsets[i] = Mathf.Repeat(offsets[i] + speed * Time.deltaTime / band, 1f);
                float side = i % 2 == 0 ? 1f : -1f;
                var rt = lines[i].rectTransform;
                rt.anchoredPosition = new Vector2(side * (InnerX + offsets[i] * band), rt.anchoredPosition.y);
                // Each streak fades in as it leaves the inner edge so lines never pop in mid-screen.
                lines[i].color = new Color(1f, 1f, 1f, alpha * Mathf.SmoothStep(0f, 1f, offsets[i] * 3f));
            }
        }
    }
}
