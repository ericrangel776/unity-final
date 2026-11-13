using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The punish window: the first damaging hit right after a Card Break is a x1.5 counter.</summary>
    public class CounterTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private readonly List<(CombatantStatus victim, int damage)> counters = new List<(CombatantStatus, int)>();

        [SetUp]
        public void SetUp() => CombatantStatus.CounterHit += RecordCounter;

        [TearDown]
        public void TearDown()
        {
            CombatantStatus.CounterHit -= RecordCounter;
            counters.Clear();
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        private void RecordCounter(CombatantStatus victim, int damage) => counters.Add((victim, damage));

        private CombatantStatus MakeBody()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spawned.Add(go);
            var status = go.AddComponent<CombatantStatus>();
            status.Team = Team.Player;
            return status;
        }

        private static AttackPayload EnemyHit(int value, bool zero = false) =>
            new AttackPayload(1, Team.Enemy, value, zero, false, 0, CardShape.Strike, Element.Physical);

        private static int Normal(int value) => value * CombatMath.DamagePerPoint;

        [UnityTest]
        public IEnumerator HitInsideWindow_DealsCounterDamage_AndRaisesEvent()
        {
            var body = MakeBody();
            body.ApplyBreak();
            Assert.IsTrue(body.IsCounterWindowOpen);

            body.TakeHit(EnemyHit(4), Vector3.back);

            int expected = CombatMath.CounterDamage(Normal(4));
            Assert.AreEqual(body.MaxHp - expected, body.Hp);
            Assert.AreEqual(1, counters.Count);
            Assert.AreSame(body, counters[0].victim);
            Assert.AreEqual(expected, counters[0].damage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CounterConsumesWindow_SecondHitIsNormal()
        {
            var body = MakeBody();
            body.ApplyBreak();

            body.TakeHit(EnemyHit(4), Vector3.back);
            int hpAfterCounter = body.Hp;
            body.TakeHit(EnemyHit(4), Vector3.back);

            Assert.IsFalse(body.IsCounterWindowOpen);
            Assert.AreEqual(hpAfterCounter - Normal(4), body.Hp, "Only the first hit is a counter.");
            Assert.AreEqual(1, counters.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HitAfterWindowExpires_IsNormal()
        {
            var body = MakeBody();
            body.ApplyBreak();

            yield return new WaitForSeconds(1.1f);
            Assert.IsFalse(body.IsCounterWindowOpen);
            body.TakeHit(EnemyHit(4), Vector3.back);

            Assert.AreEqual(body.MaxHp - Normal(4), body.Hp);
            Assert.AreEqual(0, counters.Count);
        }

        [UnityTest]
        public IEnumerator HitWithoutBreak_IsNormal()
        {
            var body = MakeBody();
            body.TakeHit(EnemyHit(4), Vector3.back);

            Assert.AreEqual(body.MaxHp - Normal(4), body.Hp);
            Assert.AreEqual(0, counters.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ZeroCardHit_DoesNotSpendWindow()
        {
            var body = MakeBody();
            body.ApplyBreak();

            body.TakeHit(EnemyHit(0, zero: true), Vector3.back);

            Assert.IsTrue(body.IsCounterWindowOpen, "A 0-damage hit must not waste the counter.");
            Assert.AreEqual(0, counters.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator IFrameHit_DoesNotSpendWindow()
        {
            var body = MakeBody();
            body.ApplyBreak();
            body.IsInvulnerable = true;

            body.TakeHit(EnemyHit(4), Vector3.back);

            Assert.IsTrue(body.IsCounterWindowOpen);
            Assert.AreEqual(0, counters.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Revive_ClosesWindow()
        {
            var body = MakeBody();
            body.ApplyBreak();
            body.Revive();

            Assert.IsFalse(body.IsCounterWindowOpen);
            yield return null;
        }

        private static IEnumerator Physics(int steps = 4)
        {
            for (int i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        /// <summary>Player swinging +Z from the origin; enemy with a body collider close enough that
        /// the player's winning strike box also overlaps the enemy's body.</summary>
        private (CombatActionController player, CombatActionController enemy) BreakSetup()
        {
            var playerGo = new GameObject("Player");
            spawned.Add(playerGo);
            playerGo.AddComponent<CombatantStatus>().Team = Team.Player;
            var player = playerGo.AddComponent<CombatActionController>();

            var enemyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 1.8f);
            enemyGo.AddComponent<CombatantStatus>().Team = Team.Enemy;
            var enemy = enemyGo.AddComponent<CombatActionController>();
            return (player, enemy);
        }

        private static AttackPayload Swing(CombatActionController c, int value, int tick) =>
            new AttackPayload(c.Status.OwnerId, c.Status.Team, value, false, false, tick, CardShape.Strike, Element.Physical);

        [UnityTest]
        public IEnumerator WinningSwing_HittingTheLoser_IsNotACounter()
        {
            var (player, enemy) = BreakSetup();
            enemy.Spawn(Swing(enemy, 2, tick: 1), Vector3.back);
            player.Spawn(Swing(player, 9, tick: 2), Vector3.forward);

            yield return Physics();

            Assert.IsTrue(enemy.Status.IsStunned, "Setup: the enemy's card should be broken.");
            Assert.AreEqual(enemy.Status.MaxHp - Normal(9), enemy.Status.Hp,
                "Setup: the winning swing should still land as a normal hit.");
            Assert.AreEqual(0, counters.Count, "The swing that won the clash must not count as the follow-up.");
            Assert.IsTrue(enemy.Status.IsCounterWindowOpen, "The window stays open for a real follow-up.");
        }

        [UnityTest]
        public IEnumerator FollowUpAfterBreak_IsACounter()
        {
            var (player, enemy) = BreakSetup();
            enemy.Spawn(Swing(enemy, 2, tick: 1), Vector3.back);
            player.Spawn(Swing(player, 9, tick: 2), Vector3.forward);
            yield return Physics();
            int hpBefore = enemy.Status.Hp;

            // A second, separate attack (a new hitbox) lands inside the window.
            player.Spawn(Swing(player, 3, tick: 3), Vector3.forward);
            yield return Physics();

            Assert.AreEqual(1, counters.Count);
            Assert.AreEqual(hpBefore - CombatMath.CounterDamage(Normal(3)), enemy.Status.Hp);
        }

        [UnityTest]
        public IEnumerator CounterHit_ShowsCounterPopup_WithoutReplacingClashPopup()
        {
            var view = new GameObject("Feedback").AddComponent<ClashFeedbackView>();
            spawned.Add(view.gameObject);
            var body = MakeBody();
            body.ApplyBreak();

            body.TakeHit(EnemyHit(4), Vector3.back);

            Assert.AreEqual("COUNTER!", view.CounterText);
            Assert.IsNull(view.ShownText, "The counter uses its own popup, not the clash popup.");
            yield return null;
        }
    }
}
