using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary><see cref="CombatantStatus.AnyHitLanded"/>: one event per damaging body hit, carrying the hit details.</summary>
    public class HitEventTests
    {
        private GameObject go;
        private CombatantStatus status;
        private readonly List<HitInfo> hits = new List<HitInfo>();

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("HitEventBody");
            go.transform.position = new Vector3(2f, 0f, 3f);
            status = go.AddComponent<CombatantStatus>();
            status.Team = Team.Enemy;
            hits.Clear();
            CombatantStatus.AnyHitLanded += Record;
        }

        [TearDown]
        public void TearDown()
        {
            CombatantStatus.AnyHitLanded -= Record;
            if (go != null) Object.Destroy(go);
        }

        private void Record(HitInfo hit)
        {
            if (hit.Victim == status) hits.Add(hit);
        }

        private static AttackPayload Card(int value, Element element = Element.Fire, Team team = Team.Player) =>
            new AttackPayload(1, team, value, false, false, 0, CardShape.Strike, element);

        [UnityTest]
        public IEnumerator DamagingHit_RaisesOneEvent_WithDamageAndPosition()
        {
            status.TakeHit(Card(4));

            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(4 * CombatMath.DamagePerPoint, hits[0].Damage);
            Assert.AreEqual(Element.Fire, hits[0].Payload.Element);
            Assert.IsFalse(hits[0].WasCounter);
            Assert.IsFalse(hits[0].Killed);
            Assert.AreEqual(go.transform.position + Vector3.up * CombatantStatus.HitFeedbackHeight, hits[0].Position);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DamagingHit_SetsLastHitElement()
        {
            Assert.AreEqual(Element.Physical, status.LastHitElement);
            status.TakeHit(Card(4, Element.Ice));
            Assert.AreEqual(Element.Ice, status.LastHitElement);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CounterHit_ReportsBoostedDamage_AndCounterFlag()
        {
            status.ApplyBreak();
            status.TakeHit(Card(4));

            Assert.AreEqual(1, hits.Count);
            Assert.IsTrue(hits[0].WasCounter);
            Assert.AreEqual(CombatMath.CounterDamage(4 * CombatMath.DamagePerPoint), hits[0].Damage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LethalHit_RaisesEvent_FlaggedKilled_BeforeDied()
        {
            status.SetMaxHp(5);
            var order = new List<string>();
            System.Action<HitInfo> onHit = _ => order.Add("hit");
            CombatantStatus.AnyHitLanded += onHit;
            status.Died += () => order.Add("died");
            try
            {
                status.TakeHit(Card(9));
            }
            finally
            {
                CombatantStatus.AnyHitLanded -= onHit;
            }

            Assert.AreEqual(1, hits.Count);
            Assert.IsTrue(hits[0].Killed);
            CollectionAssert.AreEqual(new[] { "hit", "died" }, order);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvulnerableHit_RaisesNothing()
        {
            status.IsInvulnerable = true;
            status.TakeHit(Card(5));
            Assert.AreEqual(0, hits.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FriendlyHit_RaisesNothing()
        {
            status.TakeHit(Card(5, team: Team.Enemy));
            Assert.AreEqual(0, hits.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ZeroDamageHit_RaisesNothing()
        {
            status.TakeHit(new AttackPayload(1, Team.Player, 0, true, false, 0, CardShape.Strike, Element.Physical));
            Assert.AreEqual(0, hits.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpendHp_RaisesNothing()
        {
            status.SpendHp(10);
            Assert.AreEqual(0, hits.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Revive_RaisesRevived_AndResetsLastHitElement()
        {
            int revived = 0;
            status.Revived += () => revived++;
            status.TakeHit(Card(5, Element.Lightning));

            status.Revive();

            Assert.AreEqual(1, revived);
            Assert.AreEqual(Element.Physical, status.LastHitElement);
            yield return null;
        }
    }
}
