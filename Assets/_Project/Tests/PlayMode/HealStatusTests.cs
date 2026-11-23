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
    /// <summary><see cref="CombatantStatus.Heal"/> and <see cref="CombatantStatus.SetMaxHpKeepHp"/> (heal rework, step 4).</summary>
    public class HealStatusTests : RunSceneTest
    {
        private GameObject go;
        private CombatantStatus status;
        private readonly List<int> healed = new List<int>();
        private readonly List<int> anyHealed = new List<int>();

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("HealBody");
            status = go.AddComponent<CombatantStatus>();
            status.Team = Team.Player;
            healed.Clear();
            anyHealed.Clear();
            status.Healed += healed.Add;
            CombatantStatus.AnyHealed += RecordAny;
        }

        [TearDown]
        public void TearDown()
        {
            CombatantStatus.AnyHealed -= RecordAny;
            if (go != null) Object.Destroy(go);
        }

        private void RecordAny(CombatantStatus who, int amount)
        {
            if (who == status) anyHealed.Add(amount);
        }

        private static AttackPayload Hit(int value) =>
            new AttackPayload(1, Team.Enemy, value, false, false, 0, CardShape.Strike, Element.Physical);

        [UnityTest]
        public IEnumerator Heal_RestoresHp_AndRaisesBothEvents()
        {
            status.SetHp(50);
            Assert.AreEqual(9, status.Heal(9));
            Assert.AreEqual(59, status.Hp);
            CollectionAssert.AreEqual(new[] { 9 }, healed);
            CollectionAssert.AreEqual(new[] { 9 }, anyHealed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Heal_ClampsToMax_AndReportsActualAmount()
        {
            status.SetHp(95);
            Assert.AreEqual(5, status.Heal(27));
            Assert.AreEqual(status.MaxHp, status.Hp);
            CollectionAssert.AreEqual(new[] { 5 }, anyHealed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Heal_AtFullHp_ReturnsZero_NoEvents()
        {
            Assert.AreEqual(0, status.Heal(9));
            CollectionAssert.IsEmpty(healed);
            CollectionAssert.IsEmpty(anyHealed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Heal_ZeroOrNegative_Ignored()
        {
            status.SetHp(50);
            Assert.AreEqual(0, status.Heal(0));
            Assert.AreEqual(0, status.Heal(-5));
            Assert.AreEqual(50, status.Hp);
            CollectionAssert.IsEmpty(anyHealed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Heal_IgnoresTheDead()
        {
            status.TakeHit(Hit(9));
            while (!status.IsDead) status.TakeHit(Hit(9));
            Assert.AreEqual(0, status.Heal(30));
            Assert.AreEqual(0, status.Hp);
            Assert.IsTrue(status.IsDead);
            CollectionAssert.IsEmpty(anyHealed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetMaxHpKeepHp_DoesNotRefill_AndClamps()
        {
            status.SetHp(40);
            status.SetMaxHpKeepHp(115, 55);
            Assert.AreEqual(115, status.MaxHp);
            Assert.AreEqual(55, status.Hp);

            status.SetMaxHpKeepHp(50, 80);
            Assert.AreEqual(50, status.MaxHp);
            Assert.AreEqual(50, status.Hp, "HP clamps to the new max");

            status.SetMaxHpKeepHp(0, 0);
            Assert.AreEqual(1, status.MaxHp);
            Assert.AreEqual(1, status.Hp, "never below 1 alive");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetMaxHpKeepHp_LeavesTheDeadDead()
        {
            while (!status.IsDead) status.TakeHit(Hit(9));
            status.SetMaxHpKeepHp(120, 60);
            Assert.AreEqual(120, status.MaxHp);
            Assert.IsTrue(status.IsDead);
            yield return null;
        }
    }

    /// <summary>The run's max HP reaches the player's status (heal rework, step 4).</summary>
    public class HealRunSyncTests
    {
        private CombatantStatus player;

        protected override void OnRunLoaded()
        {
            player = director.Player.GetComponent<CombatantStatus>();
        }

        [Test]
        public void RestFortify_ShowsOnPlayerStatus_AfterLeaving()
        {
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.Rest);
            int baseMax = director.Run.MaxHp;
            director.Run.SetHp(40);
            player.SetHp(40);
            int fortify = director.CurrentRest.FortifyAmount;

            Assert.IsTrue(director.CurrentRest.TryFortify(director.Run));
            director.LeaveUtility();

            Assert.AreEqual(baseMax + fortify, player.MaxHp);
            Assert.AreEqual(40 + fortify, player.Hp, "Fortify heals its amount but doesn't refill");
        }

        [Test]
        public void HeartwoodSeed_RaisesPlayerMaxHp_WhenChosen()
        {
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.EliteTrial);
            // Own every other relic, so the Elite's relic choice can only be Heartwood Seed.
            foreach (RelicId r in Enum.GetValues(typeof(RelicId)))
                if (r != RelicId.HeartwoodSeed) director.Run.AddRelic(r);
            int baseMax = director.Run.MaxHp;
            director.DebugDefeatAllEnemies();
            CollectionAssert.AreEqual(new[] { RelicId.HeartwoodSeed }, director.CurrentReward.RelicOptions);
            int hpBefore = player.Hp;

            Assert.IsTrue(director.ChooseRelic(0));

            Assert.AreEqual(baseMax + RelicRules.HeartwoodSeedMaxHp, player.MaxHp);
            Assert.AreEqual(Mathf.Min(player.MaxHp, hpBefore + RelicRules.HeartwoodSeedMaxHp), player.Hp);
        }

        [Test]
        public void RoomStart_SyncsMaxHp_WithoutRefill()
        {
            director.RestartRun(3);
            director.Run.IncreaseMaxHp(10);
            director.Run.SetHp(70);
            director.ChooseDoor(0);
            Assert.AreEqual(director.Run.MaxHp, player.MaxHp);
            Assert.AreEqual(70, player.Hp);
        }

        [Test]
        public void NewRun_DropsLastRunsMaxHpUpgrades()
        {
            director.RestartRun(3);
            int baseMax = director.Run.MaxHp;
            director.Run.IncreaseMaxHp(12);
            director.ChooseDoor(0);
            Assert.AreEqual(baseMax + 12, player.MaxHp);

            director.RestartRun(4);
            Assert.AreEqual(baseMax, director.Run.MaxHp);
            Assert.AreEqual(baseMax, director.Run.StartingMaxHp);
            Assert.AreEqual(baseMax, player.MaxHp);
            Assert.AreEqual(baseMax, player.Hp);
        }
    }
}
