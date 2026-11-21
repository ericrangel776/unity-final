using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>The room/run timing log and report (docs/specs/run-timing.md).</summary>
    public class RunTimingLogTests
    {
        private static RoomTiming Fight(int room, MapCardKind kind, float seconds, int hpBefore = 100, int hpAfter = 100,
                                        RoomOutcome outcome = RoomOutcome.Cleared, int number = 4) =>
            new RoomTiming(room, new MapCard(kind, number), "B2 R1", 1, seconds, hpBefore, hpAfter, 100, outcome);

        private static RoomTiming Blue(int room, int hpBefore = 50, int hpAfter = 80) =>
            new RoomTiming(room, new MapCard(MapCardKind.Rest, 3), "", 0, 0f, hpBefore, hpAfter, 100, RoomOutcome.Utility);

        [Test]
        public void NewLog_IsIncomplete_AndEmpty()
        {
            var log = new RunTimingLog(7);
            Assert.AreEqual(7, log.Seed);
            Assert.AreEqual(RunResult.Incomplete, log.Result);
            Assert.AreEqual(0, log.Rooms.Count);
            Assert.AreEqual(0f, log.CombatSeconds);
            Assert.AreEqual(0, log.HpLost);
        }

        [Test]
        public void Add_KeepsRoomsInOrder()
        {
            var log = new RunTimingLog(1);
            log.Add(Fight(1, MapCardKind.Vanguard, 10f));
            log.Add(Fight(2, MapCardKind.Hexfire, 20f));
            Assert.AreEqual(2, log.Rooms.Count);
            Assert.AreEqual(MapCardKind.Vanguard, log.Rooms[0].Card.Kind);
            Assert.AreEqual(2, log.Rooms[1].RoomNumber);
        }

        [Test]
        public void CombatSeconds_SumsFights_AndSkipsBlueRooms()
        {
            var log = new RunTimingLog(1);
            log.Add(Fight(1, MapCardKind.Vanguard, 10f));
            log.Add(Blue(2));
            log.Add(Fight(3, MapCardKind.Hexfire, 25.5f));
            Assert.AreEqual(35.5f, log.CombatSeconds, 1e-4f);
        }

        [Test]
        public void HpLost_IsClampedAtZero_WhenARoomHeals()
        {
            Assert.AreEqual(0, Blue(2, hpBefore: 50, hpAfter: 80).HpLost);
            Assert.AreEqual(17, Fight(1, MapCardKind.Vanguard, 5f, 88, 71).HpLost);
        }

        [Test]
        public void HpLost_SumsRooms()
        {
            var log = new RunTimingLog(1);
            log.Add(Fight(1, MapCardKind.Vanguard, 5f, 100, 90));
            log.Add(Blue(2, 90, 100));
            log.Add(Fight(3, MapCardKind.Vanguard, 5f, 100, 75));
            Assert.AreEqual(35, log.HpLost);
        }

        [Test]
        public void Finish_SetsTheResult()
        {
            var log = new RunTimingLog(1);
            log.Finish(RunResult.Loss);
            Assert.AreEqual(RunResult.Loss, log.Result);
        }

        [Test]
        public void FormatRoom_MatchesTheSpecLine()
        {
            var line = RunTimingLog.FormatRoom(Fight(2, MapCardKind.Vanguard, 31.44f, 88, 71));
            Assert.AreEqual("room 2  Vanguard I4  mix B2 R1  waves 1  31.4s  HP 88->71/100  Cleared", line);
        }

        [Test]
        public void FormatRoom_Elite_NamesItsTrial()
        {
            var elite = new RoomTiming(4, new MapCard(MapCardKind.EliteTrial, 5, EliteTrial.Hexer), "B1", 1, 8f,
                82, 0, 100, RoomOutcome.Loss);
            StringAssert.StartsWith("room 4  EliteTrial I5 Hexer  mix B1", RunTimingLog.FormatRoom(elite));
        }

        [Test]
        public void FormatRoom_BlueRoom_HasNoTime()
        {
            var line = RunTimingLog.FormatRoom(Blue(3));
            Assert.AreEqual("room 3  Rest Q3  HP 50->80/100  Utility", line);
        }

        [Test]
        public void FormatRun_HasAHeaderAndOneLinePerRoom()
        {
            var log = new RunTimingLog(4);
            log.Add(Fight(1, MapCardKind.Vanguard, 10f, 100, 90));
            log.Add(Fight(2, MapCardKind.Boss, 60f, 90, 0, RoomOutcome.Loss, number: 9));
            log.Finish(RunResult.Loss);
            var lines = log.FormatRun().TrimEnd('\n').Split('\n');
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("seed 4: LOSS  combat 70.0s  rooms 2  HP lost 100", lines[0]);
            StringAssert.EndsWith("Loss", lines[2]);
        }

        [Test]
        public void FormatSummary_Empty_SaysNoRuns()
        {
            Assert.AreEqual("no runs\n", RunTimingLog.FormatSummary(new List<RunTimingLog>()));
            Assert.AreEqual("no runs\n", RunTimingLog.FormatSummary(null));
        }

        [Test]
        public void FormatSummary_CountsWinsTimeouts_AndListsOnlyVisitedKinds()
        {
            var a = new RunTimingLog(1);
            a.Add(Fight(1, MapCardKind.Vanguard, 10f));
            a.Add(Fight(2, MapCardKind.Vanguard, 30f, outcome: RoomOutcome.Timeout));
            a.Finish(RunResult.Victory);
            var b = new RunTimingLog(2);
            b.Add(Fight(1, MapCardKind.Vanguard, 20f));
            b.Add(Blue(2));
            b.Finish(RunResult.Loss);

            var text = RunTimingLog.FormatSummary(new[] { a, b });
            StringAssert.Contains("runs 2  wins 1  losses 1  win rate 50%  timeouts 1", text);
            StringAssert.Contains("Vanguard", text);
            StringAssert.Contains("n  3  mean  20.0s  median  20.0s  max  30.0s", text);
            StringAssert.DoesNotContain("Hexfire", text);
            StringAssert.DoesNotContain("Rest", text); // Blue rooms aren't timed
        }

        [Test]
        public void Median_OddCount_TakesTheMiddle()
        {
            Assert.AreEqual(5f, RunTimingLog.Median(new[] { 9f, 1f, 5f }));
        }

        [Test]
        public void Median_EvenCount_AveragesTheTwoMiddles()
        {
            Assert.AreEqual(4f, RunTimingLog.Median(new[] { 8f, 1f, 3f, 5f }));
        }

        [Test]
        public void Median_Empty_IsZero()
        {
            Assert.AreEqual(0f, RunTimingLog.Median(new float[0]));
        }

        [Test]
        public void Formatting_UsesInvariantCulture()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // comma decimals
                StringAssert.Contains("31.4s", RunTimingLog.FormatRoom(Fight(1, MapCardKind.Vanguard, 31.4f)));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
