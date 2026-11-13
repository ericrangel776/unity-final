namespace DeckArpg.Core
{
    /// <summary>
    /// Tracks a hit-stop freeze on a caller-supplied clock (use unscaled/realtime). Overlapping
    /// requests extend the freeze to the later end time instead of stacking, so a burst of clashes
    /// never freezes the game for the sum of their durations.
    /// </summary>
    public sealed class HitStopTimer
    {
        private float endTime = float.NegativeInfinity;

        public void Request(float seconds, float now)
        {
            if (seconds <= 0f) return;
            float end = now + seconds;
            if (end > endTime) endTime = end;
        }

        public bool IsFrozen(float now) => now < endTime;

        public float Remaining(float now) => now < endTime ? endTime - now : 0f;

        public void Clear() => endTime = float.NegativeInfinity;
    }
}
