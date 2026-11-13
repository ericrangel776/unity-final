namespace DeckArpg.Core
{
    /// <summary>Tunable combat numbers that are pure functions of a payload.</summary>
    public static class CombatMath
    {
        public const int DamagePerPoint = 3;

        /// <summary>HP damage an attack deals on a body hit. 0-cards are parries and deal no HP damage.</summary>
        public static int Damage(in AttackPayload payload) =>
            payload.IsZeroBreaker ? 0 : payload.Value * DamagePerPoint;
    }
}
