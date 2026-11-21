using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>Mob archetype. Brute is the original melee dummy; Elites and the boss are always Brutes.</summary>
    public enum EnemyVariant
    {
        Brute,
        Caster,
        Rusher
    }

    /// <summary>A variant's stats as percentages of a Brute in the same room (100 = unchanged).</summary>
    public readonly struct VariantStats
    {
        public readonly int MovePercent, HpPercent, TelegraphPercent, RecoverPercent, PosturePercent;

        public VariantStats(int move, int hp, int telegraph, int recover, int posture)
        {
            MovePercent = move;
            HpPercent = hp;
            TelegraphPercent = telegraph;
            RecoverPercent = recover;
            PosturePercent = posture;
        }
    }

    /// <summary>What a Caster does with its feet this frame.</summary>
    public enum CasterMove
    {
        Advance,
        Hold,
        Retreat
    }

    /// <summary>
    /// Enemy variants (docs/specs/enemy-variants.md): which variants a combat room spawns, its second wave, the variant
    /// stat table and the Caster's keep-distance rules. Pure, so it is EditMode-testable.
    /// </summary>
    public static class EnemyVariantRules
    {
        public const float CasterPreferredRange = 7f;
        public const float CasterRetreatRange = 4f;
        public const float CasterMaxCastRange = 9f;
        public const float CasterRetreatSpeedFactor = 0.8f;
        public const float CasterCorneredSeconds = 1.5f;
        /// <summary>Currency percent for a room with a second wave.</summary>
        public const int WaveCurrencyPercent = 125;
        /// <summary>Delay between the last wave-1 death and the wave-2 spawn (scaled time).</summary>
        public const float WaveSpawnDelaySeconds = 1.0f;

        public static VariantStats Stats(EnemyVariant v)
        {
            switch (v)
            {
                case EnemyVariant.Brute: return new VariantStats(100, 100, 100, 100, 100);
                case EnemyVariant.Rusher: return new VariantStats(180, 50, 75, 50, 60);
                case EnemyVariant.Caster: return new VariantStats(90, 70, 100, 100, 80);
                default: throw new ArgumentOutOfRangeException(nameof(v), v, "Unknown variant.");
            }
        }

        /// <summary>
        /// Wave composition for a combat room. Order: Brutes first, then variants. Elite/Boss -> all Brute.
        /// Throws for non-combat rooms or count &lt; 1. <paramref name="rng"/> is used only by Green rooms.
        /// </summary>
        public static IReadOnlyList<EnemyVariant> Composition(in MapCard room, int count, Random rng)
        {
            if (!room.IsCombat) throw new ArgumentException($"{room.Kind} is not a combat room.", nameof(room));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "A wave has at least 1 enemy.");

            int variants;
            EnemyVariant variant;
            switch (room.Kind)
            {
                case MapCardKind.Hexfire:
                    variant = EnemyVariant.Caster;
                    variants = (count + 1) / 2;
                    break;
                case MapCardKind.Vanguard:
                    variant = EnemyVariant.Rusher;
                    variants = count / 2;
                    break;
                case MapCardKind.KindledGround:
                case MapCardKind.BountyWind:
                    if (rng == null) throw new ArgumentNullException(nameof(rng), "Green rooms roll their variant.");
                    variant = rng.Next(2) == 0 ? EnemyVariant.Caster : EnemyVariant.Rusher;
                    variants = 1;
                    break;
                default: // Elite, Boss
                    variant = EnemyVariant.Brute;
                    variants = 0;
                    break;
            }

            var mix = new EnemyVariant[count];
            for (int i = 0; i < count; i++)
                mix[i] = i < count - variants ? EnemyVariant.Brute : variant;
            return mix;
        }

        /// <summary>0 for Low band, Elite and Boss; 1 for Mid; 2 for High.</summary>
        public static int WaveTwoCount(in MapCard room)
        {
            if (!room.IsCombat) throw new ArgumentException($"{room.Kind} is not a combat room.", nameof(room));
            if (room.IsBigFight) return 0;
            return RoomIntensity.BandStep(room.Number, 0, 1, 2);
        }

        public static CasterMove CasterMovement(float distance)
        {
            if (distance > CasterPreferredRange) return CasterMove.Advance;
            if (distance < CasterRetreatRange) return CasterMove.Retreat;
            return CasterMove.Hold;
        }

        /// <summary>In cast range [4, 9], or cornered (too close for <see cref="CasterCorneredSeconds"/>).</summary>
        public static bool CasterMayCast(float distance, float closeSeconds) =>
            (distance >= CasterRetreatRange && distance <= CasterMaxCastRange)
            || closeSeconds >= CasterCorneredSeconds;
    }
}
