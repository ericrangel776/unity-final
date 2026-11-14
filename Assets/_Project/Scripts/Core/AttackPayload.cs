using System;

namespace DeckArpg.Core
{
    /// <summary>
    /// Immutable snapshot attached to a hitbox/projectile at spawn time, so a slow projectile keeps
    /// its own value even after the owner plays another card.
    /// </summary>
    public readonly struct AttackPayload
    {
        public const int PokeValue = 1;

        public readonly int OwnerId;
        public readonly Team Team;
        /// <summary>Clamped to [1, 9] for single cards; Sleights sum 3 cards so may exceed 9. 0 for zero-breakers.</summary>
        public readonly int Value;
        public readonly bool IsZeroBreaker;
        public readonly bool IsSleight;
        public readonly int ActivationTick;
        public readonly CardShape Shape;
        public readonly Element Element;
        /// <summary>Sleight slot-3 value (0-9) that scales damage; 0 for anything that is not a Sleight.</summary>
        public readonly int FinisherValue;
        /// <summary>Hits a multi-hit Sleight can land per body; its damage is split across them. 1 for everything else.</summary>
        public readonly int HitCount;

        public float DamageMultiplier => 1f + FinisherValue * SleightRules.MultiplierPerPoint;

        public AttackPayload(int ownerId, Team team, int value, bool isZeroBreaker, bool isSleight,
            int activationTick, CardShape shape, Element element, int finisherValue = 0, int hitCount = 1)
        {
            FinisherValue = isSleight ? Math.Max(0, Math.Min(CardValue.Max, finisherValue)) : 0;
            HitCount = isSleight ? Math.Max(1, hitCount) : 1;
            OwnerId = ownerId;
            Team = team;
            IsZeroBreaker = isZeroBreaker;
            if (isZeroBreaker) Value = 0;
            else if (isSleight) Value = Math.Max(CardValue.Min, value);
            else Value = CardValue.Clamp(value);
            IsSleight = isSleight;
            ActivationTick = activationTick;
            Shape = shape;
            Element = element;
        }

        public static AttackPayload FromCard(RuntimeCardInstance card, int ownerId, Team team, int activationTick)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            return new AttackPayload(ownerId, team, card.GetEffectiveValue(), card.IsZeroBreaker, false,
                activationTick, card.Data.Shape, card.Data.Element);
        }

        /// <summary>Snapshot of a composed Sleight. Never a zero-breaker: stocked 0s mirror a numbered card.</summary>
        public static AttackPayload FromSleight(in SleightResult sleight, int ownerId, Team team, int activationTick,
            int hitCount = 1) =>
            new AttackPayload(ownerId, team, sleight.Value, false, true, activationTick, sleight.Shape,
                sleight.Element, sleight.FinisherValue, hitCount);

        /// <summary>The always-available off-deck Value 1 poke, so the player is never helpless.</summary>
        public static AttackPayload Poke(int ownerId, Team team, int activationTick) =>
            new AttackPayload(ownerId, team, PokeValue, false, false, activationTick, CardShape.Strike, Element.Physical);
    }
}
