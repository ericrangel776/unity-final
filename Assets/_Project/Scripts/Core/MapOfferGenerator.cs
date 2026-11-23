using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>
    /// Deals the 3 Map Cards offered at a door (rooms 1-5). Seeded through the passed RNG.
    /// Room 1: Red + Green + one more Red/Green (3 distinct kinds). Rooms 2-5: 3 distinct families out of 4;
    /// room 4 forces Purple if none has been offered yet. Numbers roll one low, one mid and one high around
    /// a depth-based center; Blue numbers (Quality) get the Momentum bonus.
    /// </summary>
    public static class MapOfferGenerator
    {
        public const int OfferSize = 3;
        public const int FirstRoom = 1;
        public const int LastChosenRoom = 5;
        public const int ForcedPurpleRoom = 4;

        private static readonly MapCardKind[] RedKinds = { MapCardKind.Vanguard, MapCardKind.Hexfire };
        private static readonly MapCardKind[] GreenKinds = { MapCardKind.KindledGround, MapCardKind.BountyWind };
        private static readonly MapCardKind[] BlueKinds = { MapCardKind.Shop, MapCardKind.Forge, MapCardKind.Rest };
        /// <summary>Every Elite trial, in offer order (also the debug room picker's list).</summary>
        public static readonly IReadOnlyList<EliteTrial> Trials = new[] { EliteTrial.Veiled, EliteTrial.Counterpuncher, EliteTrial.Hexer };
        private static readonly Element[] Elements = { Element.Physical, Element.Fire, Element.Ice, Element.Lightning, Element.Wind };

        /// <summary>Center of the number band: rooms 1-5 give 2, 3, 4, 6, 7.</summary>
        public static int Center(int room)
        {
            RequireRoom(room);
            return 1 + 13 * room / 10;
        }

        public static List<MapCard> Deal(int room, bool purpleOfferedBefore, Momentum momentum, Random rng)
        {
            RequireRoom(room);
            if (momentum == null) throw new ArgumentNullException(nameof(momentum));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var kinds = PickKinds(room, purpleOfferedBefore, rng);
            var numbers = RollNumbers(room, rng);

            var offer = new List<MapCard>(OfferSize);
            for (int i = 0; i < OfferSize; i++)
            {
                var kind = kinds[i];
                int number = numbers[i];
                if (MapCard.FamilyOf(kind) == MapFamily.Blue) number = momentum.ApplyQualityBonus(number);

                var trial = kind == MapCardKind.EliteTrial ? Trials[rng.Next(Trials.Count)] : EliteTrial.None;
                var element = kind == MapCardKind.KindledGround ? Elements[rng.Next(Elements.Length)] : Element.Physical;
                offer.Add(new MapCard(kind, number, trial, element));
            }
            return offer;
        }

        private static List<MapCardKind> PickKinds(int room, bool purpleOfferedBefore, Random rng)
        {
            var kinds = new List<MapCardKind>(OfferSize);
            if (room == FirstRoom)
            {
                var red = RedKinds[rng.Next(RedKinds.Length)];
                var green = GreenKinds[rng.Next(GreenKinds.Length)];
                kinds.Add(red);
                kinds.Add(green);
                // Third card: the other kind of Red or Green, so all three differ.
                bool extraRed = rng.Next(2) == 0;
                kinds.Add(extraRed ? Other(RedKinds, red) : Other(GreenKinds, green));
            }
            else
            {
                var families = new List<MapFamily> { MapFamily.Red, MapFamily.Green, MapFamily.Blue, MapFamily.Purple };
                bool forcePurple = room == ForcedPurpleRoom && !purpleOfferedBefore;
                var droppable = forcePurple ? families.FindAll(f => f != MapFamily.Purple) : families;
                families.Remove(droppable[rng.Next(droppable.Count)]);

                foreach (var family in families) kinds.Add(RandomKind(family, rng));
            }
            ListShuffle.Shuffle(kinds, rng);
            return kinds;
        }

        private static int[] RollNumbers(int room, Random rng)
        {
            int c = Center(room);
            var numbers = new[]
            {
                CardValue.Clamp(c - 2 + rng.Next(2)), // low: c-2 or c-1
                CardValue.Clamp(c),                   // mid
                CardValue.Clamp(c + 1 + rng.Next(3))  // high: c+1..c+3
            };
            ListShuffle.Shuffle(numbers, rng);
            return numbers;
        }

        private static MapCardKind RandomKind(MapFamily family, Random rng)
        {
            switch (family)
            {
                case MapFamily.Red: return RedKinds[rng.Next(RedKinds.Length)];
                case MapFamily.Green: return GreenKinds[rng.Next(GreenKinds.Length)];
                case MapFamily.Blue: return BlueKinds[rng.Next(BlueKinds.Length)];
                default: return MapCardKind.EliteTrial;
            }
        }

        private static MapCardKind Other(MapCardKind[] pair, MapCardKind taken) => pair[0] == taken ? pair[1] : pair[0];

        private static void RequireRoom(int room)
        {
            if (room < FirstRoom || room > LastChosenRoom)
                throw new ArgumentOutOfRangeException(nameof(room), room, "Offers are dealt for rooms 1-5.");
        }
    }
}
