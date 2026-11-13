using System;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>Everything about a finished Deadlock, for the result reveal.</summary>
    public readonly struct DeadlockResult
    {
        public readonly DeadlockGrade PlayerGrade;
        public readonly int PlayerCommittedValue;
        public readonly int PlayerPower;
        public readonly DeadlockGrade EnemyGrade;
        public readonly int EnemyCommittedValue;
        public readonly int EnemyPower;
        /// <summary>AWins = player wins, BWins = enemy wins, Neutral = tie.</summary>
        public readonly ClashOutcome Outcome;
        public readonly Vector3 Position;

        public DeadlockResult(DeadlockGrade playerGrade, int playerCommittedValue, int playerPower, DeadlockGrade enemyGrade,
            int enemyCommittedValue, int enemyPower, ClashOutcome outcome, Vector3 position)
        {
            PlayerGrade = playerGrade;
            PlayerCommittedValue = playerCommittedValue;
            PlayerPower = playerPower;
            EnemyGrade = enemyGrade;
            EnemyCommittedValue = enemyCommittedValue;
            EnemyPower = enemyPower;
            Outcome = outcome;
            Position = position;
        }
    }

    /// <summary>
    /// Runs the Deadlock tie-break. An equal-value clash between a player and an enemy card (neither
    /// a 0) freezes time for <see cref="DeadlockRules.WindowSeconds"/> of real time. The player presses
    /// once: Poke pushes on timing alone, a hand slot also commits that card (spent like a play) and
    /// adds its value. The enemy rolls its own timing and sometimes commits a card. The higher push
    /// breaks the other as a Card Break; equal pushes cancel both like a normal tie.
    /// </summary>
    [RequireComponent(typeof(HitStop))]
    public sealed class DeadlockController : MonoBehaviour
    {
        [Tooltip("Seed for the enemy's rolls. 0 = random each run.")]
        [SerializeField] private int seed;

        private HitStop hitStop;
        private System.Random random;
        private AttackHitbox playerHitbox;
        private AttackHitbox enemyHitbox;
        private DeckRuntimeManager playerDeck;
        private DeckRuntimeManager enemyDeck;
        private float startTime;
        private (DeadlockGrade grade, bool commit) enemyRoll;

        /// <summary>The scene's controller (the most recently enabled one).</summary>
        public static DeadlockController Current { get; private set; }
        public static event Action<DeadlockController> Started;
        public static event Action<DeadlockResult> Resolved;

        /// <summary>Clock in seconds; defaults to unscaled time. Tests replace it to control timing exactly.</summary>
        public Func<float> Clock { get; set; }
        /// <summary>Forces the enemy's next roll (testing/debug). Cleared after use.</summary>
        public (DeadlockGrade grade, bool commit)? EnemyRollOverride { get; set; }

        public bool IsActive { get; private set; }
        public bool HasPlayerSubmitted { get; private set; }
        public DeadlockGrade PlayerGrade { get; private set; }
        public int PlayerCommittedValue { get; private set; }
        public Vector3 Position { get; private set; }
        public float Elapsed => IsActive ? Now - startTime : 0f;
        /// <summary>0..1 through the window.</summary>
        public float Progress => Mathf.Clamp01(Elapsed / DeadlockRules.WindowSeconds);
        /// <summary>The player's current hand, for the UI hint (empty if the player has no deck).</summary>
        public DeckCycle PlayerCycle => playerDeck != null ? playerDeck.Cycle : null;

        private float Now => Clock != null ? Clock() : Time.unscaledTime;

        private void Awake()
        {
            hitStop = GetComponent<HitStop>();
            random = new System.Random(seed != 0 ? seed : Environment.TickCount);
        }

        private void OnEnable()
        {
            Current = this;
            ClashSystem.Deadlock = this;
        }

        private void OnDisable()
        {
            // Never leave a half-finished Deadlock behind: cancel it like a tie.
            if (IsActive) Finish(abort: true);
            if (ClashSystem.Deadlock == this) ClashSystem.Deadlock = null;
            if (Current == this) Current = null;
        }

        /// <summary>Starts a Deadlock if <paramref name="a"/> and <paramref name="b"/> are an eligible tie.</summary>
        internal bool TryBegin(AttackHitbox a, AttackHitbox b, Vector3 position)
        {
            if (IsActive || !isActiveAndEnabled || a == null || b == null) return false;
            if (a.Payload.IsZeroBreaker || b.Payload.IsZeroBreaker) return false; // 0s keep their own rules

            if (a.Payload.Team == Team.Player && b.Payload.Team == Team.Enemy) { playerHitbox = a; enemyHitbox = b; }
            else if (b.Payload.Team == Team.Player && a.Payload.Team == Team.Enemy) { playerHitbox = b; enemyHitbox = a; }
            else return false;

            playerDeck = playerHitbox.Owner.GetComponent<DeckRuntimeManager>();
            enemyDeck = enemyHitbox.Owner.GetComponent<DeckRuntimeManager>();
            enemyRoll = EnemyRollOverride ?? DeadlockRules.RollEnemy(random);
            EnemyRollOverride = null;

            IsActive = true;
            HasPlayerSubmitted = false;
            PlayerGrade = DeadlockGrade.Miss;
            PlayerCommittedValue = 0;
            Position = position;
            startTime = Now;
            // Freeze the world for the whole window; the result's own hit-stop extends it.
            hitStop.Freeze(DeadlockRules.WindowSeconds + 0.1f);
            Started?.Invoke(this);
            return true;
        }

        /// <summary>
        /// The player's one press. Returns false if no Deadlock is running or they already pressed.
        /// Committing a slot spends that card immediately (into discard, the next card is drawn).
        /// </summary>
        public bool SubmitPlayer(DeadlockChoice choice)
        {
            if (!IsActive || HasPlayerSubmitted) return false;
            HasPlayerSubmitted = true;
            PlayerGrade = DeadlockRules.Grade(Now - startTime);

            int slot = DeadlockRules.SlotIndex(choice);
            var cycle = PlayerCycle;
            if (slot >= 0 && cycle != null && cycle.Hand[slot] != null && !cycle.Hand[slot].IsZeroBreaker
                && cycle.TryPlay(slot, out var card))
            {
                PlayerCommittedValue = card.GetEffectiveValue();
            }
            return true;
        }

        private void Update()
        {
            if (IsActive && Now - startTime >= DeadlockRules.WindowSeconds) Finish(abort: false);
        }

        private void Finish(bool abort)
        {
            IsActive = false;
            // If either attack vanished (owner destroyed/disabled), there's nothing to resolve: cancel,
            // and report it as a tie so the result reveal matches what actually happens.
            bool bothLive = playerHitbox != null && playerHitbox.IsLive && enemyHitbox != null && enemyHitbox.IsLive;
            if (!bothLive) abort = true;
            int enemyCommitted = abort ? 0 : CommitEnemyCard();
            int playerPower = DeadlockRules.Power(PlayerGrade, PlayerCommittedValue);
            int enemyPower = DeadlockRules.Power(enemyRoll.grade, enemyCommitted);
            var outcome = abort ? ClashOutcome.Neutral : DeadlockRules.Resolve(playerPower, enemyPower);

            Resolved?.Invoke(new DeadlockResult(PlayerGrade, PlayerCommittedValue, playerPower, enemyRoll.grade,
                enemyCommitted, enemyPower, outcome, Position));
            ClashSystem.ResolveDeadlock(playerHitbox, enemyHitbox, outcome, Math.Abs(playerPower - enemyPower), Position);

            playerHitbox = enemyHitbox = null;
            playerDeck = enemyDeck = null;
        }

        // The enemy commits its first usable hand card if its roll said so.
        private int CommitEnemyCard()
        {
            if (!enemyRoll.commit || enemyDeck == null) return 0;
            var cycle = enemyDeck.Cycle;
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var card = cycle.Hand[i];
                if (card != null && !card.IsZeroBreaker && cycle.TryPlay(i, out card)) return card.GetEffectiveValue();
            }
            return 0;
        }
    }
}
