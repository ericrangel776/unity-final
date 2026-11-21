using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Greybox enemy: walks toward the player, telegraphs its next card's value, then swings it.
    /// The card only activates when the swing starts, so a 0-parry must come after the swing begins.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(CombatActionController), typeof(DeckRuntimeManager))]
    public sealed class TrainingDummyEnemy : MonoBehaviour
    {
        private enum State { Approach, Telegraph, Recover }

        private static readonly HashSet<TrainingDummyEnemy> Registered = new HashSet<TrainingDummyEnemy>();

        /// <summary>Enabled enemies that are alive. Scales the player's break stun (crowds stun less).</summary>
        public static int LiveCount
        {
            get
            {
                int n = 0;
                foreach (var e in Registered)
                    if (e != null && e.status != null && !e.status.IsDead) n++;
                return n;
            }
        }

        /// <summary>Debug menu: enemies stand still and don't attack (knockback still moves them).</summary>
        public static bool DebugFreezeAI { get; set; }

        // Static state survives play sessions when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDebugStatics() => DebugFreezeAI = false;

        [SerializeField] private Transform target;
        [SerializeField] private float moveSpeed = 2.5f;
        [SerializeField] private float attackRange = 2.2f;
        [SerializeField] private float telegraphSeconds = 0.7f;
        [SerializeField] private float recoverSeconds = 1.2f;
        [SerializeField] private float respawnDelay = 2f;
        [Tooltip("Off in a run: a dead enemy is removed instead of coming back.")]
        [SerializeField] private bool respawnOnDeath = true;

        private CharacterController body;
        private CombatActionController combat;
        private DeckRuntimeManager deck;
        private CombatantStatus status;
        private ElementStatusEffects elementFx;
        private State state;
        private float timer;
        private int telegraphSlot = -1;
        private Vector3 spawnPosition;
        private bool variantApplied;
        /// <summary>Caster: seconds spent closer than its retreat range (cornered once it reaches the limit).</summary>
        private float closeTimer;

        /// <summary>Card currently being telegraphed, or null.</summary>
        public RuntimeCardInstance CurrentTelegraph { get; private set; }
        /// <summary>0..1 progress through the telegraph wind-up.</summary>
        public float TelegraphProgress => state == State.Telegraph ? 1f - Mathf.Clamp01(timer / telegraphSeconds) : 0f;
        /// <summary>
        /// Veiled trial: the telegraphed value stays hidden until this many seconds before the swing (0 = off).
        /// Absolute time, so room tempo doesn't shrink it.
        /// </summary>
        public float VeiledRevealSeconds { get; set; }
        /// <summary>True while the current telegraph's value should show as <c>?</c>.</summary>
        public bool IsTelegraphVeiled =>
            state == State.Telegraph && CurrentTelegraph != null && VeiledRevealSeconds > 0f && timer > VeiledRevealSeconds;
        public CombatantStatus Status => status;
        public DeckRuntimeManager Deck => deck;

        public event Action<RuntimeCardInstance> TelegraphStarted;
        public event Action TelegraphEnded;
        /// <summary>A telegraphed card was swung (it activated).</summary>
        public event Action<RuntimeCardInstance> Swung;

        /// <summary>Optional turn override (the boss). Null for mobs and Elites.</summary>
        public IEnemyTurnHook TurnHook { get; set; }

        public Transform Target { get => target; set => target = value; }
        public bool RespawnOnDeath { get => respawnOnDeath; set => respawnOnDeath = value; }
        public float MoveSpeed => moveSpeed;
        public float TelegraphSeconds => telegraphSeconds;
        public float RecoverSeconds => recoverSeconds;
        /// <summary>Mob archetype (docs/specs/enemy-variants.md). Brute unless <see cref="ApplyVariant"/> changed it.</summary>
        public EnemyVariant Variant { get; private set; } = EnemyVariant.Brute;

        /// <summary>
        /// Scales move speed and wind-up/recover time by the variant's stats; a Caster also keeps its distance. Call
        /// once, right after spawning. Multiplicative, so the order with <see cref="ApplyTempo"/> doesn't matter.
        /// HP, posture, deck and look are the spawner's job. The boss (<see cref="TurnHook"/>) is always a Brute.
        /// </summary>
        public void ApplyVariant(EnemyVariant v)
        {
            if (variantApplied) throw new InvalidOperationException("ApplyVariant was already called on this enemy.");
            if (TurnHook != null) throw new InvalidOperationException("An enemy with a TurnHook (the boss) stays a Brute.");
            var stats = EnemyVariantRules.Stats(v);
            moveSpeed *= stats.MovePercent / 100f;
            telegraphSeconds *= stats.TelegraphPercent / 100f;
            recoverSeconds *= stats.RecoverPercent / 100f;
            Variant = v;
            variantApplied = true;
        }

        /// <summary>
        /// Room tempo: moves faster and winds up/recovers quicker by <paramref name="multiplier"/> (1 = unchanged).
        /// Call once, right after spawning. The parry window (BodyHitDelay) is not scaled.
        /// </summary>
        public void ApplyTempo(float multiplier)
        {
            if (multiplier <= 0f) throw new ArgumentOutOfRangeException(nameof(multiplier));
            moveSpeed *= multiplier;
            telegraphSeconds /= multiplier;
            recoverSeconds /= multiplier;
        }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            combat = GetComponent<CombatActionController>();
            deck = GetComponent<DeckRuntimeManager>();
            status = GetComponent<CombatantStatus>();
            elementFx = GetComponent<ElementStatusEffects>();
            status.Team = Team.Enemy;
            spawnPosition = transform.position;
        }

        private void OnEnable()
        {
            Registered.Add(this);
            status.Died += HandleDied;
            status.Broken += CancelTelegraph;
            status.Flinched += CancelTelegraph;
        }

        private void OnDisable()
        {
            Registered.Remove(this);
            status.Died -= HandleDied;
            status.Broken -= CancelTelegraph;
            status.Flinched -= CancelTelegraph;
        }

        private void Start()
        {
            // Again here: runtime-built enemies (tests) may add the effects component after Awake.
            if (elementFx == null) elementFx = GetComponent<ElementStatusEffects>();
            if (target == null)
            {
                var player = FindAnyObjectByType<PlayerMotor>();
                if (player != null) target = player.transform;
            }
            state = State.Recover;
            timer = recoverSeconds;
        }

        private void Update()
        {
            if (status.IsDead)
            {
                timer -= Time.deltaTime;
                if (timer > 0f) return;
                if (respawnOnDeath) Respawn();
                else Destroy(gameObject);
                return;
            }
            // Before the stun check so a flinched dummy still slides back. A bump during a Wind push is a slam.
            if (status.KnockbackVelocity != Vector3.zero &&
                (body.Move(status.KnockbackVelocity * Time.deltaTime) & CollisionFlags.Sides) != 0 && elementFx != null)
                elementFx.NotifyKnockbackBlocked();
            if (status.IsStunned || target == null || DebugFreezeAI) return;
            // Chill slows movement and the wind-up/recover timers alike.
            float speed = elementFx != null ? elementFx.SpeedMultiplier : 1f;
            if (TurnHook != null && TurnHook.IsBusy) return;

            var toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(toTarget);

            timer -= Time.deltaTime * speed;
            switch (state)
            {
                case State.Approach:
                    if (Variant == EnemyVariant.Caster)
                    {
                        CasterApproach(toTarget, speed);
                        break;
                    }
                    if (toTarget.magnitude > attackRange * 0.8f)
                        body.SimpleMove(toTarget.normalized * (moveSpeed * speed));
                    else if (timer <= 0f)
                        BeginTelegraph();
                    break;
                case State.Telegraph:
                    if (timer <= 0f) Swing(toTarget);
                    break;
                case State.Recover:
                    if (timer <= 0f) state = State.Approach;
                    break;
            }
        }

        /// <summary>
        /// Keeps 4-7 u from the target: advances from beyond 7, backs off inside 4, holds between. Casts from 4-9 u, or
        /// point-blank once it has been stuck too close for <see cref="EnemyVariantRules.CasterCorneredSeconds"/>.
        /// </summary>
        private void CasterApproach(Vector3 toTarget, float speed)
        {
            float distance = toTarget.magnitude;
            var dir = distance > 0.001f ? toTarget / distance : transform.forward;
            switch (EnemyVariantRules.CasterMovement(distance))
            {
                case CasterMove.Advance:
                    closeTimer = 0f;
                    body.SimpleMove(dir * (moveSpeed * speed));
                    break;
                case CasterMove.Retreat:
                    closeTimer += Time.deltaTime;
                    body.SimpleMove(-dir * (moveSpeed * EnemyVariantRules.CasterRetreatSpeedFactor * speed));
                    break;
                default:
                    closeTimer = 0f;
                    break;
            }
            if (timer <= 0f && EnemyVariantRules.CasterMayCast(distance, closeTimer))
                BeginTelegraph();
        }

        /// <summary>Tests: start winding up the first hand card now.</summary>
        internal void ForceTelegraph() => BeginTelegraph();

        private void BeginTelegraph()
        {
            var cycle = deck.Cycle;
            if (TurnHook != null && TurnHook.TryTakeTurn())
            {
                state = State.Recover;
                timer = recoverSeconds;
                return;
            }

            telegraphSlot = -1;
            if (TurnHook != null) telegraphSlot = cycle.OldestSlot; // the boss plays in draw order (spaced 9s)
            else
                for (int i = 0; i < DeckCycle.HandSize; i++)
                    if (cycle.Hand[i] != null) { telegraphSlot = i; break; }

            if (telegraphSlot < 0)
            {
                state = State.Recover;
                timer = recoverSeconds;
                // Out of cards: mobs reload at once; the boss opens its punishable reload window instead.
                if (TurnHook != null)
                {
                    TurnHook.OnOutOfCards();
                    return;
                }
                cycle.Reload();
                return;
            }

            CurrentTelegraph = cycle.Hand[telegraphSlot];
            state = State.Telegraph;
            status.FlinchArmor = true; // only a card clash can stop the wind-up
            timer = telegraphSeconds;
            TelegraphStarted?.Invoke(CurrentTelegraph);
        }

        private void Swing(Vector3 toTarget)
        {
            EndTelegraph();
            if (deck.Cycle.TryPlay(telegraphSlot, out var card))
            {
                var payload = AttackPayload.FromCard(card, status.OwnerId, Team.Enemy, Time.frameCount);
                combat.Spawn(payload, toTarget, card);
                Swung?.Invoke(card);
            }
            state = State.Recover;
            timer = recoverSeconds;
        }

        /// <summary>
        /// <paramref name="incoming"/> reached our body mid wind-up: clash it with the telegraphed card. A higher
        /// card breaks the anticipated attack, an equal one cancels both, a lower one is broken and we still swing.
        /// Pokes and 0-cards (either side) don't clash: a 0 only answers a live swing.
        /// </summary>
        internal void ResolveTelegraphClash(AttackHitbox incoming)
        {
            if (TurnHook != null && TurnHook.IsBusy && TurnHook.ResolveBusyClash(incoming)) return;
            var telegraph = CurrentTelegraph;
            if (state != State.Telegraph || telegraph == null || incoming == null || !incoming.IsLive) return;
            var payload = incoming.Payload;
            if (payload.Team == Team.Enemy || payload.IsPoke || payload.IsZeroBreaker || telegraph.IsZeroBreaker) return;

            int slot = telegraphSlot;
            var anticipated = AttackPayload.FromCard(telegraph, status.OwnerId, Team.Enemy, Time.frameCount);
            var result = ClashSystem.ResolveTelegraph(incoming, combat, anticipated);
            if (result.Outcome != ClashOutcome.AWins && result.Outcome != ClashOutcome.Neutral) return;

            // The anticipated card is spent (a win's break already ended the telegraph via Broken).
            deck.Cycle.TryPlay(slot, out _);
            CancelTelegraph();
        }

        private void CancelTelegraph()
        {
            if (state != State.Telegraph) return;
            EndTelegraph();
            state = State.Recover;
            timer = recoverSeconds;
        }

        private void EndTelegraph()
        {
            CurrentTelegraph = null;
            status.FlinchArmor = false;
            TelegraphEnded?.Invoke();
        }

        private void HandleDied()
        {
            status.FlinchArmor = false;
            CancelTelegraph();
            combat.CancelActiveAction();
            closeTimer = 0f;
            timer = respawnDelay;
        }

        private void Respawn()
        {
            body.enabled = false; // CharacterController overrides direct position writes
            transform.position = spawnPosition;
            body.enabled = true;
            status.Revive();
            deck.Reinitialize();
            closeTimer = 0f;
            state = State.Recover;
            timer = recoverSeconds;
        }
    }
}
