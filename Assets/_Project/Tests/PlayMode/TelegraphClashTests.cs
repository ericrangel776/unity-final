using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// A card that reaches an enemy mid wind-up clashes with the telegraphed card: higher breaks it, equal
    /// cancels both, lower is broken. Pokes and 0s don't clash. The player's break stun shrinks in crowds.
    /// </summary>
    public class TelegraphClashTests
    {
        private const int TelegraphValue = 5;
        private readonly List<Object> spawned = new List<Object>();
        private readonly List<ClashEvent> clashes = new List<ClashEvent>();

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            clashes.Clear();
            ClashSystem.ClashOccurred += Record;
        }

        [TearDown]
        public void TearDown()
        {
            ClashSystem.ClashOccurred -= Record;
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private void Record(ClashEvent e) => clashes.Add(e);

        private CombatActionController MakePlayer(Vector3 position)
        {
            var go = new GameObject("TelegraphPlayer");
            spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CombatantStatus>().Team = Team.Player;
            return go.AddComponent<CombatActionController>();
        }

        // No target: once winding up it stays frozen until a test sets one, so it can't race the physics steps.
        private TrainingDummyEnemy MakeDummy(Vector3 position)
        {
            var go = new GameObject("TelegraphDummy");
            spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var dummy = go.AddComponent<TrainingDummyEnemy>();
            var cards = new List<CardData>();
            for (int i = 0; i < 6; i++)
            {
                var data = CardData.Create($"tele_{i}", TelegraphValue, CardShape.Strike, Element.Physical);
                cards.Add(data);
                spawned.Add(data);
            }
            dummy.Deck.SetStartingDeck(cards, newSeed: 3);
            return dummy;
        }

        // After a frame: the dummy's Start() resets it to Recover, so the wind-up must begin after it.
        private static IEnumerator BeginTelegraph(TrainingDummyEnemy dummy)
        {
            yield return null;
            dummy.ForceTelegraph();
            Assert.IsNotNull(dummy.CurrentTelegraph, "Setup: the dummy should be winding up.");
        }

        private static AttackPayload Card(CombatActionController c, int value, bool zero = false) =>
            new AttackPayload(c.Status.OwnerId, Team.Player, value, zero, false, Time.frameCount, CardShape.Strike,
                Element.Physical);

        private static IEnumerator Physics(int steps = 4)
        {
            for (int i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        // Player at the origin swinging +Z reaches the dummy's body 1.6 m ahead.
        private (CombatActionController player, TrainingDummyEnemy dummy) Pair()
        {
            var player = MakePlayer(Vector3.zero);
            var dummy = MakeDummy(new Vector3(0f, 0f, 1.6f));
            return (player, dummy);
        }

        [UnityTest]
        public IEnumerator HigherCard_BreaksTheTelegraph_SpendsTheCard_AndStillHits()
        {
            var (player, dummy) = Pair();
            yield return BeginTelegraph(dummy);
            player.Spawn(Card(player, 7), Vector3.forward);

            yield return Physics();

            Assert.IsNull(dummy.CurrentTelegraph, "A higher card breaks the anticipated attack.");
            Assert.AreEqual(1, dummy.Deck.Cycle.DiscardCount, "The broken card is spent.");
            Assert.IsTrue(dummy.Status.IsStunned, "The enemy is card-broken.");
            Assert.Less(dummy.Status.Hp, dummy.Status.MaxHp, "The winning swing follows through.");
            Assert.IsFalse(dummy.Status.FlinchArmor, "Armor ends with the telegraph.");
            Assert.AreEqual(1, clashes.Count);
            Assert.AreEqual(ClashOutcome.AWins, clashes[0].Result.Outcome);
            Assert.AreEqual(TelegraphValue, clashes[0].B.Value);
        }

        [UnityTest]
        public IEnumerator EqualCard_CancelsBoth_WithoutDamageOrStun()
        {
            var (player, dummy) = Pair();
            yield return BeginTelegraph(dummy);
            player.Spawn(Card(player, TelegraphValue), Vector3.forward);

            yield return Physics();

            Assert.IsNull(dummy.CurrentTelegraph, "A tie cancels the anticipated attack.");
            Assert.AreEqual(1, dummy.Deck.Cycle.DiscardCount, "The cancelled card is spent.");
            Assert.AreEqual(0, player.ActiveHitboxCount, "The player's action is cancelled too.");
            Assert.AreEqual(dummy.Status.MaxHp, dummy.Status.Hp);
            Assert.IsFalse(dummy.Status.IsStunned);
            Assert.IsFalse(player.Status.IsStunned);
            Assert.AreEqual(1, clashes.Count);
            Assert.AreEqual(ClashOutcome.Neutral, clashes[0].Result.Outcome);
        }

        [UnityTest]
        public IEnumerator LowerCard_IsBroken_AndTheEnemyStillSwings()
        {
            var (player, dummy) = Pair();
            yield return BeginTelegraph(dummy);
            player.Spawn(Card(player, 3), Vector3.forward);

            yield return Physics();

            Assert.IsTrue(player.Status.IsStunned, "The lower card is broken.");
            Assert.AreEqual(0, player.ActiveHitboxCount);
            Assert.AreEqual(dummy.Status.MaxHp, dummy.Status.Hp, "A broken card deals no damage.");
            Assert.IsNotNull(dummy.CurrentTelegraph, "The countdown keeps going.");
            Assert.AreEqual(ClashOutcome.BWins, clashes[0].Result.Outcome);

            dummy.Target = player.transform;
            float end = Time.realtimeSinceStartup + 3f;
            while (dummy.Deck.Cycle.DiscardCount == 0 && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(1, dummy.Deck.Cycle.DiscardCount, "The telegraphed card was swung.");
        }

        [UnityTest]
        public IEnumerator LowerWindCard_IsBroken_ButStillShovesTheEnemy()
        {
            var (player, dummy) = Pair();
            yield return BeginTelegraph(dummy);
            player.Spawn(new AttackPayload(player.Status.OwnerId, Team.Player, 3, false, false, Time.frameCount,
                CardShape.Strike, Element.Wind), Vector3.forward);

            yield return Physics();

            Assert.AreEqual(ClashOutcome.BWins, clashes[0].Result.Outcome);
            Assert.IsTrue(player.Status.IsStunned, "The lower Gust is still broken.");
            Assert.AreEqual(dummy.Status.MaxHp, dummy.Status.Hp, "A broken Gust deals no damage.");
            Assert.Greater(dummy.Status.KnockbackVelocity.z, 0f, "...but it shoves the enemy away from the player.");
            Assert.IsNotNull(dummy.CurrentTelegraph, "The wind-up is not cancelled, only pushed out of reach.");
        }

        [UnityTest]
        public IEnumerator LowerPhysicalCard_DoesNotShove()
        {
            var (player, dummy) = Pair();
            yield return BeginTelegraph(dummy);
            player.Spawn(Card(player, 3), Vector3.forward);
            yield return Physics();
            Assert.AreEqual(Vector3.zero, dummy.Status.KnockbackVelocity);
        }

        [UnityTest]
        public IEnumerator Poke_DuringTelegraph_OnlyDamages()
        {
            var (player, dummy) = Pair();
            yield return BeginTelegraph(dummy);
            player.Spawn(AttackPayload.Poke(player.Status.OwnerId, Team.Player, Time.frameCount), Vector3.forward);

            yield return Physics();

            Assert.IsNotNull(dummy.CurrentTelegraph, "A poke never interrupts a telegraph.");
            Assert.AreEqual(dummy.Status.MaxHp - CombatMath.DamagePerPoint, dummy.Status.Hp, "A poke still chips.");
            Assert.IsFalse(dummy.Status.IsStunned);
            Assert.AreEqual(Vector3.zero, dummy.Status.KnockbackVelocity, "A poke doesn't shove.");
            Assert.AreEqual(0, clashes.Count);
            Assert.AreEqual(0, dummy.Deck.Cycle.DiscardCount);
        }

        [UnityTest]
        public IEnumerator ZeroCard_DuringTelegraph_DoesNotClash()
        {
            var (player, dummy) = Pair();
            yield return BeginTelegraph(dummy);
            player.Spawn(Card(player, 0, zero: true), Vector3.forward);

            yield return Physics();

            Assert.IsNotNull(dummy.CurrentTelegraph, "A 0 only answers a live swing.");
            Assert.AreEqual(0, clashes.Count);
            Assert.IsFalse(player.Status.IsStunned);
        }

        [UnityTest]
        public IEnumerator Telegraph_PlainHit_DamagesButDoesNotFlinchOrCancel()
        {
            var dummy = MakeDummy(new Vector3(20f, 0f, 0f));
            yield return BeginTelegraph(dummy);
            int flinches = 0;
            dummy.Status.Flinched += () => flinches++;

            var hit = new AttackPayload(1, Team.Player, 3, false, false, 0, CardShape.Strike, Element.Physical);
            Assert.IsTrue(dummy.Status.TakeHit(hit, Vector3.forward));
            yield return null;

            Assert.AreEqual(0, flinches, "Flinch armor during the wind-up.");
            Assert.IsNotNull(dummy.CurrentTelegraph);
        }

        [UnityTest]
        public IEnumerator Poke_NeverFlinchesOrShoves_OutsideATelegraph()
        {
            var go = new GameObject("PokeTarget");
            spawned.Add(go);
            var status = go.AddComponent<CombatantStatus>();
            status.Team = Team.Enemy;
            int flinches = 0;
            status.Flinched += () => flinches++;

            Assert.IsTrue(status.TakeHit(AttackPayload.Poke(1, Team.Player, 0), Vector3.forward));
            yield return null;

            Assert.AreEqual(status.MaxHp - CombatMath.DamagePerPoint, status.Hp);
            Assert.AreEqual(0, flinches);
            Assert.IsFalse(status.IsStunned);
            Assert.AreEqual(Vector3.zero, status.KnockbackVelocity);
        }

        [Test]
        public void PlayerBreakStun_ShrinksWithMoreLiveEnemies()
        {
            MakeDummy(new Vector3(30f, 0f, 0f));
            int fewCount = TrainingDummyEnemy.LiveCount;
            float fewStun = BreakPlayer(new Vector3(40f, 0f, 0f));

            MakeDummy(new Vector3(30f, 0f, 5f));
            MakeDummy(new Vector3(30f, 0f, 10f));
            int crowdCount = TrainingDummyEnemy.LiveCount;
            float crowdStun = BreakPlayer(new Vector3(50f, 0f, 0f));

            Assert.AreEqual(fewCount + 2, crowdCount);
            Assert.Less(crowdStun, fewStun, "More enemies -> shorter player stun.");
            Assert.AreEqual(fewStun / CombatMath.CrowdStunScale(fewCount) * CombatMath.CrowdStunScale(crowdCount),
                crowdStun, 1e-4f);
        }

        [Test]
        public void EnemyBreakStun_IsNotCrowdScaled()
        {
            MakeDummy(new Vector3(30f, 0f, 0f));
            MakeDummy(new Vector3(30f, 0f, 5f));
            var enemy = MakeDummy(new Vector3(30f, 0f, 10f));
            var hb = enemy.GetComponent<CombatActionController>().Spawn(
                new AttackPayload(enemy.Status.OwnerId, Team.Enemy, 4, false, false, 0, CardShape.Strike, Element.Physical),
                Vector3.forward);

            enemy.GetComponent<CombatActionController>().OnClashLost(hb, false, CombatMath.BaselineBreakMargin, Vector3.zero);

            Assert.AreEqual(0.8f, enemy.Status.StunRemaining, 1e-4f, "Baseline break stun, unscaled.");
        }

        private float BreakPlayer(Vector3 position)
        {
            var player = MakePlayer(position);
            var hb = player.Spawn(Card(player, 4), Vector3.forward);
            player.OnClashLost(hb, false, CombatMath.BaselineBreakMargin, Vector3.zero);
            return player.Status.StunRemaining;
        }
    }
}
