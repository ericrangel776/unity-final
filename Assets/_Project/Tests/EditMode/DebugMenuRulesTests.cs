using System;
using System.IO;
using DeckArpg.Core;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>The debug menu's pure parts (docs/specs/debug-menu.md) and its release-build guard.</summary>
    public class DebugMenuRulesTests
    {
        // ---- room picker ----

        [Test]
        public void Picker_EveryKindAndStep_MakesAValidCard()
        {
            var picker = new DebugRoomPicker();
            foreach (MapCardKind unused in Enum.GetValues(typeof(MapCardKind)))
            {
                for (int step = 0; step < 4; step++)
                {
                    Assert.DoesNotThrow(() => { var _ = picker.Card; }, picker.KindLabel);
                    picker.StepExtra(1);
                }
                picker.StepKind(1);
            }
        }

        [Test]
        public void Picker_EliteAlwaysCarriesATrial_OthersNever()
        {
            var picker = new DebugRoomPicker();
            while (picker.Kind != MapCardKind.EliteTrial) picker.StepKind(1);
            Assert.AreNotEqual(EliteTrial.None, picker.Card.Trial);
            picker.StepExtra(1);
            Assert.AreEqual(EliteTrial.Counterpuncher, picker.Card.Trial);

            picker.StepKind(1);
            Assert.AreEqual(EliteTrial.None, picker.Card.Trial);
        }

        [Test]
        public void Picker_KindWrapsBothWays()
        {
            var picker = new DebugRoomPicker();
            var first = picker.Kind;
            picker.StepKind(-1);
            Assert.AreEqual(MapCardKind.Boss, picker.Kind, "the last kind");
            picker.StepKind(1);
            Assert.AreEqual(first, picker.Kind);
        }

        [Test]
        public void Picker_NumberClampsToOneToNine()
        {
            var picker = new DebugRoomPicker();
            for (int i = 0; i < 20; i++) picker.StepNumber(1);
            Assert.AreEqual(9, picker.Number);
            for (int i = 0; i < 20; i++) picker.StepNumber(-1);
            Assert.AreEqual(1, picker.Number);
        }

        [Test]
        public void Picker_KindledGroundSteppingChangesTheBuffElement()
        {
            var picker = new DebugRoomPicker();
            while (picker.Kind != MapCardKind.KindledGround) picker.StepKind(1);
            var before = picker.Card.BuffElement;
            picker.StepExtra(1);
            Assert.AreNotEqual(before, picker.Card.BuffElement);
        }

        // ---- run timer ----

        [Test]
        public void Timer_RecordsRoomTimes_AndKeepsTheLastSix()
        {
            var timer = new DebugRunTimer();
            timer.StartRun(0f);
            for (int i = 0; i < 8; i++)
            {
                timer.RoomStarted(i * 10f, $"room{i}");
                timer.RoomEnded(i * 10f + 2f + i);
            }
            Assert.AreEqual(DebugRunTimer.HistorySize, timer.History.Count);
            Assert.AreEqual("room7", timer.History[timer.History.Count - 1].room);
            Assert.AreEqual(9f, timer.History[timer.History.Count - 1].seconds, 1e-4f);
            Assert.AreEqual("room2", timer.History[0].room);
        }

        [Test]
        public void Timer_ElapsedAndRestart()
        {
            var timer = new DebugRunTimer();
            timer.StartRun(5f);
            Assert.AreEqual(0f, timer.RoomElapsed(8f), "no room open");
            timer.RoomStarted(6f, "a");
            Assert.AreEqual(2f, timer.RoomElapsed(8f), 1e-4f);
            Assert.AreEqual(3f, timer.RunElapsed(8f), 1e-4f);
            timer.RoomEnded(8f);
            timer.RoomEnded(9f); // no room open: ignored
            Assert.AreEqual(1, timer.History.Count);

            timer.StartRun(20f);
            Assert.AreEqual(0, timer.History.Count);
            Assert.IsFalse(timer.InRoom);
        }

        // ---- hitbox mode ----

        [Test]
        public void HitboxMode_CyclesDefaultOnOff()
        {
            Assert.AreEqual(true, DebugCommands.NextHitboxMode(null));
            Assert.AreEqual(false, DebugCommands.NextHitboxMode(true));
            Assert.AreEqual(null, DebugCommands.NextHitboxMode(false));
        }

        // ---- release guard ----

        [Test]
        public void EveryDebugMenuFile_IsCompiledOnlyInEditorAndDevBuilds()
        {
            string dir = Path.Combine(Application.dataPath, "_Project", "Scripts", "UI", "Debug");
            var files = Directory.GetFiles(dir, "*.cs");
            Assert.IsNotEmpty(files);
            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                Assert.AreEqual("#if UNITY_EDITOR || DEVELOPMENT_BUILD", lines[0].Trim(), Path.GetFileName(file));
                int last = lines.Length - 1;
                while (last > 0 && lines[last].Trim().Length == 0) last--;
                Assert.AreEqual("#endif", lines[last].Trim(), Path.GetFileName(file));
            }
        }
    }
}
