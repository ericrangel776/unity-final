using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Greybox readability for the hand-heal channel: a green ring at the player's feet that fills clockwise with
    /// <see cref="HealChannel.Progress01"/>. Hidden when not channelling. See docs/specs/heal-rework.md.
    /// </summary>
    [RequireComponent(typeof(HealChannel))]
    public sealed class HealChannelFx : MonoBehaviour
    {
        public const float Radius = 0.9f;
        public const float Width = 0.09f;
        /// <summary>Just above the ground so the ring doesn't z-fight it.</summary>
        public const float Height = 0.05f;
        public const int Points = 48;

        private HealChannel channel;
        private LineRenderer ring;

        /// <summary>True while the ring is drawn.</summary>
        public bool RingVisible => ring != null && ring.enabled;
        /// <summary>The fill fraction drawn last frame (0 when hidden).</summary>
        public float ShownFill { get; private set; }
        public LineRenderer Ring => ring;

        private void Awake()
        {
            channel = GetComponent<HealChannel>();
            ring = FxShapes.CreateLine(transform, "HealChannelRing", Points, Width);
            FxShapes.SetColor(ring, ElementColors.Heal);
            ring.enabled = false;
        }

        private void OnDisable() => Hide();

        private void LateUpdate()
        {
            if (channel == null || !channel.IsChannelling)
            {
                Hide();
                return;
            }
            ShownFill = channel.Progress01;
            // A sliver at 0 so the ring appears the moment the channel starts.
            FxShapes.SetArc(ring, Feet, Radius, Mathf.Max(0.02f, ShownFill));
            ring.enabled = true;
        }

        private Vector3 Feet
        {
            get
            {
                // The rig's root sits at body centre when it has a CharacterController (the player prefab), else at the feet.
                var body = GetComponent<CharacterController>();
                float down = body != null ? body.height * 0.5f - body.center.y : 0f;
                return transform.position + Vector3.up * (Height - down);
            }
        }

        private void Hide()
        {
            ShownFill = 0f;
            if (ring != null) ring.enabled = false;
        }
    }
}
