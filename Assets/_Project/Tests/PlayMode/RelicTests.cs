using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Relic choice and the fight effects in the M3 run scene (docs/specs/m3-relics.md).</summary>
    public class RelicTests : RunSceneTest
    {
        private CombatantStatus player;
        private CombatActionController playerCombat;
        private DeckRuntimeManager playerDeck;

        protected override void OnRunLoaded()
        {
            player = director.Player.GetComponent<CombatantStatus>();
            playerCombat = director.Player.GetComponent<CombatActionController>();
            playerDeck = director.Player.GetComponent<DeckRuntimeManager>();
        }

        // ---- helpers ----

        private RelicEffects Relics => director.PlayerRelics;
        private DeckCycle Cycle => playerDeck.Cycle;

        /// <summary>Fresh run holding <paramref name="relics"/>, in room 1 (always combat).</summary>
        private void FightWith(params RelicId[] relics)
        {
            director.RestartRun(3);
            foreach (var r in relics) director.Run.AddRelic(r);
            director.ChooseDoor(0);
            Assert.AreEqual(RunDirectorState.Fighting, director.State);
        }

        private void FireSleight()
        {
            for (int i = 0; i < SleightRules.StockSize; i++)
                Assert.IsTrue(director.Player.Execute(CombatCommand.StockSlot1), "Stocking failed.");
        }

        private void EmptyDrawQueue()
        {
            while (Cycle.DrawCount > 0)
            {
                int slot = Enumerable.Range(0, DeckCycle.HandSize).First(i => Cycle.Hand[i] != null);
                Cycle.TryPlay(slot, out _);
            }
        }

        /// <summary>Runs the Active Reload bar to its sweet spot and presses (sync: no frames pass).</summary>
        private ReloadGrade PerfectReload()
        {
            var reload = playerDeck.Reload;
            Assert.IsTrue(reload.Begin(), "Reload needs an empty draw queue.");
            reload.Tick(reload.BarDuration * reload.Target01);
            return reload.Press();
        }

        /// <summary>A reload that just runs out (grade None).</summary>
        private void PlainReload()
        {
            var reload = playerDeck.Reload;
            Assert.IsTrue(reload.Begin());
            reload.Tick(reload.BarDuration + 0.01f);
            Assert.AreEqual(ReloadState.Idle, reload.State);
        }

        // ---- earning relics ----

        [Test]
        public void EliteClear_OffersTwoRelics_AndChooseRelicAddsOne()
        {
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.EliteTrial);
            director.DebugDefeatAllEnemies();
            var options = director.CurrentReward.RelicOptions;
            Assert.AreEqual(2, options.Count);

            Assert.IsTrue(director.ChooseRelic(1));

            Assert.IsTrue(director.Run.HasRelic(options[1]));
            Assert.IsTrue(Relics.Has(options[1]));
            Assert.IsFalse(director.ChooseRelic(0), "Only one relic per reward.");
            Assert.IsFalse(director.Run.HasRelic(options[0]));
        }

        [Test]
        public void NormalRoomClear_HasNoRelicToChoose()
        {
            director.RestartRun(3);
            director.ChooseDoor(0);
            director.DebugDefeatAllEnemies();
            // Room 1 rolls at most Intensity 5, so it is never Frantic and never offers a relic.
            Assert.IsFalse(director.CurrentReward.OffersRelic);

            Assert.IsFalse(director.ChooseRelic(0));
        }

        [Test]
        public void RelicsCarryIntoTheNextRoom()
        {
            FightWith(RelicId.CinderLantern);
            director.DebugDefeatAllEnemies();
            director.FinishReward();
            int combat = Enumerable.Range(0, director.Run.CurrentOffer.Count)
                .First(i => director.Run.CurrentOffer[i].IsCombat);
            director.ChooseDoor(combat);

            Assert.IsTrue(Relics.Has(RelicId.CinderLantern));
            Assert.AreEqual(1, playerDeck.Reload.FreeOverburnsLeft);
        }

        // ---- Ashen Sigil ----

        [Test]
        public void AshenSigil_SleightBurn_SpeedsUpTheActionLock_AndHeals()
        {
            FightWith(RelicId.AshenSigil);
            player.SetHp(50);

            FireSleight();

            Assert.AreEqual(1, Relics.AshenStacks);
            Assert.AreEqual(RelicRules.ActionSpeedMultiplier(1), director.Player.ActionSpeedMultiplier, 1e-5f);
            Assert.Less(director.Player.ActionRecoverySeconds, 0.25f);
            Assert.AreEqual(50 + RelicRules.AshenSigilHealPerBurn, player.Hp);
        }

        [Test]
        public void AshenSigil_CrackedStock_DoesNotStack()
        {
            FightWith(RelicId.AshenSigil);
            Assert.IsTrue(director.Player.Execute(CombatCommand.StockSlot1));

            Assert.IsTrue(Cycle.InterruptStock());

            Assert.AreEqual(1, Cycle.BurnedCount, "The cracked catalyst burned.");
            Assert.AreEqual(0, Relics.AshenStacks);
        }

        [Test]
        public void AshenSigil_StacksResetNextRoom()
        {
            FightWith(RelicId.AshenSigil);
            FireSleight();
            director.DebugDefeatAllEnemies();
            director.FinishReward();
            director.ChooseDoor(0);

            Assert.AreEqual(0, Relics.AshenStacks);
            Assert.AreEqual(1f, director.Player.ActionSpeedMultiplier);
        }

        [Test]
        public void WithoutAshenSigil_SleightBurnChangesNothing()
        {
            FightWith();
            player.SetHp(50);
            FireSleight();

            Assert.AreEqual(1, Cycle.BurnedCount);
            Assert.AreEqual(1f, director.Player.ActionSpeedMultiplier);
            Assert.AreEqual(50, player.Hp);
        }

        // ---- Tempered Coil ----

        [Test]
        public void TemperedCoil_PerfectReload_RestoresABurnedCard()
        {
            FightWith(RelicId.TemperedCoil);
            FireSleight();
            var burned = Cycle.BurnedPile.Single();
            EmptyDrawQueue();

            Assert.AreEqual(ReloadGrade.Perfect, PerfectReload());

            Assert.AreEqual(0, Cycle.BurnedCount);
            Assert.IsFalse(burned.IsBurnedThisFight);
        }

        [Test]
        public void WithoutTemperedCoil_PerfectReload_KeepsTheBurn()
        {
            FightWith();
            FireSleight();
            EmptyDrawQueue();

            Assert.AreEqual(ReloadGrade.Perfect, PerfectReload());

            Assert.AreEqual(1, Cycle.BurnedCount);
        }

        // ---- Iron Will Idol ----

        [Test]
        public void IronWill_NotArmedAtRoomStart()
        {
            FightWith(RelicId.IronWillIdol);
            Assert.IsFalse(director.Player.NextSleightZeroProof);
        }

        [Test]
        public void IronWill_FirstSleightAfterAReload_IsZeroProof_OnlyThatOne()
        {
            FightWith(RelicId.IronWillIdol);
            var sleights = new List<AttackPayload>();
            playerCombat.HitboxSpawned += hb => { if (hb.Payload.IsSleight) sleights.Add(hb.Payload); };
            EmptyDrawQueue();
            PlainReload();
            Assert.IsTrue(director.Player.NextSleightZeroProof);

            FireSleight();
            int first = sleights.Count;
            FireSleight();

            Assert.Greater(first, 0);
            Assert.IsTrue(sleights.Take(first).All(p => p.ZeroProof), "First Sleight after the reload.");
            Assert.IsTrue(sleights.Skip(first).All(p => !p.ZeroProof), "Consumed by the first one.");
            Assert.Greater(sleights.Count, first);
        }

        [Test]
        public void WithoutIronWill_ReloadDoesNotArm()
        {
            FightWith();
            EmptyDrawQueue();
            PlainReload();
            Assert.IsFalse(director.Player.NextSleightZeroProof);
        }

        // ---- Shattered Hourglass ----

        [Test]
        public void PlayerZeroBreak_RaisesClashWonByZero_WithTheLoser()
        {
            FightWith();
            var enemy = director.Enemies.First();
            var enemyCombat = enemy.GetComponent<CombatActionController>();
            CombatantStatus loser = null;
            bool? byZero = null;
            playerCombat.ClashWon += (l, z) => { loser = l; byZero = z; };

            int tick = Time.frameCount;
            var enemyHit = enemyCombat.Spawn(new AttackPayload(enemy.Status.OwnerId, Team.Enemy, 5, false, false, tick,
                CardShape.Strike, Element.Physical), Vector3.back);
            var zeroHit = playerCombat.Spawn(new AttackPayload(player.OwnerId, Team.Player, 0, true, false, tick + 1,
                CardShape.Strike, Element.Physical), Vector3.forward);
            ClashSystem.Resolve(zeroHit, enemyHit);

            Assert.AreEqual(true, byZero);
            Assert.AreSame(enemy.Status, loser);
        }

        [Test]
        public void Hourglass_ZeroWin_ReturnsASpentCard_AndAddsPosture()
        {
            FightWith(RelicId.ShatteredHourglass);
            var enemy = director.Enemies.First().Status;
            Cycle.TryPlay(0, out _);
            Assert.AreEqual(1, Cycle.DiscardCount);
            float posture = enemy.Posture;

            playerCombat.OnClashWon(enemy, true);

            Assert.AreEqual(0, Cycle.DiscardCount);
            Assert.AreEqual(posture + RelicRules.HourglassPostureDamage, enemy.Posture, 1e-4f);
        }

        [Test]
        public void Hourglass_NonZeroWin_DoesNothing()
        {
            FightWith(RelicId.ShatteredHourglass);
            var enemy = director.Enemies.First().Status;
            Cycle.TryPlay(0, out _);

            playerCombat.OnClashWon(enemy, false);

            Assert.AreEqual(1, Cycle.DiscardCount);
            Assert.AreEqual(0f, enemy.Posture);
        }

        [Test]
        public void AddPostureDamage_FillingTheBar_PostureBreaks()
        {
            FightWith();
            var enemy = director.Enemies.First().Status;
            bool broken = false;
            enemy.Broken += () => broken = true;

            enemy.AddPostureDamage(enemy.MaxPosture);

            Assert.IsTrue(broken);
            Assert.IsTrue(enemy.IsStunned);
            Assert.AreEqual(0f, enemy.Posture);
        }

        // ---- Cinder Lantern ----

        [Test]
        public void CinderLantern_FirstOverburnEachRoomIsFree()
        {
            FightWith(RelicId.CinderLantern);
            Assert.AreEqual(1, playerDeck.Reload.FreeOverburnsLeft);
            Assert.AreEqual(0, playerDeck.Reload.NextOverburnCost(player.MaxHp));
        }

        [Test]
        public void WithoutCinderLantern_OverburnCostsHp()
        {
            FightWith();
            Assert.AreEqual(0, playerDeck.Reload.FreeOverburnsLeft);
            Assert.AreEqual(OverburnRules.Cost(player.MaxHp, 0), playerDeck.Reload.NextOverburnCost(player.MaxHp));
        }
    }
}
