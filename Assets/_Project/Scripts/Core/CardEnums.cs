namespace DeckArpg.Core
{
    /// <summary>Delivery shape of an attack. For Sleights, Slot 1's shape defines the attack.</summary>
    public enum CardShape
    {
        Strike, // melee arc / dash-lunge
        Throw,  // projectile
        Arcana  // radial burst around the caster
    }

    /// <summary>Element/status tag. For Sleights, Slot 2's element defines the status effect.</summary>
    public enum Element
    {
        Physical,
        Fire,
        Ice,
        Lightning
    }

    /// <summary>Which side an attack belongs to. Attacks on the same team never clash.</summary>
    public enum Team
    {
        Player,
        Enemy
    }
}
