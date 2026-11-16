using System;

namespace DeckArpg.Core
{
    /// <summary>Colour family of a Map Card: Red/Green/Purple are fights, Blue is a non-combat room.</summary>
    public enum MapFamily
    {
        Red,
        Green,
        Blue,
        Purple
    }

    public enum MapCardKind
    {
        Vanguard,      // Red: more Strike-heavy dummies
        Hexfire,       // Red: fewer dummies with stronger elemental cards
        KindledGround, // Green: +N to one element, enemies faster
        BountyWind,    // Green: more currency, enemies tankier
        Shop,          // Blue
        Forge,         // Blue
        Rest,          // Blue
        EliteTrial,    // Purple: one large dummy + a trial
        Boss           // room 6
    }

    /// <summary>The twist on a Purple Elite. None for every other card.</summary>
    public enum EliteTrial
    {
        None,
        Veiled,         // telegraphs hidden until just before the swing
        Counterpuncher, // answers your plays with 0-cards
        Hexer           // your broken cards burn for the room
    }

    /// <summary>
    /// One Map Card offered at a door. <see cref="Number"/> is the room's Intensity (combat) or
    /// Quality (Blue), always within [1, 9].
    /// </summary>
    public readonly struct MapCard : IEquatable<MapCard>
    {
        public readonly MapCardKind Kind;
        public readonly int Number;
        public readonly EliteTrial Trial;
        /// <summary>Element KindledGround buffs. Ignored for other kinds.</summary>
        public readonly Element BuffElement;

        public MapCard(MapCardKind kind, int number, EliteTrial trial = EliteTrial.None,
            Element buffElement = Element.Physical)
        {
            if (number < CardValue.Min || number > CardValue.Max)
                throw new ArgumentOutOfRangeException(nameof(number), number, "Map Card number must be in [1, 9].");
            if ((kind == MapCardKind.EliteTrial) != (trial != EliteTrial.None))
                throw new ArgumentException("Exactly the EliteTrial kind carries a trial.", nameof(trial));

            Kind = kind;
            Number = number;
            Trial = trial;
            BuffElement = kind == MapCardKind.KindledGround ? buffElement : Element.Physical;
        }

        public MapFamily Family => FamilyOf(Kind);
        public bool IsCombat => IsCombatKind(Kind);
        /// <summary>Elite and Boss fights: one big enemy, a relic-tier reward.</summary>
        public bool IsBigFight => Kind == MapCardKind.EliteTrial || Kind == MapCardKind.Boss;

        public static MapFamily FamilyOf(MapCardKind kind)
        {
            switch (kind)
            {
                case MapCardKind.Vanguard:
                case MapCardKind.Hexfire: return MapFamily.Red;
                case MapCardKind.KindledGround:
                case MapCardKind.BountyWind: return MapFamily.Green;
                case MapCardKind.Shop:
                case MapCardKind.Forge:
                case MapCardKind.Rest: return MapFamily.Blue;
                default: return MapFamily.Purple;
            }
        }

        public static bool IsCombatKind(MapCardKind kind) => FamilyOf(kind) != MapFamily.Blue;

        public bool Equals(MapCard other) =>
            Kind == other.Kind && Number == other.Number && Trial == other.Trial && BuffElement == other.BuffElement;

        public override bool Equals(object obj) => obj is MapCard other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ (Number * 31) ^ ((int)Trial << 8) ^ ((int)BuffElement << 12);
        public override string ToString() => $"{Kind} {Number}" + (Trial != EliteTrial.None ? $" ({Trial})" : "");
    }
}
