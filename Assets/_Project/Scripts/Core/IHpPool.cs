namespace DeckArpg.Core
{
    /// <summary>HP that a rule can spend (e.g. Overburn). Implemented in Gameplay so Core never references it.</summary>
    public interface IHpPool
    {
        int Hp { get; }
        int MaxHp { get; }
        void SpendHp(int amount);
    }
}
