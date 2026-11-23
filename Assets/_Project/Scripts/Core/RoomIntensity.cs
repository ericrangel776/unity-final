using System;

namespace DeckArpg.Core
{
    /// <summary>Thirds of the 1-9 scale: 1-3 Low, 4-6 Mid, 7-9 High.</summary>
    public enum IntensityBand
    {
        Low,
        Mid,
        High
    }

    /// <summary>
    /// How a combat Map Card's Intensity shapes its room. Percentages are integers (100 = unchanged)
    /// so the rules stay exact; Gameplay converts them when applying.
    /// </summary>
    public static class RoomIntensity
    {
        public static IntensityBand Band(int number)
        {
            if (number < CardValue.Min || number > CardValue.Max)
                throw new ArgumentOutOfRangeException(nameof(number), number, "Must be in [1, 9].");
            if (number <= 3) return IntensityBand.Low;
            if (number <= 6) return IntensityBand.Mid;
            return IntensityBand.High;
        }

        /// <summary>Base count by kind plus <c>(I - 1) / 3</c>. Elite/Boss stay at one enemy.</summary>
        public static int EnemyCount(in MapCard room)
        {
            RequireCombat(room);
            if (room.IsBigFight) return 1;
            int baseCount = room.Kind == MapCardKind.Vanguard ? 3 : 2;
            return baseCount + (room.Number - 1) / 3;
        }

        /// <summary><c>80 + 10 * I</c>, plus 15/25/35 on BountyWind.</summary>
        public static int EnemyHpPercent(in MapCard room)
        {
            RequireCombat(room);
            int percent = 80 + 10 * room.Number;
            if (room.Kind == MapCardKind.BountyWind) percent += BandStep(room.Number, 15, 25, 35);
            return percent;
        }

        public const int EliteHpPercent = 250;
        /// <summary>
        /// One enemy's max HP: <paramref name="baseHp"/> x <see cref="EnemyHpPercent"/> x size class
        /// (Elite 250%), rounded half up, at least 1. The boss's HP doesn't come from here: it's relative to the player's
        /// (<see cref="BossRules.MaxHp"/>).
        /// </summary>
        public static int EnemyMaxHp(int baseHp, in MapCard room)
        {
            if (baseHp < 1) throw new ArgumentOutOfRangeException(nameof(baseHp));
            int hp = RewardRules.PercentHalfUp(baseHp, EnemyHpPercent(room));
            if (room.Kind == MapCardKind.EliteTrial) hp = RewardRules.PercentHalfUp(hp, EliteHpPercent);
            return Math.Max(1, hp);
        }

        /// <summary>Max posture by size class: mob 100%, Elite 150%, Boss 200%.</summary>
        public static int EnemyPosturePercent(in MapCard room)
        {
            RequireCombat(room);
            if (room.Kind == MapCardKind.EliteTrial) return 150;
            if (room.Kind == MapCardKind.Boss) return 200;
            return 100;
        }

        /// <summary>Move speed and wind-up speed: <c>90 + 5 * I</c>, plus 10/20/30 on KindledGround.</summary>
        public static int EnemyTempoPercent(in MapCard room)
        {
            RequireCombat(room);
            int percent = 90 + 5 * room.Number;
            if (room.Kind == MapCardKind.KindledGround) percent += BandStep(room.Number, 10, 20, 30);
            return percent;
        }

        /// <summary>
        /// Added to every enemy card as a room mod (so still clamped): <c>(I - 1) / 4</c>, +1 on Hexfire,
        /// <see cref="EliteRules.CardValueOffset"/> on an Elite. The boss gets 0: its deck is tuned directly so 9s are
        /// regular but spaced (M4).
        /// </summary>
        public static int EnemyCardValueBonus(in MapCard room)
        {
            RequireCombat(room);
            if (room.Kind == MapCardKind.Boss) return 0;
            int bonus = (room.Number - 1) / 4;
            if (room.Kind == MapCardKind.Hexfire) bonus += 1;
            if (room.Kind == MapCardKind.EliteTrial) bonus += EliteRules.CardValueOffset;
            return bonus;
        }

        /// <summary>KindledGround: +1/+2/+3 to the player's cards of its element. 0 for other rooms.</summary>
        public static int PlayerElementBuff(in MapCard room) =>
            room.Kind == MapCardKind.KindledGround ? BandStep(room.Number, 1, 2, 3) : 0;

        /// <summary>BountyWind currency percent: 125/150/175. 100 for other rooms.</summary>
        public static int BountyCurrencyPercent(in MapCard room) =>
            room.Kind == MapCardKind.BountyWind ? BandStep(room.Number, 125, 150, 175) : 100;

        /// <summary>Veiled: the value shows this long before the swing (0.40 s at I1 down to 0.15 s at I9).</summary>
        public static float VeiledRevealSeconds(int intensity)
        {
            Band(intensity);
            return 0.40f - (intensity - 1) * 0.03125f;
        }

        /// <summary>Counterpuncher: number of 0-cards in its deck (1/2/3).</summary>
        public static int CounterpuncherZeros(int intensity) => BandStep(intensity, 1, 2, 3);

        /// <summary>Hexer: HP lost per Card Break you suffer (2 at I >= 7).</summary>
        public static int HexerHpPerBreak(int intensity) => BandStep(intensity, 0, 0, 2);

        /// <summary>Low, mid or high by the band of the number (1-9).</summary>
        internal static int BandStep(int number, int low, int mid, int high)
        {
            switch (Band(number))
            {
                case IntensityBand.Low: return low;
                case IntensityBand.Mid: return mid;
                default: return high;
            }
        }

        private static void RequireCombat(in MapCard room)
        {
            if (!room.IsCombat) throw new ArgumentException($"{room.Kind} is not a combat room.", nameof(room));
        }
    }
}
