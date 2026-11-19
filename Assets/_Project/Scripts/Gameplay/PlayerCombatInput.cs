using DeckArpg.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeckArpg.Gameplay
{
    public enum CombatCommand
    {
        PlaySlot1,
        PlaySlot2,
        PlaySlot3,
        Parry,
        Poke,
        Reload,
        StockSlot1,
        StockSlot2,
        StockSlot3,
        StockParry,
        Overburn
    }

    /// <summary>
    /// Reads the project-wide Player actions. Card commands go through a 0.2 s buffer so presses made
    /// during recovery or hit-stop still fire; movement, aim and dash are read directly.
    /// Holding Stock turns slot/Parry presses into Sleight stocks and Reload into an Overburn. While the
    /// Active Reload bar runs, card presses are dropped and a Reload press is the (unbuffered) timing press.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(CombatActionController), typeof(DeckRuntimeManager))]
    public sealed class PlayerCombatInput : MonoBehaviour
    {
        [Tooltip("Minimum time between card plays; presses during it wait in the input buffer.")]
        [SerializeField] private float actionRecovery = 0.25f;
        [SerializeField] private float respawnDelay = 1.5f;
        [Tooltip("Off in a run: death ends the run instead of reviving here.")]
        [SerializeField] private bool respawnOnDeath = true;

        private readonly InputBuffer<CombatCommand> buffer = new InputBuffer<CombatCommand>();
        private PlayerMotor motor;
        private CombatActionController combat;
        private DeckRuntimeManager deck;
        private CombatantStatus status;
        private ElementStatusEffects elementFx;
        private InputAction move, aim, dash, slot1, slot2, slot3, parry, poke, reload, stock;
        private float recoveryRemaining;
        private float respawnRemaining;
        private float actionSpeedMultiplier = 1f;

        public bool RespawnOnDeath { get => respawnOnDeath; set => respawnOnDeath = value; }
        /// <summary>Divides the action lock after each play (Ashen Sigil). 1 = unchanged; values below 1 are raised to 1.</summary>
        public float ActionSpeedMultiplier { get => actionSpeedMultiplier; set => actionSpeedMultiplier = Mathf.Max(1f, value); }
        public float ActionRecoverySeconds => actionRecovery / actionSpeedMultiplier;
        /// <summary>Iron Will Idol: the next Sleight fired is zero-proof. Consumed when it fires.</summary>
        public bool NextSleightZeroProof { get; set; }
        /// <summary>
        /// While true (a run screen is open) the player can't move, dash, aim, reload or play cards, and buffered
        /// presses are dropped. Number keys double as menu keys, so they must not reach the card slots.
        /// Only <c>RunScreens</c> writes this (one owner).
        /// </summary>
        public bool InputBlocked { get; set; }

        /// <summary>Between rooms: drops buffered inputs, cancels live hitboxes and any pending respawn.</summary>
        public void ResetForNewRoom()
        {
            buffer.Clear();
            recoveryRemaining = 0f;
            respawnRemaining = 0f;
            combat.CancelActiveAction();
        }

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            combat = GetComponent<CombatActionController>();
            deck = GetComponent<DeckRuntimeManager>();
            status = GetComponent<CombatantStatus>();
            status.Team = Team.Player;

            var map = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player") : null;
            if (map == null)
            {
                Debug.LogError("PlayerCombatInput: project-wide 'Player' action map not found.", this);
                enabled = false;
                return;
            }
            move = map.FindAction("Move");
            aim = map.FindAction("Aim");
            dash = map.FindAction("Dash");
            slot1 = map.FindAction("PlaySlot1");
            slot2 = map.FindAction("PlaySlot2");
            slot3 = map.FindAction("PlaySlot3");
            parry = map.FindAction("Parry");
            poke = map.FindAction("Poke");
            reload = map.FindAction("Reload");
            stock = map.FindAction("Stock");
            map.Enable();
        }

        private void OnEnable()
        {
            if (status != null) status.Died += HandleDied;
        }

        private void OnDisable()
        {
            if (status != null) status.Died -= HandleDied;
        }

        private void Update()
        {
            if (respawnRemaining > 0f)
            {
                respawnRemaining -= Time.deltaTime;
                if (respawnRemaining <= 0f) Respawn();
                return;
            }
            // Without auto-respawn (a run) a dead player stays down until the run restarts.
            if (status.IsDead)
            {
                motor.SetMoveInput(Vector2.zero);
                return;
            }

            if (InputBlocked)
            {
                motor.SetMoveInput(Vector2.zero);
                buffer.Clear();
                return;
            }

            // Before movement/dash: a dash during the frozen Deadlock would grant i-frames through its result.
            if (RouteToDeadlock()) return;

            motor.SetMoveInput(move.ReadValue<Vector2>());
            // Chill multiplies with the reload boost. Dash, i-frames and the reload bar are untouched.
            if (elementFx == null) elementFx = GetComponent<ElementStatusEffects>();
            motor.MoveSpeedMultiplier = deck.Reload.SpeedMultiplier * (elementFx != null ? elementFx.SpeedMultiplier : 1f);
            ReadAim();
            if (dash.WasPressedThisFrame()) motor.TryDash();

            float now = Time.unscaledTime;
            bool stockHeld = stock != null && stock.IsPressed();
            ReadReload(stockHeld, now);
            // Card presses during the reload bar are dropped, not held for after it.
            if (!deck.Reload.LocksCardPlays)
            {
                Buffer(slot1, stockHeld ? CombatCommand.StockSlot1 : CombatCommand.PlaySlot1, now);
                Buffer(slot2, stockHeld ? CombatCommand.StockSlot2 : CombatCommand.PlaySlot2, now);
                Buffer(slot3, stockHeld ? CombatCommand.StockSlot3 : CombatCommand.PlaySlot3, now);
                Buffer(parry, stockHeld ? CombatCommand.StockParry : CombatCommand.Parry, now);
            }
            Buffer(poke, CombatCommand.Poke, now);

            if (recoveryRemaining > 0f) recoveryRemaining -= Time.deltaTime;

            // Hold buffered commands while frozen, stunned, recovering, or mid-dash.
            bool canAct = Time.timeScale > 0f && !status.IsStunned && !status.IsDead && !motor.IsDashing
                          && recoveryRemaining <= 0f;
            if (canAct && buffer.TryDequeue(now, out var command) && Execute(command))
                recoveryRemaining = ActionRecoverySeconds;
        }

        /// <summary>
        /// While a Deadlock runs, Poke and slot presses are its input (one press counts) and are never
        /// buffered, so they can't also fire as card plays after it. Other presses are dropped.
        /// </summary>
        private bool RouteToDeadlock()
        {
            var deadlock = DeadlockController.Current;
            if (deadlock == null || !deadlock.IsActive) return false;

            if (Pressed(poke)) deadlock.SubmitPlayer(DeadlockChoice.Poke);
            else if (Pressed(slot1)) deadlock.SubmitPlayer(DeadlockChoice.Slot1);
            else if (Pressed(slot2)) deadlock.SubmitPlayer(DeadlockChoice.Slot2);
            else if (Pressed(slot3)) deadlock.SubmitPlayer(DeadlockChoice.Slot3);
            return true;
        }

        /// <summary>
        /// The timing press is graded the frame it happens (buffering would skew it). Starting a reload
        /// is buffered like any action; with Stock held it starts an Overburn instead.
        /// </summary>
        private void ReadReload(bool stockHeld, float now)
        {
            if (!Pressed(reload)) return;
            switch (deck.Reload.State)
            {
                case ReloadState.Running:
                    deck.Reload.Press();
                    break;
                case ReloadState.Idle:
                    buffer.Enqueue(stockHeld ? CombatCommand.Overburn : CombatCommand.Reload, now);
                    break;
                // Jammed: presses are ignored.
            }
        }

        private static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();

        private void Buffer(InputAction action, CombatCommand command, float now)
        {
            if (action != null && action.WasPressedThisFrame()) buffer.Enqueue(command, now);
        }

        private void ReadAim()
        {
            if (aim == null) return;
            var control = aim.activeControl;
            if (control != null && control.device is Gamepad) motor.AimWithStick(aim.ReadValue<Vector2>());
            else if (Mouse.current != null) motor.AimAtScreenPoint(Mouse.current.position.ReadValue(), Camera.main);
        }

        /// <summary>Runs one combat command. Returns true if something was actually played.</summary>
        public bool Execute(CombatCommand command)
        {
            int tick = Time.frameCount;
            RuntimeCardInstance card;
            if (deck.Reload.LocksCardPlays && IsCardCommand(command)) return false;
            switch (command)
            {
                case CombatCommand.StockSlot1:
                case CombatCommand.StockSlot2:
                case CombatCommand.StockSlot3:
                    return HandleStock(deck.Cycle.TryStock((int)command - (int)CombatCommand.StockSlot1, out var sleight),
                        sleight, tick);
                case CombatCommand.StockParry:
                    return HandleStock(deck.Cycle.TryStockParry(out var parrySleight), parrySleight, tick);
                case CombatCommand.Overburn:
                    return deck.Reload.BeginOverburn(status);
                case CombatCommand.PlaySlot1:
                case CombatCommand.PlaySlot2:
                case CombatCommand.PlaySlot3:
                    if (!deck.Cycle.TryPlay((int)command - (int)CombatCommand.PlaySlot1, out card)) return false;
                    combat.Spawn(AttackPayload.FromCard(card, status.OwnerId, Team.Player, tick), motor.AimDirection, card);
                    return true;
                case CombatCommand.Parry:
                    if (!deck.Cycle.TryPlayParry(out card)) return false;
                    combat.Spawn(AttackPayload.FromCard(card, status.OwnerId, Team.Player, tick), motor.AimDirection, card);
                    return true;
                case CombatCommand.Poke:
                    combat.Spawn(AttackPayload.Poke(status.OwnerId, Team.Player, tick), motor.AimDirection);
                    return true;
                case CombatCommand.Reload:
                    // Buffered Reload only starts the bar. The timing press is graded unbuffered in ReadReload,
                    // so a double-tap during recovery can't land as an instant Miss/jam.
                    return deck.Reload.Begin();
                default:
                    return false;
            }
        }

        private static bool IsCardCommand(CombatCommand command) =>
            command != CombatCommand.Poke && command != CombatCommand.Reload && command != CombatCommand.Overburn;

        /// <summary>A stock that completes the Sleight fires it: each delivery has its own attack, Strike also lunges.</summary>
        private bool HandleStock(StockResult result, in SleightResult sleight, int tick)
        {
            if (result == StockResult.Rejected) return false;
            if (result == StockResult.Fired)
            {
                var payload = AttackPayload.FromSleight(sleight, status.OwnerId, Team.Player, tick,
                    SleightRules.HitCount(sleight.Delivery), NextSleightZeroProof);
                NextSleightZeroProof = false;
                combat.SpawnSleight(payload, motor.AimDirection);
                if (sleight.Delivery == Delivery.Strike) motor.TryLunge(motor.AimDirection);
            }
            return true;
        }

        private void HandleDied()
        {
            deck.Reload.Cancel();
            combat.CancelActiveAction();
            buffer.Clear();
            if (respawnOnDeath) respawnRemaining = respawnDelay;
        }

        private void Respawn()
        {
            status.Revive();
            deck.Reinitialize();
        }
    }
}
