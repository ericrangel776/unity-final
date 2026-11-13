using System;

namespace DeckArpg.Core
{
    public enum DeadlockGrade
    {
        Miss,
        Good,
        Perfect
    }

    /// <summary>What the player pressed during a Deadlock: Poke (timing only) or a hand slot (commit that card).</summary>
    public enum DeadlockChoice
    {
        Poke,
        Slot1,
        Slot2,
        Slot3
    }

    /// <summary>
    /// Tie-break rules for equal-value clashes. Each side pushes with a timing grade bonus plus the
    /// value of a hand card it chooses to commit (spent like a normal play); higher push wins.
    /// Pure and deterministic: callers supply times and the random source.
    /// </summary>
    public static class DeadlockRules
    {
        /// <summary>Real-time length of the Deadlock.</summary>
        public const float WindowSeconds = 1.0f;
        /// <summary>When (seconds after start) the shrinking ring meets the target.</summary>
        public const float TargetTime = 0.75f;
        public const float PerfectTolerance = 0.06f;
        public const float GoodTolerance = 0.15f;
        public const int PerfectBonus = 3;
        public const int GoodBonus = 1;
        /// <summary>Chance the enemy commits its first hand card.</summary>
        public const double EnemyCommitChance = 0.4;

        /// <summary>Grade for a press <paramref name="pressOffset"/> seconds after the Deadlock started.</summary>
        public static DeadlockGrade Grade(float pressOffset)
        {
            if (float.IsNaN(pressOffset) || pressOffset < 0f || pressOffset > WindowSeconds) return DeadlockGrade.Miss;
            float error = Math.Abs(pressOffset - TargetTime);
            if (error <= PerfectTolerance) return DeadlockGrade.Perfect;
            if (error <= GoodTolerance) return DeadlockGrade.Good;
            return DeadlockGrade.Miss;
        }

        public static int Bonus(DeadlockGrade grade) =>
            grade == DeadlockGrade.Perfect ? PerfectBonus : grade == DeadlockGrade.Good ? GoodBonus : 0;

        /// <summary>
        /// Push strength: grade bonus plus the committed card's value (0 = no card). Card values are
        /// clamped to [1, 9] like everywhere else.
        /// </summary>
        public static int Power(DeadlockGrade grade, int committedValue) =>
            Bonus(grade) + (committedValue > 0 ? CardValue.Clamp(committedValue) : 0);

        /// <summary>Player is A, enemy is B. Higher power wins; equal power stays a neutral cancel.</summary>
        public static ClashOutcome Resolve(int playerPower, int enemyPower)
        {
            if (playerPower > enemyPower) return ClashOutcome.AWins;
            if (enemyPower > playerPower) return ClashOutcome.BWins;
            return ClashOutcome.Neutral;
        }

        /// <summary>Enemy's timing (Miss 30% / Good 50% / Perfect 20%) and whether it commits a card.</summary>
        public static (DeadlockGrade grade, bool commit) RollEnemy(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            double g = random.NextDouble();
            var grade = g < 0.3 ? DeadlockGrade.Miss : g < 0.8 ? DeadlockGrade.Good : DeadlockGrade.Perfect;
            bool commit = random.NextDouble() < EnemyCommitChance;
            return (grade, commit);
        }

        /// <summary>Hand slot index for a choice, or -1 for Poke.</summary>
        public static int SlotIndex(DeadlockChoice choice) =>
            choice == DeadlockChoice.Poke ? -1 : (int)choice - (int)DeadlockChoice.Slot1;
    }
}
