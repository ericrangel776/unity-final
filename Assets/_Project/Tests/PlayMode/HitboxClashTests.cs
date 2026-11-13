using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Local hitbox-vs-hitbox clashes and body hits in a live physics scene.</summary>
    public class HitboxClashTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        // Combatants have no body collider, so only their hitboxes can touch.
        private CombatActionController MakeCombatant(Team team, Vector3 position)
        {
            var go = new GameObject($"{team}_combatant");
            spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CombatantStatus>().Team = team;
            return go.AddComponent<CombatActionController>();
        }

        private static AttackPayload Payload(CombatActionController c, int value, int tick, bool zero = false) =>
            new AttackPayload(c.Status.OwnerId, c.Status.Team, value, zero, false, tick, CardShape.Strike, Element.Physical);

        private static IEnumerator Physics(int steps = 3)
        {
            for (int i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        // Player at origin swinging +Z, enemy 2.2 m ahead swinging -Z: the two strike boxes overlap.
        private (CombatActionController player, CombatActionController enemy) FacingPair()
        {
            return (MakeCombatant(Team.Player, Vector3.zero), MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 2.2f)));
        }

        [UnityTest]
        public IEnumerator HigherCard_BreaksLower_CancelsLoserHitboxes_AndStunsLoser()
        {
            var (player, enemy) = FacingPair();
            enemy.Spawn(Payload(enemy, 5, tick: 1), Vector3.back);
            player.Spawn(Payload(player, 7, tick: 2), Vector3.forward);

            yield return Physics();

            Assert.AreEqual(0, enemy.ActiveHitboxCount, "Loser's hitboxes should be cancelled.");
            Assert.IsTrue(enemy.Status.IsStunned, "Loser should be stunned.");
            Assert.AreEqual(1, player.ActiveHitboxCount, "Winner's swing should continue.");
            Assert.IsFalse(player.Status.IsStunned);
        }

        [UnityTest]
        public IEnumerator EqualCards_CancelBoth_WithoutStun()
        {
            var (player, enemy) = FacingPair();
            enemy.Spawn(Payload(enemy, 6, tick: 1), Vector3.back);
            player.Spawn(Payload(player, 6, tick: 1), Vector3.forward);

            yield return Physics();

            Assert.AreEqual(0, enemy.ActiveHitboxCount);
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.IsFalse(enemy.Status.IsStunned);
            Assert.IsFalse(player.Status.IsStunned);
        }

        [UnityTest]
        public IEnumerator Zero_PlayedAfterNine_BreaksIt_AndDeletesCardFromEnemyCycle()
        {
            var (player, enemy) = FacingPair();
            var nine = CardData.Create("enemy_nine", 9);
            var filler = CardData.Create("enemy_filler", 3);
            spawned.Add(nine);
            spawned.Add(filler);

            var enemyDeck = enemy.gameObject.AddComponent<DeckRuntimeManager>();
            enemyDeck.SetStartingDeck(new[] { nine, filler, filler }, newSeed: 1);
            int nineSlot = enemyDeck.Cycle.Hand[0].Data == nine ? 0 : enemyDeck.Cycle.Hand[1].Data == nine ? 1 : 2;
            Assert.IsTrue(enemyDeck.Cycle.TryPlay(nineSlot, out var nineCard));

            enemy.Spawn(AttackPayload.FromCard(nineCard, enemy.Status.OwnerId, Team.Enemy, 10), Vector3.back, nineCard);
            player.Spawn(Payload(player, 0, tick: 11, zero: true), Vector3.forward);

            yield return Physics();

            Assert.IsTrue(enemy.Status.IsStunned);
            Assert.AreEqual(0, enemy.ActiveHitboxCount);
            Assert.AreEqual(1, enemyDeck.Cycle.DeletedThisCycleCount, "A 0-break deletes the card from the current cycle.");
            Assert.AreEqual(0, enemyDeck.Cycle.DiscardCount);
        }

        [UnityTest]
        public IEnumerator Zero_PlayedFirst_IsBroken()
        {
            var (player, enemy) = FacingPair();
            player.Spawn(Payload(player, 0, tick: 5, zero: true), Vector3.forward);
            enemy.Spawn(Payload(enemy, 2, tick: 6), Vector3.back);

            yield return Physics();

            Assert.IsTrue(player.Status.IsStunned);
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.IsFalse(enemy.Status.IsStunned);
        }

        [UnityTest]
        public IEnumerator Hitbox_ExpiresAndUnregisters_WhenDurationEnds()
        {
            var player = MakeCombatant(Team.Player, Vector3.zero);
            player.Spawn(Payload(player, 4, tick: 1), Vector3.forward);
            Assert.AreEqual(1, player.ActiveHitboxCount);

            float end = Time.time + 1f;
            while (player.ActiveHitboxCount > 0 && Time.time < end) yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, player.ActiveHitboxCount, "Expired hitboxes must not linger.");
        }

        private CombatantStatus MakeBody(Team team, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spawned.Add(go);
            go.transform.position = position;
            var status = go.AddComponent<CombatantStatus>();
            status.Team = team;
            return status;
        }

        [UnityTest]
        public IEnumerator BodyHit_DealsDamage()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 1.5f));
            enemy.Spawn(Payload(enemy, 5, tick: 1), Vector3.back);

            yield return Physics();

            Assert.AreEqual(body.MaxHp - 5 * CombatMath.DamagePerPoint, body.Hp);
        }

        [UnityTest]
        public IEnumerator BodyHit_DuringIFrames_DealsNoDamage()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            body.IsInvulnerable = true;
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 1.5f));
            enemy.Spawn(Payload(enemy, 5, tick: 1), Vector3.back);

            yield return Physics();

            Assert.AreEqual(body.MaxHp, body.Hp);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator EnemyWindup_WithoutParry_BodyHitLandsAfterDelay()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 1.5f));
            enemy.BodyHitDelay = 0.3f;
            enemy.Spawn(Payload(enemy, 9, tick: 10), Vector3.back);

            yield return Physics();
            Assert.AreEqual(body.MaxHp, body.Hp, "No damage during the windup.");

            yield return WaitSeconds(0.4f);
            Assert.AreEqual(body.MaxHp - 9 * CombatMath.DamagePerPoint, body.Hp);
        }

        [UnityTest]
        public IEnumerator Zero_PlayedDuringEnemyWindup_PreventsBodyDamage()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            var player = MakeCombatant(Team.Player, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 1.5f));
            enemy.BodyHitDelay = 0.3f;
            enemy.Spawn(Payload(enemy, 9, tick: 10), Vector3.back); // already overlaps the body

            yield return Physics(2);
            player.Spawn(Payload(player, 0, tick: 11, zero: true), Vector3.forward);
            yield return WaitSeconds(0.5f);

            Assert.AreEqual(body.MaxHp, body.Hp, "A timely 0-parry must stop the hit.");
            Assert.IsTrue(enemy.Status.IsStunned);
            Assert.AreEqual(0, enemy.ActiveHitboxCount);
        }

        [UnityTest]
        public IEnumerator ClashLoser_OverlappingBody_DealsNoDamage()
        {
            // Enemy's losing box touches the player's body in the same step it clashes.
            var body = MakeBody(Team.Player, Vector3.zero);
            var player = MakeCombatant(Team.Player, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 1.5f));
            player.Spawn(Payload(player, 7, tick: 1), Vector3.forward);
            enemy.Spawn(Payload(enemy, 3, tick: 2), Vector3.back);

            yield return Physics();

            Assert.IsTrue(enemy.Status.IsStunned);
            Assert.AreEqual(body.MaxHp, body.Hp, "A hitbox that lost its clash must not deal damage.");
        }

        [UnityTest]
        public IEnumerator BodyHit_PushesVictimAwayFromAttacker()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 1.5f));
            Vector3? pushed = null;
            Vector3 velocityAtHit = Vector3.zero;
            body.HitTaken += (_, dir) => { pushed = dir; velocityAtHit = body.KnockbackVelocity; };
            enemy.Spawn(Payload(enemy, 5, tick: 1), Vector3.back);

            yield return Physics();

            Assert.IsTrue(pushed.HasValue, "A damaging body hit should raise HitTaken.");
            Assert.Greater(Vector3.Dot(pushed.Value, Vector3.back), 0.99f, "Push must point away from the attacker (+Z).");
            Assert.AreEqual(CombatMath.KnockbackSpeed(Payload(enemy, 5, tick: 1)), velocityAtHit.magnitude, 0.001f);
            Assert.Greater(Vector3.Dot(velocityAtHit, Vector3.back), 0f);
        }

        private static AttackPayload EnemyHit(int value, bool zero = false) =>
            new AttackPayload(1, Team.Enemy, value, zero, false, 0, CardShape.Strike, Element.Physical);

        [UnityTest]
        public IEnumerator BodyHit_FlinchesVictim()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            int flinches = 0;
            body.Flinched += () => flinches++;

            Assert.IsTrue(body.TakeHit(EnemyHit(4), Vector3.back));

            Assert.IsTrue(body.IsStunned, "A damaging hit should flinch (short stun).");
            Assert.AreEqual(1, flinches);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SecondHit_WithinFlinchCooldown_DamagesButDoesNotFlinchAgain()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            int flinches = 0;
            body.Flinched += () => flinches++;

            body.TakeHit(EnemyHit(1), Vector3.back);
            body.TakeHit(EnemyHit(1), Vector3.back);

            Assert.AreEqual(1, flinches, "Flinch cooldown should block a second flinch (no poke stunlock).");
            Assert.AreEqual(body.MaxHp - 2 * CombatMath.DamagePerPoint, body.Hp, "Both hits still deal damage.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ZeroCardBodyHit_CausesNoReaction()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            bool reacted = false;
            body.HitTaken += (_, __) => reacted = true;

            body.TakeHit(EnemyHit(0, zero: true), Vector3.back);

            Assert.IsFalse(reacted);
            Assert.IsFalse(body.IsStunned);
            Assert.AreEqual(Vector3.zero, body.KnockbackVelocity);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BodyHit_DuringIFrames_CausesNoKnockbackOrFlinch()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            body.IsInvulnerable = true;

            Assert.IsFalse(body.TakeHit(EnemyHit(6), Vector3.back));

            Assert.IsFalse(body.IsStunned);
            Assert.AreEqual(Vector3.zero, body.KnockbackVelocity);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BodyHit_FlashesRenderers()
        {
            var body = MakeBody(Team.Player, Vector3.zero);
            var flash = body.gameObject.AddComponent<HitFlash>();

            body.TakeHit(EnemyHit(3), Vector3.back);

            Assert.IsTrue(flash.IsFlashing);
            float end = Time.realtimeSinceStartup + 1f;
            while (flash.IsFlashing && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsFalse(flash.IsFlashing, "Flash should clear on its own.");
        }

        [UnityTest]
        public IEnumerator BodyHit_SameTeam_DealsNoDamage()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 1.5f));
            enemy.Spawn(Payload(enemy, 5, tick: 1), Vector3.back);

            yield return Physics();

            Assert.AreEqual(body.MaxHp, body.Hp);
        }
    }
}
