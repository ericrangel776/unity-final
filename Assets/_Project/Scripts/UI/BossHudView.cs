using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Boss-only top bar (docs/specs/m4-overview.md): name, HP, posture, the 3 hand cards face-up, a 15-pip deck counter
    /// (left / stocked / spent / deleted / burned) and the SLEIGHT / RELOADING / EXHAUSTED label. The queue order stays
    /// hidden. Binds on <see cref="RunDirector.BossSpawned"/>, hides when there's no live boss. Polls in LateUpdate
    /// (HP, posture and timers change every frame). Sorted below the run screens, so they hide it like the rest.
    /// </summary>
    public sealed class BossHudView : MonoBehaviour
    {
        public const int SortingOrder = 2;
        public const string BossName = "THE WARDEN";
        private const float Width = 760f;
        private const int PipCount = RunContent.BossDeckSize;

        // See-through: the boss often stands right under the bar with the isometric camera.
        private static readonly Color Back = new Color(0.05f, 0.06f, 0.08f, 0.55f);
        private static readonly Color HpColor = new Color(0.85f, 0.2f, 0.2f);
        private static readonly Color PostureColor = new Color(1f, 0.8f, 0.3f);
        private static readonly Color BarBack = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        private static readonly Color EmptyCard = new Color(0.15f, 0.15f, 0.15f, 0.6f);
        public static readonly Color SleightColor = new Color(0.75f, 0.35f, 1f);
        public static readonly Color ReloadColor = new Color(0.4f, 0.85f, 1f);
        public static readonly Color ExhaustedColor = new Color(1f, 0.45f, 0.2f);

        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private RunDirector director;

        private BossEnemy boss;
        private GameObject panel;
        private Image hpFill, postureFill;
        private Text nameText, stateText, counterText;
        private readonly Image[] handCards = new Image[DeckCycle.HandSize];
        private readonly Text[] handValues = new Text[DeckCycle.HandSize];
        private readonly Image[] pips = new Image[PipCount];
        private readonly List<BossPip> pipState = new List<BossPip>();
        private (BossState state, int tenths, int sleight) shownState = ((BossState)(-1), -1, -1);
        private (int left, int stocked, int spent, int deleted, int burned, bool midCycle) shownCounter = (-1, -1, -1, -1, -1, false);

        public BossEnemy Boss => boss;
        public bool IsVisible => panel != null && panel.activeSelf;
        public string NameText => nameText.text;
        public string StateText => stateText.text;
        public string CounterText => counterText.text;
        public string HandValue(int slot) => handValues[slot].text;
        public float HpFill01 => hpFill.rectTransform.localScale.x;
        public float PostureFill01 => postureFill.rectTransform.localScale.x;
        public IReadOnlyList<BossPip> Pips => pipState;

        private void Awake()
        {
            if (director == null) director = FindAnyObjectByType<RunDirector>();
            Build();
            if (director != null)
            {
                director.BossSpawned += Bind;
                if (director.Boss != null) Bind(director.Boss);
            }
        }

        private void OnDestroy()
        {
            if (director != null) director.BossSpawned -= Bind;
        }

        /// <summary>Shows <paramref name="target"/> (null hides the bar).</summary>
        public void Bind(BossEnemy target)
        {
            boss = target;
            Refresh();
        }

        private void Build()
        {
            var canvas = UiFactory.CreateOverlayCanvas(transform, "BossHudCanvas", SortingOrder);
            var rect = UiFactory.CreateImage(canvas.transform, "BossBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -150f), new Vector2(Width, 150f), Back).rectTransform;
            panel = rect.gameObject;

            nameText = Label(rect, "Name", new Vector2(16f, -6f), new Vector2(300f, 30f), 24, TextAnchor.UpperLeft, Color.white);
            nameText.text = BossName;
            stateText = Label(rect, "State", new Vector2(Width - 316f, -6f), new Vector2(300f, 30f), 24,
                TextAnchor.UpperRight, Color.white);

            hpFill = Bar(rect, "Hp", -38f, 16f, HpColor);
            postureFill = Bar(rect, "Posture", -58f, 8f, PostureColor);

            // Hand: 3 face-up cards on the left.
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                handCards[i] = UiFactory.CreateImage(rect, $"BossCard{i + 1}", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(16f + i * 52f, -72f), new Vector2(46f, 64f), EmptyCard);
                handValues[i] = UiFactory.CreateText(handCards[i].transform, "Value", 30, TextAnchor.MiddleCenter, Color.white);
                UiFactory.ReadableOutline(handValues[i], 1.5f);
            }

            // Deck counter: 15 pips and a line of text.
            for (int i = 0; i < PipCount; i++)
                pips[i] = UiFactory.CreateImage(rect, $"Pip{i + 1}", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(190f + i * 36f, -76f), new Vector2(30f, 30f), BarBack);
            counterText = Label(rect, "Counter", new Vector2(190f, -112f), new Vector2(Width - 206f, 28f), 18,
                TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.85f));
            panel.SetActive(false);
        }

        private static Text Label(RectTransform parent, string name, Vector2 pos, Vector2 size, int fontSize,
            TextAnchor anchor, Color color)
        {
            var r = UiFactory.CreateRect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            return UiFactory.CreateText(r, name + "Text", fontSize, anchor, color);
        }

        private static Image Bar(RectTransform parent, string name, float y, float height, Color color)
        {
            var size = new Vector2(Width - 32f, height);
            UiFactory.CreateImage(parent, name + "Back", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, y), size, BarBack);
            var fill = UiFactory.CreateImage(parent, name + "Fill", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, y), size, color);
            fill.rectTransform.pivot = new Vector2(0f, 1f); // scale from the left edge
            return fill;
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (panel == null) return;
            bool show = boss != null && boss.Status != null && !boss.Status.IsDead;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) return;

            var status = boss.Status;
            hpFill.rectTransform.localScale = new Vector3(status.MaxHp > 0 ? Mathf.Clamp01(status.Hp / (float)status.MaxHp) : 0f, 1f, 1f);
            postureFill.rectTransform.localScale = new Vector3(status.MaxPosture > 0f ? Mathf.Clamp01(status.Posture / status.MaxPosture) : 0f, 1f, 1f);

            int sleight = boss.PendingSleight.HasValue ? boss.PendingSleight.Value.Value : 0;
            // The state line shows tenths of a second: rebuild it only when the state, the tenth or the value changes.
            var nowState = (boss.State, (int)System.Math.Round(Mathf.Max(0f, boss.StateRemaining) * 10f, System.MidpointRounding.AwayFromZero), sleight);
            if (nowState != shownState)
            {
                shownState = nowState;
                stateText.text = RunUiText.BossState(boss.State, boss.StateRemaining, sleight);
            }
            stateText.color = boss.State == BossState.SleightWindup ? SleightColor
                : boss.State == BossState.ReloadWindow ? ReloadColor
                : boss.State == BossState.Exhausted ? ExhaustedColor : new Color(0.7f, 0.7f, 0.75f);

            var cycle = boss.Deck.Cycle;
            int inHand = 0;
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var card = cycle.Hand[i];
                if (card != null) inHand++;
                handCards[i].color = card != null ? ElementColors.CardFace(card.Data.Element) : EmptyCard;
                handValues[i].text = card != null ? RunUiText.Number(card.GetEffectiveValue()) : "";
            }

            int left = cycle.DrawCount + inHand;
            bool midCycle = boss.State == BossState.Idle || boss.State == BossState.SleightWindup;
            var nowCounter = (left, cycle.StockCount, cycle.DiscardCount, cycle.DeletedThisCycleCount, cycle.BurnedCount, midCycle);
            if (nowCounter == shownCounter) return;
            shownCounter = nowCounter;
            pipState.Clear();
            pipState.AddRange(RunUiText.BossPips(left, cycle.StockCount, cycle.DiscardCount,
                cycle.DeletedThisCycleCount, cycle.BurnedCount));
            for (int i = 0; i < PipCount; i++)
                pips[i].color = i < pipState.Count ? PipColor(pipState[i]) : BarBack;
            counterText.text = RunUiText.BossCounter(left, cycle.StockCount, cycle.DiscardCount,
                cycle.DeletedThisCycleCount, cycle.BurnedCount, BossRules.ExhaustDeleteThreshold, midCycle);
        }


        private static Color PipColor(BossPip pip)
        {
            switch (pip)
            {
                case BossPip.Left: return new Color(0.9f, 0.9f, 0.9f);
                case BossPip.Stocked: return SleightColor;
                case BossPip.Spent: return new Color(0.35f, 0.35f, 0.38f);
                case BossPip.Deleted: return new Color(0.95f, 0.2f, 0.25f);
                default: return new Color(0.55f, 0.3f, 0.1f);
            }
        }
    }
}
