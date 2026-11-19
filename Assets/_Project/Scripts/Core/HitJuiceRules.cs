namespace DeckArpg.Core
{
    /// <summary>
    /// Pure numbers for the feedback on plain (non-clash) damaging body hits: how long the hit freezes the
    /// game and how big its damage number is (docs/specs/hit-juice.md). Every freeze stays under the
    /// input buffer window, like the clash freezes in <see cref="CombatMath.HitStopSeconds"/>.
    /// </summary>
    public static class HitJuiceRules
    {
        /// <summary>Freeze for a value-1 card hit.</summary>
        public const float MinCardHitStop = 0.03f;
        /// <summary>Freeze for a value-9 card hit and for every Sleight hit.</summary>
        public const float MaxCardHitStop = 0.05f;
        /// <summary>Flat freeze when the player takes a hit, whatever its value.</summary>
        public const float PlayerHurtHitStop = 0.04f;

        public const float DamageNumberMinScale = 1f;
        public const float DamageNumberMaxScale = 2f;
        /// <summary>Damage at which a damage number reaches its maximum scale.</summary>
        public const int DamageNumberFullScaleDamage = 60;

        /// <summary>
        /// Freeze for a damaging body hit. 0 for pokes, zero-breakers and hits that dealt no damage. A hit on the
        /// player freezes <see cref="PlayerHurtHitStop"/>, a Sleight freezes <see cref="MaxCardHitStop"/>, and a card
        /// scales linearly from <see cref="MinCardHitStop"/> at value 1 to <see cref="MaxCardHitStop"/> at value 9.
        /// </summary>
        public static float BodyHitStopSeconds(in AttackPayload payload, Team victimTeam, int damage)
        {
            if (damage <= 0 || payload.IsZeroBreaker || payload.IsPoke) return 0f;
            if (victimTeam == Team.Player) return PlayerHurtHitStop;
            if (payload.IsSleight) return MaxCardHitStop;
            float t = (CardValue.Clamp(payload.Value) - CardValue.Min) / (float)(CardValue.Max - CardValue.Min);
            return MinCardHitStop + (MaxCardHitStop - MinCardHitStop) * t;
        }

        /// <summary>Damage-number scale: 1 + damage / 60, clamped to [1, 2]. 1 for non-positive damage.</summary>
        public static float DamageNumberScale(int damage)
        {
            if (damage <= 0) return DamageNumberMinScale;
            float t = damage / (float)DamageNumberFullScaleDamage;
            if (t > 1f) t = 1f;
            return DamageNumberMinScale + (DamageNumberMaxScale - DamageNumberMinScale) * t;
        }
    }
}
