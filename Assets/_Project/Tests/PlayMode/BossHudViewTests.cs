using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    public class BossHudViewTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private BossEnemy boss;
        private BossHudView view;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var go = new GameObject("HudBoss");
            spawned.Add(go);
            go.AddComponent<CharacterController>();
            var body = go.AddComponent<TrainingDummyEnemy>();
            body.RespawnOnDeath = false;
            boss = go.AddComponent<BossEnemy>();
            var cards = new List<CardData>();
            for (int i = 0; i < RunContent.BossDeckSize; i++)
            {
                var data = CardData.Create($"hud_{i}", 4 + i % 6, CardShape.Arcana, Element.Physical);
                cards.Add(data);
                spawned.Add(data);
            }
            body.Deck.SetStartingDeck(cards, newSeed: 5);

            var hud = new GameObject("BossHud");
            spawned.Add(hud);
            view = hud.AddComponent<BossHudView>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Hidden_WithoutABoss()
        {
            yield return null;
            Assert.IsFalse(view.IsVisible);
        }

        [UnityTest]
        public IEnumerator Bound_ShowsHandAndFullCounter()
        {
            view.Bind(boss);
            yield return null;
            Assert.IsTrue(view.IsVisible);
            Assert.AreEqual(BossHudView.BossName, view.NameText);
            var hand = boss.Deck.Cycle.Hand;
            for (int i = 0; i < DeckCycle.HandSize; i++)
                Assert.AreEqual(hand[i].GetEffectiveValue().ToString(), view.HandValue(i));
            Assert.AreEqual(RunContent.BossDeckSize, view.Pips.Count);
            Assert.IsTrue(view.Pips.All(p => p == BossPip.Left));
            Assert.AreEqual("15 left", view.CounterText);
            Assert.AreEqual(1f, view.HpFill01, 1e-4f);
            Assert.AreEqual("", view.StateText);
        }

        [UnityTest]
        public IEnumerator Windup_ShowsTheSumAndStockedPips()
        {
            view.Bind(boss);
            yield return null;
            Assert.IsTrue(boss.ForceSleight());
            yield return null;
            Assert.AreEqual($"SLEIGHT {boss.PendingSleight.Value.Value}", view.StateText);
            Assert.AreEqual(2, view.Pips.Count(p => p == BossPip.Stocked));
        }

        [UnityTest]
        public IEnumerator ReloadWindow_ShowsReloadingAndSpentPips()
        {
            view.Bind(boss);
            yield return null;
            var cycle = boss.Deck.Cycle;
            while (cycle.OldestSlot >= 0) cycle.TryPlay(cycle.OldestSlot, out _);
            cycle.DeleteFromCurrentCycle(cycle.DiscardPile[0]);
            boss.ForceCycleEnd();
            yield return null;
            StringAssert.StartsWith("RELOADING", view.StateText);
            Assert.AreEqual(14, view.Pips.Count(p => p == BossPip.Spent));
            Assert.AreEqual(1, view.Pips.Count(p => p == BossPip.Deleted));
            StringAssert.Contains("3 more = EXHAUSTED", view.CounterText);
        }

        [UnityTest]
        public IEnumerator Damage_ShrinksTheHpBar()
        {
            view.Bind(boss);
            boss.Status.TakeHit(new AttackPayload(1, Team.Player, 9, false, false, 0, CardShape.Strike, Element.Physical));
            yield return null;
            Assert.AreEqual(boss.Status.Hp / (float)boss.Status.MaxHp, view.HpFill01, 1e-4f);
            Assert.Less(view.HpFill01, 1f);
        }

        [UnityTest]
        public IEnumerator DeadOrUnbound_Hides()
        {
            view.Bind(boss);
            yield return null;
            Assert.IsTrue(view.IsVisible);
            view.Bind(null);
            yield return null;
            Assert.IsFalse(view.IsVisible);

            view.Bind(boss);
            for (int i = 0; i < 200 && !boss.Status.IsDead; i++)
                boss.Status.TakeHit(new AttackPayload(1, Team.Player, 9, false, false, 0, CardShape.Strike, Element.Physical));
            yield return null;
            Assert.IsFalse(view.IsVisible);
        }

        [Test]
        public void Canvas_SortsBelowTheRunScreens() =>
            Assert.Less(BossHudView.SortingOrder, RunScreens.SortingOrder);
    }
}
