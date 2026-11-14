using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Paying HP as a cost (Overburn) is not a hit: no reactions, and it never kills.</summary>
    public class SpendHpTests
    {
        private GameObject go;
        private CombatantStatus status;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("SpendHpBody");
            status = go.AddComponent<CombatantStatus>();
            status.Team = Team.Player;
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator SpendHp_ReducesHp_WithoutHitReactions()
        {
            int damagedEvents = 0, hits = 0, flinches = 0;
            status.Damaged += _ => damagedEvents++;
            status.HitTaken += (_, __) => hits++;
            status.Flinched += () => flinches++;

            status.SpendHp(10);

            Assert.AreEqual(status.MaxHp - 10, status.Hp);
            Assert.AreEqual(0, damagedEvents);
            Assert.AreEqual(0, hits);
            Assert.AreEqual(0, flinches);
            Assert.IsFalse(status.IsStunned);
            Assert.AreEqual(Vector3.zero, status.KnockbackVelocity);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpendHp_NeverKills()
        {
            bool died = false;
            status.Died += () => died = true;

            status.SpendHp(status.MaxHp * 5);

            Assert.AreEqual(1, status.Hp);
            Assert.IsFalse(status.IsDead);
            Assert.IsFalse(died);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpendHp_IgnoresNonPositiveAmounts()
        {
            status.SpendHp(0);
            status.SpendHp(-20);
            Assert.AreEqual(status.MaxHp, status.Hp);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpendHp_IgnoredWhenDead()
        {
            status.TakeHit(new AttackPayload(1, Team.Enemy, 9, false, true, 0, CardShape.Strike, Element.Physical, 9),
                Vector3.zero);
            while (!status.IsDead)
                status.TakeHit(new AttackPayload(1, Team.Enemy, 9, false, true, 0, CardShape.Strike, Element.Physical, 9),
                    Vector3.zero);

            status.SpendHp(10);

            Assert.AreEqual(0, status.Hp);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Status_IsAnHpPool_ForOverburn()
        {
            IHpPool pool = status;
            var deck = new DeckCycle();
            var cards = new System.Collections.Generic.List<CardData>();
            var list = new System.Collections.Generic.List<RuntimeCardInstance>();
            for (int v = 1; v <= 7; v++)
            {
                var data = CardData.Create($"spend_{v}", v, CardShape.Strike, Element.Physical);
                cards.Add(data);
                list.Add(new RuntimeCardInstance(data));
            }
            deck.Initialize(list, new System.Random(42));
            for (int s = 0; s < DeckCycle.HandSize; s++) deck.TryStock(s, out _); // burns one
            while (deck.DrawCount > 0) deck.TryPlay(0, out _);
            var reload = new ActiveReload(deck, new System.Random(1));

            Assert.IsTrue(reload.BeginOverburn(pool));
            Assert.AreEqual(status.MaxHp - OverburnRules.Cost(status.MaxHp, 0), status.Hp);

            foreach (var c in cards) Object.Destroy(c);
            yield return null;
        }
    }
}
