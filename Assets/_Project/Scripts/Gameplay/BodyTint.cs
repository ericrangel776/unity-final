using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// The one owner of a body's colour override. <see cref="HitFlash"/>, <see cref="BossStateFx"/> and
    /// <see cref="ElementStatusFx"/> each set a layer; the highest active layer is applied once per frame through a
    /// <see cref="MaterialPropertyBlock"/> (shared materials are never modified). A flash ending hands the body back to
    /// whatever tint is still active, instead of wiping it. Added on demand by <see cref="For"/>.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after every FX LateUpdate that sets a layer this frame
    public sealed class BodyTint : MonoBehaviour
    {
        /// <summary>Higher wins.</summary>
        public enum Layer { Status = 0, State = 1, Flash = 2 }

        private const int LayerCount = 3;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly bool[] active = new bool[LayerCount];
        private readonly Color[] colors = new Color[LayerCount];
        private readonly float[] amounts = new float[LayerCount];
        private readonly bool[] fromWhite = new bool[LayerCount];
        private Renderer[] renderers;
        private Color[] baseColors;
        private MaterialPropertyBlock block;
        private bool applied;
        private bool hasBaseOverride;

        /// <summary>The body's tint owner, added on first use. Captures the renderers that exist at that moment.</summary>
        public static BodyTint For(GameObject body)
        {
            var tint = body.GetComponent<BodyTint>();
            return tint != null ? tint : body.AddComponent<BodyTint>();
        }

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var m = renderers[i].sharedMaterial;
                baseColors[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            }
            block = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Turns <paramref name="layer"/> on: each renderer goes <paramref name="amount"/> of the way from its own colour
        /// (or from white, with <paramref name="lerpFromWhite"/>) to <paramref name="color"/>. 1 = solid colour.
        /// </summary>
        public void Set(Layer layer, Color color, float amount = 1f, bool lerpFromWhite = false)
        {
            int i = (int)layer;
            active[i] = true;
            colors[i] = color;
            amounts[i] = amount;
            fromWhite[i] = lerpFromWhite;
        }

        /// <summary>
        /// Replaces every renderer's own colour with <paramref name="color"/> (an enemy variant's look). With no layer
        /// active the body shows it; layers still win over it, and a lerp from the own colour starts from it.
        /// </summary>
        public void SetBaseColor(Color color)
        {
            for (int i = 0; i < baseColors.Length; i++) baseColors[i] = color;
            hasBaseOverride = true;
            if (!applied) Release(); // no layer showing: put the base colour on now
        }

        public void Clear(Layer layer) => active[(int)layer] = false;

        public bool IsActive(Layer layer) => active[(int)layer];

        private void LateUpdate()
        {
            int top = -1;
            for (int i = LayerCount - 1; i >= 0; i--)
                if (active[i]) { top = i; break; }

            if (top < 0)
            {
                if (applied) Release();
                return;
            }

            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                var from = fromWhite[top] ? Color.white : baseColors[r];
                block.SetColor(BaseColorId, Color.Lerp(from, colors[top], amounts[top]));
                renderers[r].SetPropertyBlock(block);
            }
            applied = true;
        }

        private void OnDisable()
        {
            if (applied) Release();
        }

        /// <summary>Back to the base: the override colour if one was set, otherwise no property block at all.</summary>
        private void Release()
        {
            applied = false;
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                if (hasBaseOverride)
                {
                    block.SetColor(BaseColorId, baseColors[r]);
                    renderers[r].SetPropertyBlock(block);
                }
                else renderers[r].SetPropertyBlock(null);
            }
        }
    }
}
