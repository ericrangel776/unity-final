using System;
using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Enemy variants (docs/specs/enemy-variants.md): the Caster keeps its distance and fires projectiles that clash
    /// mid-air; the Rusher's timings are the Brute's scaled by its stats; the boss can't take a variant.
    /// </summary>
    public class EnemyVariantTests
    {
        private const float Height = 1.1f; // CharacterController centre above the floor
        private readonly List<Object> spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            spawned.Add(floor);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private Transform MakeTarget(Vector3 position)
        {
            var go = new GameObject("VariantTarget");
            spawned.Add(go);
            go.transform.position = position + Vector3.up * Height;
            return go.transform;
        }

        private TrainingDummyEnemy MakeEnemy(Vector3 position, Transform target, EnemyVariant? variant,
            CardShape shape = CardShape.Throw, Element element = Element.Fire, int value = 4)
        {
            var go = new GameObject($"{variant} Enemy");
            spawned.Add(go);
            go.transform.position = position + Vector3.up * Height;
            go.AddComponent<CharacterController>().minMoveDistance = 0f;
            var enemy = go.AddComponent<TrainingDummyEnemy>();
            enemy.Target = target;
            enemy.RespawnOnDeath = false;
            var cards = new List<CardData>();
            for (int i = 0; i < 4; i++)
            {
                var data = CardData.Create($"variant_{i}", value, shape, element);
                cards.Add(data);
                spawned.Add(data);
            }
            enemy.Deck.SetStartingDeck(cards, newSeed: 5);
            if (variant.HasValue) enemy.ApplyVariant(variant.Value);
            return enemy;
        }

        private static float FlatDistance(Component a, Component b)
        {
            var d = a.transform.position - b.transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private static IEnumerator Seconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        // ---- Caster movement ----

        [UnityTest]
        public IEnumerator Caster_TooClose_BacksAway()
        {
            var target = MakeTarget(Vector3.zero);
            var caster = MakeEnemy(new Vector3(0f, 0f, 2f), target, EnemyVariant.Caster);

            yield return Seconds(2f); // 1.2 s recover, then retreat

            Assert.Greater(FlatDistance(caster, target), 2.5f, "A Caster inside 4 u backs off.");
        }

        [UnityTest]
        public IEnumerator Caster_FarAway_AdvancesWithoutCasting()
        {
            var target = MakeTarget(Vector3.zero);
            var caster = MakeEnemy(new Vector3(0f, 0f, 13f), target, EnemyVariant.Caster);
            int telegraphs = 0;
            caster.TelegraphStarted += _ => telegraphs++;

            yield return Seconds(2f);

            float d = FlatDistance(caster, target);
            Assert.Less(d, 12.9f, "A Caster beyond 7 u moves in.");
            Assert.Greater(d, EnemyVariantRules.CasterMaxCastRange, "Setup: still out of cast range.");
            Assert.AreEqual(0, telegraphs, "It never casts from beyond 9 u.");
        }

        [UnityTest]
        public IEnumerator Caster_InRange_TelegraphsAndFiresAProjectile()
        {
            var target = MakeTarget(Vector3.zero);
            var caster = MakeEnemy(new Vector3(0f, 0f, 6f), target, EnemyVariant.Caster);
            AttackHitbox shot = null;
            caster.GetComponent<CombatActionController>().HitboxSpawned += hb => shot = hb;
            int telegraphs = 0;
            caster.TelegraphStarted += _ => telegraphs++;

            float end = Time.realtimeSinceStartup + 3f;
            while (shot == null && Time.realtimeSinceStartup < end) yield return null;

            Assert.AreEqual(1, telegraphs, "It winds up from where it stands.");
            Assert.IsNotNull(shot, "The swing spawns a hitbox.");
            Assert.AreEqual(Delivery.Projectile, shot.Payload.Delivery);
            Assert.AreEqual(6f, FlatDistance(caster, target), 0.1f, "It held its position between 4 and 7 u.");
        }

        [UnityTest]
        public IEnumerator Caster_Cornered_CastsPointBlankAfterTheTimer()
        {
            var target = MakeTarget(Vector3.zero);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(wall);
            wall.transform.position = new Vector3(0f, 2f, 3f);
            wall.transform.localScale = new Vector3(6f, 4f, 0.5f);
            var caster = MakeEnemy(new Vector3(0f, 0f, 2f), target, EnemyVariant.Caster);
            float startedAt = -1f, distanceAtCast = -1f;
            float t0 = Time.realtimeSinceStartup;
            caster.TelegraphStarted += _ =>
            {
                startedAt = Time.realtimeSinceStartup - t0;
                distanceAtCast = FlatDistance(caster, target);
            };

            float end = Time.realtimeSinceStartup + 4f;
            while (startedAt < 0f && Time.realtimeSinceStartup < end) yield return null;

            Assert.GreaterOrEqual(startedAt, 0f, "A pinned Caster eventually shoots.");
            Assert.Less(distanceAtCast, EnemyVariantRules.CasterRetreatRange, "It shot from inside 4 u.");
            // Recover (1.2 s) + cornered timer (1.5 s), give or take a frame.
            Assert.GreaterOrEqual(startedAt, 1.2f + EnemyVariantRules.CasterCorneredSeconds - 0.1f,
                "It waits out the cornered timer first.");
        }

        // ---- stats ----

        [Test]
        public void Rusher_SpeedAndTimings_AreTheBrutesTimesItsStats()
        {
            var target = MakeTarget(Vector3.zero);
            var brute = MakeEnemy(new Vector3(5f, 0f, 0f), target, null);
            var rusher = MakeEnemy(new Vector3(-5f, 0f, 0f), target, EnemyVariant.Rusher);
            var stats = EnemyVariantRules.Stats(EnemyVariant.Rusher);

            Assert.AreEqual(EnemyVariant.Brute, brute.Variant);
            Assert.AreEqual(EnemyVariant.Rusher, rusher.Variant);
            Assert.AreEqual(brute.MoveSpeed * stats.MovePercent / 100f, rusher.MoveSpeed, 1e-4f);
            Assert.AreEqual(brute.TelegraphSeconds * stats.TelegraphPercent / 100f, rusher.TelegraphSeconds, 1e-4f);
            Assert.AreEqual(brute.RecoverSeconds * stats.RecoverPercent / 100f, rusher.RecoverSeconds, 1e-4f);
        }

        [Test]
        public void ApplyVariant_WithTempo_IsOrderIndependent()
        {
            var target = MakeTarget(Vector3.zero);
            var a = MakeEnemy(new Vector3(5f, 0f, 0f), target, null);
            a.ApplyTempo(1.3f);
            a.ApplyVariant(EnemyVariant.Rusher);
            var b = MakeEnemy(new Vector3(-5f, 0f, 0f), target, EnemyVariant.Rusher);
            b.ApplyTempo(1.3f);

            Assert.AreEqual(a.MoveSpeed, b.MoveSpeed, 1e-4f);
            Assert.AreEqual(a.TelegraphSeconds, b.TelegraphSeconds, 1e-4f);
            Assert.AreEqual(a.RecoverSeconds, b.RecoverSeconds, 1e-4f);
        }

        [Test]
        public void ApplyVariant_Twice_Throws()
        {
            var enemy = MakeEnemy(Vector3.zero, null, EnemyVariant.Rusher);
            Assert.Throws<InvalidOperationException>(() => enemy.ApplyVariant(EnemyVariant.Caster));
        }

        [Test]
        public void ApplyVariant_WithTurnHook_Throws()
        {
            var enemy = MakeEnemy(Vector3.zero, null, null);
            enemy.TurnHook = new IdleHook();
            Assert.Throws<InvalidOperationException>(() => enemy.ApplyVariant(EnemyVariant.Brute));
        }

        private sealed class IdleHook : IEnemyTurnHook
        {
            public bool IsBusy => false;
            public bool TryTakeTurn() => false;
            public void OnOutOfCards() { }
            public bool ResolveBusyClash(AttackHitbox incoming) => false;
        }

        // ---- look ----

        [UnityTest]
        public IEnumerator Look_TintsAndScalesVariants_AndLeavesBrutesAlone()
        {
            var brute = MakeEnemy(new Vector3(5f, 0f, 0f), null, null);
            var caster = MakeEnemy(new Vector3(-5f, 0f, 0f), null, EnemyVariant.Caster);
            foreach (var e in new[] { brute, caster })
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(visual.GetComponent<Collider>());
                visual.transform.SetParent(e.transform, false);
            }
            EnemyVariantLook.Apply(brute, EnemyVariant.Brute);
            EnemyVariantLook.Apply(caster, EnemyVariant.Caster);
            yield return null;
            yield return null;

            Assert.AreEqual(Vector3.one, brute.transform.localScale);
            Assert.IsNull(brute.GetComponent<BodyTint>(), "A Brute gets no tint owner from its look.");
            Assert.AreEqual(EnemyVariantLook.CasterScale, caster.transform.localScale);
            var block = new MaterialPropertyBlock();
            caster.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
            var shown = block.GetColor(Shader.PropertyToID("_BaseColor"));
            Assert.That(Vector4.Distance(EnemyVariantLook.CasterColor, shown), Is.LessThan(1e-3f), shown.ToString());
        }

        // ---- mid-air clash ----

        private CombatActionController MakePlayer(Vector3 position)
        {
            var go = new GameObject("VariantPlayer");
            spawned.Add(go);
            go.transform.position = position + Vector3.up * Height;
            go.AddComponent<CombatantStatus>().Team = Team.Player;
            return go.AddComponent<CombatActionController>();
        }

        // Player at the origin, Caster 6 u ahead (no target, so it never swings on its own). Both fire at once; the
        // projectiles meet halfway.
        private IEnumerator MidAirClash(int playerValue, Action<CombatActionController, CombatActionController, TrainingDummyEnemy> check)
        {
            var player = MakePlayer(Vector3.zero);
            var caster = MakeEnemy(new Vector3(0f, 0f, 6f), null, EnemyVariant.Caster);
            var casterCombat = caster.GetComponent<CombatActionController>();
            yield return null;

            var enemyShot = new AttackPayload(caster.Status.OwnerId, Team.Enemy, 4, false, false, Time.frameCount,
                CardShape.Throw, Element.Fire);
            var playerShot = new AttackPayload(player.Status.OwnerId, Team.Player, playerValue, false, false,
                Time.frameCount + 1, CardShape.Throw, Element.Physical);
            Assert.AreEqual(Delivery.Projectile, enemyShot.Delivery);
            Assert.AreEqual(Delivery.Projectile, playerShot.Delivery);
            casterCombat.Spawn(enemyShot, Vector3.back);
            player.Spawn(playerShot, Vector3.forward);

            float end = Time.realtimeSinceStartup + 1f;
            while (!caster.Status.IsStunned && !player.Status.IsStunned && Time.realtimeSinceStartup < end)
                yield return new WaitForFixedUpdate();

            check(player, casterCombat, caster);
        }

        [UnityTest]
        public IEnumerator MidAirClash_HigherPlayerCard_CancelsTheProjectile_AndStunsTheCaster() =>
            MidAirClash(7, (player, casterCombat, caster) =>
            {
                Assert.IsTrue(caster.Status.IsStunned, "The Caster is broken at range.");
                Assert.AreEqual(0, casterCombat.ActiveHitboxCount, "Its projectile is cancelled.");
                Assert.IsFalse(player.Status.IsStunned);
            });

        [UnityTest]
        public IEnumerator MidAirClash_LowerPlayerCard_BreaksThePlayer() =>
            MidAirClash(2, (player, casterCombat, caster) =>
            {
                Assert.IsTrue(player.Status.IsStunned, "The lower card is broken.");
                Assert.AreEqual(0, player.ActiveHitboxCount, "The player's projectile is cancelled.");
                Assert.IsFalse(caster.Status.IsStunned);
                Assert.AreEqual(1, casterCombat.ActiveHitboxCount, "The Caster's projectile flies on.");
            });
    }
}
