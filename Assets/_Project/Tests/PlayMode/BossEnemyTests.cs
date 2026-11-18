using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The boss brain (docs/specs/m4-overview.md step 3): Sleight pattern and wind-up, reload window, Exhausted.</summary>
    public class BossEnemyTests
    {
        private static readonly int[] Warden = { 4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 9, 9, 9 };
        private readonly List<Object> spawned = new List<Object>();
        private BossEnemy boss;
        private DeckCycle cycle;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            // No target: the body never approaches or swings by itself, so the tests drive every turn.
            var go = new GameObject("TestBoss");
            spawned.Add(go);
            go.AddComponent<CharacterController>();
            var body = go.AddComponent<TrainingDummyEnemy>();
            body.RespawnOnDeath = false;
            boss = go.AddComponent<BossEnemy>();
            var cards = new List<CardData>();
            for (int i = 0; i < Warden.Length; i++)
            {
                var data = CardData.Create($"boss_{i}", Warden[i], CardShape.Arcana, Element.Physical);
                cards.Add(data);
                spawned.Add(data);
            }
            body.Deck.SetStartingDeck(cards, newSeed: 7);
            body.Status.SetMaxHp(500);
            cycle = body.Deck.Cycle;
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
            while (cycle.OldestSlot >= 0) cycle.TryPlay(cycle.OldestSlot, out _);
        }

        private void DeleteSpent(int count)
        {
            var spent = cycle.DiscardPile.Take(count).ToList();
            foreach (var c in spent) Assert.IsTrue(cycle.DeleteFromCurrentCycle(c));
        }

        private AttackPayload PlayerHit(int value) =>
            new AttackPayload(12345, Team.Player, value, false, false, Time.frameCount, CardShape.Strike, Element.Physical);

        [Test]
        public void Boss_SpacesItsNinesAndHooksTheBody()
        {
            Assert.IsNotNull(cycle.SpacingRule);
            Assert.AreSame(boss, boss.Body.TurnHook);
            var order = cycle.Hand.Concat(cycle.DrawOrder).ToList();
            for (int i = 1; i < order.Count; i++)
                Assert.IsFalse(order[i].GetEffectiveValue() == 9 && order[i - 1].GetEffectiveValue() == 9);
        }

        [UnityTest]
        public IEnumerator Telegraph_PlaysTheOldestHandCard()
        {
            yield return null; // Start() resets the body
            var oldest = cycle.Hand[cycle.OldestSlot];
            boss.Body.ForceTelegraph();
            Assert.AreSame(oldest, boss.Body.CurrentTelegraph);
        }

        [Test]
        public void EveryThirdTurn_IsASleightWindup()
        {
            Assert.IsFalse(boss.TryTakeTurn());
            Assert.IsFalse(boss.TryTakeTurn());
            int? sum = null;
            boss.SleightWindupStarted += v => sum = v;
            Assert.IsTrue(boss.TryTakeTurn());

            Assert.AreEqual(BossState.SleightWindup, boss.State);
            Assert.AreEqual(3, boss.AttackCount);
            Assert.AreEqual(2, cycle.StockCount, "two stocked, the third reserved in hand");
            Assert.IsTrue(boss.Status.FlinchArmor);
            Assert.IsTrue(boss.PendingSleight.HasValue);
            Assert.AreEqual(boss.PendingSleight.Value.Value, sum);
            Assert.AreEqual(BossRules.SleightWindupSeconds, boss.StateRemaining, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Windup_FiresTheSleightWithThePreviewedValue()
        {
            yield return null;
            Assert.IsTrue(boss.ForceSleight());
            int preview = boss.PendingSleight.Value.Value;
            AttackPayload? fired = null;
            boss.SleightFired += p => fired = p;

            boss.ForceStateEnd();
            yield return null;

            Assert.IsTrue(fired.HasValue, "the Sleight fired at the end of the wind-up");
            Assert.IsTrue(fired.Value.IsSleight);
            Assert.AreEqual(Team.Enemy, fired.Value.Team);
            Assert.AreEqual(preview, fired.Value.Value);
            Assert.AreEqual(3, boss.LastSleightCards.Count);
            Assert.AreEqual(0, cycle.StockCount);
            Assert.AreEqual(1, cycle.BurnedCount, "slot 1 burns (player rules, above the Floor of 3)");
            Assert.AreEqual(BossState.Idle, boss.State);
            Assert.IsFalse(boss.Status.FlinchArmor);
            Assert.Greater(boss.LastSleightHitboxes.Count, 0);
        }

        [UnityTest]
        public IEnumerator BrokenDuringWindup_CracksTheStock()
        {
            yield return null;
            Assert.IsTrue(boss.ForceSleight());
            bool cancelled = false;
            boss.SleightWindupCancelled += () => cancelled = true;

            boss.Status.ApplyBreak();

            Assert.IsTrue(cancelled);
            Assert.AreEqual(BossState.Idle, boss.State);
            Assert.AreEqual(0, cycle.StockCount);
            Assert.AreEqual(1, cycle.BurnedCount, "the catalyst cracks");
            Assert.IsFalse(boss.PendingSleight.HasValue);
        }

        [UnityTest]
        public IEnumerator OutOfCards_ThreeDeletes_OpensTheReloadWindow()
        {
            yield return null;
            PlayOutTheHand();
            DeleteSpent(3);
            BossCycleEnd? end = null;
            boss.CycleEnded += e => end = e;

            boss.Body.ForceTelegraph(); // empty hand: the body hands the cycle end to the boss

            Assert.AreEqual(BossCycleEnd.ReloadWindow, end);
            Assert.AreEqual(BossState.ReloadWindow, boss.State);
            Assert.AreEqual(BossRules.ReloadPostureMultiplier, boss.Status.PostureTakenMultiplier);
            Assert.AreEqual(BossRules.ReloadWindowDamageMultiplier, boss.Status.DamageTakenMultiplier, "full damage in the window");
            Assert.AreEqual(0, cycle.DrawCount, "no instant reload: the window is punishable");
            Assert.IsNull(boss.Body.CurrentTelegraph);

            boss.ForceStateEnd();
            yield return null;
            Assert.AreEqual(BossState.Idle, boss.State);
            Assert.AreEqual(1f, boss.Status.PostureTakenMultiplier);
            Assert.AreEqual(15, cycle.CyclingCardCount, "reloaded, deleted cards back");
            Assert.AreEqual(0, cycle.DeletedThisCycleCount);
        }

        [UnityTest]
        public IEnumerator OutOfCards_FourDeletes_IsExhaustedWithDoubleDamage()
        {
            yield return null;
            PlayOutTheHand();
            DeleteSpent(BossRules.ExhaustDeleteThreshold);
            bool exhausted = false;
            boss.ExhaustedStarted += () => exhausted = true;

            boss.ForceCycleEnd();

            Assert.IsTrue(exhausted);
            Assert.AreEqual(BossState.Exhausted, boss.State);
            Assert.AreEqual(BossRules.ExhaustedSeconds, boss.StateRemaining, 1e-4f);

            var hit = PlayerHit(5);
            int normal = CombatMath.Damage(hit);
            int before = boss.Status.Hp;
            boss.Status.TakeHit(hit);
            Assert.AreEqual(Mathf.RoundToInt(normal * BossRules.ExhaustedDamageMultiplier), before - boss.Status.Hp);

            boss.ForceStateEnd();
            yield return null;
            Assert.AreEqual(BossState.Idle, boss.State);
            Assert.AreEqual(BossRules.GuardDamageMultiplier, boss.Status.DamageTakenMultiplier, "back to guarded");
            Assert.AreEqual(15, cycle.CyclingCardCount);
        }

        [UnityTest]
        public IEnumerator ReloadWindow_TakesExtraPostureFromBreaks()
        {
            yield return null;
            var reference = new GameObject("RefStatus").AddComponent<CombatantStatus>();
            spawned.Add(reference.gameObject);
            reference.SetMaxPosture(boss.Status.MaxPosture);
            reference.ApplyBreak();
            float normal = reference.Posture;

            PlayOutTheHand();
            boss.ForceCycleEnd();
            boss.Status.ApplyBreak();
            Assert.AreEqual(normal * BossRules.ReloadPostureMultiplier, boss.Status.Posture, 1e-3f);
        }

        [UnityTest]
        public IEnumerator ForceExhausted_MidCycle_DoesNotReload()
        {
            yield return null;
            boss.Body.ForceTelegraph();
            int queued = cycle.DrawCount;

            boss.ForceExhausted();
            Assert.AreEqual(BossState.Exhausted, boss.State);
            boss.ForceStateEnd();
            yield return null;
            Assert.AreEqual(BossState.Idle, boss.State);
            Assert.AreEqual(queued, cycle.DrawCount, "the boss continues its current cycle");
        }

        [UnityTest]
        public IEnumerator ForceExhausted_DuringWindup_CancelsTheSleight()
        {
            yield return null;
            Assert.IsTrue(boss.ForceSleight());
            boss.ForceExhausted();
            Assert.AreEqual(BossState.Exhausted, boss.State);
            Assert.AreEqual(0, cycle.StockCount);
            Assert.IsFalse(boss.Status.FlinchArmor);
        }

        [UnityTest]
        public IEnumerator Death_ResetsTheBoss()
        {
            yield return null;
            PlayOutTheHand();
            DeleteSpent(BossRules.ExhaustDeleteThreshold);
            boss.ForceCycleEnd();
            for (int i = 0; i < 200 && !boss.Status.IsDead; i++)
                boss.Status.TakeHit(PlayerHit(9));
            Assert.IsTrue(boss.Status.IsDead);
            Assert.AreEqual(BossState.Idle, boss.State);
            Assert.AreEqual(1f, boss.Status.DamageTakenMultiplier);
            Assert.AreEqual(1f, boss.Status.PostureTakenMultiplier);
        }

        [UnityTest]
        public IEnumerator BossAttacks_DealHalfDamage()
        {
            yield return null;
            Assert.AreEqual(BossRules.DamageDealtMultiplier, boss.Combat.OutgoingDamageScale);
            AttackHitbox fired = null;
            boss.Combat.HitboxSpawned += hb => fired = hb;
            Assert.IsTrue(boss.ForceSleight());
            int sum = boss.PendingSleight.Value.Value;
            boss.ForceStateEnd();
            yield return null;
            Assert.IsNotNull(fired);
            Assert.AreEqual(BossRules.DamageDealtMultiplier, fired.Payload.DamageScale);
            Assert.AreEqual(sum, fired.Payload.Value, "the value (and so every clash) is unchanged");
        }

        [Test]
        public void Idle_IsGuarded()
        {
            Assert.AreEqual(BossRules.GuardDamageMultiplier, boss.Status.DamageTakenMultiplier);
            var hit = PlayerHit(6);
            int before = boss.Status.Hp;
            boss.Status.TakeHit(hit);
            Assert.AreEqual(Mathf.RoundToInt(CombatMath.Damage(hit) * BossRules.GuardDamageMultiplier), before - boss.Status.Hp);
        }

        [Test]
        public void SleightResetsTheCountSinceTheLastOne()
        {
            Assert.IsFalse(boss.TryTakeTurn());
            Assert.AreEqual(1, boss.AttacksSinceSleight);
            Assert.IsFalse(boss.TryTakeTurn());
            Assert.IsTrue(boss.TryTakeTurn()); // the default policy is the pattern: 3rd attack
            Assert.AreEqual(0, boss.AttacksSinceSleight);
        }

        /// <summary>Records what the boss passes to its policy.</summary>
        private sealed class SpyPolicy : IBossAttackPolicy
        {
            public BossTurnContext Last;
            public BossAction Next(in BossTurnContext context) { Last = context; return BossAction.Swing; }
        }

        [UnityTest]
        public IEnumerator Policy_SeesThePlayersOpenings()
        {
            var player = new GameObject("SpyPlayer");
            spawned.Add(player);
            var playerStatus = player.AddComponent<CombatantStatus>();
            playerStatus.Team = Team.Player;
            var playerDeck = player.AddComponent<DeckRuntimeManager>();
            var cards = new List<CardData>();
            for (int i = 0; i < 4; i++)
            {
                var c = CardData.Create($"spy_{i}", 3 + i);
                cards.Add(c);
                spawned.Add(c);
            }
            playerDeck.SetStartingDeck(cards, newSeed: 1); // no 0-cards
            boss.Body.Target = player.transform;
            var spy = new SpyPolicy();
            boss.Policy = spy;
            yield return null;

            boss.TryTakeTurn();
            Assert.AreEqual(0, spy.Last.PlayerZerosReady);
            Assert.IsFalse(spy.Last.PlayerReloading);
            int sum = 0;
            foreach (var c in cycle.Hand) if (c != null) sum += c.GetEffectiveValue();
            Assert.AreEqual(sum, spy.Last.HandSum);

            while (playerDeck.Cycle.OldestSlot >= 0) playerDeck.Cycle.TryPlay(playerDeck.Cycle.OldestSlot, out _);
            Assert.IsTrue(playerDeck.Reload.Begin());
            playerStatus.ApplyBreak();
            boss.TryTakeTurn();
            Assert.IsTrue(spy.Last.PlayerReloading);
            Assert.IsTrue(spy.Last.PlayerStunned);
            Assert.AreEqual(1, spy.Last.AttacksSinceSleight);
        }

        [Test]
        public void Mobs_HaveNoTurnHook()
        {
            var go = new GameObject("Mob");
            spawned.Add(go);
            go.AddComponent<CharacterController>();
            Assert.IsNull(go.AddComponent<TrainingDummyEnemy>().TurnHook);
        }
    }
}
