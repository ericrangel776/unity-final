using DeckArpg.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Snaps the camera's field of view in on a Card Break (harder on a Zero Break), holds it while
    /// the hit-stop has time frozen, then eases back out. Runs on unscaled time so the zoom frames the
    /// freeze instead of waiting for it.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class CameraPunch : MonoBehaviour
    {
        [Tooltip("FOV reduction (degrees) on a Card Break.")]
        [SerializeField] private float cardBreakZoom = 3f;
        [Tooltip("FOV reduction (degrees) on a Zero Break.")]
        [SerializeField] private float zeroBreakZoom = 5f;
        [Tooltip("FOV reduction (degrees) held for a whole Deadlock.")]
        [SerializeField] private float deadlockZoom = 6f;
        [Tooltip("Minimum time held at full zoom, even without a freeze.")]
        [SerializeField] private float minHoldSeconds = 0.08f;
        [SerializeField] private float easeOutSeconds = 0.3f;

        private CinemachineCamera vcam;
        private float punchedFov;
        private float holdRemaining;
        private float easeElapsed;
        private bool active;

        public float BaseFov { get; private set; }
        public float CurrentFov => vcam.Lens.FieldOfView;

        private void Awake()
        {
            vcam = GetComponent<CinemachineCamera>();
            BaseFov = vcam.Lens.FieldOfView;
        }

        private void OnEnable()
        {
            ClashSystem.ClashOccurred += HandleClash;
            DeadlockController.Started += HandleDeadlock;
        }

        private void OnDisable()
        {
            ClashSystem.ClashOccurred -= HandleClash;
            DeadlockController.Started -= HandleDeadlock;
            if (active) SetFov(BaseFov);
            active = false;
        }

        private void HandleClash(ClashEvent e)
        {
            if (e.Result.Outcome != ClashOutcome.AWins && e.Result.Outcome != ClashOutcome.BWins) return;
            Punch(e.Result.BrokeByZero ? zeroBreakZoom : cardBreakZoom);
        }

        // Held for the whole Deadlock automatically: it keeps time frozen, and the zoom holds while frozen.
        private void HandleDeadlock(DeadlockController deadlock) => Punch(deadlockZoom);

        /// <summary>Zooms in by <paramref name="degrees"/>. A punch during a punch keeps the stronger zoom.</summary>
        public void Punch(float degrees)
        {
            if (degrees <= 0f) return;
            float target = BaseFov - degrees;
            punchedFov = active ? Mathf.Min(CurrentFov, target) : target;
            holdRemaining = minHoldSeconds;
            easeElapsed = 0f;
            active = true;
            SetFov(punchedFov);
        }

        private void LateUpdate()
        {
            if (!active) return;
            float dt = Time.unscaledDeltaTime;

            // Hold through the freeze, then for at least the minimum hold.
            if (Time.timeScale == 0f || holdRemaining > 0f)
            {
                holdRemaining -= dt;
                return;
            }

            easeElapsed += dt;
            float t = easeOutSeconds > 0f ? Mathf.Clamp01(easeElapsed / easeOutSeconds) : 1f;
            SetFov(Mathf.Lerp(punchedFov, BaseFov, Mathf.SmoothStep(0f, 1f, t)));
            if (t >= 1f) active = false;
        }

        private void SetFov(float fov)
        {
            var lens = vcam.Lens;
            lens.FieldOfView = fov;
            vcam.Lens = lens;
        }
    }
}
