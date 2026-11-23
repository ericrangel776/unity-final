using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Boss wind-up readability (docs/specs/m4-overview.md): a ground warning ring with an inner ring that grows to meet
    /// it as the swing comes, and for Sleights a distinct purple style plus a big overhead "SLEIGHT 15" label (the normal
    /// card value already shows overhead via the telegraph view), drawn on the ring's near edge so it stays on screen. The FX root is unparented so the boss's scale doesn't
    /// distort it, and is destroyed with the boss.
    /// </summary>
    [RequireComponent(typeof(BossEnemy))]
    public sealed class BossTelegraphFx : MonoBehaviour
    {
        public const float Radius = 3.2f;
        private const int Segments = 64;
        private const float LabelHeight = 0.6f;
        public static readonly Color SwingColor = new Color(1f, 0.3f, 0.2f, 0.9f);
        public static readonly Color SleightColor = new Color(0.75f, 0.35f, 1f, 1f);

        private BossEnemy boss;
        private Transform root;
        private LineRenderer outer, inner;
        private TextMesh label;
        private int shownLabelValue = int.MinValue;

        public bool IsShowing => root != null && root.gameObject.activeSelf;
        public bool IsSleightStyle { get; private set; }
        public string LabelText => label != null && label.gameObject.activeSelf ? label.text : "";
        /// <summary>0..1: how far the inner ring has grown toward the outer one.</summary>
        public float Progress { get; private set; }
        public GameObject Root => root != null ? root.gameObject : null;

        private void Awake()
        {
            boss = GetComponent<BossEnemy>();
            root = new GameObject("BossTelegraphFx").transform;
            outer = FxShapes.CreateLine(root, "Outer", Segments, 0.12f);
            inner = FxShapes.CreateLine(root, "Inner", Segments, 0.06f);

            var labelGo = new GameObject("SleightLabel");
            labelGo.transform.SetParent(root, false);
            label = labelGo.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = 0.08f;
            label.fontStyle = FontStyle.Bold;
            label.color = SleightColor;
            root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        private void LateUpdate()
        {
            bool sleight = boss.State == BossState.SleightWindup && !boss.Status.IsDead;
            bool swing = !sleight && boss.Body.CurrentTelegraph != null && !boss.Status.IsDead;
            bool show = sleight || swing;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;

            IsSleightStyle = sleight;
            Progress = sleight
                ? 1f - Mathf.Clamp01(boss.StateRemaining / Mathf.Max(0.0001f, boss.StateDuration))
                : boss.Body.TelegraphProgress;

            var center = transform.position + Vector3.up * 0.05f;
            var color = sleight ? SleightColor : SwingColor;
            // Pulses faster as the swing approaches (unscaled: still alive during hit-stop).
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(6f, 22f, Progress));
            FxShapes.SetColor(outer, color * new Color(1f, 1f, 1f, pulse));
            FxShapes.SetColor(inner, color);
            FxShapes.SetArc(outer, center, Radius, 1f);
            FxShapes.SetArc(inner, center, Radius * Mathf.Max(0.05f, Progress), 1f);
            outer.startWidth = outer.endWidth = sleight ? 0.2f : 0.12f;

            label.gameObject.SetActive(sleight);
            if (sleight)
            {
                int value = boss.PendingSleight.HasValue ? boss.PendingSleight.Value.Value : -1;
                if (value != shownLabelValue)
                {
                    shownLabelValue = value;
                    label.text = value >= 0 ? $"SLEIGHT {value}" : "SLEIGHT";
                }
                // On the ring's near edge (toward the camera): above the boss it would leave the top of the screen.
                var cam = Camera.main;
                var toward = FlatDirection.Of(cam != null ? -cam.transform.forward : Vector3.back, Vector3.back);
                label.transform.position = transform.position + toward * Radius + Vector3.up * LabelHeight;
                if (cam != null) label.transform.rotation = cam.transform.rotation;
                float pop = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 14f);
                label.transform.localScale = Vector3.one * pop;
            }
        }
    }
}
