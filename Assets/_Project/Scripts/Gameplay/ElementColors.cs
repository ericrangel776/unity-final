using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>Greybox color coding so cards can be read at a glance without text.</summary>
    public static class ElementColors
    {
        public static readonly Color Zero = new Color(0.95f, 0.95f, 1f, 0.6f);

        public static Color For(Element element)
        {
            switch (element)
            {
                case Element.Fire: return new Color(1f, 0.45f, 0.15f, 0.5f);
                case Element.Ice: return new Color(0.4f, 0.8f, 1f, 0.5f);
                case Element.Lightning: return new Color(1f, 0.9f, 0.2f, 0.5f);
                default: return new Color(0.8f, 0.8f, 0.8f, 0.5f);
            }
        }

        public static Color For(in AttackPayload payload) => payload.IsZeroBreaker ? Zero : For(payload.Element);
    }
}
