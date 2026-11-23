namespace DeckArpg.Core
{
    /// <summary>
    /// Elite tuning: readable and punishable instead of a damage wall (docs/specs/elite-rework.md). Its cards sit below
    /// the mob curve so clashes are winnable, its hits land lighter, the player's break stun and posture damage shrink in
    /// an Elite room so breaks can't chain, and a heavy swing leaves it Overextended (takes extra damage).
    /// </summary>
    public static class EliteRules
    {
        /// <summary>Added to the room's enemy card bonus in an Elite room (values still clamp to [1, 9]).</summary>
        public const int CardValueOffset = -2;
        /// <summary>HP damage the Elite's attacks deal. Card values and clashes are unchanged.</summary>
        public const float DamageDealtMultiplier = 0.8f;
        /// <summary>Player's break stun in an Elite room (it is always alone, so crowd stun scaling never helps).</summary>
        public const float PlayerBreakStunScale = 0.7f;
        /// <summary>Player's posture damage taken in an Elite room: about three heavy breaks to posture-break, not two.</summary>
        public const float PlayerPostureTakenScale = 0.67f;

        /// <summary>A card with at least this effective value is a heavy swing.</summary>
        public const int OverextendMinValue = 6;
        /// <summary>A heavy card's wind-up is this much longer (absolute seconds, a readable tell).</summary>
        public const float HeavyTelegraphBonusSeconds = 0.15f;
        /// <summary>After a heavy swing the recover is this much longer.</summary>
        public const float OverextendSeconds = 0.6f;
        /// <summary>HP and posture damage taken while Overextended.</summary>
        public const float OverextendDamageMultiplier = 1.5f;

        /// <summary>True for a heavy swing: a non-zero card with effective value of at least <see cref="OverextendMinValue"/>.</summary>
        public static bool IsHeavy(int effectiveValue, bool isZeroBreaker) =>
            !isZeroBreaker && effectiveValue >= OverextendMinValue;

        /// <summary>True for a heavy swing (null = no card = not heavy).</summary>
        public static bool IsHeavy(RuntimeCardInstance card) =>
            card != null && IsHeavy(card.GetEffectiveValue(), card.IsZeroBreaker);
    }
}
