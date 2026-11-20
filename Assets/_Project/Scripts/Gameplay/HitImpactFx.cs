using DeckArpg.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// World-space juice for plain damaging body hits (docs/specs/hit-juice.md): a small spark burst in the
    /// attack's element colour on every hit, and a camera kick when the player is the one hurt. Runs on
    /// unscaled time so the sparks fly during the hit-stop freeze. Lives on its own object so its impulse
    /// source is never confused with <see cref="ClashImpactFx"/>'s.
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public sealed class HitImpactFx : MonoBehaviour
    {
        public const int PokeSparks = 6;
        public const int CardSparks = 10;
        /// <summary>Effect damage (burn tick, chain jump, finisher): a small puff and no camera kick.</summary>
        public const int EffectSparks = 3;
        /// <summary>A Lightning chain link: a bigger burst than other effect damage, so each jolt reads.</summary>
        public const int ChainSparks = 14;
        public const float PlayerHurtShake = 0.3f;

        [Tooltip("Additive particle material. Created at runtime if left empty.")]
        [SerializeField] private Material particleMaterial;

        private CinemachineImpulseSource impulse;
        private ParticleSystem sparks;

        public Material ParticleMaterial { get => particleMaterial; set => particleMaterial = value; }
        public int LastSparkCount { get; private set; }
        public Color LastSparkColor { get; private set; }
        /// <summary>How many player-hurt camera kicks have fired (for tests).</summary>
        public int PlayerHurtImpulses { get; private set; }

        private void Awake()
        {
            impulse = GetComponent<CinemachineImpulseSource>();
            var def = impulse.ImpulseDefinition;
            def.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
            def.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            def.ImpulseDuration = 0.2f;

            var material = particleMaterial != null ? particleMaterial : ClashImpactFx.CreateParticleMaterial();
            sparks = ClashImpactFx.CreateSystem(transform, "HitSparks", material, maxParticles: 128);
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.gravityModifier = 1.2f;
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
        }

        // Shake during the freeze, not after it (same as ClashImpactFx).
        private void Start() => CinemachineImpulseManager.Instance.IgnoreTimeScale = true;

        private void OnEnable() => CombatantStatus.AnyHitLanded += HandleHit;
        private void OnDisable() => CombatantStatus.AnyHitLanded -= HandleHit;

        private void HandleHit(HitInfo hit)
        {
            int count = hit.IsEffect
                ? (hit.Victim != null && hit.Victim.LastHitElement == Element.Lightning ? ChainSparks : EffectSparks)
                : hit.Payload.IsPoke ? PokeSparks : CardSparks;
            var color = ClashImpactFx.Opaque(ElementColors.For(hit.Payload));
            ClashImpactFx.Emit(sparks, hit.Position, color, count);
            LastSparkCount = count;
            LastSparkColor = color;

            if (hit.IsEffect || hit.Victim == null || hit.Victim.Team != Team.Player) return;
            impulse.GenerateImpulseAtPositionWithVelocity(hit.Position, Random.onUnitSphere * PlayerHurtShake);
            PlayerHurtImpulses++;
        }
    }
}
