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
        }

        [Test]
        public void Shop_CantAfford_DisablesTheTile_AndBuysNothing()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.TrySpend(director.Run.Currency); // broke
            shop.Refresh();
            int deck = director.Run.Deck.Count;

            screens.HandleKey(0);

            Assert.IsFalse(shop.Tile(0).Interactable);
            Assert.AreEqual(deck, director.Run.Deck.Count);
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
            shop.Deck.Pick(0);

            Assert.AreEqual(deck - 1, director.Run.Deck.Count);
            Assert.IsFalse(director.Run.OwnsCard(target));
            Assert.AreEqual(currency - cost, director.Run.Currency);
            Assert.IsFalse(shop.PurgeMode, "back to the stock after a purge");
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
        public void Shop_EscInPurgeMode_ReturnsToStock_ThenLeaves()
        {
            var shop = Enter<ShopView>(MapCardKind.Shop);
            director.Run.AddCurrency(500);
            shop.Refresh();
            shop.TogglePurge();

            screens.HandleBack();
            Assert.IsFalse(shop.PurgeMode);
            Assert.IsTrue(shop.IsOpen);

            screens.HandleBack();
            Assert.IsFalse(shop.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
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

            Assert.AreEqual(before + amount, card.PermanentRunUpgradeMod);
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
            screens.HandleConfirm();
            Assert.IsFalse(rest.IsOpen);
            Assert.AreEqual(RunDirectorState.ChoosingDoor, director.State);
            yield return null;
            Assert.IsFalse(director.Player.InputBlocked);
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
