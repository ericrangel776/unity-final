using System;

namespace DeckArpg.Core
{
    /// <summary>Per-run wrapper around a read-only <see cref="CardData"/>. All mutable card state lives here.</summary>
    public sealed class RuntimeCardInstance
    {
        public CardData Data { get; }
        public int PermanentRunUpgradeMod { get; set; }
        public int TemporaryRoomMod { get; set; }
        public bool IsBurnedThisFight { get; set; }

        public RuntimeCardInstance(CardData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Data = data;
        }

        public bool IsZeroBreaker => Data.IsZeroBreaker;

        /// <summary>
        /// 0 for zero-breakers (mods never touch them); otherwise base + mods clamped to [1, 9].
        /// Callers must check <see cref="IsZeroBreaker"/> rather than comparing against 0.
        /// </summary>
        public int GetEffectiveValue()
        {
            if (Data.IsZeroBreaker) return 0;
            return CardValue.Clamp(Data.BaseValue + PermanentRunUpgradeMod + TemporaryRoomMod);
        }

        public override string ToString() => $"{Data.DisplayName} [{GetEffectiveValue()}]";
    }
}
