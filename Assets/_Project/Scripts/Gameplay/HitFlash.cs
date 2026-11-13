using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Flashes every child renderer white for a moment when a damaging body hit lands. Uses a
    /// <see cref="MaterialPropertyBlock"/> so shared materials are never modified, and unscaled
    /// time so the flash still plays during hit-stop.
    /// </summary>
    [RequireComponent(typeof(CombatantStatus))]
    public sealed class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private float flashSeconds = 0.1f;
        [SerializeField] private Color flashColor = Color.white;

        private CombatantStatus status;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float remaining;

        public bool IsFlashing => remaining > 0f;

        private void Awake()
        {
            status = GetComponent<CombatantStatus>();
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        private void OnEnable() => status.HitTaken += HandleHit;

        private void OnDisable()
        {
            status.HitTaken -= HandleHit;
            if (remaining > 0f) Clear();
        }

        private void HandleHit(int damage, Vector3 direction)
        {
            remaining = flashSeconds;
            block.SetColor(BaseColorId, flashColor);
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(block);
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
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(null);
        }
    }
}
