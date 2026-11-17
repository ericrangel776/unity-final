using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Boss telegraph and stagger FX follow the boss states and leave nothing behind (m4-overview step 6).</summary>
    public class BossFxTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private BossEnemy boss;
        private BossTelegraphFx telegraph;
        private BossStateFx state;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var go = new GameObject("FxBoss");
            spawned.Add(go);
            go.AddComponent<CharacterController>();
            var body = go.AddComponent<TrainingDummyEnemy>();
            body.RespawnOnDeath = false;
            boss = go.AddComponent<BossEnemy>();
            telegraph = go.AddComponent<BossTelegraphFx>();
            state = go.AddComponent<BossStateFx>();
            var cards = new List<CardData>();
            for (int i = 0; i < 15; i++)
            {
                var data = CardData.Create($"fx_{i}", 5, CardShape.Arcana, Element.Physical);
                cards.Add(data);
                spawned.Add(data);
            }
            body.Deck.SetStartingDeck(cards, newSeed: 4);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private void PlayOutTheHand()
        {
            var cycle = boss.Deck.Cycle;
            while (cycle.OldestSlot >= 0) cycle.TryPlay(cycle.OldestSlot, out _);
        }

        [UnityTest]
        public IEnumerator Idle_ShowsNothing()
        {
            yield return null;
            Assert.IsFalse(telegraph.IsShowing);
            Assert.IsFalse(state.IsShowing);
            Assert.IsFalse(state.IsTinted);
        }

        [UnityTest]
        public IEnumerator SwingTelegraph_ShowsTheRedRingWithoutALabel()
        {
            yield return null;
            boss.Body.ForceTelegraph();
            yield return null;
            Assert.IsTrue(telegraph.IsShowing);
            Assert.IsFalse(telegraph.IsSleightStyle);
            Assert.AreEqual("", telegraph.LabelText);
        }

        [UnityTest]
        public IEnumerator SleightWindup_ShowsThePurpleStyleAndTheSum()
        {
            yield return null;
            Assert.IsTrue(boss.ForceSleight());
            yield return null;
            Assert.IsTrue(telegraph.IsShowing);
            Assert.IsTrue(telegraph.IsSleightStyle);
            Assert.AreEqual("SLEIGHT 15", telegraph.LabelText);
            float early = telegraph.Progress;
            yield return null;
            yield return null;
            Assert.GreaterOrEqual(telegraph.Progress, early, "the inner ring grows toward the swing");

            boss.ForceStateEnd();
            yield return null; // fires
            yield return null;
            Assert.IsFalse(telegraph.IsShowing);
        }

        [UnityTest]
        public IEnumerator ReloadWindow_ShowsCountdownTintAndBurstsAtTheEnd()
        {
            yield return null;
            PlayOutTheHand();
            boss.ForceCycleEnd();
            yield return null;
            Assert.IsTrue(state.IsShowing);
            Assert.IsTrue(state.IsTinted);
            Assert.Greater(state.CountdownFraction, 0f);
            Assert.LessOrEqual(state.CountdownFraction, 1f);

            boss.ForceStateEnd();
            yield return null; // EndStagger runs in this Update; the burst starts from its event
            Assert.IsTrue(state.IsBursting);
            yield return null; // the tint clears in LateUpdate, which a yield resumes before
            Assert.IsFalse(state.IsTinted);

            float end = Time.realtimeSinceStartup + 2f;
            while (state.IsBursting && Time.realtimeSinceStartup < end) yield return null;
            yield return null;
            Assert.IsFalse(state.IsShowing);
        }

        [UnityTest]
        public IEnumerator Exhausted_ShowsTheStaggerFx()
        {
            yield return null;
            boss.ForceExhausted();
            yield return null;
            Assert.IsTrue(state.IsShowing);
            Assert.IsTrue(state.IsTinted);
        }

        [UnityTest]
        public IEnumerator DeathAndDestroy_LeaveNothingBehind()
        {
            yield return null;
            Assert.IsTrue(boss.ForceSleight());
            yield return null;
            var telegraphRoot = telegraph.Root;
            var stateRoot = state.Root;
            Assert.IsNotNull(telegraphRoot);
            Assert.IsNotNull(stateRoot);

            for (int i = 0; i < 200 && !boss.Status.IsDead; i++)
                boss.Status.TakeHit(new AttackPayload(1, Team.Player, 9, false, false, 0, CardShape.Strike, Element.Physical));
            yield return null;
            Assert.IsFalse(telegraph.IsShowing, "hidden once dead");

            Object.Destroy(boss.gameObject);
            yield return null;
            Assert.IsTrue(telegraphRoot == null, "the telegraph FX root is destroyed with the boss");
            Assert.IsTrue(stateRoot == null, "the stagger FX root is destroyed with the boss");
        }
    }
}
