using UnityEngine;

namespace DeckArpg.Core
{
    /// <summary>
    /// Read-only card template. Never mutate at runtime: changes made in Play Mode persist
    /// to disk in the Editor. Per-run state lives in <see cref="RuntimeCardInstance"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Card", menuName = "DeckArpg/Card Data")]
    public sealed class CardData : ScriptableObject
    {
        [SerializeField] private string cardId = "";
        [SerializeField] private string displayName = "";
        [Tooltip("A 0-card: breaks any card if played second. Stored as a flag, never as the integer 0.")]
        [SerializeField] private bool isZeroBreaker;
        [Range(CardValue.Min, CardValue.Max)]
        [SerializeField] private int baseValue = CardValue.Min;
        [SerializeField] private CardShape shape;
        [SerializeField] private Element element;
        [SerializeField] private bool isHeal;
        [SerializeField] private bool isPremium;

        public string CardId => cardId;
        public string DisplayName => displayName;
        public bool IsZeroBreaker => isZeroBreaker;
        public int BaseValue => baseValue;
        public CardShape Shape => shape;
        public Element Element => element;
        public bool IsHeal => isHeal;
        public bool IsPremium => isPremium;

        private void OnValidate()
        {
            baseValue = CardValue.Clamp(baseValue);
        }

        /// <summary>
        /// Builds an in-memory card. Public so the Editor content builder can author assets;
        /// gameplay code must never call this to "edit" an existing card.
        /// </summary>
        public static CardData Create(string id, int value, CardShape shape = CardShape.Strike,
            Element element = Element.Physical, bool zeroBreaker = false, bool heal = false,
            bool premium = false, string displayName = null)
        {
            var card = CreateInstance<CardData>();
            card.cardId = id;
            card.displayName = displayName ?? id;
            card.baseValue = CardValue.Clamp(value);
            card.shape = shape;
            card.element = element;
            card.isZeroBreaker = zeroBreaker;
            card.isHeal = heal;
            card.isPremium = premium;
            card.name = id;
            return card;
        }
    }
}
