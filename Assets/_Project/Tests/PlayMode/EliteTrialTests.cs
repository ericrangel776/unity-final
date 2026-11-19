using System;
using System.Collections;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Purple Elite trials in the M3 run scene (docs/specs/m3-elite-trials.md).</summary>
    public class EliteTrialTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private CombatantStatus player;
        private CombatActionController playerCombat;
        private DeckRuntimeManager playerDeck;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            Assert.IsNotNull(director, "RunDirector missing from M3_Run.");
            player = director.Player.GetComponent<CombatantStatus>();
            playerCombat = director.Player.GetComponent<CombatActionController>();
            playerDeck = director.Player.GetComponent<DeckRuntimeManager>();
        }

        /// <summary>Unload so live enemies can't affect later fixtures (see CLAUDE.md lessons).</summary>
        [UnityTearDown]
        public IEnumerator UnloadRun()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Empty_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        // ---- helpers ----

        private MapCard EnterTrial(EliteTrial trial, Func<MapCard, bool> extra = null) =>
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.EliteTrial && c.Trial == trial && (extra == null || extra(c)));

        private TrainingDummyEnemy Elite => director.Enemies.Single();

        /// <summary>Freezes the elite's own swing AI and puts the player <paramref name="distance"/> m in front of it.</summary>
        private void StandBeforeElite(float distance)
        {
            var elite = Elite;
            elite.Target = null;
            var front = elite.transform.position + elite.transform.forward * distance;
            front.y = director.Player.transform.position.y;
            RunTestHelpers.Place(director.Player, front);
        }

        /// <summary>Plays the player's first hand card straight at the elite. Returns the card.</summary>
        private RuntimeCardInstance PlayCardAtElite(out AttackHitbox hitbox)
        {
            Assert.IsTrue(playerDeck.Cycle.TryPlay(0, out var card));
            var aim = Elite.transform.position - director.Player.transform.position;
            hitbox = playerCombat.Spawn(AttackPayload.FromCard(card, player.OwnerId, Team.Player, Time.frameCount), aim, card);
            return card;
        }

        private static IEnumerator WaitUntilRealtime(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        // ---- setup per trial ----

        [Test]
        public void Elite_GetsTheBiggerDeathPop()
        {
            EnterTrial(EliteTrial.Veiled);
            var pop = Elite.GetComponent<DeathPop>();
            Assert.IsNotNull(pop, "DeathPop missing from the enemy prefab (rebuild the arena).");
            Assert.AreEqual(DeathPop.EliteScale, pop.Scale, 1e-5f);
        }

        [Test]
        public void Veiled_SetsRevealTimeFromIntensity()
        {
            var room = EnterTrial(EliteTrial.Veiled);
            Assert.AreEqual(RoomIntensity.VeiledRevealSeconds(room.Number), Elite.VeiledRevealSeconds, 1e-5f);
            Assert.IsNull(Elite.GetComponent<CounterpuncherTrial>());
        }

        [Test]
        public void Counterpuncher_AddsZerosByIntensity_AndWatchesThePlayer()
        {
            var room = EnterTrial(EliteTrial.Counterpuncher);
            Assert.AreEqual(RoomIntensity.CounterpuncherZeros(room.Number), Elite.Deck.Cycle.ZerosReady);
            Assert.IsNotNull(Elite.GetComponent<CounterpuncherTrial>());
            Assert.AreEqual(0f, Elite.VeiledRevealSeconds);
        }

        [Test]
        public void OtherTrials_AddNoZeros()
        {
            EnterTrial(EliteTrial.Hexer);
            Assert.AreEqual(0, Elite.Deck.Cycle.ZerosReady);
            Assert.IsNull(Elite.GetComponent<CounterpuncherTrial>());
        }

        // ---- Veiled ----

        [UnityTest]
        public IEnumerator Veiled_ShowsQuestionMark_ThenRevealsBeforeTheSwing()
        {
            EnterTrial(EliteTrial.Veiled);
            var elite = Elite;
            RunTestHelpers.Place(director.Player, elite.transform.position + elite.transform.forward * 1.5f);

            yield return WaitUntilRealtime(() => elite == null || elite.CurrentTelegraph != null, 6f);
            Assert.IsNotNull(elite.CurrentTelegraph, "The elite never telegraphed.");
            Assert.IsTrue(elite.IsTelegraphVeiled, "Value is hidden at the start of the wind-up.");

            // Freeze so the view's LateUpdate draws this same telegraph (see CLAUDE.md lessons).
            Time.timeScale = 0f;
            yield return null;
            yield return null;
            var value = elite.GetComponentsInChildren<Text>().Single(t => t.name == "Value");
            Assert.AreEqual("?", value.text);
            Time.timeScale = 1f;

            yield return WaitUntilRealtime(() => !elite.IsTelegraphVeiled, 2f);
            Assert.IsFalse(elite.IsTelegraphVeiled);
            Assert.IsNotNull(elite.CurrentTelegraph, "The value is revealed before the swing, not at it.");
        }

        // ---- Counterpuncher ----

        [UnityTest]
        public IEnumerator Counterpuncher_BreaksAPlayedCard_WithAZero()
        {
            EnterTrial(EliteTrial.Counterpuncher);
            StandBeforeElite(2.8f);
            var counter = Elite.GetComponent<CounterpuncherTrial>();
            int zerosBefore = Elite.Deck.Cycle.ZerosReady;
            bool? brokeByZero = null;
            playerCombat.CardBroken += (_, zero) => brokeByZero = zero;

            PlayCardAtElite(out _);
            Assert.AreEqual(zerosBefore - 1, Elite.Deck.Cycle.ZerosReady, "The 0 is spent at once.");
            yield return WaitUntilRealtime(() => brokeByZero.HasValue, 1f);

            Assert.AreEqual(1, counter.CountersFired);
            Assert.AreEqual(true, brokeByZero, "The answer comes a frame later, so the 0 wins.");
        }

        [UnityTest]
        public IEnumerator Counterpuncher_IgnoresAttacksOutOfRange()
        {
            EnterTrial(EliteTrial.Counterpuncher);
            var counter = Elite.GetComponent<CounterpuncherTrial>();
            StandBeforeElite(counter.CounterRange + 2f);
            int zerosBefore = Elite.Deck.Cycle.ZerosReady;

            PlayCardAtElite(out _);
            yield return WaitRealtime(0.2f);

            Assert.AreEqual(0, counter.CountersFired);
            Assert.AreEqual(zerosBefore, Elite.Deck.Cycle.ZerosReady);
        }

        [UnityTest]
        public IEnumerator Counterpuncher_WithOneZeroReady_LetsASleightThrough()
        {
            EnterTrial(EliteTrial.Counterpuncher);
            StandBeforeElite(2.8f);
            var counter = Elite.GetComponent<CounterpuncherTrial>();
            var cycle = Elite.Deck.Cycle;
            while (cycle.ZerosReady > 1) cycle.TryPlayParry(out _);
            Assert.AreEqual(1, cycle.ZerosReady);

            var aim = Elite.transform.position - director.Player.transform.position;
            playerCombat.SpawnSleight(new AttackPayload(player.OwnerId, Team.Player, 12, false, true, Time.frameCount,
                CardShape.Strike, Element.Physical, 3, SleightRules.HitCount(CardShape.Strike)), aim);
            yield return WaitRealtime(0.5f); // every hit of the combo has spawned

            Assert.AreEqual(0, counter.CountersFired);
            Assert.AreEqual(1, cycle.ZerosReady);
        }

        [UnityTest]
        public IEnumerator Counterpuncher_WithTwoZerosReady_AnswersASleightOnce_SpendingBoth()
        {
            EnterTrial(EliteTrial.Counterpuncher, c => c.Number >= 4);
            StandBeforeElite(2.8f);
            var counter = Elite.GetComponent<CounterpuncherTrial>();
            var cycle = Elite.Deck.Cycle;
            int zerosBefore = cycle.ZerosReady;
            Assert.GreaterOrEqual(zerosBefore, 2);

            var aim = Elite.transform.position - director.Player.transform.position;
            playerCombat.SpawnSleight(new AttackPayload(player.OwnerId, Team.Player, 12, false, true, Time.frameCount,
                CardShape.Strike, Element.Physical, 3, SleightRules.HitCount(CardShape.Strike)), aim);
            yield return WaitRealtime(0.5f);

            Assert.AreEqual(1, counter.CountersFired);
            Assert.AreEqual(zerosBefore - 2, cycle.ZerosReady);
        }

        // ---- Hexer ----

        [Test]
        public void Hexer_BrokenCard_IsBurnedForTheRoom()
        {
            EnterTrial(EliteTrial.Hexer);
            var card = PlayCardAtElite(out var hitbox);

            playerCombat.OnClashLost(hitbox, false, 1, Vector3.back);

            Assert.IsTrue(card.IsBurnedThisFight);
            CollectionAssert.Contains(playerDeck.Cycle.BurnedPile, card);
        }

        [Test]
        public void Hexer_CardBrokenByAZero_IsBurnedNotSetAside()
        {
            EnterTrial(EliteTrial.Hexer);
            var card = PlayCardAtElite(out var hitbox);

            playerCombat.OnClashLost(hitbox, true, 1, Vector3.back);

            Assert.IsTrue(card.IsBurnedThisFight);
            Assert.AreEqual(0, playerDeck.Cycle.DeletedThisCycleCount);
        }

        [Test]
        public void Hexer_BelowIntensity7_CostsNoHp()
        {
            EnterTrial(EliteTrial.Hexer, c => c.Number < 7);
            int hp = player.Hp;
            PlayCardAtElite(out var hitbox);

            playerCombat.OnClashLost(hitbox, false, 1, Vector3.back);

            Assert.AreEqual(hp, player.Hp);
        }

        [Test]
        public void Hexer_AtIntensity7Plus_EachBreakCostsHexerHp()
        {
            var room = EnterTrial(EliteTrial.Hexer, c => c.Number >= 7);
            int hp = player.Hp;
            PlayCardAtElite(out var hitbox);

            playerCombat.OnClashLost(hitbox, false, 1, Vector3.back);

            Assert.AreEqual(hp - RoomIntensity.HexerHpPerBreak(room.Number), player.Hp);
        }

        [Test]
        public void Hexer_BrokenPoke_BurnsNothing()
        {
            EnterTrial(EliteTrial.Hexer);
            var poke = playerCombat.Spawn(AttackPayload.Poke(player.OwnerId, Team.Player, Time.frameCount), Vector3.forward);

            playerCombat.OnClashLost(poke, false, 1, Vector3.back);

            Assert.AreEqual(0, playerDeck.Cycle.BurnedCount);
        }

        [Test]
        public void NonHexerRoom_BrokenCard_IsNotBurned()
        {
            EnterTrial(EliteTrial.Veiled);
            var card = PlayCardAtElite(out var hitbox);

            playerCombat.OnClashLost(hitbox, false, 1, Vector3.back);

            Assert.IsFalse(card.IsBurnedThisFight);
        }
    }
}
