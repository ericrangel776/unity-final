using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Elite punish window (docs/specs/elite-rework.md). A heavy card winds up a little longer (the tell); after the
    /// swing the Elite recovers longer and is Overextended: it takes extra HP and posture damage and is tinted orange.
    /// The window ends on its timer, at the next wind-up, or on death.
    /// </summary>
    [RequireComponent(typeof(TrainingDummyEnemy))]
    public sealed class EliteOpening : MonoBehaviour
    {
        private TrainingDummyEnemy enemy;
        private BodyTint tint;
        private float remaining;

        public bool IsOverextended => remaining > 0f;
        public float RemainingSeconds => remaining;
        /// <summary>Overextended windows opened so far.</summary>
        public int WindowsOpened { get; private set; }

        private void Awake()
        {
            enemy = GetComponent<TrainingDummyEnemy>();
            tint = BodyTint.For(gameObject);
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
            tint.Set(BodyTint.Layer.State, BossStateFx.ExhaustedColor, 0.6f);
            WindowsOpened++;
        }

        private void Update()
        {
            if (remaining <= 0f) return;
            remaining -= Time.deltaTime;
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
