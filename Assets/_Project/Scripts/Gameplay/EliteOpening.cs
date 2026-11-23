using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Elite punish window (docs/specs/elite-rework.md). A heavy card winds up a little longer (the tell); after the
    /// swing the Elite recovers longer and is Overextended: it takes extra HP and posture damage and is tinted gold.
    /// The window ends on its timer, at the next wind-up, or on death. Its timer runs on the Elite's own recover clock:
    /// paused while it is stunned (a posture break mid-window keeps the opening) and slowed by Chill.
    /// </summary>
    [RequireComponent(typeof(TrainingDummyEnemy))]
    public sealed class EliteOpening : MonoBehaviour
    {
        /// <summary>Bright gold: reads against every enemy body colour (the Elite is red; orange didn't stand out).</summary>
        public static readonly Color OverextendedColor = new Color(1f, 0.85f, 0.15f, 1f);

        private TrainingDummyEnemy enemy;
        private BodyTint tint;
        private ElementStatusEffects elementFx;
        private float remaining;

        public bool IsOverextended => remaining > 0f;
        public float RemainingSeconds => remaining;
        /// <summary>Overextended windows opened so far.</summary>
        public int WindowsOpened { get; private set; }

        private void Awake()
        {
            enemy = GetComponent<TrainingDummyEnemy>();
            tint = BodyTint.For(gameObject);
            elementFx = GetComponent<ElementStatusEffects>();
        }

        private void OnEnable()
        {
            enemy.TelegraphStarted += HandleTelegraphStarted;
            enemy.Swung += HandleSwung;
            enemy.Status.Died += Close;
        }

        private void OnDisable()
        {
            enemy.TelegraphStarted -= HandleTelegraphStarted;
            enemy.Swung -= HandleSwung;
            enemy.Status.Died -= Close;
            Close();
        }

        private void HandleTelegraphStarted(RuntimeCardInstance card)
        {
            Close();
            if (EliteRules.IsHeavy(card)) enemy.ExtendTelegraph(EliteRules.HeavyTelegraphBonusSeconds);
        }

        private void HandleSwung(RuntimeCardInstance card)
        {
            if (!EliteRules.IsHeavy(card)) return;
            enemy.ExtendNextRecover(EliteRules.OverextendSeconds);
            Open(enemy.RecoverSeconds + EliteRules.OverextendSeconds);
        }

        /// <summary>Opens (or restarts) the Overextended window for <paramref name="seconds"/>. Public for the debug menu.</summary>
        public void Open(float seconds)
        {
            if (enemy.Status.IsDead || seconds <= 0f) return;
            remaining = seconds;
            enemy.Status.DamageTakenMultiplier = EliteRules.OverextendDamageMultiplier;
            enemy.Status.PostureTakenMultiplier = EliteRules.OverextendDamageMultiplier;
            tint.Set(BodyTint.Layer.State, OverextendedColor, 0.85f);
            WindowsOpened++;
        }

        private void Update()
        {
            if (remaining <= 0f || enemy.Status.IsStunned) return;
            remaining -= Time.deltaTime * (elementFx != null ? elementFx.SpeedMultiplier : 1f);
            if (remaining <= 0f) Close();
        }

        private void Close()
        {
            remaining = 0f;
            if (enemy == null || enemy.Status == null) return;
            enemy.Status.DamageTakenMultiplier = 1f;
            enemy.Status.PostureTakenMultiplier = 1f;
            if (tint != null) tint.Clear(BodyTint.Layer.State);
        }
    }
}
