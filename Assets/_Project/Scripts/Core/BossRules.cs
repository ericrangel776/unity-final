using System;

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
        /// <summary>Applied to both HP and posture damage while Exhausted.</summary>
        public const float ExhaustedDamageMultiplier = 2f;
        public const float SleightWindupSeconds = 1.5f;
        /// <summary>After a boss Sleight activates, a player 0 or Sleight clashes it at any range for this long.</summary>
        public const float GlobalZeroWindowSeconds = 0.5f;
        /// <summary>Every Nth boss attack is a Sleight (pattern policy).</summary>
        public const int SleightEvery = 3;

        public static BossCycleEnd CycleEnd(int deletedThisCycle) =>
            deletedThisCycle >= ExhaustDeleteThreshold ? BossCycleEnd.Exhausted : BossCycleEnd.ReloadWindow;

        /// <summary>Damage capped so the target keeps at least 1 HP (Big Deadlock loss). Never negative.</summary>
        public static int NonLethalDamage(int damage, int currentHp) =>
            Math.Max(0, Math.Min(damage, currentHp - 1));
    }
}
