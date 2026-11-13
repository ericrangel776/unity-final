using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>A composed Sleight: what three stocked cards add up to once mirroring is applied.</summary>
    public readonly struct SleightResult
    {
        /// <summary>Sum of the three composed values (at least 3; may exceed 9).</summary>
        public readonly int Value;
        /// <summary>Slot 1's shape defines the attack.</summary>
        public readonly CardShape Shape;
        /// <summary>Slot 2's element defines the status effect.</summary>
        public readonly Element Element;
        /// <summary>Slot 3's composed value (1-9), which scales damage.</summary>
        public readonly int FinisherValue;
        /// <summary>All three composed elements match (finisher shockwave trigger).</summary>
        public readonly bool AllSameElement;

        public SleightResult(int value, CardShape shape, Element element, int finisherValue, bool allSameElement)
        {
            Value = value;
            Shape = shape;
            Element = element;
            FinisherValue = finisherValue;
            AllSameElement = allSameElement;
        }

        public float DamageMultiplier => 1f + FinisherValue * SleightRules.MultiplierPerPoint;
    }

    /// <summary>
    /// Pure rules for 3-card Sleights. A stocked 0-card "mirrors" the highest-value numbered card in
    /// the same Sleight (value, shape and element). This is flag logic, so 0 never enters the sum
    /// as an integer. With nothing to mirror, a 0 counts as <see cref="CardValue.Min"/>.
    /// </summary>
    public static class SleightRules
    {
        public const int StockSize = 3;
        /// <summary>Floor of 3: a Sleight only burns its slot-1 card while more than this many cards cycle.</summary>
        public const int BurnFloor = 3;
        public const float MultiplierPerPoint = 0.1f;
        /// <summary>A partial stock with no new card for this long falls into discard (no burn).</summary>
        public const float StockTimeoutSeconds = 5f;

        public static bool CanBurn(int cyclingCardCount) => cyclingCardCount > BurnFloor;

        /// <summary>
        /// Hits a Sleight of this shape can land on one body: Strike combo 3, Throw boomerang 2 (out + back),
        /// Arcana blast 1. Total damage is split across them.
        /// </summary>
        public static int HitCount(CardShape shape)
        {
            switch (shape)
            {
                case CardShape.Strike: return 3;
                case CardShape.Throw: return 2;
                default: return 1;
            }
        }

        public static SleightResult Compose(IReadOnlyList<RuntimeCardInstance> stocked)
        {
            Validate(stocked);
            var first = Resolve(stocked, 0);
            var second = Resolve(stocked, 1);
            var third = Resolve(stocked, 2);
            int v1 = ValueOf(stocked, 0), v2 = ValueOf(stocked, 1), v3 = ValueOf(stocked, 2);

            var e1 = first.Data.Element;
            var e2 = second.Data.Element;
            var e3 = third.Data.Element;
            return new SleightResult(v1 + v2 + v3, first.Data.Shape, e2, v3, e1 == e2 && e2 == e3);
        }

        /// <summary>The value slot <paramref name="slot"/> contributes after mirroring.</summary>
        public static int ComposedValue(IReadOnlyList<RuntimeCardInstance> stocked, int slot)
        {
            Validate(stocked);
            if (slot < 0 || slot >= StockSize) throw new ArgumentOutOfRangeException(nameof(slot));
            return ValueOf(stocked, slot);
        }

        private static int ValueOf(IReadOnlyList<RuntimeCardInstance> stocked, int slot)
        {
            var card = Resolve(stocked, slot);
            return card.IsZeroBreaker ? CardValue.Min : card.GetEffectiveValue();
        }

        /// <summary>The card whose stats a slot uses: itself, or the mirror source for a 0.</summary>
        private static RuntimeCardInstance Resolve(IReadOnlyList<RuntimeCardInstance> stocked, int slot)
        {
            var card = stocked[slot];
            if (!card.IsZeroBreaker) return card;
            return MirrorSource(stocked) ?? card;
        }

        /// <summary>Highest-value numbered card; ties go to the lowest slot. Null if all are 0s.</summary>
        private static RuntimeCardInstance MirrorSource(IReadOnlyList<RuntimeCardInstance> stocked)
        {
            RuntimeCardInstance best = null;
            for (int i = 0; i < StockSize; i++)
            {
                var c = stocked[i];
                if (c.IsZeroBreaker) continue;
                if (best == null || c.GetEffectiveValue() > best.GetEffectiveValue()) best = c;
            }
            return best;
        }

        private static void Validate(IReadOnlyList<RuntimeCardInstance> stocked)
        {
            if (stocked == null) throw new ArgumentNullException(nameof(stocked));
            if (stocked.Count != StockSize)
                throw new ArgumentException($"A Sleight needs exactly {StockSize} cards.", nameof(stocked));
            for (int i = 0; i < StockSize; i++)
                if (stocked[i] == null) throw new ArgumentException("Stocked cards cannot be null.", nameof(stocked));
        }
    }
}
