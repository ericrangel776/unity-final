namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Lets a component take over parts of a <see cref="TrainingDummyEnemy"/>'s turn (the boss). Mobs and Elites
    /// have no hook and behave as before.
    /// </summary>
    public interface IEnemyTurnHook
    {
        /// <summary>While true the dummy neither approaches nor attacks (wind-up, reload window, Exhausted).</summary>
        bool IsBusy { get; }

        /// <summary>The dummy is about to telegraph. Return true to take the turn yourself (e.g. a Sleight).</summary>
        bool TryTakeTurn();

        /// <summary>The hand is empty. Replaces the dummy's instant reload.</summary>
        void OnOutOfCards();

        /// <summary>A player attack reached the body while <see cref="IsBusy"/>. Return true if it was handled as a clash.</summary>
        bool ResolveBusyClash(AttackHitbox incoming);
    }
}
