#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;

namespace DeckArpg.UI
{
    /// <summary>
    /// Room and run timing for the debug menu's Info tab, in real seconds. A manual first step toward the planned
    /// room/run timing measurement. Pure (time is passed in) so it is EditMode-testable.
    /// </summary>
    public sealed class DebugRunTimer
    {
        public const int HistorySize = 6;

        private readonly List<(string room, float seconds)> history = new List<(string, float)>();
        private float runStart;
        private float roomStart = -1f;
        private string roomLabel;

        /// <summary>Most recent last.</summary>
        public IReadOnlyList<(string room, float seconds)> History => history;
        public bool InRoom => roomStart >= 0f;

        public void StartRun(float now)
        {
            runStart = now;
            roomStart = -1f;
            roomLabel = null;
            history.Clear();
        }

        public void RoomStarted(float now, string label)
        {
            roomStart = now;
            roomLabel = label;
        }

        /// <summary>Records the open room's time. Ignored if no room is open.</summary>
        public void RoomEnded(float now)
        {
            if (!InRoom) return;
            history.Add((roomLabel, now - roomStart));
            if (history.Count > HistorySize) history.RemoveAt(0);
            roomStart = -1f;
        }

        public float RoomElapsed(float now) => InRoom ? now - roomStart : 0f;
        public float RunElapsed(float now) => now - runStart;
    }
}
#endif
