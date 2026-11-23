using System.Collections;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Elite rework in the run scene: lighter hits, softer breaks, and the Overextended window (docs/specs/elite-rework.md).</summary>
    public class EliteOpeningTests : RunSceneTest
    {
        private const float Eps = 1e-4f;

        private CombatantStatus player;
        private CombatActionController playerCombat;
        private DeckRuntimeManager playerDeck;

        protected override void OnRunLoaded()
        {
            player = director.Player.GetComponent<CombatantStatus>();
            playerCombat = director.Player.GetComponent<CombatActionController>();
            playerDeck = director.Player.GetComponent<DeckRuntimeManager>();
        }

        private TrainingDummyEnemy EnterElite(int intensity = 2)
        {
            director.DebugEnterRoom(new MapCard(MapCardKind.EliteTrial, intensity, EliteTrial.Hexer));
            return director.Enemies.Single();
        }

        /// <summary>Re-deals the elite's deck with every card shifted by <paramref name="mod"/> (+8 = all 9s, -8 = all 1s).</summary>
        private static void ShiftEliteDeck(TrainingDummyEnemy elite, int mod)
        {
            elite.Deck.SetRoomValueMod(mod);
            elite.Deck.Reinitialize();
        }

        /// <summary>Breaks a hand card (margin 1) and returns the stun right after and the posture it added.</summary>
        private (float stun, float posture) BreakPlayerCard()
        {
            float postureBefore = player.Posture;
            Assert.IsTrue(playerDeck.Cycle.TryPlay(playerDeck.Cycle.FirstFilledSlot(), out var card));
            var hitbox = playerCombat.Spawn(AttackPayload.FromCard(card, player.OwnerId, Team.Player, Time.frameCount),
                Vector3.forward, card);
            playerCombat.OnClashLost(hitbox, false, 1, Vector3.back);
            Assert.Greater(player.Posture, postureBefore, "no posture break (the bar would reset)");
            return (player.StunRemaining, player.Posture - postureBefore);
        }

        // ---- setup ----

        [Test]
        public void Elite_HitsLighter_AndHasTheOpening()
        {
            var elite = EnterElite();
            Assert.AreEqual(EliteRules.DamageDealtMultiplier, elite.GetComponent<CombatActionController>().OutgoingDamageScale, Eps);
            Assert.IsNotNull(elite.GetComponent<EliteOpening>());
        }

        [Test]
        public void Mobs_KeepFullDamage_AndNoOpening()
        {
            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 2));
            foreach (var mob in director.Enemies)
            {
                Assert.AreEqual(1f, mob.GetComponent<CombatActionController>().OutgoingDamageScale, Eps);
                Assert.IsNull(mob.GetComponent<EliteOpening>());
            }
        }

        [Test]
        public void PlayerScales_ApplyInAnEliteRoom_AndResetInTheNextRoom()
        {
            EnterElite();
            Assert.AreEqual(EliteRules.PlayerBreakStunScale, playerCombat.BreakStunScale, Eps);
            Assert.AreEqual(EliteRules.PlayerPostureTakenScale, player.PostureTakenMultiplier, Eps);

            director.DebugEnterRoom(new MapCard(MapCardKind.Vanguard, 3));
            Assert.AreEqual(1f, playerCombat.BreakStunScale, Eps);
            Assert.AreEqual(1f, player.PostureTakenMultiplier, Eps);
        }

        [Test]
        public void PlayerBreak_InAnEliteRoom_StunsAndStaggersLess()
        {
            EnterElite();
            var softened = BreakPlayerCard();

            // Same room and lone Elite, with the scales put back to 1 by hand: the full break. Its stun is longer, so
            // it replaces the first one (stun takes the max).
            playerCombat.BreakStunScale = 1f;
            player.PostureTakenMultiplier = 1f;
            var full = BreakPlayerCard();

            Assert.Greater(full.stun, 0f);
            Assert.AreEqual(full.stun * EliteRules.PlayerBreakStunScale, softened.stun, Eps);
            Assert.AreEqual(full.posture * EliteRules.PlayerPostureTakenScale, softened.posture, 1e-3f);
        }

        // ---- Overextended ----

        [UnityTest]
        public IEnumerator HeavySwing_WindsUpLonger_ThenOpensTheWindow_ThatCloses()
        {
            var elite = EnterElite();
            yield return null; // the elite's Start (it enters Recover) must run before we force a wind-up
            var opening = elite.GetComponent<EliteOpening>();
            ShiftEliteDeck(elite, 8);
            bool swung = false;
            elite.Swung += _ => swung = true;

            float start = Time.realtimeSinceStartup;
            Assert.IsTrue(elite.DebugForceTelegraph());
            Assert.IsTrue(EliteRules.IsHeavy(elite.CurrentTelegraph));
            yield return TestWait.UntilRealtime(() => swung, 3f);
            Assert.IsTrue(swung, "The elite never swung.");
            Assert.GreaterOrEqual(Time.realtimeSinceStartup - start, elite.TelegraphSeconds + EliteRules.HeavyTelegraphBonusSeconds - 0.02f,
                "A heavy wind-up is longer.");

            Assert.IsTrue(opening.IsOverextended);
            Assert.AreEqual(EliteRules.OverextendDamageMultiplier, elite.Status.DamageTakenMultiplier, Eps);
            Assert.AreEqual(EliteRules.OverextendDamageMultiplier, elite.Status.PostureTakenMultiplier, Eps);

            yield return TestWait.UntilRealtime(() => !opening.IsOverextended, elite.RecoverSeconds + EliteRules.OverextendSeconds + 1f);
            Assert.IsFalse(opening.IsOverextended, "The window closes.");
            Assert.AreEqual(1f, elite.Status.DamageTakenMultiplier, Eps);
            Assert.AreEqual(1f, elite.Status.PostureTakenMultiplier, Eps);
        }

        [UnityTest]
        public IEnumerator LightSwing_OpensNoWindow()
        {
            var elite = EnterElite();
            yield return null; // the elite's Start (it enters Recover) must run before we force a wind-up
            var opening = elite.GetComponent<EliteOpening>();
            ShiftEliteDeck(elite, -8);
            bool swung = false;
            elite.Swung += _ => swung = true;

            Assert.IsTrue(elite.DebugForceTelegraph());
            Assert.IsFalse(EliteRules.IsHeavy(elite.CurrentTelegraph));
            yield return TestWait.UntilRealtime(() => swung, 3f);
            Assert.IsTrue(swung, "The elite never swung.");

            Assert.IsFalse(opening.IsOverextended);
            Assert.AreEqual(0, opening.WindowsOpened);
            Assert.AreEqual(1f, elite.Status.DamageTakenMultiplier, Eps);
        }

        [Test]
        public void Overextended_TakesMoreDamage()
        {
            var elite = EnterElite();
            var hit = new AttackPayload(player.OwnerId, Team.Player, 4, false, false, Time.frameCount,
                CardShape.Strike, Element.Physical);

            int before = elite.Status.Hp;
            elite.Status.TakeHit(hit, Vector3.zero, canCounter: false);
            int normal = before - elite.Status.Hp;

            elite.GetComponent<EliteOpening>().Open(1f);
            before = elite.Status.Hp;
            elite.Status.TakeHit(hit, Vector3.zero, canCounter: false);
            int opened = before - elite.Status.Hp;

            Assert.Greater(normal, 0);
            Assert.AreEqual(Mathf.RoundToInt(normal * EliteRules.OverextendDamageMultiplier), opened);
        }

        [Test]
        public void Death_ClosesTheWindow()
        {
            var elite = EnterElite();
            var opening = elite.GetComponent<EliteOpening>();
            opening.Open(5f);

            director.DebugDefeatAllEnemies();

            Assert.IsTrue(elite.Status.IsDead);
            Assert.IsFalse(opening.IsOverextended);
            Assert.AreEqual(1f, elite.Status.DamageTakenMultiplier, Eps);
        }
    }
}
