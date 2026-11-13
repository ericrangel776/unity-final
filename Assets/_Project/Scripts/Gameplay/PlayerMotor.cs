using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Camera-relative movement, aim, and an i-frame dash that is never gated by cards,
    /// so the player can always reposition while reloading.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(CombatantStatus))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float dashSpeed = 20f;
        [SerializeField] private float dashDuration = 0.15f;
        [SerializeField] private float dashCooldown = 0.5f;
        [SerializeField] private float gravity = -20f;

        private CharacterController controller;
        private CombatantStatus status;
        private Vector2 moveInput;
        private Vector3 dashDirection;
        private float dashRemaining;
        private float dashCooldownRemaining;
        private float verticalVelocity;

        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public bool IsDashing => dashRemaining > 0f;
        public float DashCooldownRemaining => dashCooldownRemaining;
        public float DashDuration => dashDuration;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            status = GetComponent<CombatantStatus>();
        }

        public void SetMoveInput(Vector2 input) => moveInput = Vector2.ClampMagnitude(input, 1f);

        /// <summary>Aims at the point under the cursor on the player's ground plane.</summary>
        public void AimAtScreenPoint(Vector2 screenPoint, Camera cam)
        {
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, transform.position);
            if (!plane.Raycast(ray, out float distance)) return;
            SetAimDirection(ray.GetPoint(distance) - transform.position);
        }

        /// <summary>Aims along a stick direction, relative to the camera.</summary>
        public void AimWithStick(Vector2 stick)
        {
            if (stick.sqrMagnitude < 0.09f) return;
            SetAimDirection(CameraRelative(stick));
        }

        public void SetAimDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) AimDirection = direction.normalized;
        }

        public bool TryDash()
        {
            if (IsDashing || dashCooldownRemaining > 0f || status.IsStunned || status.IsDead) return false;

            var dir = CameraRelative(moveInput);
            dashDirection = dir.sqrMagnitude > 0.01f ? dir.normalized : AimDirection;
            dashRemaining = dashDuration;
            dashCooldownRemaining = dashCooldown + dashDuration;
            status.IsInvulnerable = true;
            return true;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dashCooldownRemaining > 0f) dashCooldownRemaining -= dt;

            Vector3 horizontal;
            if (IsDashing)
            {
                horizontal = dashDirection * dashSpeed;
                dashRemaining -= dt;
                if (dashRemaining <= 0f) status.IsInvulnerable = false;
            }
            else if (status.IsStunned || status.IsDead)
            {
                horizontal = Vector3.zero;
            }
            else
            {
                horizontal = CameraRelative(moveInput) * moveSpeed;
            }
            if (!IsDashing) horizontal += status.KnockbackVelocity;

            verticalVelocity = controller.isGrounded ? -1f : verticalVelocity + gravity * dt;
            controller.Move((horizontal + Vector3.up * verticalVelocity) * dt);

            if (AimDirection.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(AimDirection);
        }

        private void OnDisable()
        {
            dashRemaining = 0f;
            if (status != null) status.IsInvulnerable = false;
        }

        private static Vector3 CameraRelative(Vector2 input)
        {
            var cam = Camera.main;
            if (cam == null) return new Vector3(input.x, 0f, input.y);
            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            return forward * input.y + right * input.x;
        }
    }
}
