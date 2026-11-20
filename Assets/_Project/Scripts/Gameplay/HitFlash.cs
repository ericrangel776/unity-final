using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Flashes every child renderer white for a moment when a damaging body hit lands, on the top
    /// <see cref="BodyTint"/> layer. Unscaled time, so the flash still plays during hit-stop.
    /// </summary>
    [RequireComponent(typeof(CombatantStatus))]
    public sealed class HitFlash : MonoBehaviour
    {
        [SerializeField] private float flashSeconds = 0.1f;
        [SerializeField] private Color flashColor = Color.white;
        [Tooltip("Flash when one of our cards is broken in a clash.")]
        [SerializeField] private float breakFlashSeconds = 0.2f;
        [SerializeField] private Color breakColor = new Color(1f, 0.85f, 0.2f);

        private CombatantStatus status;
        private BodyTint tint;
        private float remaining;

        public bool IsFlashing => remaining > 0f;

        private void Awake()
        {
            status = GetComponent<CombatantStatus>();
            tint = BodyTint.For(gameObject);
        }

        public Color CurrentColor { get; private set; }

        private void OnEnable()
        {
            status.HitTaken += HandleHit;
            status.Broken += HandleBroken;
        }

        private void OnDisable()
        {
            status.HitTaken -= HandleHit;
            status.Broken -= HandleBroken;
            if (remaining > 0f) Clear();
        }

        private void HandleHit(int damage, Vector3 direction) => Flash(flashColor, flashSeconds);

        private void HandleBroken() => Flash(breakColor, breakFlashSeconds);

        private void Flash(Color color, float seconds)
        {
            remaining = seconds;
            CurrentColor = color;
            tint.Set(BodyTint.Layer.Flash, color);
        }

        private void LateUpdate()
        {
            if (remaining <= 0f) return;
            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f) Clear();
        }

        private void Clear()
        {
            remaining = 0f;
            tint.Clear(BodyTint.Layer.Flash);
        }
    }
}
