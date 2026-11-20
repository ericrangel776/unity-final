using System;

namespace DeckArpg.Core
{
    /// <summary>
    /// One combatant's element statuses (burn, chill), advanced manually through <see cref="Tick"/> so the rules can be
    /// tested without a scene. Statuses refresh, never stack.
    /// </summary>
    public sealed class ElementStatusState
    {
        private float durationScale = 1f;
        private int burnPending;
        private int burnTicksLeft;
        private float nextBurnTick;
        private float chillRemaining;

        /// <summary>Multiplies every status duration (and burn's total damage). 0.5 on the boss and Elites.</summary>
        public float DurationScale
        {
            get => durationScale;
            set => durationScale = value > 0f ? value : 0f;
        }

        public bool IsBurning => burnTicksLeft > 0 && burnPending > 0;
        /// <summary>Seconds until the last burn tick lands; 0 when not burning.</summary>
        public float BurnRemaining => IsBurning ? (burnTicksLeft - 1) * ElementRules.BurnTickSeconds + nextBurnTick : 0f;
        /// <summary>Burn damage still to be dealt.</summary>
        public int BurnPending => IsBurning ? burnPending : 0;

        public bool IsChilled => chillRemaining > 0f;
        public float ChillRemaining => chillRemaining > 0f ? chillRemaining : 0f;

        /// <summary>1 - <see cref="ElementRules.ChillSlow"/> while chilled, else 1.</summary>
        public float SpeedMultiplier => IsChilled ? 1f - ElementRules.ChillSlow : 1f;

        /// <summary>
        /// Starts or refreshes a burn of <paramref name="totalDamage"/> (scaled by <see cref="DurationScale"/>, at
        /// least 1). Refresh: the duration resets and pending damage = max(pending, new total).
        /// </summary>
        public void ApplyBurn(int totalDamage, bool isSleight)
        {
            if (totalDamage <= 0) return;
            float seconds = ElementRules.StatusSeconds(Element.Fire, isSleight, durationScale);
            int ticks = (int)Math.Ceiling(seconds / ElementRules.BurnTickSeconds - 0.0001f);
            if (ticks <= 0) return;
            int scaledTotal = Math.Max(1, (int)Math.Ceiling(totalDamage * (double)durationScale - 0.0001));
            burnPending = IsBurning ? Math.Max(burnPending, scaledTotal) : scaledTotal;
            burnTicksLeft = ticks;
            nextBurnTick = ElementRules.BurnTickSeconds;
        }

        /// <summary>Starts or refreshes a chill: remaining = max(remaining, new duration). The slow never stacks.</summary>
        public void ApplyChill(bool isSleight)
        {
            float seconds = ElementRules.StatusSeconds(Element.Ice, isSleight, durationScale);
            if (seconds > chillRemaining) chillRemaining = seconds;
        }

        /// <summary>
        /// Advances every timer by <paramref name="dt"/> and returns the burn damage due this step (0 if none). Pending
        /// damage spreads evenly over the remaining ticks, rounding up, and never exceeds what is pending.
        /// </summary>
        public int Tick(float dt)
        {
            if (!(dt > 0f)) return 0;
            if (chillRemaining > 0f) chillRemaining = Math.Max(0f, chillRemaining - dt);

            int dealt = 0;
            float left = dt;
            while (IsBurning && left >= nextBurnTick)
            {
                left -= nextBurnTick;
                int perTick = (burnPending + burnTicksLeft - 1) / burnTicksLeft;
                if (perTick < 1) perTick = 1;
                if (perTick > burnPending) perTick = burnPending;
                burnPending -= perTick;
                burnTicksLeft--;
                dealt += perTick;
                nextBurnTick = ElementRules.BurnTickSeconds;
            }
            if (IsBurning) nextBurnTick -= left;
            else ClearBurn();
            return dealt;
        }

        public void Clear()
        {
            ClearBurn();
            chillRemaining = 0f;
        }

        private void ClearBurn()
        {
            burnPending = 0;
            burnTicksLeft = 0;
            nextBurnTick = 0f;
        }
    }
}
