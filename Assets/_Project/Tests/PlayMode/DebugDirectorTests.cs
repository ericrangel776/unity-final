using System;
using System.Collections;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>RunDirector's debug-menu entry points (docs/specs/debug-menu.md) on the real run scene.</summary>
    public class DebugDirectorTests : RunSceneTest
    {
        private CombatantStatus player;
        private DeckRuntimeManager playerDeck;

        protected override void OnRunLoaded()
        {
            player = director.Player.GetComponent<CombatantStatus>();
            playerDeck = director.Player.GetComponent<DeckRuntimeManager>();
            director.RestartRun(5);
        }

        private void KillLiving(Func<TrainingDummyEnemy, bool> which = null)
        {
            var lethal = new AttackPayload(player.OwnerId, Team.Player, 999, false, true, Time.frameCount,
                CardShape.Strike, Element.Physical);
            foreach (var e in director.Enemies.ToArray())
            {
                if (e == null || (which != null && !which(e))) continue;
                for (int hit = 0; hit < 10 && !e.Status.IsDead; hit++) e.Status.TakeHit(lethal);
            }
        }

        private int Living => director.Enemies.Count(e => e != null && !e.Status.IsDead);

        [Test]
        public void LastSeed_IsTheRunsSeed()
        {
            director.RestartRun(42);
            Assert.AreEqual(42, director.LastSeed);
        }

        [Test]
        public void EnterRoom_FromTheFirstDoor()
        {
            var card = new MapCard(MapCardKind.Hexfire, 7);
            director.DebugEnterRoom(card);

            Assert.AreEqual(RunDirectorState.Fighting, director.State);
            Assert.AreEqual(card, director.Run.CurrentRoom);
            Assert.AreEqual(1, director.Run.RoomNumber);
            Assert.AreEqual(RoomIntensity.EnemyCount(card), director.Enemies.Count);
            Assert.IsTrue(director.CurrentMix.Contains(EnemyVariant.Caster));
        }

        [Test]
        public void EnterRoom_FromMidFight_FinishesTheFightFirst()
        {
            director.ChooseDoor(0);
            Assert.AreEqual(RunDirectorState.Fighting, director.State);

            director.DebugEnterRoom(new MapCard(MapCardKind.Shop, 5));

            Assert.AreEqual(RunDirectorState.Utility, director.State);
            Assert.IsNotNull(director.CurrentShop);
            Assert.AreEqual(2, director.Run.RoomNumber);
            Assert.AreEqual(0, Living, "the old room's enemies are gone");
        }

        [Test]
        public void EnterRoom_EliteWithTrial()
        {
            director.DebugEnterRoom(new MapCard(MapCardKind.EliteTrial, 4, EliteTrial.Counterpuncher));
            Assert.AreEqual(EliteTrial.Counterpuncher, director.Run.CurrentRoom.Trial);
            Assert.IsNotNull(director.Enemies[0].GetComponent<CounterpuncherTrial>());
        }

        [Test]
        public void BossRoom_OnlyAcceptsTheBoss()
        {
            for (int room = 1; room < RunState.BossRoom; room++) director.DebugEnterRoom(new MapCard(MapCardKind.Rest, 3));
            director.DebugFinishCurrentRoom();
            Assert.AreEqual(RunState.BossRoom, director.Run.RoomNumber);

            var mob = new MapCard(MapCardKind.Vanguard, 2);
            Assert.IsFalse(director.DebugCanEnterRoom(mob));
            Assert.Throws<InvalidOperationException>(() => director.DebugEnterRoom(mob));

            director.DebugEnterRoom(new MapCard(MapCardKind.Boss, RunState.BossIntensity));
            Assert.IsNotNull(director.Boss);
        }

        [Test]
        public void SpawnedCaster_JoinsTheRoom_AndMustDieBeforeItClears()
        {
            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 2)); // Low: no wave
            int before = director.Enemies.Count;
            var caster = director.DebugSpawnEnemy(EnemyVariant.Caster, new Vector3(3f, 0f, 0f));

            Assert.IsNotNull(caster);
            Assert.AreEqual(EnemyVariant.Caster, caster.Variant);
            Assert.AreEqual(before + 1, director.Enemies.Count);
            Assert.AreEqual(EnemyVariant.Caster, director.CurrentMix.Last());
            Assert.AreEqual(3f, caster.transform.position.x, 0.01f);

            KillLiving(e => e != caster);
            Assert.AreEqual(RunDirectorState.Fighting, director.State, "the spawned Caster still stands");
            KillLiving();
            Assert.AreEqual(RunDirectorState.Reward, director.State);
        }

        [Test]
        public void Spawn_NotInEliteOrOutsideAFight()
        {
            Assert.IsNull(director.DebugSpawnEnemy(EnemyVariant.Rusher, Vector3.zero), "at a door");
            director.DebugEnterRoom(new MapCard(MapCardKind.EliteTrial, 3, EliteTrial.Veiled));
            Assert.IsFalse(director.DebugCanSpawnEnemy);
            Assert.IsNull(director.DebugSpawnEnemy(EnemyVariant.Rusher, Vector3.zero));
        }

        [Test]
        public void SkipWaveDelay_SpawnsWaveTwoNow()
        {
            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 5));
            KillLiving();
            Assert.IsTrue(director.IsWaveIncoming);

            director.DebugSkipWaveDelay();

            Assert.IsFalse(director.IsWaveIncoming);
            Assert.AreEqual(1, Living);
        }

        [Test]
        public void SetPlayerHp_SurvivesTheNextRoom()
        {
            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 2));
            director.DebugSetPlayerHp(37);
            Assert.AreEqual(37, player.Hp);

            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 2));
            Assert.AreEqual(37, player.Hp);
            Assert.AreEqual(37, director.Run.Hp);
        }

        [Test]
        public void SetPlayerHp_ClampsToOneAndMax()
        {
            director.DebugSetPlayerHp(0);
            Assert.AreEqual(1, player.Hp);
            director.DebugSetPlayerHp(9999);
            Assert.AreEqual(player.MaxHp, player.Hp);
        }

        [Test]
        public void GrantRelic_MidFight_KeepsTheDamageTaken()
        {
            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 2));
            player.SetHp(50);
            int maxBefore = player.MaxHp;

            Assert.IsTrue(director.DebugGrantRelic(RelicId.HeartwoodSeed));

            Assert.IsTrue(director.Run.HasRelic(RelicId.HeartwoodSeed));
            Assert.IsTrue(director.PlayerRelics.Has(RelicId.HeartwoodSeed));
            Assert.Greater(player.MaxHp, maxBefore);
            Assert.AreEqual(director.Run.MaxHp, player.MaxHp);
            Assert.That(player.Hp, Is.InRange(50, 50 + (player.MaxHp - maxBefore)), "not refilled to the room-start HP");
            Assert.IsFalse(director.DebugGrantRelic(RelicId.HeartwoodSeed), "already held");
        }

        [Test]
        public void IncreaseMaxHp_ShowsOnThePlayer()
        {
            int before = player.MaxHp;
            director.DebugIncreaseMaxHp(10);
            Assert.AreEqual(before + 10, player.MaxHp);
            Assert.AreEqual(before + 10, director.Run.MaxHp);
        }

        [Test]
        public void AddAndRemoveCard_ReachThePlayersCycle()
        {
            var card = director.Content.DraftPool.First(c => c.CardId == "cleave_9");
            int before = director.Run.Deck.Count;

            director.DebugAddCard(card);
            Assert.AreEqual(before + 1, director.Run.Deck.Count);
            Assert.AreEqual(director.Run.Deck.Count, playerDeck.Cycle.CyclingCardCount);

            Assert.IsTrue(director.DebugRemoveCard(card));
            Assert.AreEqual(before, director.Run.Deck.Count);
            Assert.AreEqual(before, playerDeck.Cycle.CyclingCardCount);
            Assert.IsFalse(director.DebugRemoveCard(card), "no copy left");
        }
    }
}
