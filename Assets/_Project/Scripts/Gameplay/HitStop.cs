using System.Collections;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Freezes the game (<c>Time.timeScale = 0</c>) for a moment on every clash, longest for a Zero
    /// Break. Waits on <see cref="WaitForSecondsRealtime"/> so the freeze can end while time is
    /// stopped. Inputs survive the freeze because <see cref="PlayerCombatInput"/> buffers them on
    /// unscaled time and holds them until the time scale returns.
    /// </summary>
    /// <remarks>
    /// <c>Time.timeScale</c> is global, so the freeze state is too: every instance shares one timer
    /// and one saved scale, and only the instance that started a freeze ends it. Otherwise a second
    /// instance would save the already-frozen 0 as the "previous" scale and lock the game forever.
    /// </remarks>
    public sealed class HitStop : MonoBehaviour
    {
        private static readonly HitStopTimer Timer = new HitStopTimer();
        private static HitStop owner;
        private static float savedTimeScale = 1f;
        private Coroutine routine;

        /// <summary>True while any HitStop has the game frozen.</summary>
        public bool IsFrozen => owner != null;

        // Static state survives play sessions when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Timer.Clear();
            owner = null;
            savedTimeScale = 1f;
        }

        private void OnEnable() => ClashSystem.ClashOccurred += HandleClash;

        private void OnDisable()
        {
            ClashSystem.ClashOccurred -= HandleClash;
            // Never leave the game frozen if the owner is disabled or the scene unloads mid-freeze.
            if (owner == this)
            {
                if (routine != null) StopCoroutine(routine);
                EndFreeze();
            }
        }

        private void HandleClash(ClashEvent e) =>
            Freeze(CombatMath.HitStopSeconds(e.Result.Outcome, e.Result.BrokeByZero));

        /// <summary>Freezes for <paramref name="seconds"/> of real time; overlapping calls extend, never stack.</summary>
        public void Freeze(float seconds)
        {
            if (seconds <= 0f || !isActiveAndEnabled) return;
            Timer.Request(seconds, Time.unscaledTime);
            if (owner != null) return; // already frozen: the owner's routine picks up the new end time

            owner = this;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            routine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            float remaining;
            while ((remaining = Timer.Remaining(Time.unscaledTime)) > 0f)
                yield return new WaitForSecondsRealtime(remaining);
            EndFreeze();
        }

        private void EndFreeze()
        {
            routine = null;
            owner = null;
            Timer.Clear();
            Time.timeScale = savedTimeScale;
        }
    }
}
