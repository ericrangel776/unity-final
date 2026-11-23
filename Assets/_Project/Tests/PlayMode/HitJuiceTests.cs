using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Juice on plain damaging body hits (docs/specs/hit-juice.md): hit-stop, sparks, impulse, death pop.</summary>
    public class HitJuiceTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        private T Spawn<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go.AddComponent<T>();
        }

        private CombatantStatus Body(Team team)
        {
            var status = Spawn<CombatantStatus>($"{team}_body");
            status.Team = team;
            return status;
        }

        private static AttackPayload Card(int value, Team attacker, Element element = Element.Fire) =>
            new AttackPayload(1, attacker, value, false, false, 0, CardShape.Strike, element);

        /// <summary>Lands the hit and measures how long (real time) the game stayed frozen.</summary>
        private static IEnumerator MeasureFreeze(CombatantStatus victim, AttackPayload payload, System.Action<bool, float> result)
        {
            float start = Time.realtimeSinceStartup;
            victim.TakeHit(payload);
            bool froze = Time.timeScale == 0f;
            yield return TestWait.UntilRealtime(() => Time.timeScale > 0f, 1f);
            result(froze, Time.realtimeSinceStartup - start);
        }

        [UnityTest]
        public IEnumerator PlayerCardHit_FreezesBriefly_ThenRestoresTimeScale()
        {
            Spawn<HitStop>("HitStop");
            var enemy = Body(Team.Enemy);
            bool froze = false;
            float seconds = 0f;

            yield return MeasureFreeze(enemy, Card(9, Team.Player), (f, s) => { froze = f; seconds = s; });

            Assert.IsTrue(froze, "A damaging card hit should freeze time.");
            Assert.AreEqual(1f, Time.timeScale, "Time scale must be restored after the freeze.");
            Assert.GreaterOrEqual(seconds, HitJuiceRules.MaxCardHitStop * 0.9f);
        }

        [UnityTest]
        public IEnumerator Poke_NeverFreezes()
        {
            Spawn<HitStop>("HitStop");
            var enemy = Body(Team.Enemy);

            enemy.TakeHit(AttackPayload.Poke(1, Team.Player, 0));

            Assert.AreEqual(1f, Time.timeScale, "Pokes must not freeze.");
            Assert.Less(enemy.Hp, enemy.MaxHp, "The poke should still have dealt damage.");
            yield return null;
        }

        // ---- HitImpactFx ----

        [UnityTest]
        public IEnumerator CardHit_EmitsSparks_InElementColour()
        {
            var fx = Spawn<HitImpactFx>("HitFx");
            var enemy = Body(Team.Enemy);

            enemy.TakeHit(Card(4, Team.Player, Element.Fire));

            Assert.AreEqual(HitImpactFx.CardSparks, fx.LastSparkCount);
            Assert.AreEqual(ParticleFx.Opaque(ElementColors.For(Element.Fire)), fx.LastSparkColor);
            Assert.AreEqual(0, fx.PlayerHurtImpulses, "Hitting an enemy must not kick the camera.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Poke_EmitsFewerSparks()
        {
            var fx = Spawn<HitImpactFx>("HitFx");
            var enemy = Body(Team.Enemy);

            enemy.TakeHit(AttackPayload.Poke(1, Team.Player, 0));

            Assert.AreEqual(HitImpactFx.PokeSparks, fx.LastSparkCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerHurt_KicksCamera()
        {
            var fx = Spawn<HitImpactFx>("HitFx");
            var player = Body(Team.Player);

            player.TakeHit(Card(3, Team.Enemy));

            Assert.AreEqual(1, fx.PlayerHurtImpulses);
            yield return null;
        }

        // ---- DeathPop ----

        /// <summary>An enemy body with a renderer child, like the greybox prefab's "Body".</summary>
        private (CombatantStatus status, DeathPop pop, Renderer body) PoppableEnemy()
        {
            var root = new GameObject("PoppableEnemy");
            spawned.Add(root);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(1f, 1.2f, 1f);
            var status = root.AddComponent<CombatantStatus>();
            status.Team = Team.Enemy;
            status.SetMaxHp(5);
            var pop = root.AddComponent<DeathPop>();
            return (status, pop, visual.GetComponent<Renderer>());
        }

        [UnityTest]
        public IEnumerator LethalHit_PopsInKillingElement_AndHidesBody()
        {
            var (status, pop, body) = PoppableEnemy();

            status.TakeHit(Card(9, Team.Player, Element.Ice));

            Assert.IsTrue(pop.IsPopped);
            Assert.AreEqual(DeathPop.BaseBurstCount, pop.LastBurstCount);
            Assert.AreEqual(ParticleFx.Opaque(ElementColors.For(Element.Ice)), pop.LastBurstColor);

            yield return TestWait.UntilRealtime(() => !body.enabled, timeout: 1f);
            Assert.IsFalse(body.enabled, "The body should be hidden after the squash.");
            Assert.Less(body.transform.localScale.x, 0.05f);
        }

        [UnityTest]
        public IEnumerator BurstCount_ScalesWithPopScale()
        {
            var (status, pop, _) = PoppableEnemy();
            pop.Scale = DeathPop.BossScale;

            status.TakeHit(Card(9, Team.Player));

            Assert.AreEqual(Mathf.RoundToInt(DeathPop.BaseBurstCount * DeathPop.BossScale), pop.LastBurstCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Burst_OutlivesTheEnemy()
        {
            var (status, pop, _) = PoppableEnemy();
            var burst = pop.transform.Find("DeathPopFx");
            Assert.IsNotNull(burst, "DeathPop should build its burst under the enemy.");

            status.TakeHit(Card(9, Team.Player));
            Object.Destroy(status.gameObject);
            yield return null;

            Assert.IsTrue(burst != null, "The burst should be detached so it survives the enemy.");
            Assert.IsNull(burst.parent);
            spawned.Add(burst.gameObject);
        }

        [UnityTest]
        public IEnumerator Revive_RestoresBody()
        {
            var (status, pop, body) = PoppableEnemy();
            status.TakeHit(Card(9, Team.Player));
            yield return TestWait.UntilRealtime(() => !body.enabled, timeout: 1f);

            status.Revive();

            Assert.IsFalse(pop.IsPopped);
            Assert.IsTrue(body.enabled);
            Assert.AreEqual(new Vector3(1f, 1.2f, 1f), body.transform.localScale);
            yield return null;
            // It can pop again on the next death.
            status.TakeHit(Card(9, Team.Player));
            Assert.IsTrue(pop.IsPopped);
        }

        [UnityTest]
        public IEnumerator NonLethalHit_DoesNotPop()
        {
            var (status, pop, body) = PoppableEnemy();
            status.SetMaxHp(100);

            status.TakeHit(Card(2, Team.Player));
            yield return null;

            Assert.IsFalse(pop.IsPopped);
            Assert.IsTrue(body.enabled);
        }

        [UnityTest]
        public IEnumerator EnemyHitOnPlayer_Freezes()
        {
            Spawn<HitStop>("HitStop");
            var player = Body(Team.Player);
            bool froze = false;
            float seconds = 0f;

            yield return MeasureFreeze(player, Card(2, Team.Enemy), (f, s) => { froze = f; seconds = s; });

            Assert.IsTrue(froze, "Getting hit should freeze time.");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.GreaterOrEqual(seconds, HitJuiceRules.PlayerHurtHitStop * 0.9f);
        }
    }
}
