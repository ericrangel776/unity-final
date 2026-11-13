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

    /// <summary>Where the reload prompt appears around the player. The HUD maps these to screen offsets.</summary>
    public enum ReloadPromptAnchor
    {
        Above,
        UpperRight,
        Right,
        Below,
        Left,
        UpperLeft
    }

    /// <summary>Tunables and pure grading for the Active Reload timing bar.</summary>
    public static class ActiveReloadRules
    {
        /// <summary>Each reload rolls its bar duration in this range (seconds).</summary>
        public const float MinBarSeconds = 0.8f;
        public const float MaxBarSeconds = 1.6f;
        /// <summary>Each reload rolls the sweet-zone center in this range (fraction of the bar).</summary>
        public const float MinTarget01 = 0.40f;
        public const float MaxTarget01 = 0.85f;
        /// <summary>Zone half-widths as a fraction of the bar, so faster bars leave less time in the zone.</summary>
        public const float PerfectTolerance01 = 0.05f;
        public const float GoodTolerance01 = 0.125f;
        /// <summary>A jammed reload finishes this long after the bar would have ended.</summary>
        public const float JamExtraSeconds = 0.8f;
        public const float BoostSpeedMultiplier = 1.3f;
        public const float BoostSeconds = 2f;

        public static readonly int AnchorCount = Enum.GetValues(typeof(ReloadPromptAnchor)).Length;

        /// <summary>Grade for a press at bar progress <paramref name="progress01"/> with the zone centered on <paramref name="target01"/>.</summary>
        public static ReloadGrade Grade(float progress01, float target01)
        {
            if (float.IsNaN(progress01) || progress01 < 0f || progress01 > 1f) return ReloadGrade.Miss;
            float error = Math.Abs(progress01 - target01);
            // Tiny epsilon so the documented inclusive bounds survive float rounding.
            const float eps = 1e-4f;
            if (error <= PerfectTolerance01 + eps) return ReloadGrade.Perfect;
            if (error <= GoodTolerance01 + eps) return ReloadGrade.Good;
            return ReloadGrade.Miss;
        }
    }

    /// <summary>
    /// Active Reload state machine for one deck. <see cref="Begin"/> rolls a bar speed, sweet-spot position
    /// and prompt anchor, then runs the bar. A Perfect/Good <see cref="Press"/> reloads instantly (Perfect
    /// adds a move-speed boost), a Miss jams it, and no press reloads when the bar ends.
    /// Time comes from the caller via <see cref="Tick"/> (scaled time, so hit-stop pauses it).
    /// </summary>
    public sealed class ActiveReload
    {
        private readonly DeckCycle deck;
        private readonly Random rng;
        private bool hasAnchor;

        public ActiveReload(DeckCycle deck, Random random)
        {
            this.deck = deck ?? throw new ArgumentNullException(nameof(deck));
            rng = random ?? throw new ArgumentNullException(nameof(random));
        }

        public ReloadState State { get; private set; }
        /// <summary>Seconds since <see cref="Begin"/>; 0 while Idle.</summary>
        public float Elapsed { get; private set; }
        /// <summary>Length of the current (or last) bar, rolled at <see cref="Begin"/>.</summary>
        public float BarDuration { get; private set; }
        /// <summary>Sweet-zone center of the current (or last) bar, as a fraction of the bar.</summary>
        public float Target01 { get; private set; }
        /// <summary>Where the current (or last) prompt appears around the player.</summary>
        public ReloadPromptAnchor Anchor { get; private set; }
        public float BoostRemaining { get; private set; }

        public float JamTotalSeconds => BarDuration + ActiveReloadRules.JamExtraSeconds;

        /// <summary>Fill of the visible bar: the timing bar while Running, the jam recharge while Jammed.</summary>
        public float Progress01
        {
            get
            {
                switch (State)
                {
                    case ReloadState.Running: return Math.Min(1f, Elapsed / BarDuration);
                    case ReloadState.Jammed: return Math.Min(1f, Elapsed / JamTotalSeconds);
                    default: return 0f;
                }
            }
        }

        /// <summary>Hand plays, Parry and stocking are locked while reloading; move/dash/poke are not.</summary>
        public bool LocksCardPlays => State != ReloadState.Idle;

        public float SpeedMultiplier => BoostRemaining > 0f ? ActiveReloadRules.BoostSpeedMultiplier : 1f;

        /// <summary>Raised after the deck reshuffles, with how the reload finished.</summary>
        public event Action<ReloadGrade> Completed;

        /// <summary>True while an Overburn bar runs (and during its <see cref="Completed"/> handlers).</summary>
        public bool IsOverburn { get; private set; }
        public int OverburnUsesThisRoom { get; private set; }

        public int NextOverburnCost(int maxHp) => OverburnRules.Cost(maxHp, OverburnUsesThisRoom);

        /// <summary>The next Overburn costs 10% again. Call on room start and respawn.</summary>
        public void ResetOverburnCost() => OverburnUsesThisRoom = 0;

        public bool Begin()
        {
            if (State != ReloadState.Idle || !deck.CanReload) return false;
            StartBar();
            return true;
        }

        /// <summary>
        /// Overburn needs an empty draw queue and at least one burned card (discard may be empty), and the
        /// payer must survive the price.
        /// </summary>
        public bool CanBeginOverburn(IHpPool payer)
        {
            if (payer == null) throw new ArgumentNullException(nameof(payer));
            if (State != ReloadState.Idle || deck.DrawCount > 0 || deck.BurnedCount == 0) return false;
            return OverburnRules.CanAfford(payer.Hp, NextOverburnCost(payer.MaxHp));
        }

        /// <summary>
        /// Pays the escalating HP price and starts the bar. When it finishes (any grade), every burned
        /// card is restored and shuffled in with the discard.
        /// </summary>
        public bool BeginOverburn(IHpPool payer)
        {
            if (!CanBeginOverburn(payer)) return false;
            payer.SpendHp(NextOverburnCost(payer.MaxHp));
            OverburnUsesThisRoom++;
            IsOverburn = true;
            StartBar();
            return true;
        }

        private void StartBar()
        {
            BarDuration = Roll(ActiveReloadRules.MinBarSeconds, ActiveReloadRules.MaxBarSeconds);
            Target01 = Roll(ActiveReloadRules.MinTarget01, ActiveReloadRules.MaxTarget01);
            Anchor = RollAnchor();
            State = ReloadState.Running;
            Elapsed = 0f;
        }

        /// <summary>Grades a press on the running bar. Ignored (returns None) while Idle or Jammed.</summary>
        public ReloadGrade Press()
        {
            if (State != ReloadState.Running) return ReloadGrade.None;

            var grade = ActiveReloadRules.Grade(Elapsed / BarDuration, Target01);
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
            if (State == ReloadState.Running && Elapsed >= BarDuration) Complete(ReloadGrade.None);
            else if (State == ReloadState.Jammed && Elapsed >= JamTotalSeconds) Complete(ReloadGrade.Miss);
        }

        /// <summary>Abort without reloading (e.g. death/respawn). Any active boost is kept; an Overburn is not refunded.</summary>
        public void Cancel()
        {
            State = ReloadState.Idle;
            Elapsed = 0f;
            IsOverburn = false;
        }

        private void Complete(ReloadGrade grade)
        {
            if (IsOverburn) deck.RestoreBurned();
            deck.Reload();
            State = ReloadState.Idle;
            Elapsed = 0f;
            Completed?.Invoke(grade);
            IsOverburn = false;
        }

        private float Roll(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Uniform over the anchors, excluding the previous one so the prompt always moves.</summary>
        private ReloadPromptAnchor RollAnchor()
        {
            if (!hasAnchor)
            {
                hasAnchor = true;
                return (ReloadPromptAnchor)rng.Next(ActiveReloadRules.AnchorCount);
            }
            int next = rng.Next(ActiveReloadRules.AnchorCount - 1);
            if (next >= (int)Anchor) next++;
            return (ReloadPromptAnchor)next;
        }
    }
}
