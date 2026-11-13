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
        Reload
    }

    /// <summary>
    /// Reads the project-wide Player actions. Card commands go through a 0.2 s buffer so presses made
    /// during recovery or hit-stop still fire; movement, aim and dash are read directly.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(CombatActionController), typeof(DeckRuntimeManager))]
    public sealed class PlayerCombatInput : MonoBehaviour
    {
        [Tooltip("Minimum time between card plays; presses during it wait in the input buffer.")]
        [SerializeField] private float actionRecovery = 0.25f;
        [SerializeField] private float respawnDelay = 1.5f;

        private readonly InputBuffer<CombatCommand> buffer = new InputBuffer<CombatCommand>();
        private PlayerMotor motor;
        private CombatActionController combat;
        private DeckRuntimeManager deck;
        private CombatantStatus status;
        private InputAction move, aim, dash, slot1, slot2, slot3, parry, poke, reload;
        private float recoveryRemaining;
        private float respawnRemaining;

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

            motor.SetMoveInput(move.ReadValue<Vector2>());
            ReadAim();
            if (dash.WasPressedThisFrame()) motor.TryDash();

            float now = Time.unscaledTime;
            Buffer(slot1, CombatCommand.PlaySlot1, now);
            Buffer(slot2, CombatCommand.PlaySlot2, now);
            Buffer(slot3, CombatCommand.PlaySlot3, now);
            Buffer(parry, CombatCommand.Parry, now);
            Buffer(poke, CombatCommand.Poke, now);
            Buffer(reload, CombatCommand.Reload, now);

            if (recoveryRemaining > 0f) recoveryRemaining -= Time.deltaTime;

            // Hold buffered commands while frozen, stunned, recovering, or mid-dash.
            bool canAct = Time.timeScale > 0f && !status.IsStunned && !status.IsDead && !motor.IsDashing
                          && recoveryRemaining <= 0f;
            if (canAct && buffer.TryDequeue(now, out var command) && Execute(command))
                recoveryRemaining = actionRecovery;
        }

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
            switch (command)
            {
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
                    return deck.Cycle.Reload();
                default:
                    return false;
            }
        }

        private void HandleDied()
        {
            combat.CancelActiveAction();
            buffer.Clear();
            respawnRemaining = respawnDelay;
        }

        private void Respawn()
        {
            status.Revive();
            deck.Reinitialize();
        }
    }
}
