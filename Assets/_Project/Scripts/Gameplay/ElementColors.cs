using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>Greybox color coding so cards can be read at a glance without text.</summary>
    public static class ElementColors
    {
        public static readonly Color Zero = new Color(0.95f, 0.95f, 1f, 0.6f);
        /// <summary>Heal cards, the heal channel ring and heal numbers.</summary>
        public static readonly Color Heal = new Color(0.35f, 0.95f, 0.45f, 0.9f);

        public static Color For(Element element)
        {
            switch (element)
            {
                case Element.Fire: return new Color(1f, 0.45f, 0.15f, 0.5f);
                case Element.Ice: return new Color(0.4f, 0.8f, 1f, 0.5f);
                case Element.Lightning: return new Color(1f, 0.9f, 0.2f, 0.5f);
                case Element.Wind: return new Color(0.33f, 0.36f, 0.45f, 0.5f); // storm-cloud slate
                default: return new Color(0.8f, 0.8f, 0.8f, 0.5f);
            }
        }

        public static Color For(in AttackPayload payload) => payload.IsZeroBreaker ? Zero : For(payload.Element);

        /// <summary>A card face in the HUD: the element colour at a stronger alpha than world FX.</summary>
        public static Color CardFace(Element element) => For(element) * new Color(1f, 1f, 1f, 1.8f);

        /// <summary>A card face in the HUD: heal green for heal cards, otherwise <see cref="CardFace(Element)"/>.</summary>
        public static Color CardFace(CardData card) => card != null && card.IsHeal ? Heal : CardFace(card != null ? card.Element : Element.Physical);
    }
}
