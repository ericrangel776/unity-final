using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// On a Card Break, shows the losing card (its value, in its element color) at the clash point,
    /// holds it while the hit-stop has time frozen, then shatters it into spinning shards. Runs on
    /// unscaled time and reuses a small pool of card widgets, so breaks never allocate.
    /// </summary>
    public sealed class CardShatterView : MonoBehaviour
    {
        [SerializeField] private float minHoldSeconds = 0.08f;
        [SerializeField] private float shatterSeconds = 0.45f;
        [SerializeField] private float shardSpeed = 520f;
        [SerializeField] private float shardGravity = 1400f;

        private const int PoolSize = 3;
        private const int ShardColumns = 2;
        private const int ShardRows = 3;
        private static readonly Vector2 CardSize = new Vector2(78f, 108f);

        private ShatterCard[] pool;
        private int next;

        /// <summary>The value shown by the most recent break (null if nothing is showing).</summary>
        public string ShownValue { get; private set; }
        public bool IsShowing
        {
            get
            {
                if (pool == null) return false;
                foreach (var card in pool) if (card.Active) return true;
                return false;
            }
        }
        /// <summary>Shards currently flying (across all cards).</summary>
        public int ActiveShardCount
        {
            get
            {
                int n = 0;
                if (pool != null) foreach (var card in pool) if (card.Shattered) n += ShardColumns * ShardRows;
                return n;
            }
        }

        private void Awake()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "CardShatterCanvas", sortingOrder: 9);
            pool = new ShatterCard[PoolSize];
            for (int i = 0; i < PoolSize; i++) pool[i] = new ShatterCard(canvas.transform, i);
        }

        private void OnEnable() => ClashSystem.ClashOccurred += HandleClash;
        private void OnDisable() => ClashSystem.ClashOccurred -= HandleClash;

        private void HandleClash(ClashEvent e)
        {
            if (e.Result.Outcome != ClashOutcome.AWins && e.Result.Outcome != ClashOutcome.BWins) return;
            var loser = e.Result.Outcome == ClashOutcome.AWins ? e.B : e.A;
            string value = loser.IsZeroBreaker ? "0" : loser.Value.ToString();
            var color = ElementColors.For(loser);
            color.a = 1f;

            var card = pool[next];
            next = (next + 1) % PoolSize;
            card.Show(value, color, e.Position, minHoldSeconds);
            ShownValue = value;
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var cam = Camera.main;
            bool frozen = Time.timeScale == 0f;
            bool any = false;
            foreach (var card in pool)
            {
                card.Tick(dt, cam, frozen, shatterSeconds, shardSpeed, shardGravity);
                any |= card.Active;
            }
            if (!any) ShownValue = null;
        }

        /// <summary>One card widget plus its pre-built shards.</summary>
        private sealed class ShatterCard
        {
            private readonly RectTransform root;
            private readonly Image face;
            private readonly Text label;
            private readonly Image[] shards = new Image[ShardColumns * ShardRows];
            private readonly Vector2[] shardHome = new Vector2[ShardColumns * ShardRows];
            private readonly Vector2[] velocity = new Vector2[ShardColumns * ShardRows];
            private readonly float[] spin = new float[ShardColumns * ShardRows];
            private Vector3 worldPosition;
            private float holdRemaining;
            private float shatterElapsed;

            public bool Active { get; private set; }
            public bool Shattered { get; private set; }

            public ShatterCard(Transform canvas, int index)
            {
                root = UiFactory.CreateRect(canvas, $"ShatterCard{index}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, CardSize);
                face = UiFactory.CreateImage(root, "Face", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, CardSize, Color.white);
                label = UiFactory.CreateText(root, "Value", 56, TextAnchor.MiddleCenter, Color.white);

                var shardSize = new Vector2(CardSize.x / ShardColumns, CardSize.y / ShardRows);
                for (int r = 0; r < ShardRows; r++)
                for (int c = 0; c < ShardColumns; c++)
                {
                    int i = r * ShardColumns + c;
                    shardHome[i] = new Vector2(
                        (c + 0.5f) * shardSize.x - CardSize.x * 0.5f,
                        CardSize.y * 0.5f - (r + 0.5f) * shardSize.y);
                    shards[i] = UiFactory.CreateImage(root, $"Shard{i}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        shardHome[i], shardSize * 0.94f, Color.white);
                }
                Hide();
            }

            public void Show(string value, Color color, Vector3 position, float minHold)
            {
                worldPosition = position;
                holdRemaining = minHold;
                shatterElapsed = 0f;
                Active = true;
                Shattered = false;

                face.color = color;
                face.enabled = true;
                label.text = value;
                label.color = Color.white;
                label.enabled = true;
                root.localScale = Vector3.one * 1.15f;
                root.localRotation = Quaternion.identity;
                foreach (var shard in shards) shard.enabled = false;
            }

            public void Tick(float dt, Camera cam, bool frozen, float shatterSeconds, float speed, float gravity)
            {
                if (!Active) return;
                Place(cam);

                if (!Shattered)
                {
                    // Hold whole (with a tremble) through the freeze, then break apart.
                    root.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-4f, 4f));
                    if (frozen || (holdRemaining -= dt) > 0f) return;
                    Shatter(speed);
                }

                shatterElapsed += dt;
                float t = Mathf.Clamp01(shatterElapsed / shatterSeconds);
                for (int i = 0; i < shards.Length; i++)
                {
                    velocity[i].y -= gravity * dt;
                    var rt = shards[i].rectTransform;
                    rt.anchoredPosition += velocity[i] * dt;
                    rt.localRotation *= Quaternion.Euler(0f, 0f, spin[i] * dt);
                    var c = shards[i].color;
                    c.a = 1f - t;
                    shards[i].color = c;
                }
                // The number flies up and fades on its own, so you can still read what broke.
                label.rectTransform.anchoredPosition += Vector2.up * (speed * 0.35f * dt);
                var lc = label.color;
                lc.a = 1f - t;
                label.color = lc;

                if (t >= 1f) Hide();
            }

            private void Shatter(float speed)
            {
                Shattered = true;
                face.enabled = false;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
                for (int i = 0; i < shards.Length; i++)
                {
                    var rt = shards[i].rectTransform;
                    rt.anchoredPosition = shardHome[i];
                    rt.localRotation = Quaternion.identity;
                    shards[i].color = face.color;
                    shards[i].enabled = true;
                    // Outward from the card's center, plus a little upward kick and randomness.
                    var dir = shardHome[i].normalized + Random.insideUnitCircle * 0.35f;
                    velocity[i] = dir.normalized * (speed * Random.Range(0.7f, 1.2f)) + Vector2.up * (speed * 0.4f);
                    spin[i] = Random.Range(-540f, 540f);
                }
            }

            private void Hide()
            {
                Active = false;
                Shattered = false;
                face.enabled = false;
                label.enabled = false;
                label.rectTransform.anchoredPosition = Vector2.zero;
                foreach (var shard in shards) shard.enabled = false;
            }

            private void Place(Camera cam)
            {
                if (cam == null) return;
                var canvasRect = (RectTransform)root.parent;
                var screen = cam.WorldToScreenPoint(worldPosition);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
                root.anchoredPosition = local;
            }
        }
    }
}
