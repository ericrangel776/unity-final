using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class BlueRoomTests
    {
        private TestCards cards;

        [SetUp] public void SetUp() => cards = new TestCards();
        [TearDown] public void TearDown() => cards.DestroyAll();

        private RunState Run(int deckSize = 10, int currency = 0, int maxHp = 100)
        {
            var run = new RunState(cards.DataSet(deckSize), maxHp, 1);
            run.AddCurrency(currency);
            return run;
        }

        private List<CardData> Pool() =>
            cards.DataSet(4, Rarity.Common).Concat(cards.DataSet(4, Rarity.Uncommon)).Concat(cards.DataSet(4, Rarity.Rare)).ToList();

        // ---- Shop ----

        [TestCase(Rarity.Common, 30)]
        [TestCase(Rarity.Uncommon, 50)]
        [TestCase(Rarity.Rare, 80)]
        public void Shop_Price(Rarity rarity, int price) => Assert.AreEqual(price, ShopRules.Price(rarity));

        [TestCase(2, 3)]
        [TestCase(5, 4)]
        [TestCase(8, 5)]
        public void Shop_StockGrowsWithQuality(int quality, int count) =>
            Assert.AreEqual(count, new ShopVisit(quality, Pool(), new Random(1)).Stock.Count);

        [TestCase(0, 5, 40)]
        [TestCase(2, 5, 80)]
        [TestCase(0, 7, 20)]
        [TestCase(1, 8, 30)]
        [TestCase(0, 3, 40)]
        public void Shop_PurgeCost_EscalatesAndHalvesAtHighQuality(int purges, int quality, int cost) =>
            Assert.AreEqual(cost, ShopRules.PurgeCost(purges, quality));

        [Test]
        public void Shop_Buy_SpendsAndAddsCard()
        {
            var run = Run(currency: 100);
            var shop = new ShopVisit(5, Pool(), new Random(2));
            var card = shop.Stock[0];
            Assert.IsTrue(shop.TryBuy(0, run));
            Assert.AreEqual(100 - ShopRules.Price(card.Rarity), run.Currency);
            Assert.AreSame(card, run.Deck.Last().Data);
            Assert.IsTrue(shop.IsSold(0));
        }

        [Test]
        public void Shop_Buy_SoldOutRejected()
        {
            var run = Run(currency: 500);
            var shop = new ShopVisit(5, Pool(), new Random(2));
            shop.TryBuy(0, run);
            int currency = run.Currency;
            Assert.IsFalse(shop.TryBuy(0, run));
            Assert.AreEqual(currency, run.Currency);
        }

        [Test]
        public void Shop_Buy_CantAfford_ChangesNothing()
        {
            var run = Run(currency: 29);
            var shop = new ShopVisit(5, Pool(), new Random(2));
            Assert.IsFalse(shop.TryBuy(0, run));
            Assert.AreEqual(29, run.Currency);
            Assert.AreEqual(10, run.Deck.Count);
            Assert.IsFalse(shop.IsSold(0));
        }

        [TestCase(-1)]
        [TestCase(99)]
        public void Shop_Buy_BadIndexRejected(int index) =>
            Assert.IsFalse(new ShopVisit(5, Pool(), new Random(2)).TryBuy(index, Run(currency: 500)));

        [Test]
        public void Shop_Purge_RemovesCard_AndRaisesNextPrice()
        {
            var run = Run(deckSize: 8, currency: 200);
            var shop = new ShopVisit(5, Pool(), new Random(2));
            var victim = run.Deck[0];
            Assert.IsTrue(shop.TryPurge(victim, run));
            CollectionAssert.DoesNotContain(run.Deck, victim);
            Assert.AreEqual(160, run.Currency);
            Assert.AreEqual(1, run.PurgeCount);
            Assert.AreEqual(60, shop.PurgeCost(run));
        }

        [Test]
        public void Shop_Purge_RefusedAtMinDeckSize()
        {
            var run = Run(deckSize: ShopRules.MinDeckSize, currency: 200);
            var shop = new ShopVisit(5, Pool(), new Random(2));
            Assert.IsFalse(shop.TryPurge(run.Deck[0], run));
            Assert.AreEqual(ShopRules.MinDeckSize, run.Deck.Count);
            Assert.AreEqual(200, run.Currency);
        }

        [Test]
        public void Shop_Purge_CantAfford_Refused()
        {
            var run = Run(currency: 39);
            Assert.IsFalse(new ShopVisit(5, Pool(), new Random(2)).TryPurge(run.Deck[0], run));
            Assert.AreEqual(10, run.Deck.Count);
        }

        [Test]
        public void Shop_Purge_CardNotInDeck_Refused()
        {
            var run = Run(currency: 200);
            var stranger = new RuntimeCardInstance(cards.Data(3));
            Assert.IsFalse(new ShopVisit(5, Pool(), new Random(2)).TryPurge(stranger, run));
            Assert.AreEqual(200, run.Currency);
        }

        // ---- Forge ----

        [TestCase(1, 1)]
        [TestCase(4, 2)]
        [TestCase(9, 3)]
        public void Forge_UpgradeAmountByQuality(int quality, int amount) =>
            Assert.AreEqual(amount, ForgeRules.UpgradeAmount(quality));

        private RunState RunWith(params CardData[] deck) => new RunState(deck, 100, 1);

        [Test]
        public void Forge_Upgrade_AddsPermanentMod()
        {
            var run = RunWith(cards.Data(3));
            var forge = new ForgeVisit(5);
            Assert.IsTrue(forge.TryUpgrade(run.Deck[0], run));
            Assert.AreEqual(2, run.Deck[0].PermanentRunUpgradeMod);
            Assert.AreEqual(5, run.Deck[0].GetEffectiveValue());
        }

        [Test]
        public void Forge_LowQuality_OneActionOnly()
        {
            var run = RunWith(cards.Data(3), cards.Data(4));
            var forge = new ForgeVisit(5);
            forge.TryUpgrade(run.Deck[0], run);
            Assert.IsFalse(forge.TryMakePremium(run.Deck[1], run));
            Assert.IsFalse(forge.TryUpgrade(run.Deck[1], run));
            Assert.IsTrue(forge.IsDone);
        }

        [Test]
        public void Forge_LowQuality_PremiumInsteadOfUpgrade()
        {
            var run = RunWith(cards.Data(3), cards.Data(4));
            var forge = new ForgeVisit(2);
            Assert.IsTrue(forge.TryMakePremium(run.Deck[0], run));
            Assert.IsTrue(run.Deck[0].IsPremium);
            Assert.IsFalse(forge.TryUpgrade(run.Deck[1], run));
        }

        [Test]
        public void Forge_HighQuality_UpgradeAndPremium_OnDifferentCards()
        {
            var run = RunWith(cards.Data(3), cards.Data(4));
            var forge = new ForgeVisit(8);
            Assert.IsTrue(forge.TryUpgrade(run.Deck[0], run));
            Assert.IsFalse(forge.TryMakePremium(run.Deck[0], run), "same card refused");
            Assert.IsTrue(forge.TryMakePremium(run.Deck[1], run));
            Assert.AreEqual(3, run.Deck[0].PermanentRunUpgradeMod);
            Assert.IsTrue(forge.IsDone);
        }

        [Test]
        public void Forge_RefusesZeroCards()
        {
            var run = RunWith(cards.Data(1, zero: true));
            var forge = new ForgeVisit(8);
            Assert.IsFalse(forge.TryUpgrade(run.Deck[0], run));
            Assert.IsFalse(forge.TryMakePremium(run.Deck[0], run));
        }

        [Test]
        public void Forge_RefusesUpgradeAtNine()
        {
            var run = RunWith(cards.Data(9), cards.Data(7));
            run.Deck[1].IsPremiumOverride = true; // 7 + 2 = 9
            var forge = new ForgeVisit(5);
            Assert.IsFalse(forge.TryUpgrade(run.Deck[0], run));
            Assert.IsFalse(forge.TryUpgrade(run.Deck[1], run));
            Assert.IsFalse(forge.IsDone);
        }

        [Test]
        public void Forge_RefusesPremiumTwice()
        {
            var run = RunWith(cards.Data(3));
            run.Deck[0].IsPremiumOverride = true;
            Assert.IsFalse(new ForgeVisit(5).TryMakePremium(run.Deck[0], run));
        }

        [Test]
        public void Forge_RefusesCardOutsideDeck()
        {
            var run = RunWith(cards.Data(3));
            var stranger = new RuntimeCardInstance(cards.Data(3));
            Assert.IsFalse(new ForgeVisit(5).TryUpgrade(stranger, run));
            Assert.AreEqual(0, stranger.PermanentRunUpgradeMod);
        }

        [Test]
        public void Forge_NeverMutatesTheAsset()
        {
            var data = cards.Data(3);
            var run = RunWith(data);
            new ForgeVisit(8).TryMakePremium(run.Deck[0], run);
            Assert.IsFalse(data.IsPremium);
            Assert.AreEqual(3, data.BaseValue);
        }

        // ---- Shop heal / Vital Draught ----

        [Test]
        public void Shop_HealAndDraught_Costs()
        {
            Assert.AreEqual(40, ShopRules.HealCost);
            Assert.AreEqual(60, ShopRules.VitalDraughtCost);
            Assert.AreEqual(10, ShopRules.VitalDraughtMaxHp);
        }

        [TestCase(2, 25)]
        [TestCase(5, 25)]
        [TestCase(7, 35)]
        [TestCase(9, 35)]
        public void Shop_HealPercent_ByBand(int quality, int percent) =>
            Assert.AreEqual(percent, ShopRules.HealPercent(quality));

        [TestCase(100, 5, 25)]
        [TestCase(100, 8, 35)]
        [TestCase(110, 5, 28)] // 27.5 -> 28
        [TestCase(33, 9, 12)]  // 11.55 -> 12
        public void Shop_HealAmount_RoundedUp(int maxHp, int quality, int heal) =>
            Assert.AreEqual(heal, ShopRules.HealAmount(maxHp, quality));

        [Test]
        public void Shop_Heal_SpendsHeals_OncePerVisit()
        {
            var run = Run(currency: 100);
            run.SetHp(50);
            var shop = new ShopVisit(5, Pool(), new Random(1));
            Assert.AreEqual(25, shop.HealAmount(run));
            Assert.IsTrue(shop.TryHeal(run));
            Assert.AreEqual(75, run.Hp);
            Assert.AreEqual(60, run.Currency);
            Assert.IsTrue(shop.HealUsed);

            Assert.IsFalse(shop.TryHeal(run));
            Assert.AreEqual(75, run.Hp);
            Assert.AreEqual(60, run.Currency);
        }

        [Test]
        public void Shop_Heal_ClampsAtMax()
        {
            var run = Run(currency: 100);
            run.SetHp(90);
            Assert.IsTrue(new ShopVisit(8, Pool(), new Random(1)).TryHeal(run));
            Assert.AreEqual(100, run.Hp);
        }

        [Test]
        public void Shop_Heal_RejectedAtFullHp_NothingSpent()
        {
            var run = Run(currency: 100);
            var shop = new ShopVisit(5, Pool(), new Random(1));
            Assert.IsFalse(shop.TryHeal(run));
            Assert.AreEqual(100, run.Currency);
            Assert.IsFalse(shop.HealUsed);
        }

        [Test]
        public void Shop_Heal_RejectedWhenUnaffordable()
        {
            var run = Run(currency: ShopRules.HealCost - 1);
            run.SetHp(50);
            var shop = new ShopVisit(5, Pool(), new Random(1));
            Assert.IsFalse(shop.TryHeal(run));
            Assert.AreEqual(50, run.Hp);
            Assert.AreEqual(ShopRules.HealCost - 1, run.Currency);
            Assert.IsFalse(shop.HealUsed);
        }

        [Test]
        public void Shop_Draught_RaisesMaxAndHeals_OncePerVisit()
        {
            var run = Run(currency: 200);
            run.SetHp(50);
            var shop = new ShopVisit(5, Pool(), new Random(1));
            Assert.IsTrue(shop.TryBuyDraught(run));
            Assert.AreEqual(110, run.MaxHp);
            Assert.AreEqual(60, run.Hp);
            Assert.AreEqual(140, run.Currency);
            Assert.IsTrue(shop.DraughtUsed);

            Assert.IsFalse(shop.TryBuyDraught(run));
            Assert.AreEqual(110, run.MaxHp);
            Assert.AreEqual(140, run.Currency);
        }

        [Test]
        public void Shop_Draught_AllowedAtFullHp()
        {
            var run = Run(currency: 60);
            Assert.IsTrue(new ShopVisit(5, Pool(), new Random(1)).TryBuyDraught(run));
            Assert.AreEqual(110, run.MaxHp);
            Assert.AreEqual(110, run.Hp);
        }

        [Test]
        public void Shop_Draught_RejectedWhenUnaffordable()
        {
            var run = Run(currency: ShopRules.VitalDraughtCost - 1);
            var shop = new ShopVisit(5, Pool(), new Random(1));
            Assert.IsFalse(shop.TryBuyDraught(run));
            Assert.AreEqual(100, run.MaxHp);
            Assert.IsFalse(shop.DraughtUsed);
        }

        [Test]
        public void Shop_HealAndDraught_AreIndependent()
        {
            var run = Run(currency: 100);
            run.SetHp(50);
            var shop = new ShopVisit(5, Pool(), new Random(1));
            Assert.IsTrue(shop.TryBuyDraught(run));
            Assert.IsTrue(shop.TryHeal(run));
            Assert.AreEqual(0, run.Currency);
            Assert.AreEqual(60 + 28, run.Hp); // 25% of 110 = 27.5 -> 28
        }

        // ---- Rest ----

        [TestCase(100, 2, 20)]
        [TestCase(100, 5, 30)]
        [TestCase(100, 9, 45)]
        [TestCase(33, 9, 15)] // 14.85 -> 15
        public void Rest_HealAmount_RoundedUp(int maxHp, int quality, int heal) =>
            Assert.AreEqual(heal, RestRules.HealAmount(maxHp, quality));

        [Test]
        public void Rest_Heal_CappedAtMax_OnceOnly()
        {
            var run = Run();
            run.SetHp(90);
            var rest = new RestVisit(5);
            Assert.IsTrue(rest.TryHeal(run));
            Assert.AreEqual(100, run.Hp);
            Assert.IsFalse(rest.TryHeal(run));
            Assert.IsFalse(rest.TryMeditate(run));
        }

        [Test]
        public void Rest_Meditate_AddsMomentum_InsteadOfHealing()
        {
            var run = Run();
            run.SetHp(50);
            var rest = new RestVisit(5);
            Assert.IsTrue(rest.TryMeditate(run));
            Assert.AreEqual(1, run.Momentum.Stacks);
            Assert.AreEqual(50, run.Hp);
            Assert.IsFalse(rest.TryHeal(run));
        }

        [TestCase(2, 8)]
        [TestCase(5, 10)]
        [TestCase(8, 12)]
        public void Rest_FortifyMaxHp_ByBand(int quality, int amount)
        {
            Assert.AreEqual(amount, RestRules.FortifyMaxHp(quality));
            Assert.AreEqual(amount, new RestVisit(quality).FortifyAmount);
        }

        [Test]
        public void Rest_Fortify_RaisesMaxAndHeals_EndsVisit()
        {
            var run = Run();
            run.SetHp(40);
            var rest = new RestVisit(5);
            Assert.IsTrue(rest.TryFortify(run));
            Assert.AreEqual(110, run.MaxHp);
            Assert.AreEqual(50, run.Hp);
            Assert.IsTrue(rest.IsDone);
            Assert.IsFalse(rest.TryFortify(run));
            Assert.IsFalse(rest.TryHeal(run));
            Assert.IsFalse(rest.TryMeditate(run));
            Assert.AreEqual(110, run.MaxHp);
        }

        [Test]
        public void Rest_Heal_ThenFortifyRejected()
        {
            var run = Run();
            run.SetHp(50);
            var rest = new RestVisit(5);
            Assert.IsTrue(rest.TryHeal(run));
            Assert.IsFalse(rest.TryFortify(run));
            Assert.AreEqual(100, run.MaxHp);
        }

        [TestCase(0)]
        [TestCase(10)]
        public void BlueVisits_RejectBadQuality(int quality)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ForgeVisit(quality));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RestVisit(quality));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShopVisit(quality, Pool(), new Random(1)));
        }
    }
}
