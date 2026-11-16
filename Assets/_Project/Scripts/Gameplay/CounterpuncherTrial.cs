using System.Collections;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Counterpuncher elite trial: when the watched opponent attacks within range, answer with a 0-card on the
    /// next frame (so its activation tick is strictly later and it breaks the attack). Costs come from
    /// <see cref="CounterRules"/>. See docs/specs/m3-elite-trials.md.
    /// </summary>
    [RequireComponent(typeof(TrainingDummyEnemy))]
    public sealed class CounterpuncherTrial : MonoBehaviour
    {
        [Tooltip("Ground-plane distance (centre to centre) inside which attacks get answered.")]
        [SerializeField] private float counterRange = 3.5f;

        private TrainingDummyEnemy enemy;
        private CombatActionController combat;
        private CombatActionController watched;
        private int lastSleightTick = int.MinValue;

        /// <summary>0-cards actually fired at the opponent.</summary>
        public int CountersFired { get; private set; }
        public float CounterRange { get => counterRange; set => counterRange = Mathf.Max(0f, value); }

        private void Awake()
        {
            enemy = GetComponent<TrainingDummyEnemy>();
            combat = GetComponent<CombatActionController>();
        }

        /// <summary>Starts answering <paramref name="opponent"/>'s attacks (replaces any previous one).</summary>
        public void Watch(CombatActionController opponent)
        {
            Unwatch();
            watched = opponent;
            if (watched != null) watched.HitboxSpawned += HandleOpponentAttack;
        }

        private void OnDestroy() => Unwatch();

        private void Unwatch()
        {
            if (watched != null) watched.HitboxSpawned -= HandleOpponentAttack;
            watched = null;
        }

        private void HandleOpponentAttack(AttackHitbox attack)
        {
            var payload = attack.Payload;
            if (payload.IsSleight)
            {
                // Every hit of a multi-hit Sleight shares its tick: decide once per Sleight.
                if (payload.ActivationTick == lastSleightTick) return;
                lastSleightTick = payload.ActivationTick;
            }

            var status = enemy.Status;
            if (status.IsDead || status.IsStunned) return;
            var offset = attack.Owner.transform.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > counterRange) return;

            var cycle = enemy.Deck.Cycle;
            int cost = CounterRules.ZerosToSpend(payload.IsSleight, cycle.ZerosReady);
            if (cost == 0) return;

            // Spend now so a second attack this frame can't count the same 0s.
            cycle.TryPlayParry(out var zero);
            for (int i = 1; i < cost; i++) cycle.TryPlayParry(out _);
            StartCoroutine(FireNextFrame(zero, attack));
        }

        private IEnumerator FireNextFrame(RuntimeCardInstance zero, AttackHitbox attack)
        {
            yield return null;
            var status = enemy.Status;
            if (attack == null || !attack.IsLive || status.IsDead || status.IsStunned) yield break; // wasted: baited
            var aim = attack.Owner.transform.position - transform.position;
            combat.Spawn(AttackPayload.FromCard(zero, status.OwnerId, Team.Enemy, Time.frameCount), aim, zero);
            CountersFired++;
        }
    }
}
