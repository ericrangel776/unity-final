using System.Collections.Generic;
using System.Linq;
using System.Text;
using DeckArpg.Core;

namespace DeckArpg.UI
{
    /// <summary>Pure text helpers for the run screens and HUD strip (no Unity objects, so EditMode-testable).</summary>
    public static class RunUiText
    {
        public const string PipOn = "●";
        public const string PipOff = "○";

        /// <summary>"Room 3/6", or "Room 6/6 · Boss" on the boss room.</summary>
        public static string Room(int roomNumber) =>
            $"Room {roomNumber}/{RunState.BossRoom}" + (roomNumber == RunState.BossRoom ? " · Boss" : "");

        /// <summary><paramref name="filled"/> of <paramref name="total"/> pips, e.g. "●●○". Clamped to [0, total].</summary>
        public static string Pips(int filled, int total)
        {
            if (total <= 0) return "";
            if (filled < 0) filled = 0;
            if (filled > total) filled = total;
            var sb = new StringBuilder(total);
            for (int i = 0; i < total; i++) sb.Append(i < filled ? PipOn : PipOff);
            return sb.ToString();
        }

        public static string Momentum(int stacks) => "Momentum " + Pips(stacks, Core.Momentum.MaxStacks);

        public static string RelicShortName(RelicId relic)
        {
            switch (relic)
            {
                case RelicId.ShatteredHourglass: return "Hourglass";
                case RelicId.AshenSigil: return "Ashen";
                case RelicId.IronWillIdol: return "Iron Will";
                case RelicId.TemperedCoil: return "Coil";
                case RelicId.CinderLantern: return "Lantern";
                case RelicId.GildedQuill: return "Quill";
                default: return relic.ToString();
            }
        }

        /// <summary>
        /// Relic short names in a fixed (enum) order, joined with " · ". Ashen shows its room stacks ("Ashen x2")
        /// when above 0. "No relics" when the set is empty or null.
        /// </summary>
        public static string Relics(IEnumerable<RelicId> relics, int ashenStacks)
        {
            var owned = relics?.OrderBy(r => (int)r).ToList();
            if (owned == null || owned.Count == 0) return "No relics";
            return string.Join(" · ", owned.Select(r =>
                r == RelicId.AshenSigil && ashenStacks > 0 ? $"{RelicShortName(r)} x{ashenStacks}" : RelicShortName(r)));
        }
    }
}
