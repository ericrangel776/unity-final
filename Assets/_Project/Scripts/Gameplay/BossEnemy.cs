using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    public enum BossState
    {
        /// <summary>The <see cref="TrainingDummyEnemy"/> body approaches, telegraphs and swings as usual.</summary>
        Idle,
        /// <summary>Two cards stocked, the third reserved: a long telegraph before the Sleight fires.</summary>
        SleightWindup,
        /// <summary>Cycle ended: a short punishable reload (extra posture damage).</summary>
        ReloadWindow,
        /// <summary>Cycle ended with enough 0-deletes (or a lost Big Deadlock): long stagger, double damage.</summary>
        Exhausted
    }

    /// <summary>
    /// The boss brain on top of a <see cref="TrainingDummyEnemy"/> body (docs/specs/m4-overview.md). It plays by
    /// player rules: 3-card hand in draw order, telegraphed 3-card Sleights (pattern policy), Floor of 3 burns, values
    /// clamped to [1, 9] with its 9s spaced apart. Running out of cards opens a punishable reload window, or a
    /// 4 s Exhausted stagger when 4+ cards were 0-deleted this cycle.
    /// </summary>
    [RequireComponent(typeof(TrainingDummyEnemy))]
    public sealed class BossEnemy : MonoBehaviour, IEnemyTurnHook
    {
        private TrainingDummyEnemy body;
        private DeckRuntimeManager deck;
        private CombatantStatus status;
        private CombatActionController combat;
        private bool reloadPending;
        private RuntimeCardInstance reservedThird;
        private readonly List<RuntimeCardInstance> sleightCards = new List<RuntimeCardInstance>(SleightRules.StockSize);
        private readonly List<AttackHitbox> sleightHitboxes = new List<AttackHitbox>();
        private bool capturingSleight;

        public IBossAttackPolicy Policy { get; set; } = new PatternBossPolicy();
        public BossState State { get; private set; }
        /// <summary>Seconds left in the current wind-up / reload window / Exhausted (0 when Idle).</summary>
        public float StateRemaining { get; private set; }
        /// <summary>Full length of the current state (for countdown rings).</summary>
        public float StateDuration { get; private set; }
        /// <summary>Attacks started this fight (swings and Sleights).</summary>
        public int AttackCount { get; private set; }
        /// <summary>The Sleight being wound up (stock + reserved card), or null.</summary>
        public SleightResult? PendingSleight { get; private set; }
        /// <summary>The cards of the last fired Sleight (for a 0-break delete), slot order.</summary>
        public IReadOnlyList<RuntimeCardInstance> LastSleightCards => sleightCards;
        /// <summary>Live hitboxes of the last fired Sleight.</summary>
        public IReadOnlyList<AttackHitbox> LastSleightHitboxes => sleightHitboxes;
        /// <summary>Scaled <see cref="Time.time"/> when the last Sleight activated (-inf before the first).</summary>
        public float LastSleightTime { get; private set; } = float.NegativeInfinity;
        public TrainingDummyEnemy Body => body;
        public DeckRuntimeManager Deck => deck;
        public CombatantStatus Status => status;
        public CombatActionController Combat => combat;
        public bool IsBusy => State != BossState.Idle;

        /// <summary>A Sleight wind-up started; the argument is its value (sum).</summary>
        public event Action<int> SleightWindupStarted;
        /// <summary>The wind-up ended without firing (broken, death, forced stagger).</summary>
        public event Action SleightWindupCancelled;
        /// <summary>The Sleight activated: its payload.</summary>
        public event Action<AttackPayload> SleightFired;
        public event Action<BossCycleEnd> CycleEnded;
        public event Action ExhaustedStarted;
        /// <summary>A reload window or Exhausted ended; the boss is back to normal.</summary>
        public event Action RecoveredFromStagger;
        public event Action<BossState> StateChanged;

        private void Awake()
        {
            body = GetComponent<TrainingDummyEnemy>();
            deck = GetComponent<DeckRuntimeManager>();
            status = GetComponent<CombatantStatus>();
            combat = GetComponent<CombatActionController>();
            body.TurnHook = this;
            // Set before the deck is dealt (RunDirector adds the boss first), so the first shuffle is spaced too.
            deck.Cycle.SpacingRule = card => !card.IsZeroBreaker && card.GetEffectiveValue() == CardValue.Max;
        }

        private void OnEnable()
        {
            status.Broken += HandleBroken;
            status.Died += HandleDied;
            combat.HitboxSpawned += HandleHitboxSpawned;
        }

        private void OnDisable()
        {
            status.Broken -= HandleBroken;
            status.Died -= HandleDied;
            combat.HitboxSpawned -= HandleHitboxSpawned;
            ResetMultipliers();
        }

        private void OnDestroy()
        {
            if (body != null && ReferenceEquals(body.TurnHook, this)) body.TurnHook = null;
        }

        private void Update()
        {
            if (State == BossState.Idle || status.IsDead) return;
            if (State == BossState.SleightWindup) FaceTarget();
            // Scaled time: hit-stop and the Deadlock freeze pause the boss too.
            StateRemaining -= Time.deltaTime;
            if (StateRemaining > 0f) return;

            switch (State)
            {
                case BossState.SleightWindup: FireSleight(); break;
                default: EndStagger(); break;
            }
        }

        // ---- IEnemyTurnHook ----

        public bool TryTakeTurn()
        {
            var cycle = deck.Cycle;
            int handCount = 0;
            foreach (var c in cycle.Hand) if (c != null) handCount++;
            if (handCount == 0) return false; // the body calls OnOutOfCards

            float hp = status.MaxHp > 0 ? status.Hp / (float)status.MaxHp : 0f;
            var action = Policy.Next(new BossTurnContext(AttackCount, handCount, hp));
            AttackCount++;
            if (action != BossAction.Sleight || handCount < SleightRules.StockSize) return false;
            return BeginSleight();
        }

        public void OnOutOfCards()
        {
            var end = BossRules.CycleEnd(deck.Cycle.DeletedThisCycleCount);
            reloadPending = true;
            CycleEnded?.Invoke(end);
            if (end == BossCycleEnd.Exhausted) BeginExhausted();
            else
            {
                status.PostureTakenMultiplier = BossRules.ReloadPostureMultiplier;
                SetState(BossState.ReloadWindow, BossRules.ReloadWindowSeconds);
            }
        }

        /// <summary>Global clash hook (step 4). The base boss doesn't clash while busy: the hit lands on the body.</summary>
        public Func<AttackHitbox, bool> BusyClashResolver { get; set; }

        public bool ResolveBusyClash(AttackHitbox incoming) => BusyClashResolver != null && BusyClashResolver(incoming);

        // ---- Sleights ----

        /// <summary>Stocks two cards (oldest first) and reserves the third; the Sleight fires after the wind-up.</summary>
        private bool BeginSleight()
        {
            var cycle = deck.Cycle;
            for (int i = 0; i < SleightRules.StockSize - 1; i++)
                if (cycle.TryStock(cycle.OldestSlot, out _) != StockResult.Stocked) return AbortStock();
            int slot = cycle.OldestSlot;
            if (slot < 0) return AbortStock();
            reservedThird = cycle.Hand[slot];

            var preview = new List<RuntimeCardInstance>(cycle.Stock) { reservedThird };
            PendingSleight = SleightRules.Compose(preview);
            status.FlinchArmor = true; // only a clash (or a posture break) stops the wind-up
            SetState(BossState.SleightWindup, BossRules.SleightWindupSeconds);
            SleightWindupStarted?.Invoke(PendingSleight.Value.Value);
            return true;
        }

        private bool AbortStock()
        {
            deck.Cycle.InterruptStock();
            return false;
        }

        private void FireSleight()
        {
            var cycle = deck.Cycle;
            status.FlinchArmor = false;
            PendingSleight = null;

            int slot = -1;
            for (int i = 0; i < DeckCycle.HandSize; i++)
                if (cycle.Hand[i] != null && cycle.Hand[i] == reservedThird) slot = i;
            var cards = new List<RuntimeCardInstance>(cycle.Stock) { reservedThird };
            reservedThird = null;

            if (slot < 0 || cycle.TryStock(slot, out var sleight) != StockResult.Fired)
            {
                // The reserved card left the hand (an effect took it): the stock falls apart.
                cycle.InterruptStock();
                SetState(BossState.Idle, 0f);
                SleightWindupCancelled?.Invoke();
                return;
            }

            sleightCards.Clear();
            sleightCards.AddRange(cards);

            var payload = AttackPayload.FromSleight(sleight, status.OwnerId, Team.Enemy, Time.frameCount,
                SleightRules.HitCount(sleight.Shape));
            sleightHitboxes.Clear();
            LastSleightTime = Time.time;
            SetState(BossState.Idle, 0f);

            capturingSleight = true;
            combat.SpawnSleight(payload, AimDirection());
            capturingSleight = false;
            SleightFired?.Invoke(payload);
        }

        private void HandleHitboxSpawned(AttackHitbox hb)
        {
            // Strike combos spawn later hits from a coroutine: keep collecting while the payload is a Sleight.
            if (hb != null && (capturingSleight || (hb.Payload.IsSleight && hb.Payload.Team == Team.Enemy)))
                sleightHitboxes.Add(hb);
        }

        private void HandleBroken()
        {
            if (State != BossState.SleightWindup) return;
            // "Crack the catalyst": slot 1 burns unless at the Floor of 3, the rest go to discard.
            CancelWindup();
        }

        private void CancelWindup()
        {
            deck.Cycle.InterruptStock();
            status.FlinchArmor = false;
            PendingSleight = null;
            reservedThird = null;
            SetState(BossState.Idle, 0f);
            SleightWindupCancelled?.Invoke();
        }

        // ---- Staggers ----

        /// <summary>
        /// Big Deadlock lost by the boss: straight to Exhausted. No reload unless the hand and queue are empty; the boss
        /// continues its current cycle afterwards.
        /// </summary>
        public void ForceExhausted()
        {
            if (status.IsDead) return;
            if (State == BossState.SleightWindup) CancelWindup();
            if (State != BossState.ReloadWindow) reloadPending = false;
            BeginExhausted();
        }

        private void BeginExhausted()
        {
            status.DamageTakenMultiplier = BossRules.ExhaustedDamageMultiplier;
            status.PostureTakenMultiplier = BossRules.ExhaustedDamageMultiplier;
            SetState(BossState.Exhausted, BossRules.ExhaustedSeconds);
            ExhaustedStarted?.Invoke();
        }

        private void EndStagger()
        {
            ResetMultipliers();
            var cycle = deck.Cycle;
            if (reloadPending || (cycle.OldestSlot < 0 && cycle.DrawCount == 0)) cycle.Reload();
            reloadPending = false;
            SetState(BossState.Idle, 0f);
            RecoveredFromStagger?.Invoke();
        }

        private void HandleDied()
        {
            if (State == BossState.SleightWindup) CancelWindup();
            ResetMultipliers();
            reloadPending = false;
            SetState(BossState.Idle, 0f);
        }

        private void ResetMultipliers()
        {
            if (status == null) return;
            status.DamageTakenMultiplier = 1f;
            status.PostureTakenMultiplier = 1f;
        }

        private void SetState(BossState next, float seconds)
        {
            StateDuration = seconds;
            StateRemaining = seconds;
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }

        private Vector3 AimDirection()
        {
            if (body.Target == null) return transform.forward;
            var d = body.Target.position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
        }

        private void FaceTarget()
        {
            var d = AimDirection();
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d);
        }

        // ---- Tests ----

        /// <summary>
        /// Cuts the wind-up short and fires the Sleight now (a player Sleight tied it: both go off for the Big Deadlock).
        /// Returns the first live Sleight hitbox, or null if nothing fired.
        /// </summary>
        internal AttackHitbox FireNow()
        {
            if (State != BossState.SleightWindup) return null;
            FireSleight();
            foreach (var hb in sleightHitboxes) if (hb != null && hb.IsLive) return hb;
            return null;
        }

        /// <summary>Tests: start a Sleight wind-up now (needs a full hand).</summary>
        internal bool ForceSleight() => BeginSleight();

        /// <summary>Tests: end the cycle now (as if the hand just ran out).</summary>
        internal void ForceCycleEnd() => OnOutOfCards();

        /// <summary>Tests: skip to the end of the current state.</summary>
        internal void ForceStateEnd() => StateRemaining = 0f;
    }
}
