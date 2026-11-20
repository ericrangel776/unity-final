using System;

namespace DeckArpg.Core
{
    /// <summary>
    /// Run-wide streak that rewards picking hard rooms (0..3 stacks).
    /// Clearing a combat room at Intensity >= 6 adds a stack; at Intensity <= 3 it resets to 0;
    /// 4-5 and non-combat (Blue) rooms leave it alone. Stacks are never spent: they boost
    /// currency and every Blue card's Quality for as long as the streak holds.
    /// </summary>
    public sealed class Momentum
    {
        public const int MaxStacks = 3;
        /// <summary>Clearing a combat room at this Intensity or higher adds a stack.</summary>
        public const int BuildAtIntensity = 6;
        /// <summary>Clearing a combat room at this Intensity or lower resets the streak.</summary>
        public const int ResetAtIntensity = 3;
        public const int CurrencyPercentPerStack = 10;

        public int Stacks { get; private set; }

        /// <summary>Updates the streak after a combat room (Red/Green/Purple/boss) is cleared.</summary>
        public void RecordCombatClear(int intensity)
        {
            if (intensity < CardValue.Min || intensity > CardValue.Max)
                throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Intensity must be in [1, 9].");

            if (intensity >= BuildAtIntensity) Stacks = Math.Min(MaxStacks, Stacks + 1);
            else if (intensity <= ResetAtIntensity) Stacks = 0;
        }

        /// <summary>Still Waters' Meditate: +1 stack (capped).</summary>
        public void AddStack() => Stacks = Math.Min(MaxStacks, Stacks + 1);

        /// <summary>Currency after the +10%-per-stack bonus, rounded half up. Integer math.</summary>
        public int ApplyCurrencyBonus(int baseAmount)
        {
            if (baseAmount < 0) throw new ArgumentOutOfRangeException(nameof(baseAmount));
            return RewardRules.PercentHalfUp(baseAmount, 100 + Stacks * CurrencyPercentPerStack);
        }

        /// <summary>A Blue card's rolled Quality plus one per stack, clamped to [1, 9].</summary>
        public int ApplyQualityBonus(int rolledQuality) => CardValue.Clamp(rolledQuality + Stacks);

        /// <summary>New run.</summary>
        public void Reset() => Stacks = 0;
    }
}
