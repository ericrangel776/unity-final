using System;
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

        [SerializeField] private Transform target;
        [SerializeField] private float moveSpeed = 2.5f;
        [SerializeField] private float attackRange = 2.2f;
        [SerializeField] private float telegraphSeconds = 0.7f;
        [SerializeField] private float recoverSeconds = 1.2f;
        [SerializeField] private float respawnDelay = 2f;

        private CharacterController body;
        private CombatActionController combat;
        private DeckRuntimeManager deck;
        private CombatantStatus status;
        private State state;
        private float timer;
        private int telegraphSlot = -1;
        private Vector3 spawnPosition;

        /// <summary>Card currently being telegraphed, or null.</summary>
        public RuntimeCardInstance CurrentTelegraph { get; private set; }
        /// <summary>0..1 progress through the telegraph wind-up.</summary>
        public float TelegraphProgress => state == State.Telegraph ? 1f - Mathf.Clamp01(timer / telegraphSeconds) : 0f;
        public CombatantStatus Status => status;
        public DeckRuntimeManager Deck => deck;

        public event Action<RuntimeCardInstance> TelegraphStarted;
        public event Action TelegraphEnded;

        public Transform Target { get => target; set => target = value; }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            combat = GetComponent<CombatActionController>();
            deck = GetComponent<DeckRuntimeManager>();
            status = GetComponent<CombatantStatus>();
            status.Team = Team.Enemy;
            spawnPosition = transform.position;
        }

        private void OnEnable()
        {
            status.Died += HandleDied;
            status.Broken += CancelTelegraph;
        }

        private void OnDisable()
        {
            status.Died -= HandleDied;
            status.Broken -= CancelTelegraph;
        }

        private void Start()
        {
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
                if (timer <= 0f) Respawn();
                return;
            }
            if (status.IsStunned || target == null) return;

            var toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(toTarget);

            timer -= Time.deltaTime;
            switch (state)
            {
                case State.Approach:
                    if (toTarget.magnitude > attackRange * 0.8f)
                        body.SimpleMove(toTarget.normalized * moveSpeed);
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

        private void BeginTelegraph()
        {
            var cycle = deck.Cycle;
            telegraphSlot = -1;
            for (int i = 0; i < DeckCycle.HandSize; i++)
                if (cycle.Hand[i] != null) { telegraphSlot = i; break; }

            if (telegraphSlot < 0)
            {
                // Out of cards: reload, then come back. (Bosses get a punishable stagger in M4.)
                cycle.Reload();
                state = State.Recover;
                timer = recoverSeconds;
                return;
            }

            CurrentTelegraph = cycle.Hand[telegraphSlot];
            state = State.Telegraph;
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
            }
            state = State.Recover;
            timer = recoverSeconds;
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
            TelegraphEnded?.Invoke();
        }

        private void HandleDied()
        {
            CancelTelegraph();
            combat.CancelActiveAction();
            timer = respawnDelay;
        }

        private void Respawn()
        {
            body.enabled = false; // CharacterController overrides direct position writes
            transform.position = spawnPosition;
            body.enabled = true;
            status.Revive();
            deck.Reinitialize();
            state = State.Recover;
            timer = recoverSeconds;
        }
    }
}
