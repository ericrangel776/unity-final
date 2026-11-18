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
    /// Each game event requests the right sound (checked through <see cref="AudioService.Played"/>, which fires even
    /// without an audio device), nothing throws in batchmode, and repeats inside the guard play once.
    /// </summary>
    public class AudioServiceTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private readonly List<SfxId> played = new List<SfxId>();
        private AudioService audio;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var go = new GameObject("Audio");
            spawned.Add(go);
            audio = go.AddComponent<AudioService>();
            audio.Played += played.Add;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            played.Clear();
            Time.timeScale = 1f;
        }

        private CombatActionController Combatant(Team team, Vector3 position)
        {
            var go = new GameObject($"{team}");
            spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CombatantStatus>().Team = team;
            return go.AddComponent<CombatActionController>();
        }

        private static AttackPayload Card(CombatActionController c, int value, int tick, bool zero = false) =>
            new AttackPayload(c.Status.OwnerId, c.Status.Team, value, zero, false, tick, CardShape.Arcana, Element.Physical);

        /// <summary>Waits past the repeat guard so the next identical sound counts.</summary>
        private static IEnumerator PastGuard()
        {
            float end = Time.unscaledTime + AudioService.RepeatGuardSeconds + 0.02f;
            while (Time.unscaledTime < end) yield return null;
        }

        [Test]
        public void Builds_OneClipPerSound() =>
            Assert.AreEqual(Enum.GetValues(typeof(SfxId)).Length, audio.ClipCount);

        [Test]
        public void Play_NeverThrows_AndReportsTheSound()
        {
            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
                Assert.DoesNotThrow(() => audio.Play(id));
            Assert.AreEqual(Enum.GetValues(typeof(SfxId)).Length, played.Count);
        }

        [Test]
        public void Play_SameSoundInsideTheGuard_PlaysOnce()
        {
            audio.Play(SfxId.Hit);
            audio.Play(SfxId.Hit);
            audio.Play(SfxId.Dash);
            CollectionAssert.AreEqual(new[] { SfxId.Hit, SfxId.Dash }, played);
        }

        [UnityTest]
        public IEnumerator Clashes_MapToBreakZeroAndNeutral()
        {
            var player = Combatant(Team.Player, new Vector3(0f, 0f, -10f));
            var enemy = Combatant(Team.Enemy, new Vector3(0f, 0f, 10f));

            var e1 = enemy.Spawn(Card(enemy, 5, 1), Vector3.forward);
            var p1 = player.Spawn(Card(player, 7, 2), Vector3.forward);
            played.Clear();
            ClashSystem.Resolve(p1, e1);
            CollectionAssert.Contains(played, SfxId.CardBreak);

            yield return PastGuard();
            var e2 = enemy.Spawn(Card(enemy, 9, 3), Vector3.forward);
            var p2 = player.Spawn(Card(player, 1, 4, zero: true), Vector3.forward);
            played.Clear();
            ClashSystem.Resolve(p2, e2);
            CollectionAssert.Contains(played, SfxId.ZeroBreak);

            yield return PastGuard();
            var e3 = enemy.Spawn(Card(enemy, 4, 5), Vector3.forward);
            var p3 = player.Spawn(Card(player, 4, 6), Vector3.forward);
            played.Clear();
            ClashSystem.Resolve(p3, e3); // no Deadlock controller here: a plain cancel
            CollectionAssert.Contains(played, SfxId.ClashNeutral);
        }

        [Test]
        public void Spawn_PlaysASwing_AndAHitPlaysAHit()
        {
            var player = Combatant(Team.Player, Vector3.zero);
            player.Spawn(Card(player, 3, 1), Vector3.forward);
            Assert.AreEqual(SfxId.Swing, played[0]);

            var enemy = Combatant(Team.Enemy, new Vector3(0f, 0f, 30f));
            enemy.Status.TakeHit(Card(player, 3, 1));
            CollectionAssert.Contains(played, SfxId.Hit);
        }

        [UnityTest]
        public IEnumerator Dash_PlaysADash()
        {
            var go = new GameObject("Runner");
            spawned.Add(go);
            go.AddComponent<CharacterController>();
            go.AddComponent<CombatantStatus>().Team = Team.Player;
            var motor = go.AddComponent<PlayerMotor>();
            audio.BindPlayer(motor, null);
            yield return null;
            Assert.IsTrue(motor.TryDash());
            CollectionAssert.Contains(played, SfxId.Dash);
        }

        [Test]
        public void Reload_PerfectAndMissHaveTheirOwnSounds()
        {
            var go = new GameObject("Reloader");
            spawned.Add(go);
            go.AddComponent<CombatantStatus>().Team = Team.Player;
            var deck = go.AddComponent<DeckRuntimeManager>();
            var cards = new List<CardData>();
            for (int i = 0; i < 4; i++)
            {
                var c = CardData.Create($"sfx_{i}", 2 + i);
                cards.Add(c);
                spawned.Add(c);
            }
            deck.SetStartingDeck(cards, newSeed: 3);
            audio.BindPlayer(null, deck);

            void Empty()
            {
                while (deck.Cycle.DrawCount > 0 || deck.Cycle.OldestSlot >= 0) deck.Cycle.TryPlay(deck.Cycle.OldestSlot, out _);
            }

            Empty();
            Assert.IsTrue(deck.Reload.Begin());
            deck.Reload.Tick(deck.Reload.Target01 * deck.Reload.BarDuration);
            Assert.AreEqual(ReloadGrade.Perfect, deck.Reload.Press());
            CollectionAssert.Contains(played, SfxId.ReloadPerfect);

            Empty();
            Assert.IsTrue(deck.Reload.Begin());
            deck.Reload.Tick(deck.Reload.BarDuration + 0.01f); // ran out: no press
            CollectionAssert.Contains(played, SfxId.ReloadMiss);
        }

        [UnityTest]
        public IEnumerator Boss_EventsMapToBossSounds()
        {
            var go = new GameObject("SfxBoss");
            spawned.Add(go);
            go.AddComponent<CharacterController>();
            var body = go.AddComponent<TrainingDummyEnemy>();
            body.RespawnOnDeath = false;
            var boss = go.AddComponent<BossEnemy>();
            var cards = new List<CardData>();
            for (int i = 0; i < 15; i++)
            {
                var c = CardData.Create($"sfxb_{i}", 5, CardShape.Arcana, Element.Physical);
                cards.Add(c);
                spawned.Add(c);
            }
            body.Deck.SetStartingDeck(cards, newSeed: 2);
            audio.BindBoss(boss);
            yield return null;

            Assert.IsTrue(boss.ForceSleight());
            CollectionAssert.Contains(played, SfxId.BossSleightWindup);
            boss.ForceStateEnd();
            yield return null;
            CollectionAssert.Contains(played, SfxId.BossSleightFire);
            CollectionAssert.DoesNotContain(played, SfxId.Swing, "the boss Sleight booms instead of swishing");

            var cycle = boss.Deck.Cycle;
            while (cycle.OldestSlot >= 0) cycle.TryPlay(cycle.OldestSlot, out _);
            boss.ForceCycleEnd();
            CollectionAssert.Contains(played, SfxId.BossReload);
            boss.ForceExhausted();
            CollectionAssert.Contains(played, SfxId.BossExhausted);
        }

        [Test]
        public void BigDeadlock_PlaysOnce_WithoutAnExtraClashSound()
        {
            var result = new DeadlockResult(DeadlockGrade.Perfect, 0, 3, DeadlockGrade.Miss, 0, 0, ClashOutcome.AWins,
                Vector3.zero, isBig: true);
            audio.HandleDeadlock(result);
            audio.HandleClash(new ClashEvent(new ClashResult(ClashOutcome.AWins, false), default, default, Vector3.zero,
                fromDeadlock: true));
            CollectionAssert.AreEqual(new[] { SfxId.BigDeadlock }, played);
        }
    }
}
