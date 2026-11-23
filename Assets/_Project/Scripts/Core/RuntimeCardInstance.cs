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
        /// <summary>Set when the Forge makes this copy Premium. The asset is never touched.</summary>
        public bool IsPremiumOverride { get; set; }

        /// <summary>Premium cards hit harder (+<see cref="PremiumBonus"/>) but burn whenever they're played.</summary>
        public const int PremiumBonus = 2;

        public RuntimeCardInstance(CardData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Data = data;
        }

        public bool IsZeroBreaker => Data.IsZeroBreaker;
        public bool IsHeal => Data.IsHeal;
        public bool IsPremium => Data.IsPremium || IsPremiumOverride;

        /// <summary>Base + permanent run upgrades + Premium, unclamped (no room buffs). The Forge caps upgrades on this.</summary>
        public int PermanentValue => Data.BaseValue + PermanentRunUpgradeMod + (IsPremium ? PremiumBonus : 0);

        /// <summary>
        /// 0 for zero-breakers (mods never touch them); otherwise base + mods (+2 if Premium) clamped to [1, 9].
        /// Callers must check <see cref="IsZeroBreaker"/> rather than comparing against 0.
        /// </summary>
        public int GetEffectiveValue()
        {
            if (Data.IsZeroBreaker) return 0;
            return CardValue.Clamp(PermanentValue + TemporaryRoomMod);
        }

        public override string ToString() => $"{Data.DisplayName} [{GetEffectiveValue()}]";
    }
}
