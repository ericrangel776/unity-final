using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Same-element finisher shockwave (player Sleights only) and the status readability FX.</summary>
    public class ElementFinisherTests
    {
        private static readonly Vector3 Origin = new Vector3(0f, 0f, -300f);
        private readonly List<Object> spawned = new List<Object>();
        private ElementSystem system;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var go = new GameObject("ElementSystem");
            spawned.Add(go);
            system = go.AddComponent<ElementSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private CombatActionController MakeCaster(Team team)
        {
            var go = new GameObject($"{team}_caster");
            spawned.Add(go);
            go.transform.position = Origin;
            go.AddComponent<CombatantStatus>().Team = team;
            return go.AddComponent<CombatActionController>();
        }

        // No collider: only the shockwave (not the Sleight's own hitbox) can reach it.
        private CombatantStatus MakeBody(Team team, Vector3 offset)
        {
            var go = new GameObject($"{team}_body");
            spawned.Add(go);
            go.transform.position = Origin + offset;
            var status = go.AddComponent<CombatantStatus>();
            status.Team = team;
            go.AddComponent<ElementStatusEffects>();
            return status;
        }

        private static AttackPayload Sleight(CombatActionController c, Element element, bool allSame, Team team = Team.Player)
        {
            var delivery = ElementRules.DeliveryFor(element, CardShape.Strike);
            return new AttackPayload(c.Status.OwnerId, team, 18, false, true, 1, CardShape.Strike, element, 6,
                SleightRules.HitCount(delivery), delivery: delivery, isFinisherSource: allSame);
        }

        [UnityTest]
        public IEnumerator AllFireSleight_ShockwaveHitsNearbyEnemy_AfterTheDelay_AndBurnsIt()
        {
            var caster = MakeCaster(Team.Player);
            var near = MakeBody(Team.Enemy, new Vector3(3f, 0f, 0f));
            var far = MakeBody(Team.Enemy, new Vector3(4f, 0f, 0f));
            var payload = Sleight(caster, Element.Fire, allSame: true);
            caster.SpawnSleight(payload, Vector3.forward);

            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(near.MaxHp, near.Hp, "waits 0.35 s");
            yield return new WaitForSeconds(0.3f);
            int expected = ElementRules.FinisherDamage(CombatMath.TotalDamage(payload));
            Assert.Greater(expected, 0);
            Assert.AreEqual(near.MaxHp - expected, near.Hp);
            Assert.IsTrue(near.GetComponent<ElementStatusEffects>().IsBurning, "Sleight-strength burn");
            Assert.AreEqual(far.MaxHp, far.Hp, "4 u is outside the 3.5 u radius");
        }

        [UnityTest]
        public IEnumerator MixedSleight_GivesNoShockwave()
        {
            var caster = MakeCaster(Team.Player);
            var near = MakeBody(Team.Enemy, new Vector3(2f, 0f, 0f));
            caster.SpawnSleight(Sleight(caster, Element.Fire, allSame: false), Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(near.MaxHp, near.Hp);
        }

        [UnityTest]
        public IEnumerator EnemyAllPhysicalSleight_GivesNoShockwave()
        {
            var boss = MakeCaster(Team.Enemy);
            var player = MakeBody(Team.Player, new Vector3(2f, 0f, 0f));
            bool fired = false;
            System.Action<Vector3, Element> handler = (_, __) => fired = true;
            ElementSystem.FinisherFired += handler;
            try
            {
                boss.SpawnSleight(Sleight(boss, Element.Physical, allSame: true, Team.Enemy), Vector3.back);
                yield return new WaitForSeconds(0.5f);
            }
            finally
            {
                ElementSystem.FinisherFired -= handler;
            }
            Assert.IsFalse(fired, "player-only");
            Assert.AreEqual(player.MaxHp, player.Hp);
        }

        [UnityTest]
        public IEnumerator PhysicalFinisher_AddsPosture_AndShowsTheRing()
        {
            var caster = MakeCaster(Team.Player);
            var near = MakeBody(Team.Enemy, new Vector3(2f, 0f, 0f));
            Element? seen = null;
            bool ringShown = false;
            System.Action<Vector3, Element> handler = (_, e) =>
            {
                seen = e;
                ringShown = system.IsFinisherRingVisible;
            };
            ElementSystem.FinisherFired += handler;
            try
            {
                caster.SpawnSleight(Sleight(caster, Element.Physical, allSame: true), Vector3.forward);
                yield return new WaitForSeconds(0.4f);
            }
            finally
            {
                ElementSystem.FinisherFired -= handler;
            }
            Assert.AreEqual(Element.Physical, seen);
            Assert.Greater(near.Posture, ElementRules.PhysicalFinisherPostureFraction * near.MaxPosture - 1f);
            Assert.IsTrue(ringShown, "the expanding ring shows when the shockwave fires");
        }

        [UnityTest]
        public IEnumerator HitStop_PausesTheFinisherDelay()
        {
            var caster = MakeCaster(Team.Player);
            var near = MakeBody(Team.Enemy, new Vector3(2f, 0f, 0f));
            caster.SpawnSleight(Sleight(caster, Element.Ice, allSame: true), Vector3.forward);
            Time.timeScale = 0f;
            float end = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(near.MaxHp, near.Hp, "scaled time: frozen with the hit-stop");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.45f);
            Assert.Less(near.Hp, near.MaxHp);
            Assert.IsTrue(near.GetComponent<ElementStatusEffects>().IsChilled);
        }

        // ---- readability FX ----

        [UnityTest]
        public IEnumerator Pips_AppearAndClear_WithStatuses()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            var fx = body.gameObject.AddComponent<ElementStatusFx>();
            var effects = body.GetComponent<ElementStatusEffects>();
            yield return null;
            Assert.AreEqual(0, fx.PipCount);

            effects.ApplyChill(false);
            effects.ApplyBurn(4, false, new AttackPayload(1, Team.Player, 4, false, false, 0, CardShape.Throw, Element.Fire));
            yield return null;
            yield return null; // LateUpdate
            Assert.IsTrue(fx.HasBurnPip);
            Assert.IsTrue(fx.HasChillPip);
            Assert.AreEqual(2, fx.PipCount);

            effects.Clear();
            yield return null;
            yield return null;
            Assert.AreEqual(0, fx.PipCount);
            Assert.AreEqual(Color.clear, fx.CurrentTint);
        }
    }
}
