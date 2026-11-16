using System;
using System.Collections;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Shop / Forge / Rest screens (docs/specs/m3-ui.md, sub-step 5).</summary>
    public class BlueRoomViewTests
    {
        private const string SceneName = "M3_Run";
        private RunDirector director;
        private RunScreens screens;

        [UnitySetUp]
        public IEnumerator LoadRun()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneName);
            yield return null; // let Start run
            director = Object.FindAnyObjectByType<RunDirector>();
            screens = Object.FindAnyObjectByType<RunScreens>();
        }

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

        private T Enter<T>(MapCardKind kind) where T : UtilityView
        {
            RunTestHelpers.EnterRoom(director, c => c.Kind == kind);
            Assert.AreEqual(RunDirectorState.Utility, director.State);
            var view = Object.FindAnyObjectByType<T>();
            Assert.IsNotNull(view, $"{typeof(T).Name} missing from M3_Run (rebuild the scene).");
            Assert.IsTrue(view.IsOpen, $"{typeof(T).Name} should open on entering the room");
            return view;
        }

        private static int FirstEnabled(DeckListView list)
        {
            for (int i = 0; i < list.Count; i++) if (list.IsEnabled(i)) return i;
            Assert.Fail("No enabled card in the deck list.");
            return -1;
        }

        // ---- shop ----

        [Test]
        public void Shop_ShowsStockWithPrices()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500); // affordable: the footer is just the price
            shop.Refresh();
            var stock = director.CurrentShop.Stock;
            for (int i = 0; i < stock.Count; i++)
            {
                Assert.IsTrue(shop.Tile(i).Visible);
                Assert.AreEqual(stock[i].DisplayName, shop.Tile(i).Title.text);
                Assert.AreEqual($"{ShopRules.Price(stock[i].Rarity)} currency", shop.Tile(i).Footer.text);
            }
            for (int i = stock.Count; i < ShopView.MaxStock; i++) Assert.IsFalse(shop.Tile(i).Visible);
        }

        [Test]
        public void Shop_Buy_SpendsCurrency_AddsTheCard_AndMarksSold()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500);
            int currency = director.Run.Currency;
            int deck = director.Run.Deck.Count;
            var card = director.CurrentShop.Stock[0];

            screens.HandleKey(0);

            Assert.AreEqual(currency - ShopRules.Price(card.Rarity), director.Run.Currency);
            Assert.AreEqual(deck + 1, director.Run.Deck.Count);
            Assert.AreEqual("SOLD", shop.Tile(0).Footer.text);
            Assert.IsFalse(shop.Tile(0).Interactable);
            Assert.AreEqual($"Bought {card.DisplayName} (-{ShopRules.Price(card.Rarity)})", shop.MessageText);
            Assert.IsFalse(shop.MessageIsWarning);
        }

        [Test]
        public void Shop_CantAfford_ShowsTheShortfall_AndBuysNothing()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.TrySpend(director.Run.Currency); // broke
            shop.Refresh();
            int deck = director.Run.Deck.Count;
            int price = ShopRules.Price(director.CurrentShop.Stock[0].Rarity);
            StringAssert.Contains($"need {price} more", shop.Tile(0).Footer.text);

            screens.HandleKey(0);

            Assert.AreEqual(deck, director.Run.Deck.Count);
            Assert.AreEqual($"Need {price} more currency", shop.MessageText);
            Assert.IsTrue(shop.MessageIsWarning);
        }

        [Test]
        public void Shop_Purge_RemovesThePickedCard_ForTheCost()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500);
            shop.Refresh();
            int cost = director.CurrentShop.PurgeCost(director.Run);
            int currency = director.Run.Currency;
            int deck = director.Run.Deck.Count;
            var target = director.Run.Deck[0];

            shop.TogglePurge();
            Assert.IsTrue(shop.PurgeMode);
            Assert.IsTrue(shop.Deck.Visible);
            Assert.AreEqual(deck, shop.Deck.Count);
            StringAssert.Contains($"({cost} currency)", shop.PromptText);
            shop.Deck.Pick(0);
            Assert.IsTrue(shop.Deck.IsSelected(0));
            Assert.AreEqual(deck, director.Run.Deck.Count, "a click only selects");
            Assert.AreEqual(RunUiText.PurgePreview(target, cost, deck), shop.MessageText);
            Assert.IsTrue(shop.CanConfirmCard);

            screens.HandleConfirm();

            Assert.AreEqual(deck - 1, director.Run.Deck.Count);
            Assert.IsFalse(director.Run.OwnsCard(target));
            Assert.AreEqual(currency - cost, director.Run.Currency);
            Assert.IsFalse(shop.PurgeMode, "back to the stock after a purge");
            Assert.AreEqual($"Removed {target.Data.DisplayName} (-{cost})", shop.MessageText);
        }

        [Test]
        public void Shop_PurgeConfirm_WithNothingSelected_ChangesNothing()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500);
            shop.Refresh();
            int deck = director.Run.Deck.Count;
            shop.TogglePurge();
            Assert.IsFalse(shop.CanConfirmCard);

            screens.HandleConfirm();

            Assert.AreEqual(deck, director.Run.Deck.Count);
            Assert.IsTrue(shop.PurgeMode);
            Assert.IsTrue(shop.MessageIsWarning);
        }

        [Test]
        public void Shop_Purge_IsDisabledAtTheMinimumDeckSize()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500);
            while (director.Run.Deck.Count > ShopRules.MinDeckSize) director.Run.RemoveCard(director.Run.Deck[0]);
            shop.Refresh();
            Assert.IsFalse(shop.CanPurge);
        }

        [Test]
        public void Shop_EscInPurgeMode_ReturnsToStock_AndNeverLeaves()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500);
            shop.Refresh();
            shop.TogglePurge();

            screens.HandleBack();
            Assert.IsFalse(shop.PurgeMode);
            Assert.IsTrue(shop.IsOpen);

            screens.HandleBack();
            Assert.IsTrue(shop.IsOpen, "Esc never leaves a room");
            Assert.AreEqual(RunDirectorState.Utility, director.State);
        }

        [Test]
        public void Shop_Hints_MatchTheMode()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500);
            shop.Refresh();
            StringAssert.Contains("buy", shop.HintText);
            shop.TogglePurge();
            StringAssert.DoesNotContain("buy", shop.HintText);
            StringAssert.Contains("Choose a card to remove", shop.PromptText);
        }

        // ---- forge ----

        [Test]
        public void Forge_Upgrade_RaisesThePickedCard()
        {
            var forge = Enter<ForgeView>(MapCardKind.Forge);
            int amount = director.CurrentForge.UpgradeAmount;
            Assert.AreEqual($"+{amount}", forge.Tile(0).Badge.text);

            screens.HandleKey(0);
            Assert.AreEqual(ForgeView.Mode.Upgrade, forge.CurrentMode);
            Assert.IsTrue(forge.Deck.Visible);
            int index = FirstEnabled(forge.Deck);
            var card = director.Run.Deck[index];
            int before = card.PermanentRunUpgradeMod;

            forge.Deck.Pick(index);
            Assert.AreEqual(before, card.PermanentRunUpgradeMod, "a click only selects");
            Assert.AreEqual(RunUiText.UpgradePreview(card, amount), forge.MessageText);
            screens.HandleConfirm();

            Assert.AreEqual(before + amount, card.PermanentRunUpgradeMod);
            Assert.AreEqual($"{card.Data.DisplayName} is now {RunUiText.DeckValue(card)}", forge.MessageText);
            Assert.AreEqual(ForgeView.Mode.Actions, forge.CurrentMode);
            Assert.IsFalse(forge.Tile(0).Interactable, "one upgrade per visit");
            Assert.AreEqual(ForgeRules.GrantsBoth(director.CurrentForge.Quality), forge.Tile(1).Interactable,
                "Premium is still available only at Quality 7-9");
        }

        [Test]
        public void Forge_MakePremium_FlagsThePickedCard()
        {
            var forge = Enter<ForgeView>(MapCardKind.Forge);
            screens.HandleKey(1);
            Assert.AreEqual(ForgeView.Mode.Premium, forge.CurrentMode);
            int index = FirstEnabled(forge.Deck);
            var card = director.Run.Deck[index];

            forge.Deck.Pick(index);
            Assert.IsFalse(card.IsPremium, "a click only selects");
            screens.HandleConfirm();

            Assert.IsTrue(card.IsPremium);
            Assert.IsFalse(forge.Tile(1).Interactable);
        }

        [Test]
        public void Forge_EscFromTheCardList_GoesBackToActions()
        {
            var forge = Enter<ForgeView>(MapCardKind.Forge);
            screens.HandleKey(0);
            screens.HandleBack();
            Assert.AreEqual(ForgeView.Mode.Actions, forge.CurrentMode);
            Assert.IsTrue(forge.IsOpen);
            Assert.IsFalse(forge.Deck.Visible);
            screens.HandleBack();
            Assert.IsTrue(forge.IsOpen, "Esc never leaves a room");
        }

        [Test]
        public void Forge_ZeroCards_AreGreyedOutWithAReason()
        {
            var forge = Enter<ForgeView>(MapCardKind.Forge);
            screens.HandleKey(0);
            var deck = director.Run.Deck;
            bool sawZero = false;
            for (int i = 0; i < forge.Deck.Count; i++)
            {
                if (!deck[i].IsZeroBreaker) continue;
                sawZero = true;
                Assert.IsFalse(forge.Deck.IsEnabled(i));
                StringAssert.Contains("0-cards can't change", forge.Deck.Label(i));
            }
            if (!sawZero) Assert.Inconclusive("Starter deck has no 0-card to check.");
        }

        // ---- rest ----

        [Test]
        public void Rest_Heal_RestoresHp_AndEndsTheChoice()
        {
            var rest = Enter<RestView>(MapCardKind.Rest);
            director.Run.SetHp(10);
            int heal = director.CurrentRest.HealAmount(director.Run);

            screens.HandleKey(0);

            Assert.AreEqual(Math.Min(director.Run.MaxHp, 10 + heal), director.Run.Hp);
            StringAssert.StartsWith($"Healed {Math.Min(director.Run.MaxHp, 10 + heal) - 10} HP", rest.MessageText);
            Assert.AreEqual(0, rest.Chosen);
            Assert.IsFalse(rest.Tile(0).Interactable);
            Assert.IsFalse(rest.Tile(1).Interactable, "only one choice");
        }

        [Test]
        public void Rest_Meditate_AddsAMomentumStack()
        {
            var rest = Enter<RestView>(MapCardKind.Rest);
            director.Run.Momentum.Reset();

            screens.HandleKey(1);

            Assert.AreEqual(1, director.Run.Momentum.Stacks);
            Assert.AreEqual(1, rest.Chosen);
            Assert.AreEqual("CHOSEN", rest.Tile(1).Footer.text);
        }

        [UnityTest]
        public IEnumerator Rest_Leave_DealsTheNextDoor_AndFreesInput()
        {
            var rest = Enter<RestView>(MapCardKind.Rest);
            screens.HandleKey(0); // used, so Leave doesn't ask
            screens.HandleConfirm();
            Assert.IsFalse(rest.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked);
        }

        [Test]
        public void Rest_LeavingUnused_AsksOnce_ThenLeaves()
        {
            var rest = Enter<RestView>(MapCardKind.Rest);

            screens.HandleConfirm();
            Assert.IsTrue(rest.IsOpen, "the first Leave on an unused room only warns");
            Assert.IsTrue(rest.LeaveWarned);
            Assert.IsTrue(rest.MessageIsWarning);
            StringAssert.Contains("Leave without using", rest.MessageText);

            screens.HandleConfirm();
            Assert.IsFalse(rest.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
        }

        [Test]
        public void Rest_EscNeverLeaves()
        {
            var rest = Enter<RestView>(MapCardKind.Rest);
            screens.HandleBack();
            Assert.IsTrue(rest.IsOpen);
            Assert.AreEqual(RunDirectorState.Utility, director.State);
        }

        [UnityTest]
        public IEnumerator CombatHud_IsHiddenWhileTheScreenIsOpen_AndBackAfterLeaving()
        {
            Enter<RestView>(MapCardKind.Rest);
            var hand = Object.FindAnyObjectByType<HandHudView>();
            Assert.IsNotNull(hand, "HandHudView missing from M3_Run.");
            var group = hand.GetComponentInChildren<Canvas>().GetComponent<CanvasGroup>();
            Assert.IsTrue(screens.CombatHudHidden);
            Assert.IsNotNull(group);
            Assert.AreEqual(0f, group.alpha);

            screens.HandleKey(0);
            screens.HandleConfirm();
            yield return null;

            Assert.IsFalse(screens.CombatHudHidden);
            Assert.AreEqual(1f, group.alpha);
        }

        [Test]
        public void LeavingElsewhere_ClosesTheScreen()
        {
            var rest = Enter<RestView>(MapCardKind.Rest);
            director.LeaveUtility(); // the Inspector debug menu does this
            Assert.IsFalse(rest.IsOpen);
            Assert.IsNull(screens.Current);
        }
    }
}
