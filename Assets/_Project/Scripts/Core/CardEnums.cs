namespace DeckArpg.Core
{
    /// <summary>Delivery shape of an attack. For Sleights, Slot 1's shape defines the attack.</summary>
    public enum CardShape
    {
        Strike, // melee arc / dash-lunge
        Throw,  // projectile
        Arcana  // radial burst around the caster
    }

    /// <summary>Element/status tag. Decides delivery (slot 1 of a Sleight) and status (slot 2 of a Sleight).</summary>
    public enum Element
    {
        Physical,
        Fire,
        Ice,
        Lightning,
        Wind // appended last so serialized ints of existing assets don't shift
    }

    /// <summary>
    /// How an attack travels. Derived from element (and shape for Physical) by
    /// <see cref="ElementRules.DeliveryFor"/>; never authored.
    /// </summary>
    public enum Delivery
    {
        Strike,     // Physical Strike: melee box / lunge combo
        Projectile, // Physical Throw or Fire: travelling projectile (fireball)
        Burst,      // Physical Arcana: radial burst around the caster
        FrostBlast, // Ice: short blast in front
        ChainArc,   // Lightning: fast mid-range arc that chains
        Gust        // Wind: forward cone that knocks back
    }

    /// <summary>Draft/shop rarity. Order matters: rarity shifts move toward Rare.</summary>
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare
    }

    /// <summary>Which side an attack belongs to. Attacks on the same team never clash.</summary>
    public enum Team
    {
        Player,
        Enemy
    }
}
