using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>How a boss deck cycle ends (docs/specs/m4-overview.md).</summary>
    public enum BossCycleEnd
    {
        /// <summary>Short punishable reload: no flinch armor, extra posture damage.</summary>
        ReloadWindow,
        /// <summary>Enough cards were 0-deleted this cycle: a long stun with double damage.</summary>
        Exhausted
    }

    /// <summary>Boss tuning and pure decisions. The boss plays by player rules; these are its extra numbers.</summary>
    public static class BossRules
    {
        public const float ReloadWindowSeconds = 1.5f;
        public const float ExhaustedSeconds = 4f;
        /// <summary>0-deleted cards in one cycle that turn the reload into Exhausted.</summary>
        public const int ExhaustDeleteThreshold = 4;
        public const float ReloadPostureMultiplier = 1.5f;
        /// <summary>
        /// HP damage the boss takes outside its punish windows (playtest-bot finding: card spam killed it in ~20 s, before
        /// its first reload). The reload window takes full damage and Exhausted double, so the windows are where it dies.
        /// </summary>
        public const float GuardDamageMultiplier = 0.35f;
        public const float ReloadWindowDamageMultiplier = 1f;
        /// <summary>
        /// Damage the boss's attacks deal (bot measurement: at full damage a player lasts ~40 s, too short for a 90-150 s
        /// fight). Card values and clashes are unchanged; only the HP damage that lands.
        /// </summary>
        public const float DamageDealtMultiplier = 0.5f;
        /// <summary>Applied to both HP and posture damage while Exhausted.</summary>
        public const float ExhaustedDamageMultiplier = 2f;
        public const float SleightWindupSeconds = 1.5f;
        /// <summary>After a boss Sleight activates, a player 0 or Sleight clashes it at any range for this long.</summary>
        public const float GlobalZeroWindowSeconds = 0.5f;
        /// <summary>Every Nth boss attack is a Sleight (pattern policy).</summary>
        public const int SleightEvery = 3;

        /// <summary>
        /// Boss max HP as a multiple of the player's max HP, per floor (row 0 = floor 1). Floor 1 is a forgiving skill check
        /// (user playtest, 2026-11-18: "a similar amount as the player but a bit more"); later floors add rows to ramp up.
        /// Floors past the table use the last row.
        /// </summary>
        public static readonly IReadOnlyList<float> HpRatioByFloor = new[] { 1.5f };

        /// <summary>The boss's max HP on <paramref name="floor"/> (1-based) against a player with <paramref name="playerMaxHp"/>.</summary>
        public static int MaxHp(int floor, int playerMaxHp)
        {
            if (floor < 1) throw new ArgumentOutOfRangeException(nameof(floor));
            if (playerMaxHp < 1) throw new ArgumentOutOfRangeException(nameof(playerMaxHp));
            float ratio = HpRatioByFloor[Math.Min(floor, HpRatioByFloor.Count) - 1];
            return Math.Max(1, (int)Math.Ceiling(playerMaxHp * ratio));
        }

        public static BossCycleEnd CycleEnd(int deletedThisCycle) =>
            deletedThisCycle >= ExhaustDeleteThreshold ? BossCycleEnd.Exhausted : BossCycleEnd.ReloadWindow;

        /// <summary>Damage capped so the target keeps at least 1 HP (Big Deadlock loss). Never negative.</summary>
        public static int NonLethalDamage(int damage, int currentHp) =>
            Math.Max(0, Math.Min(damage, currentHp - 1));
    }
}
