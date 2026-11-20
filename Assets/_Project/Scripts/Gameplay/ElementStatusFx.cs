using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Greybox readability for element statuses: afflicted bodies pulse in the element colour, and one small pip per
    /// active status floats above the head. The tint goes through a <see cref="MaterialPropertyBlock"/> and yields
    /// to <see cref="HitFlash"/> while it flashes. Runs on unscaled time.
    /// </summary>
    [RequireComponent(typeof(ElementStatusEffects))]
    public sealed class ElementStatusFx : MonoBehaviour
    {
        /// <summary>
        /// Pip height above the root (the feet). The greybox body is 2 u tall, so this floats them just over the head
        /// (the spec's HitFeedbackHeight + 0.6 = 1.6 sat inside the body).
        /// </summary>
        public const float PipHeight = 2.35f;
        public const float PipSize = 0.22f;
        public const float PipSpacing = 0.3f;
        public const float PulseHz = 3f;
        public const float MaxTint = 0.65f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private ElementStatusEffects effects;
        private HitFlash hitFlash;
        private Renderer[] renderers;
        private Color[] baseColors;
        private MaterialPropertyBlock block;
        private Transform burnPip, chillPip;
        private bool tinted;

        public bool HasBurnPip => burnPip != null && burnPip.gameObject.activeSelf;
        public bool HasChillPip => chillPip != null && chillPip.gameObject.activeSelf;
        public int PipCount => (HasBurnPip ? 1 : 0) + (HasChillPip ? 1 : 0);
        /// <summary>The tint applied last frame (clear when untinted).</summary>
        public Color CurrentTint { get; private set; }

        private void Awake()
        {
            effects = GetComponent<ElementStatusEffects>();
            hitFlash = GetComponent<HitFlash>();
            // Before the pips exist, so they are never tinted.
            renderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var m = renderers[i].sharedMaterial;
                baseColors[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            }
            block = new MaterialPropertyBlock();
            burnPip = CreatePip("BurnPip", ElementColors.For(Element.Fire));
            chillPip = CreatePip("ChillPip", ElementColors.For(Element.Ice));
        }

        private void OnDisable() => ClearTint();

        private void LateUpdate()
        {
            bool burning = effects.IsBurning, chilled = effects.IsChilled;
            burnPip.gameObject.SetActive(burning);
            chillPip.gameObject.SetActive(chilled);
            if (burning || chilled) PlacePips(burning, chilled);

            if (!burning && !chilled)
            {
                ClearTint();
                return;
            }
            if (hitFlash != null && hitFlash.IsFlashing) return; // the flash owns the block while it plays

            var element = burning && (!chilled || Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f) ? Element.Fire : Element.Ice;
            var color = ClashImpactFx.Opaque(ElementColors.For(element));
            float pulse = (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseHz * Mathf.PI * 2f)) * MaxTint;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                block.SetColor(BaseColorId, Color.Lerp(baseColors[i], color, pulse));
                renderers[i].SetPropertyBlock(block);
            }
            CurrentTint = color;
            tinted = true;
        }

        private void PlacePips(bool burning, bool chilled)
        {
            var head = transform.position + Vector3.up * PipHeight * Mathf.Max(0.01f, transform.lossyScale.y);
            var cam = Camera.main;
            var right = cam != null ? cam.transform.right : Vector3.right;
            int count = (burning ? 1 : 0) + (chilled ? 1 : 0);
            float x = -(count - 1) * 0.5f * PipSpacing;
            if (burning) { Place(burnPip, head + right * x); x += PipSpacing; }
            if (chilled) Place(chillPip, head + right * x);
        }

        private void Place(Transform pip, Vector3 position)
        {
            pip.position = position;
            // World size stays constant on scaled bodies (Elites, the boss).
            float s = Mathf.Max(0.01f, transform.lossyScale.x);
            pip.localScale = Vector3.one * (PipSize / s);
        }

        private void ClearTint()
        {
            if (!tinted) return;
            tinted = false;
            CurrentTint = Color.clear;
            if (hitFlash != null && hitFlash.IsFlashing) return;
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(null);
        }

        private Transform CreatePip(string name, Color color)
        {
            var pip = FxShapes.CreateVisual(PrimitiveType.Sphere, name, transform, ClashImpactFx.Opaque(color));
            pip.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pip.SetActive(false);
            return pip.transform;
        }
    }
}
