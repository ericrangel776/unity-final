using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>M2 wiring on a real player rig: stocking/firing Sleights, Active Reload, Overburn, break interrupt.</summary>
    public class SleightWiringTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private readonly List<AttackHitbox> hitboxes = new List<AttackHitbox>();
        private GameObject player;
        private PlayerCombatInput input;
        private DeckRuntimeManager deck;
        private CombatActionController combat;
        private PlayerMotor motor;
        private CombatantStatus status;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            spawned.Add(ground);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            hitboxes.Clear();
            Time.timeScale = 1f;
        }

        /// <summary>Player rig like the prefab, with a deck of the given values, all of one shape.</summary>
        private void MakePlayer(CardShape shape, params int[] values)
        {
            player = new GameObject("TestPlayer");
            spawned.Add(player);
            player.transform.position = new Vector3(0f, 1.1f, 0f);
            player.AddComponent<CharacterController>();
            status = player.AddComponent<CombatantStatus>();
            status.Team = Team.Player;
            combat = player.AddComponent<CombatActionController>();
            deck = player.AddComponent<DeckRuntimeManager>();
            motor = player.AddComponent<PlayerMotor>();
            input = player.AddComponent<PlayerCombatInput>();
            combat.HitboxSpawned += hb => hitboxes.Add(hb);

            var cards = new List<CardData>();
            foreach (var v in values)
            {
                var data = CardData.Create($"wire_{shape}_{v}_{cards.Count}", v, shape, Element.Physical);
                cards.Add(data);
                spawned.Add(data);
            }
            deck.SetStartingDeck(cards, newSeed: 42);
            motor.SetAimDirection(Vector3.forward);
        }

        private void FireSleight()
        {
            Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));
            Assert.IsTrue(input.Execute(CombatCommand.StockSlot2));
            Assert.IsTrue(input.Execute(CombatCommand.StockSlot3));
        }

        private void EmptyDrawQueue()
        {
            while (deck.Cycle.DrawCount > 0) deck.Cycle.TryPlay(0, out _);
        }

        /// <summary>Waits until <paramref name="done"/> or a real-time timeout (never hangs).</summary>
        private static IEnumerator WaitUntil(System.Func<bool> done, float seconds = 3f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        // ---- stocking & Sleights ----

        [UnityTest]
        public IEnumerator Stocking_DoesNotAttack_UntilThirdCardFires()
        {
            MakePlayer(CardShape.Arcana, 1, 2, 3, 4, 5, 6, 7);
            Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));
            Assert.IsTrue(input.Execute(CombatCommand.StockSlot2));
            Assert.AreEqual(2, deck.Cycle.StockCount);
            Assert.IsEmpty(hitboxes);

            Assert.IsTrue(input.Execute(CombatCommand.StockSlot3));

            Assert.AreEqual(1, hitboxes.Count);
            Assert.IsTrue(hitboxes[0].Payload.IsSleight);
            Assert.AreEqual(1, deck.Cycle.BurnedCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StrikeSleight_ThreeTimedHits_WhileLungingForward()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            float startZ = player.transform.position.z;

            FireSleight();
            Assert.AreEqual(1, hitboxes.Count, "First hit spawns immediately.");
            Assert.IsTrue(motor.IsLunging);

            yield return WaitUntil(() => hitboxes.Count >= 3);

            Assert.AreEqual(3, hitboxes.Count);
            foreach (var hb in hitboxes)
            {
                Assert.IsTrue(hb.Payload.IsSleight);
                Assert.AreEqual(3, hb.Payload.HitCount);
            }
            yield return WaitUntil(() => !motor.IsLunging);
            Assert.Greater(player.transform.position.z - startZ, 1.5f, "The lunge carried the player forward.");
        }

        [UnityTest]
        public IEnumerator ThrowSleight_FliesOut_ThenComesBack()
        {
            MakePlayer(CardShape.Throw, 1, 2, 3, 4, 5, 6, 7);
            FireSleight();
            Assert.AreEqual(1, hitboxes.Count);
            var boomerang = hitboxes[0];
            Assert.AreEqual(2, boomerang.Payload.HitCount);

            float maxDistance = 0f;
            float end = Time.realtimeSinceStartup + 3f;
            bool sawReturn = false;
            while (boomerang != null && Time.realtimeSinceStartup < end)
            {
                float d = Vector3.Distance(boomerang.transform.position, player.transform.position);
                maxDistance = Mathf.Max(maxDistance, d);
                if (boomerang.IsReturning) sawReturn = true;
                yield return null;
            }

            Assert.Greater(maxDistance, 3f, "It flew out.");
            Assert.IsTrue(sawReturn, "It turned around.");
            Assert.IsTrue(boomerang == null, "It was caught (despawned) on return.");
        }

        [UnityTest]
        public IEnumerator ArcanaSleight_BlastExpands()
        {
            MakePlayer(CardShape.Arcana, 1, 2, 3, 4, 5, 6, 7);
            FireSleight();
            var blast = hitboxes[0];
            float startRadius = blast.Radius;

            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            Assert.IsTrue(blast != null);
            Assert.Greater(blast.Radius, startRadius + 1f);
        }

        // ---- card break ----

        [UnityTest]
        public IEnumerator CardBreak_CracksPartialStock()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            var stocked = deck.Cycle.Hand[0];
            Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));

            // Player pokes (1) into an enemy 5: the poke is broken.
            var enemyGo = new GameObject("Enemy");
            spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 1.1f, 2.2f);
            enemyGo.AddComponent<CombatantStatus>().Team = Team.Enemy;
            var enemy = enemyGo.AddComponent<CombatActionController>();
            enemy.Spawn(new AttackPayload(enemy.Status.OwnerId, Team.Enemy, 5, false, false, 1, CardShape.Strike,
                Element.Physical), Vector3.back);
            Assert.IsTrue(input.Execute(CombatCommand.Poke));

            yield return WaitUntil(() => deck.Cycle.StockCount == 0, 1f);

            Assert.AreEqual(0, deck.Cycle.StockCount);
            CollectionAssert.Contains(deck.Cycle.BurnedPile, stocked, "Crack the catalyst: slot 1 burns.");
        }

        [UnityTest]
        public IEnumerator CardBreak_MidStrikeCombo_StopsRemainingHits()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            FireSleight();
            Assert.AreEqual(1, hitboxes.Count);

            combat.CancelActiveAction(); // what a Card Break does
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(1, hitboxes.Count, "No more combo hits after the break.");
            Assert.IsFalse(combat.IsSleightActive);
        }

        // ---- Active Reload ----

        [UnityTest]
        public IEnumerator ReloadBar_LocksCardPlays_ButNotPoke()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            EmptyDrawQueue();

            Assert.IsTrue(input.Execute(CombatCommand.Reload));
            Assert.AreEqual(ReloadState.Running, deck.Reload.State);

            Assert.IsFalse(input.Execute(CombatCommand.PlaySlot1));
            Assert.IsFalse(input.Execute(CombatCommand.StockSlot1));
            Assert.IsFalse(input.Execute(CombatCommand.Parry));
            Assert.IsTrue(input.Execute(CombatCommand.Poke), "Poke stays available.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReloadBar_FinishesOnItsOwn_AndReshuffles()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            EmptyDrawQueue();
            Assert.IsTrue(input.Execute(CombatCommand.Reload));

            yield return WaitUntil(() => deck.Reload.State == ReloadState.Idle, 3f);

            Assert.AreEqual(ReloadState.Idle, deck.Reload.State);
            Assert.Greater(deck.Cycle.DrawCount, 0);
            Assert.IsTrue(input.Execute(CombatCommand.PlaySlot1), "Card plays unlock after the bar.");
        }

        [UnityTest]
        public IEnumerator SecondReload_IsTheTimingPress()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            EmptyDrawQueue();
            Assert.IsTrue(input.Execute(CombatCommand.Reload));

            // Pressed immediately: well before any sweet spot, so it jams.
            Assert.IsTrue(input.Execute(CombatCommand.Reload));
            Assert.AreEqual(ReloadState.Jammed, deck.Reload.State);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PerfectReload_BoostsWalkSpeed()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            EmptyDrawQueue();
            Assert.IsTrue(deck.Reload.Begin());
            deck.Reload.Tick(deck.Reload.Target01 * deck.Reload.BarDuration);
            Assert.AreEqual(ReloadGrade.Perfect, deck.Reload.Press());

            yield return null; // PlayerCombatInput.Update copies the multiplier to the motor

            Assert.AreEqual(ActiveReloadRules.BoostSpeedMultiplier, motor.MoveSpeedMultiplier, 1e-5f);
        }

        // ---- Overburn ----

        [UnityTest]
        public IEnumerator Overburn_PaysHp_AndRestoresBurnedOnCompletion()
        {
            MakePlayer(CardShape.Arcana, 1, 2, 3, 4, 5, 6, 7);
            FireSleight(); // burns one
            EmptyDrawQueue();
            Assert.AreEqual(1, deck.Cycle.BurnedCount);

            Assert.IsTrue(input.Execute(CombatCommand.Overburn));
            Assert.AreEqual(status.MaxHp - OverburnRules.Cost(status.MaxHp, 0), status.Hp);
            Assert.IsTrue(deck.Reload.IsOverburn);

            yield return WaitUntil(() => deck.Reload.State == ReloadState.Idle, 3f);

            Assert.AreEqual(0, deck.Cycle.BurnedCount);
            Assert.AreEqual(7, deck.Cycle.CyclingCardCount);
        }

        [UnityTest]
        public IEnumerator Overburn_WithNothingBurned_IsRefused_AndFree()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            EmptyDrawQueue();
            Assert.IsFalse(input.Execute(CombatCommand.Overburn));
            Assert.AreEqual(status.MaxHp, status.Hp);
            yield return null;
        }

        // ---- timers & reset ----

        [UnityTest]
        public IEnumerator StockTimer_AdvancesWithGameTime()
        {
            MakePlayer(CardShape.Strike, 1, 2, 3, 4, 5, 6, 7);
            Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Greater(deck.Cycle.StockIdleSeconds, 0f);
        }

        [UnityTest]
        public IEnumerator Reinitialize_CancelsBar_ClearsStock_AndResetsOverburnCost()
        {
            MakePlayer(CardShape.Arcana, 1, 2, 3, 4, 5, 6, 7);
            FireSleight();
            EmptyDrawQueue();
            Assert.IsTrue(input.Execute(CombatCommand.Overburn));
            Assert.AreEqual(1, deck.Reload.OverburnUsesThisRoom);

            deck.Reinitialize();

            Assert.AreEqual(ReloadState.Idle, deck.Reload.State);
            Assert.AreEqual(0, deck.Reload.OverburnUsesThisRoom);
            Assert.AreEqual(0, deck.Cycle.StockCount);
            yield return null;
        }
    }
}
