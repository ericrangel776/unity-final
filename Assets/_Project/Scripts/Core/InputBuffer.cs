using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>
    /// Short-lived FIFO input buffer so presses made during hit-stop (timeScale 0) or an action's
    /// recovery are not dropped. Timestamps are supplied by the caller (use unscaled/realtime clocks).
    /// </summary>
    public sealed class InputBuffer<T>
    {
        public const float DefaultWindowSeconds = 0.2f;
        public const int DefaultCapacity = 8;

        private readonly Queue<(T action, float time)> entries = new Queue<(T, float)>();

        public float WindowSeconds { get; }
        public int Capacity { get; }
        public int Count => entries.Count;

        public InputBuffer(float windowSeconds = DefaultWindowSeconds, int capacity = DefaultCapacity)
        {
            WindowSeconds = windowSeconds;
            Capacity = capacity < 1 ? 1 : capacity;
        }

        public void Enqueue(T action, float time)
        {
            if (entries.Count >= Capacity) entries.Dequeue(); // drop the oldest when full
            entries.Enqueue((action, time));
        }

        /// <summary>Returns the oldest press that is still inside the window; expired presses are discarded.</summary>
        public bool TryDequeue(float now, out T action)
        {
            while (entries.Count > 0)
            {
                var (a, t) = entries.Dequeue();
                if (now - t <= WindowSeconds)
                {
                    action = a;
                    return true;
                }
            }
            action = default;
            return false;
        }

        /// <summary>Returns the oldest live press without consuming it (expired presses are still discarded).</summary>
        public bool TryPeek(float now, out T action)
        {
            while (entries.Count > 0)
            {
                var (a, t) = entries.Peek();
                if (now - t <= WindowSeconds)
                {
                    action = a;
                    return true;
                }
                entries.Dequeue();
            }
            action = default;
            return false;
        }

        public void Clear() => entries.Clear();
    }
}
