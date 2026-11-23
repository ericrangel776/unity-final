using System;
using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>The reward screen: relic first, then the draft (docs/specs/m3-ui.md, sub-step 4).</summary>
    public class RewardViewTests : RunSceneTest
    {
        private RunScreens screens;
        private RewardView reward;

        protected override void OnRunLoaded()
        {
            screens = Object.FindAnyObjectByType<RunScreens>();
            reward = Object.FindAnyObjectByType<RewardView>();
            Assert.IsNotNull(reward, "RewardView missing from M3_Run (rebuild the scene).");
        }

        /// <summary>Room 1 only offers Red/Green fights at low Intensity, so door 1 is a plain fight with no relic.</summary>
        private void ClearPlainFight()
        {
            director.ChooseDoor(0);
            Assert.AreEqual(RunDirectorState.Fighting, director.State);
            director.DebugDefeatAllEnemies();
            Assert.AreEqual(RunDirectorState.Reward, director.State);
            Assert.AreEqual(0, director.CurrentReward.RelicOptions.Count, "room 1 shouldn't offer a relic");
        }

        private void ClearElite(Action beforeClear = null)
        {
            RunTestHelpers.EnterRoom(director, c => c.Kind == MapCardKind.EliteTrial);
            beforeClear?.Invoke();
            director.DebugDefeatAllEnemies();
            Assert.AreEqual(RunDirectorState.Reward, director.State);
        }

        // ---- plain fight ----

        [Test]
        public void Clear_OpensTheReward_WithTheCurrencyGained()
        {
            ClearPlainFight();
            Assert.IsTrue(reward.IsOpen);
            Assert.AreEqual(RewardView.Phase.Draft, reward.CurrentPhase);
            StringAssert.Contains($"+{director.CurrentReward.Currency} currency", reward.HeaderText);
            Assert.AreEqual(director.CurrentReward.Options.Count, reward.VisibleCount);
            Assert.AreEqual(director.CurrentReward.Options[0].DisplayName, reward.Tile(0).Title.text);
            Assert.IsTrue(reward.CanContinue);
        }

        [Test]
        public void Pick_AddsTheCardToTheDeck_AndMarksItTaken()
        {
            ClearPlainFight();
            int deckBefore = director.Run.Deck.Count;
            var picked = director.CurrentReward.Options[0];

            screens.HandleKey(0);

            Assert.AreEqual(deckBefore + 1, director.Run.Deck.Count);
            Assert.AreSame(picked, director.Run.Deck[director.Run.Deck.Count - 1].Data);
            Assert.AreEqual("TAKEN", reward.Tile(0).Footer.text);
            Assert.IsFalse(reward.Tile(0).Interactable);
        }

        [Test]
        public void LastPick_ClosesTheDraft_AndHidesSkip()
        {
            ClearPlainFight();
            for (int i = 0; i < director.CurrentReward.PicksAllowed; i++) screens.HandleKey(i);
            Assert.AreEqual(RewardView.Phase.Done, reward.CurrentPhase);
            Assert.IsFalse(reward.CanSkip);
            for (int i = 0; i < reward.VisibleCount; i++) Assert.IsFalse(reward.Tile(i).Interactable, $"tile {i}");
        }

        [Test]
        public void Skip_PaysTheSkipBonus_AndTakesNothing()
        {
            ClearPlainFight();
            int currency = director.Run.Currency;
            int deck = director.Run.Deck.Count;
            StringAssert.Contains($"+{director.CurrentReward.SkipBonus}", reward.SkipText);

            screens.HandleBack();

            Assert.AreEqual(currency + director.CurrentReward.SkipBonus, director.Run.Currency);
            Assert.AreEqual(deck, director.Run.Deck.Count);
            Assert.AreEqual(RewardView.Phase.Done, reward.CurrentPhase);
        }

        [UnityTest]
        public IEnumerator Continue_ClosesAndDealsTheNextDoor()
        {
            ClearPlainFight();
            screens.HandleKey(0);
            screens.HandleConfirm();
            Assert.IsFalse(reward.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            Assert.AreEqual(2, director.Run.RoomNumber);
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked);
        }

        [Test]
        public void FinishingTheRewardElsewhere_ClosesTheScreen()
        {
            ClearPlainFight();
            director.FinishReward(); // the Inspector debug menu does this
            Assert.IsFalse(reward.IsOpen);
            Assert.IsNull(screens.Current);
        }

        // ---- relics ----

        [Test]
        public void EliteClear_ForcesTheRelicPickFirst()
        {
            ClearElite();
            var options = director.CurrentReward.RelicOptions;
            Assert.Greater(options.Count, 0);
            Assert.AreEqual(RewardView.Phase.Relic, reward.CurrentPhase);
            Assert.AreEqual(RunUiText.RelicName(options[0]), reward.Tile(0).Title.text);
            Assert.IsFalse(reward.CanContinue);
            Assert.IsFalse(reward.CanSkip, "no skipping the relic");

            screens.HandleConfirm();
            screens.HandleBack();
            Assert.IsTrue(reward.IsOpen, "Enter/Esc can't leave without a relic");
            Assert.AreEqual(RunDirectorState.Reward, director.State);
        }

        [Test]
        public void PickingARelic_GrantsIt_ThenShowsTheDraft()
        {
            ClearElite();
            var relic = director.CurrentReward.RelicOptions[0];

            screens.HandleKey(0);

            Assert.IsTrue(director.Run.HasRelic(relic));
            Assert.IsTrue(director.PlayerRelics.Has(relic));
            Assert.AreEqual(RewardView.Phase.Draft, reward.CurrentPhase);
            Assert.AreEqual(director.CurrentReward.Options[0].DisplayName, reward.Tile(0).Title.text);
            Assert.IsTrue(reward.CanContinue);
        }

        [Test]
        public void AllRelicsOwned_SkipsTheRelicPhase_AndSaysWhy()
        {
            ClearElite(() =>
            {
                foreach (RelicId r in Enum.GetValues(typeof(RelicId))) director.Run.AddRelic(r);
            });
            Assert.IsTrue(director.CurrentReward.RelicFallbackPaid);
            Assert.AreEqual(RewardView.Phase.Draft, reward.CurrentPhase);
            StringAssert.Contains("All relics owned", reward.SubheaderText);
        }
    }
}
