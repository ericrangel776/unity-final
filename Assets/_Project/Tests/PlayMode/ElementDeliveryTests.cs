using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Element decides delivery (docs/specs/element-mechanics.md): hitbox type, size, placement per delivery.</summary>
    public class ElementDeliveryTests
    {
        private const float Eps = 0.02f;
        // Far from other fixtures' leftovers.
        private static readonly Vector3 Origin = new Vector3(300f, 0f, 0f);

        private readonly List<Object> spawned = new List<Object>();
        private readonly List<AttackHitbox> hitboxes = new List<AttackHitbox>();

        [SetUp]
        public void SetUp() => Time.timeScale = 1f;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            hitboxes.Clear();
            Time.timeScale = 1f;
        }

        // No body collider: only its hitboxes can touch anything.
        private CombatActionController MakeAttacker()
        {
            var go = new GameObject("Attacker");
            spawned.Add(go);
            go.transform.position = Origin;
            go.AddComponent<CombatantStatus>().Team = Team.Player;
            var combat = go.AddComponent<CombatActionController>();
            combat.HitboxSpawned += hb => hitboxes.Add(hb);
            return combat;
        }

        private CombatantStatus MakeBody(Vector3 offset)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spawned.Add(go);
            go.transform.position = Origin + offset;
            var status = go.AddComponent<CombatantStatus>();
            status.Team = Team.Enemy;
            return status;
        }

        private static AttackPayload Card(CombatActionController c, Element element, CardShape shape = CardShape.Strike,
            int value = 5) =>
            new AttackPayload(c.Status.OwnerId, Team.Player, value, false, false, 1, shape, element);

        private static AttackPayload Sleight(CombatActionController c, Element element, CardShape shape = CardShape.Strike) =>
            new AttackPayload(c.Status.OwnerId, Team.Player, 15, false, true, 1, shape, element, 5,
                SleightRules.HitCount(ElementRules.DeliveryFor(element, shape)));

        private static IEnumerator Physics(int steps = 3)
        {
            for (int i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        private static Vector3 Local(AttackHitbox hb) => hb.transform.position - Origin;

        [Test]
        public void Fire_IsAProjectileFireball_WhateverItsShape()
        {
            var c = MakeAttacker();
            var hb = c.Spawn(Card(c, Element.Fire, CardShape.Arcana), Vector3.forward);
            Assert.AreEqual(Delivery.Projectile, hb.Payload.Delivery);
            Assert.IsNotNull(hb.GetComponent<SphereCollider>());
            Assert.AreEqual(0.35f, hb.Radius, Eps);
            Assert.IsNull(hb.transform.parent, "projectiles fly free of the caster");
        }

        [Test]
        public void Ice_IsAFrostBlast_InFrontOfTheCaster()
        {
            var c = MakeAttacker();
            var hb = c.Spawn(Card(c, Element.Ice, CardShape.Throw), Vector3.forward);
            Assert.AreEqual(Delivery.FrostBlast, hb.Payload.Delivery);
            Assert.AreEqual(1.4f, hb.Radius, Eps);
            Assert.AreEqual(1f, Local(hb).z, Eps);
            Assert.AreEqual(0.5f, Local(hb).y, Eps);
        }

        [UnityTest]
        public IEnumerator Ice_FrostBlast_IsShortLived()
        {
            var c = MakeAttacker();
            c.Spawn(Card(c, Element.Ice), Vector3.forward);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(0, c.ActiveHitboxCount, "a 0.25 s blast");
        }

        [Test]
        public void Lightning_IsAFastSmallArc_ThatDespawnsOnHit()
        {
            var c = MakeAttacker();
            var hb = c.Spawn(Card(c, Element.Lightning), Vector3.forward);
            Assert.AreEqual(Delivery.ChainArc, hb.Payload.Delivery);
            Assert.AreEqual(0.3f, hb.Radius, Eps);
            Assert.IsTrue(hb.DespawnOnBodyHit);
        }

        [UnityTest]
        public IEnumerator Lightning_ChainArc_HitsOnlyTheFirstBodyInLine()
        {
            var c = MakeAttacker();
            var first = MakeBody(new Vector3(0f, 0f, 2.5f));
            var second = MakeBody(new Vector3(0f, 0f, 4f));
            c.Spawn(Card(c, Element.Lightning), Vector3.forward);
            yield return new WaitForSeconds(0.4f);
            Assert.Less(first.Hp, first.MaxHp, "the arc hits the first body");
            Assert.AreEqual(second.MaxHp, second.Hp, "then despawns: the chain does the crowd work");
            Assert.AreEqual(0, c.ActiveHitboxCount);
        }

        [UnityTest]
        public IEnumerator Lightning_ChainArc_NeverReachesEightUnits()
        {
            var c = MakeAttacker();
            var hb = c.Spawn(Card(c, Element.Lightning), Vector3.forward);
            float farthest = 0f;
            float end = Time.realtimeSinceStartup + 1f;
            while (hb != null && Time.realtimeSinceStartup < end)
            {
                farthest = Mathf.Max(farthest, Local(hb).z);
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(hb == null, "the arc expired");
            Assert.Greater(farthest, 5f, "mid-range: it travels well past melee");
            Assert.Less(farthest, 8f);
        }

        [Test]
        public void Wind_IsAGustBox_InFront()
        {
            var c = MakeAttacker();
            var hb = c.Spawn(Card(c, Element.Wind), Vector3.forward);
            Assert.AreEqual(Delivery.Gust, hb.Payload.Delivery);
            var box = hb.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.AreEqual(new Vector3(2.4f, 1.2f, 2.5f), box.size);
            Assert.AreEqual(1.6f, Local(hb).z, Eps);
        }

        [UnityTest]
        public IEnumerator Wind_Gust_HitsTwoBodiesAtOnce()
        {
            var c = MakeAttacker();
            var left = MakeBody(new Vector3(-0.7f, 0f, 1.8f));
            var right = MakeBody(new Vector3(0.7f, 0f, 1.8f));
            c.Spawn(Card(c, Element.Wind), Vector3.forward);
            yield return Physics();
            Assert.Less(left.Hp, left.MaxHp);
            Assert.Less(right.Hp, right.MaxHp);
        }

        [Test]
        public void Physical_KeepsItsShape()
        {
            var c = MakeAttacker();
            Assert.AreEqual(Delivery.Strike, c.Spawn(Card(c, Element.Physical, CardShape.Strike), Vector3.forward).Payload.Delivery);
            Assert.AreEqual(Delivery.Projectile, c.Spawn(Card(c, Element.Physical, CardShape.Throw), Vector3.forward).Payload.Delivery);
            Assert.AreEqual(Delivery.Burst, c.Spawn(Card(c, Element.Physical, CardShape.Arcana), Vector3.forward).Payload.Delivery);
        }

        // ---- Sleights ----

        [UnityTest]
        public IEnumerator IceSleight_GrowsAFrostBlastAroundSelf()
        {
            var c = MakeAttacker();
            c.SpawnSleight(Sleight(c, Element.Ice), Vector3.forward);
            Assert.AreEqual(1, hitboxes.Count);
            var hb = hitboxes[0];
            Assert.AreEqual(0f, Local(hb).z, Eps, "centred on the caster");
            Assert.AreEqual(0.5f, hb.Radius, Eps, "starts small");
            float peak = 0f;
            float end = Time.realtimeSinceStartup + 1f;
            while (hb != null && Time.realtimeSinceStartup < end)
            {
                peak = Mathf.Max(peak, hb.Radius);
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual(1.4f * 2.1f, peak, 0.05f, "grows to ~3 u before it expires");
        }

        [Test]
        public void LightningSleight_IsABiggerArc()
        {
            var c = MakeAttacker();
            c.SpawnSleight(Sleight(c, Element.Lightning), Vector3.forward);
            Assert.AreEqual(1, hitboxes.Count);
            Assert.AreEqual(0.45f, hitboxes[0].Radius, Eps);
            Assert.IsTrue(hitboxes[0].DespawnOnBodyHit);
            Assert.AreEqual(1, hitboxes[0].Payload.HitCount);
        }

        [Test]
        public void WindSleight_IsABiggerGust()
        {
            var c = MakeAttacker();
            c.SpawnSleight(Sleight(c, Element.Wind), Vector3.forward);
            Assert.AreEqual(1, hitboxes.Count);
            Assert.AreEqual(new Vector3(2.4f, 1.2f, 2.5f) * 1.5f, hitboxes[0].GetComponent<BoxCollider>().size);
        }

        [Test]
        public void FireSleight_IsABoomerang()
        {
            var c = MakeAttacker();
            c.SpawnSleight(Sleight(c, Element.Fire), Vector3.forward);
            Assert.AreEqual(1, hitboxes.Count);
            Assert.AreEqual(0.35f * 1.5f, hitboxes[0].Radius, Eps);
            Assert.AreEqual(2, hitboxes[0].Payload.HitCount, "out and back");
        }

        [Test]
        public void SleightSpawned_IsRaisedWithThePayload()
        {
            var c = MakeAttacker();
            AttackPayload? seen = null;
            System.Action<CombatActionController, AttackPayload> handler = (_, p) => seen = p;
            CombatActionController.SleightSpawned += handler;
            try
            {
                c.SpawnSleight(Sleight(c, Element.Wind), Vector3.forward);
            }
            finally
            {
                CombatActionController.SleightSpawned -= handler;
            }
            Assert.IsTrue(seen.HasValue);
            Assert.AreEqual(Delivery.Gust, seen.Value.Delivery);
        }
    }
}
