using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// World-space label above an enemy: the value of the card it is winding up (so the player can
    /// answer with a higher card or a timed 0), plus its HP. Faces the camera.
    /// </summary>
    public sealed class EnemyTelegraphView : MonoBehaviour
    {
        [SerializeField] private TrainingDummyEnemy enemy;
        [SerializeField] private float height = 2.6f;

        private Transform anchor;
        private Text valueText;
        private Text hpText;
        private Image fill;

        private void Awake()
        {
            if (enemy == null) enemy = GetComponentInParent<TrainingDummyEnemy>();
            Build();
        }

        private void Build()
        {
            var go = new GameObject("TelegraphCanvas", typeof(RectTransform));
            anchor = go.transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = Vector3.up * height;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)anchor;
            rt.sizeDelta = new Vector2(200f, 200f);
            rt.localScale = Vector3.one * 0.01f;

            fill = UiFactory.CreateImage(anchor, "WindUp", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
                new Vector2(0f, -40f), new Vector2(120f, 120f), new Color(1f, 0.3f, 0.2f, 0.35f));
            valueText = UiFactory.CreateText(anchor, "Value", 110, TextAnchor.MiddleCenter, Color.white);
            var hpRect = UiFactory.CreateRect(anchor, "Hp", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(200f, 40f));
            hpText = UiFactory.CreateText(hpRect, "HpText", 32, TextAnchor.UpperCenter, Color.white);
        }

        private void LateUpdate()
        {
            if (enemy == null) return;

            var cam = Camera.main;
            if (cam != null) anchor.rotation = cam.transform.rotation;

            var card = enemy.CurrentTelegraph;
            valueText.text = card == null ? "" : card.IsZeroBreaker ? "0" : card.GetEffectiveValue().ToString();
            float progress = enemy.TelegraphProgress;
            fill.enabled = card != null;
            fill.rectTransform.sizeDelta = new Vector2(120f, 120f * progress);

            var status = enemy.Status;
            hpText.text = status.IsDead ? "down" : status.IsStunned ? $"STUNNED  {status.Hp}" : $"HP {status.Hp}";
        }
    }
}
