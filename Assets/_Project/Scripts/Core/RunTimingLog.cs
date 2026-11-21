using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DeckArpg.Core
{
    /// <summary>How a timed room ended. <see cref="Utility"/> marks a Blue room (not timed).</summary>
    public enum RoomOutcome
    {
        Cleared,
        Loss,
        Timeout,
        Utility
    }

    /// <summary>How a measured run ended. <see cref="Incomplete"/> until <see cref="RunTimingLog.Finish"/>.</summary>
    public enum RunResult
    {
        Incomplete,
        Victory,
        Loss
    }

    /// <summary>One room of a measured run (docs/specs/run-timing.md). Seconds are real (unscaled) time.</summary>
    public readonly struct RoomTiming
    {
        public readonly int RoomNumber;
        public readonly MapCard Card;
        /// <summary>Enemy mix label such as "B2 R1" (built by the caller); empty for Blue rooms.</summary>
        public readonly string Mix;
        public readonly int Waves;
        public readonly float Seconds;
        public readonly int HpBefore;
        public readonly int HpAfter;
        public readonly int MaxHp;
        public readonly RoomOutcome Outcome;

        public RoomTiming(int roomNumber, MapCard card, string mix, int waves, float seconds,
                          int hpBefore, int hpAfter, int maxHp, RoomOutcome outcome)
        {
            RoomNumber = roomNumber;
            Card = card;
            Mix = mix ?? string.Empty;
            Waves = waves;
            Seconds = seconds;
            HpBefore = hpBefore;
            HpAfter = hpAfter;
            MaxHp = maxHp;
            Outcome = outcome;
        }

        /// <summary>HP lost in the room; a heal (HP went up) counts as 0.</summary>
        public int HpLost => Math.Max(0, HpBefore - HpAfter);

        public bool IsTimed => Outcome != RoomOutcome.Utility;
    }

    /// <summary>
    /// The per-room log of one measured run, plus report formatting (docs/specs/run-timing.md). Pure, so it is
    /// EditMode-testable; the PlayMode measurement fills it.
    /// </summary>
    public sealed class RunTimingLog
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly List<RoomTiming> rooms = new List<RoomTiming>();

        public RunTimingLog(int seed)
        {
            Seed = seed;
        }

        public int Seed { get; }
        public IReadOnlyList<RoomTiming> Rooms => rooms;
        public RunResult Result { get; private set; } = RunResult.Incomplete;

        public void Add(in RoomTiming room) => rooms.Add(room);

        public void Finish(RunResult result) => Result = result;

        /// <summary>Sum of the timed (non-Blue) rooms' seconds.</summary>
        public float CombatSeconds
        {
            get
            {
                float total = 0f;
                foreach (var r in rooms) if (r.IsTimed) total += r.Seconds;
                return total;
            }
        }

        public int HpLost
        {
            get
            {
                int total = 0;
                foreach (var r in rooms) total += r.HpLost;
                return total;
            }
        }

        /// <summary>A header line, then one line per room.</summary>
        public string FormatRun()
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(Inv, "seed {0}: {1}  combat {2:0.0}s  rooms {3}  HP lost {4}\n",
                Seed, ResultLabel(Result), CombatSeconds, rooms.Count, HpLost));
            foreach (var r in rooms) sb.Append("  ").Append(FormatRoom(r)).Append('\n');
            return sb.ToString();
        }

        /// <summary>e.g. "room 2  Vanguard I4  mix B2 R1  waves 1  31.4s  HP 88->71/100  Cleared".</summary>
        public static string FormatRoom(in RoomTiming r)
        {
            string label = r.Card.Family == MapFamily.Blue ? "Q" : "I";
            if (!r.IsTimed)
                return string.Format(Inv, "room {0}  {1} {2}{3}  HP {4}->{5}/{6}  Utility",
                    r.RoomNumber, r.Card.Kind, label, r.Card.Number, r.HpBefore, r.HpAfter, r.MaxHp);
            return string.Format(Inv, "room {0}  {1} {2}{3}  mix {4}  waves {5}  {6:0.0}s  HP {7}->{8}/{9}  {10}",
                r.RoomNumber, r.Card.Kind, label, r.Card.Number, r.Mix, r.Waves, r.Seconds,
                r.HpBefore, r.HpAfter, r.MaxHp, r.Outcome);
        }

        /// <summary>
        /// Win rate, mean/median run combat time, then per room kind: count, mean/median/max seconds, mean HP lost.
        /// Only timed rooms count toward the per-kind stats. "no runs" for an empty list.
        /// </summary>
        public static string FormatSummary(IReadOnlyList<RunTimingLog> runs)
        {
            if (runs == null || runs.Count == 0) return "no runs\n";
            var sb = new StringBuilder();
            int wins = runs.Count(r => r.Result == RunResult.Victory);
            int losses = runs.Count(r => r.Result == RunResult.Loss);
            var combat = runs.Select(r => r.CombatSeconds).ToList();
            int timeouts = runs.Sum(r => r.Rooms.Count(x => x.Outcome == RoomOutcome.Timeout));
            sb.Append(string.Format(Inv, "SUMMARY  runs {0}  wins {1}  losses {2}  win rate {3:0}%  timeouts {4}\n",
                runs.Count, wins, losses, 100f * wins / runs.Count, timeouts));
            sb.Append(string.Format(Inv, "run combat time  mean {0:0.0}s  median {1:0.0}s  max {2:0.0}s\n",
                combat.Average(), Median(combat), combat.Max()));

            foreach (MapCardKind kind in Enum.GetValues(typeof(MapCardKind)))
            {
                var timed = runs.SelectMany(r => r.Rooms).Where(x => x.IsTimed && x.Card.Kind == kind).ToList();
                if (timed.Count == 0) continue;
                var secs = timed.Select(x => x.Seconds).ToList();
                sb.Append(string.Format(Inv, "  {0,-13} n {1,2}  mean {2,5:0.0}s  median {3,5:0.0}s  max {4,5:0.0}s  HP lost {5:0.0}\n",
                    kind, timed.Count, secs.Average(), Median(secs), secs.Max(), timed.Average(x => x.HpLost)));
            }
            return sb.ToString();
        }

        /// <summary>Middle value; the mean of the two middle values for an even count. 0 for an empty list.</summary>
        public static float Median(IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0) return 0f;
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2f;
        }

        private static string ResultLabel(RunResult result) =>
            result == RunResult.Victory ? "WIN" : result == RunResult.Loss ? "LOSS" : "INCOMPLETE";
    }
}
