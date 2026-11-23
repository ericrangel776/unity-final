using System;
using System.Collections;
using UnityEngine;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Coroutine waits on real time, so they keep running while <see cref="Time.timeScale"/> is 0 (hit-stop, Deadlock).
    /// <see cref="UntilRealtime"/> gives up silently at the timeout; the test's next assertion reports what didn't happen.
    /// </summary>
    internal static class TestWait
    {
        public static IEnumerator Realtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        public static IEnumerator UntilRealtime(Func<bool> done, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
