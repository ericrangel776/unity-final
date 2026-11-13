using System;

namespace DeckArpg.Core
{
    /// <summary>How an Active Reload finished. None = the bar ran out with no press.</summary>
    public enum ReloadGrade
    {
        None,
        Miss,
        Good,
        Perfect
    }

    public enum ReloadState
    {
        Idle,
        Running,
        Jammed
    }

    /// <summary>Tunables and pure grading for the Active Reload timing bar.</summary>
    public static class ActiveReloadRules
    {
        public const float BarSeconds = 1.2f;
        /// <summary>Center of the bar: where the sweet spot sits.</summary>
        public const float TargetTime = 0.6f;
        public const float PerfectTolerance = 0.06f;
        public const float GoodTolerance = 0.15f;
        /// <summary>A mistimed press jams the reload; it finishes this long after the bar started.</summary>
        public const float JamTotalSeconds = 2.0f;
        public const float BoostSpeedMultiplier = 1.3f;
        public const float BoostSeconds = 2f;

        /// <summary>Grade for a press <paramref name="offset"/> seconds after the bar started.</summary>
        public static ReloadGrade Grade(float offset)
        {
            if (float.IsNaN(offset) || offset < 0f || offset > BarSeconds) return ReloadGrade.Miss;
            float error = Math.Abs(offset - TargetTime);
            // Tiny epsilon so the documented inclusive bounds survive float rounding (0.6 - 0.54 != 0.06f).
            const float eps = 1e-4f;
            if (error <= PerfectTolerance + eps) return ReloadGrade.Perfect;
            if (error <= GoodTolerance + eps) return ReloadGrade.Good;
            return ReloadGrade.Miss;
        }
    }

    /// <summary>
    /// Active Reload state machine for one deck. <see cref="Begin"/> starts a 1.2 s bar; a Perfect/Good
    /// <see cref="Press"/> reloads instantly (Perfect adds a move-speed boost), a Miss jams it until
    /// <see cref="ActiveReloadRules.JamTotalSeconds"/>, and no press reloads when the bar ends.
    /// Time comes from the caller via <see cref="Tick"/> (scaled time, so hit-stop pauses it).
    /// </summary>
    public sealed class ActiveReload
    {
        private readonly DeckCycle deck;

        public ActiveReload(DeckCycle deck)
        {
            this.deck = deck ?? throw new ArgumentNullException(nameof(deck));
        }

        public ReloadState State { get; private set; }
        /// <summary>Seconds since <see cref="Begin"/>; 0 while Idle.</summary>
        public float Elapsed { get; private set; }
        public float BoostRemaining { get; private set; }

        /// <summary>Fill of the visible bar: the timing bar while Running, the jam recharge while Jammed.</summary>
        public float Progress01
        {
            get
            {
                switch (State)
                {
                    case ReloadState.Running: return Math.Min(1f, Elapsed / ActiveReloadRules.BarSeconds);
                    case ReloadState.Jammed: return Math.Min(1f, Elapsed / ActiveReloadRules.JamTotalSeconds);
                    default: return 0f;
                }
            }
        }

        /// <summary>Hand plays, Parry and stocking are locked while reloading; move/dash/poke are not.</summary>
        public bool LocksCardPlays => State != ReloadState.Idle;

        public float SpeedMultiplier => BoostRemaining > 0f ? ActiveReloadRules.BoostSpeedMultiplier : 1f;

        /// <summary>Raised after the deck reshuffles, with how the reload finished.</summary>
        public event Action<ReloadGrade> Completed;

        public bool Begin()
        {
            if (State != ReloadState.Idle || !deck.CanReload) return false;
            State = ReloadState.Running;
            Elapsed = 0f;
            return true;
        }

        /// <summary>Grades a press on the running bar. Ignored (returns None) while Idle or Jammed.</summary>
        public ReloadGrade Press()
        {
            if (State != ReloadState.Running) return ReloadGrade.None;

            var grade = ActiveReloadRules.Grade(Elapsed);
            if (grade == ReloadGrade.Miss)
            {
                State = ReloadState.Jammed;
                return grade;
            }

            if (grade == ReloadGrade.Perfect) BoostRemaining = ActiveReloadRules.BoostSeconds;
            Complete(grade);
            return grade;
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds)) return;

            if (BoostRemaining > 0f) BoostRemaining = Math.Max(0f, BoostRemaining - deltaSeconds);

            if (State == ReloadState.Idle) return;
            Elapsed += deltaSeconds;
            if (State == ReloadState.Running && Elapsed >= ActiveReloadRules.BarSeconds) Complete(ReloadGrade.None);
            else if (State == ReloadState.Jammed && Elapsed >= ActiveReloadRules.JamTotalSeconds) Complete(ReloadGrade.Miss);
        }

        /// <summary>Abort without reloading (e.g. death/respawn). Any active boost is kept.</summary>
        public void Cancel()
        {
            State = ReloadState.Idle;
            Elapsed = 0f;
        }

        private void Complete(ReloadGrade grade)
        {
            deck.Reload();
            State = ReloadState.Idle;
            Elapsed = 0f;
            Completed?.Invoke(grade);
        }
    }
}
