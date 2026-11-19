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
    /// <summary>Floating damage numbers (docs/specs/hit-juice.md).</summary>
    public class DamageNumberViewTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private DamageNumberView view;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var go = new GameObject("DamageNumbers");
            spawned.Add(go);
            view = go.AddComponent<DamageNumberView>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        private CombatantStatus Body(Team team, int maxHp = 1000)
        {
            var go = new GameObject($"{team}_body");
            spawned.Add(go);
            var status = go.AddComponent<CombatantStatus>();
            status.Team = team;
            status.SetMaxHp(maxHp);
            return status;
        }

        private static AttackPayload Card(int value, Team attacker, Element element = Element.Lightning) =>
            new AttackPayload(1, attacker, value, false, false, 0, CardShape.Strike, element);

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyHit_ShowsDamage_InElementColour()
        {
            var enemy = Body(Team.Enemy);

            enemy.TakeHit(Card(4, Team.Player, Element.Lightning));

            Assert.AreEqual(1, view.ActiveCount);
            var shown = view.Active[0];
            Assert.AreEqual((4 * CombatMath.DamagePerPoint).ToString(), shown.Text);
            var expected = ElementColors.For(Element.Lightning);
            Assert.AreEqual(new Color(expected.r, expected.g, expected.b, 1f), shown.Color);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerHit_ShowsRedNumber()
        {
            var player = Body(Team.Player);

            player.TakeHit(Card(3, Team.Enemy, Element.Fire));

            Assert.AreEqual(1, view.ActiveCount);
            Assert.AreEqual(DamageNumberView.PlayerHurtColor, view.Active[0].Color);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BigHit_ShowsBiggerNumber_ThanSmallHit()
        {
            var enemy = Body(Team.Enemy);

            enemy.TakeHit(AttackPayload.Poke(1, Team.Player, 0));
            enemy.TakeHit(new AttackPayload(1, Team.Player, 20, false, true, 0, CardShape.Strike, Element.Fire, 9));

            var shown = view.Active;
            Assert.AreEqual(2, shown.Count);
            Assert.Greater(shown[1].Scale, shown[0].Scale);
            Assert.Greater(shown[1].Scale, 1f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator QuickHitsOnSameVictim_StackUpward()
        {
            var enemy = Body(Team.Enemy);
            var other = Body(Team.Enemy);

            enemy.TakeHit(Card(3, Team.Player));
            enemy.TakeHit(Card(5, Team.Player));
            other.TakeHit(Card(4, Team.Player));

            var shown = view.Active;
            Assert.AreEqual(0, shown[0].StackIndex);
            Assert.AreEqual(1, shown[1].StackIndex, "The second hit on the same victim should stack above the first.");
            Assert.AreEqual(0, shown[2].StackIndex, "A different victim starts its own stack.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator HitsAfterStackWindow_DoNotStack()
        {
            var enemy = Body(Team.Enemy);
            enemy.TakeHit(Card(3, Team.Player));

            yield return WaitRealtime(0.35f);
            enemy.TakeHit(Card(5, Team.Player));

            var shown = view.Active;
            Assert.AreEqual(0, shown[shown.Count - 1].StackIndex);
        }

        [UnityTest]
        public IEnumerator CounterHit_ShowsBoostedDamage()
        {
            var enemy = Body(Team.Enemy);
            enemy.ApplyBreak();

            enemy.TakeHit(Card(4, Team.Player));

            Assert.AreEqual(CombatMath.CounterDamage(4 * CombatMath.DamagePerPoint).ToString(), view.Active[0].Text);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LethalHit_StillShowsNumber()
        {
            var enemy = Body(Team.Enemy, maxHp: 5);

            enemy.TakeHit(Card(9, Team.Player));

            Assert.AreEqual(1, view.ActiveCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ZeroDamageHit_ShowsNothing()
        {
            var enemy = Body(Team.Enemy);

            enemy.TakeHit(new AttackPayload(1, Team.Player, 0, true, false, 0, CardShape.Strike, Element.Physical));

            Assert.AreEqual(0, view.ActiveCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManyHits_RecycleThePool()
        {
            var enemy = Body(Team.Enemy, maxHp: 100000);

            for (int i = 0; i < DamageNumberView.PoolSize + 1; i++) enemy.TakeHit(Card(1 + i % 9, Team.Player));

            Assert.AreEqual(DamageNumberView.PoolSize, view.ActiveCount);
            // The oldest (the first hit, value 1) was recycled for the newest (value 25 % 9 = 7).
            var shown = view.Active;
            Assert.AreEqual((7 * CombatMath.DamagePerPoint).ToString(), shown[shown.Count - 1].Text);
            Assert.AreEqual((2 * CombatMath.DamagePerPoint).ToString(), shown[0].Text);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Numbers_FadeOut()
        {
            var enemy = Body(Team.Enemy);
            enemy.TakeHit(Card(4, Team.Player));
            Assert.AreEqual(1, view.ActiveCount);

            yield return WaitRealtime(0.8f);

            Assert.AreEqual(0, view.ActiveCount);
        }

        [UnityTest]
        public IEnumerator Numbers_AnimateWhileTimeIsFrozen()
        {
            var enemy = Body(Team.Enemy);
            Time.timeScale = 0f;
            enemy.TakeHit(Card(4, Team.Player));

            yield return WaitRealtime(0.8f);

            Assert.AreEqual(0, view.ActiveCount, "Numbers run on unscaled time, so the freeze must not hold them.");
        }
    }
}
