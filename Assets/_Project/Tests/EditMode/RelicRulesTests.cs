using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Core relic rules (docs/specs/m3-relics.md): the offer, rewards, Gilded Quill and the deck/reload/clash hooks.</summary>
    public class RelicRulesTests
    {
        private sealed class FakeHp : IHpPool
        {
            public int Hp { get; set; }
            public int MaxHp { get; set; } = 100;
            public void SpendHp(int amount) => Hp -= amount;
        }

        private TestCards cards;
        private DeckCycle deck;

        [SetUp]
        public void SetUp()
        {
            cards = new TestCards();
            deck = new DeckCycle();
        }

        [TearDown] public void TearDown() => cards.DestroyAll();

        private static readonly RelicId[] AllRelics = (RelicId[])Enum.GetValues(typeof(RelicId));

        private static MapCard Elite(int n = 5) => new MapCard(MapCardKind.EliteTrial, n, EliteTrial.Veiled);

        private void Init(params int[] values) => deck.Initialize(cards.Numbered(values), new Random(42));

        private void FireSleight()
        {
            for (int s = 0; s < DeckCycle.HandSize; s++) deck.TryStock(s, out _);
        }

        // ---- RollOffer ----

        [Test]
        public void RollOffer_NoneOwned_GivesTwoDistinctRelics()
        {
            var offer = RelicRules.RollOffer(new List<RelicId>(), new Random(3));
            Assert.AreEqual(RelicRules.ChoiceSize, offer.Count);
            Assert.AreEqual(offer.Count, offer.Distinct().Count());
        }

        [Test]
        public void RollOffer_NeverOffersAnOwnedRelic()
        {
            var owned = new List<RelicId> { RelicId.AshenSigil, RelicId.GildedQuill, RelicId.TemperedCoil };
            for (int seed = 1; seed < 50; seed++)
                CollectionAssert.IsEmpty(RelicRules.RollOffer(owned, new Random(seed)).Intersect(owned));
        }

        [Test]
        public void RollOffer_OneLeft_OffersJustThatOne()
        {
            var owned = AllRelics.Where(r => r != RelicId.CinderLantern).ToList();
            CollectionAssert.AreEqual(new[] { RelicId.CinderLantern }, RelicRules.RollOffer(owned, new Random(1)));
        }

        [Test]
        public void RollOffer_AllOwned_IsEmpty()
        {
            CollectionAssert.IsEmpty(RelicRules.RollOffer(AllRelics, new Random(1)));
        }

        [Test]
        public void RollOffer_SameSeed_SameOffer()
        {
            CollectionAssert.AreEqual(RelicRules.RollOffer(null, new Random(9)), RelicRules.RollOffer(null, new Random(9)));
        }

        [Test]
        public void RollOffer_NullRng_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RelicRules.RollOffer(null, null));
        }

        // ---- Ashen Sigil multiplier ----

        [TestCase(0, 1f)]
        [TestCase(1, 1.1f)]
        [TestCase(3, 1.3f)]
        public void ActionSpeedMultiplier_AddsTenPercentPerStack(int stacks, float expected)
        {
            Assert.AreEqual(expected, RelicRules.ActionSpeedMultiplier(stacks), 1e-5f);
        }

        [Test]
        public void ActionSpeedMultiplier_NegativeStacks_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RelicRules.ActionSpeedMultiplier(-1));
        }

        // ---- rewards ----

        [Test]
        public void EliteReward_OffersTwoRelicOptions()
        {
            var reward = RewardRoller.RollCombatReward(Elite(), cards.DataSet(12), new Momentum(), new Random(1));
            Assert.IsTrue(reward.OffersRelic);
            Assert.AreEqual(2, reward.RelicOptions.Count);
            Assert.IsFalse(reward.RelicFallbackPaid);
        }

        [Test]
        public void EliteReward_AllRelicsOwned_PaysFallbackCurrencyInstead()
        {
            var pool = cards.DataSet(12);
            int normal = RewardRoller.RollCombatReward(Elite(), pool, new Momentum(), new Random(1)).Currency;

            var reward = RewardRoller.RollCombatReward(Elite(), pool, new Momentum(), new Random(1), ownedRelics: AllRelics);

            CollectionAssert.IsEmpty(reward.RelicOptions);
            Assert.IsTrue(reward.RelicFallbackPaid);
            Assert.AreEqual(normal + RelicRules.FallbackCurrency, reward.Currency);
        }

        [Test]
        public void NonRelicReward_HasNoRelicOptions()
        {
            var room = new MapCard(MapCardKind.Vanguard, 2);
            var reward = RewardRoller.RollCombatReward(room, cards.DataSet(12), new Momentum(), new Random(1));
            Assert.IsFalse(reward.OffersRelic);
            CollectionAssert.IsEmpty(reward.RelicOptions);
            Assert.IsFalse(reward.RelicFallbackPaid);
        }

        [Test]
        public void TryPickRelic_AddsItToTheRun_OnlyOnce()
        {
            var run = new RunState(cards.DataSet(10), 100, 1);
            var reward = new CombatReward(0, cards.DataSet(3), 1, 0, true,
                new List<RelicId> { RelicId.AshenSigil, RelicId.IronWillIdol });

            Assert.IsTrue(reward.TryPickRelic(1, run));
            Assert.IsTrue(run.HasRelic(RelicId.IronWillIdol));
            Assert.IsTrue(reward.IsRelicClaimed);

            Assert.IsFalse(reward.TryPickRelic(0, run));
            Assert.IsFalse(run.HasRelic(RelicId.AshenSigil));
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void TryPickRelic_BadIndex_ReturnsFalse(int index)
        {
            var run = new RunState(cards.DataSet(10), 100, 1);
            var reward = new CombatReward(0, cards.DataSet(3), 1, 0, true,
                new List<RelicId> { RelicId.AshenSigil, RelicId.IronWillIdol });

            Assert.IsFalse(reward.TryPickRelic(index, run));
            Assert.IsFalse(reward.IsRelicClaimed);
            CollectionAssert.IsEmpty(run.Relics);
        }

        [Test]
        public void TryPickRelic_NoRelicEarned_ReturnsFalse()
        {
            var reward = new CombatReward(0, cards.DataSet(3), 1, 0, false);
            Assert.IsFalse(reward.TryPickRelic(0, new RunState(cards.DataSet(10), 100, 1)));
        }

        // ---- Gilded Quill (through RunState) ----

        private CombatReward ClearFirstRoom(bool quill)
        {
            var pool = cards.DataSet(12);
            var run = new RunState(cards.DataSet(10), 100, 4);
            if (quill) run.AddRelic(RelicId.GildedQuill);
            run.DealOffer();
            run.EnterRoom(0);
            return run.CompleteRoom(pool);
        }

        [Test]
        public void GildedQuill_DraftOffersOneMoreCard()
        {
            int without = ClearFirstRoom(false).Options.Count;
            Assert.AreEqual(without + RelicRules.GildedQuillExtraOptions, ClearFirstRoom(true).Options.Count);
        }

        [Test]
        public void GildedQuill_SkipPaysTheExtraBonus()
        {
            var run = new RunState(cards.DataSet(10), 100, 1);
            var reward = ClearFirstRoom(true);
            Assert.AreEqual(RewardRules.SkipBonus + RelicRules.GildedQuillExtraSkipBonus, reward.SkipBonus);

            reward.Skip(run);
            Assert.AreEqual(reward.SkipBonus, run.Currency);
        }

        [Test]
        public void WithoutGildedQuill_SkipBonusIsTheBaseOne()
        {
            Assert.AreEqual(RewardRules.SkipBonus, ClearFirstRoom(false).SkipBonus);
        }

        // ---- DeckCycle hooks ----

        [Test]
        public void CardBurned_SleightBurn_ReportsSleightCause()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            var causes = new List<BurnCause>();
            deck.CardBurned += (_, cause) => causes.Add(cause);

            FireSleight();

            CollectionAssert.AreEqual(new[] { BurnCause.Sleight }, causes);
        }

        [Test]
        public void CardBurned_CrackedStock_ReportsStockCracked()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            var causes = new List<BurnCause>();
            deck.CardBurned += (_, cause) => causes.Add(cause);

            deck.TryStock(0, out _);
            deck.InterruptStock();

            CollectionAssert.AreEqual(new[] { BurnCause.StockCracked }, causes);
        }

        [Test]
        public void CardBurned_PremiumPlay_ReportsPremium()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            deck.Hand[0].IsPremiumOverride = true;
            var causes = new List<BurnCause>();
            deck.CardBurned += (_, cause) => causes.Add(cause);

            deck.TryPlay(0, out _);

            CollectionAssert.AreEqual(new[] { BurnCause.Premium }, causes);
        }

        [Test]
        public void CardBurned_BurnFromCycle_ReportsEffect()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            deck.TryPlay(0, out var played);
            var causes = new List<BurnCause>();
            deck.CardBurned += (_, cause) => causes.Add(cause);

            deck.BurnFromCycle(played);

            CollectionAssert.AreEqual(new[] { BurnCause.Effect }, causes);
        }

        [Test]
        public void ReturnRandomSpent_MovesADiscardCardToTheBottomOfTheDrawQueue()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            deck.TryPlay(0, out var played);
            int queued = deck.DrawCount;

            Assert.AreSame(played, deck.ReturnRandomSpentToDrawQueue());
            Assert.AreEqual(0, deck.DiscardCount);
            Assert.AreEqual(queued + 1, deck.DrawCount);
        }

        [Test]
        public void ReturnRandomSpent_EmptyQueue_FillsTheEmptyHandSlot()
        {
            Init(1, 2, 3, 4);
            deck.TryPlay(0, out _); // draws the last queued card
            deck.TryPlay(1, out var played); // slot 1 now empty
            Assert.AreEqual(0, deck.DrawCount);
            Assert.IsNull(deck.Hand[1]);

            var returned = deck.ReturnRandomSpentToDrawQueue();

            Assert.IsNotNull(returned);
            Assert.AreSame(returned, deck.Hand[1]);
            Assert.AreEqual(0, deck.DrawCount);
        }

        [Test]
        public void ReturnRandomSpent_EmptyDiscard_ReturnsNull()
        {
            Init(1, 2, 3, 4, 5);
            Assert.IsNull(deck.ReturnRandomSpentToDrawQueue());
        }

        [Test]
        public void RestoreRandomBurned_UnburnsIntoTheDrawQueue()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            FireSleight();
            var burnedCard = deck.BurnedPile.Single();
            int queued = deck.DrawCount;

            Assert.AreSame(burnedCard, deck.RestoreRandomBurnedToDrawQueue());
            Assert.IsFalse(burnedCard.IsBurnedThisFight);
            Assert.AreEqual(0, deck.BurnedCount);
            Assert.AreEqual(queued + 1, deck.DrawCount);
        }

        [Test]
        public void RestoreRandomBurned_NothingBurned_ReturnsNull()
        {
            Init(1, 2, 3, 4, 5);
            Assert.IsNull(deck.RestoreRandomBurnedToDrawQueue());
        }

        // ---- Cinder Lantern (free Overburn) ----

        /// <summary>7-card deck with one burned card and an empty draw queue, ready to Overburn.</summary>
        private ActiveReload ReadyToOverburn()
        {
            Init(1, 2, 3, 4, 5, 6, 7);
            FireSleight();
            while (deck.DrawCount > 0)
            {
                int slot = Array.FindIndex(new[] { deck.Hand[0], deck.Hand[1], deck.Hand[2] }, c => c != null);
                deck.TryPlay(slot, out _);
            }
            return new ActiveReload(deck, new Random(7));
        }

        [Test]
        public void FreeOverburn_CostsNoHp()
        {
            var reload = ReadyToOverburn();
            reload.FreeOverburnsPerRoom = 1;
            var hp = new FakeHp { Hp = 50 };

            Assert.AreEqual(0, reload.NextOverburnCost(hp.MaxHp));
            Assert.IsTrue(reload.BeginOverburn(hp));
            Assert.AreEqual(50, hp.Hp);
            Assert.AreEqual(0, reload.FreeOverburnsLeft);
        }

        [Test]
        public void FreeOverburn_DoesNotEscalate_NextCostsTenPercent()
        {
            var reload = ReadyToOverburn();
            reload.FreeOverburnsPerRoom = 1;
            reload.BeginOverburn(new FakeHp { Hp = 50 });

            Assert.AreEqual(OverburnRules.Cost(100, 0), reload.NextOverburnCost(100));
            Assert.AreEqual(0, reload.OverburnUsesThisRoom);
        }

        [Test]
        public void FreeOverburn_RefillsOnResetOverburnCost()
        {
            var reload = ReadyToOverburn();
            reload.FreeOverburnsPerRoom = 1;
            reload.BeginOverburn(new FakeHp { Hp = 50 });

            reload.ResetOverburnCost();

            Assert.AreEqual(1, reload.FreeOverburnsLeft);
        }

        [Test]
        public void NoFreeOverburns_ByDefault()
        {
            var reload = ReadyToOverburn();
            Assert.AreEqual(0, reload.FreeOverburnsLeft);
            Assert.AreEqual(OverburnRules.Cost(100, 0), reload.NextOverburnCost(100));
        }

        // ---- Iron Will Idol (zero-proof clash) ----

        private static AttackPayload PlayerSleight(int tick, bool zeroProof) =>
            new AttackPayload(1, Team.Player, 12, false, true, tick, CardShape.Strike, Element.Physical, 3, 1, zeroProof);

        private static AttackPayload EnemyZero(int tick) =>
            new AttackPayload(2, Team.Enemy, 0, true, false, tick, CardShape.Strike, Element.Physical);

        [Test]
        public void ZeroProofSleight_BeatsAZeroPlayedSecond()
        {
            var r = ClashResolver.Resolve(PlayerSleight(10, true), EnemyZero(11));
            Assert.AreEqual(ClashOutcome.AWins, r.Outcome);
            Assert.IsFalse(r.BrokeByZero);
        }

        [Test]
        public void ZeroProofSleight_BeatsAZero_InEitherArgumentOrder()
        {
            Assert.AreEqual(ClashOutcome.BWins, ClashResolver.Resolve(EnemyZero(11), PlayerSleight(10, true)).Outcome);
        }

        [Test]
        public void ZeroProofSleight_BeatsAZeroOnTheSameTick()
        {
            Assert.AreEqual(ClashOutcome.AWins, ClashResolver.Resolve(PlayerSleight(10, true), EnemyZero(10)).Outcome);
        }

        [Test]
        public void NormalSleight_StillLosesToAZeroPlayedSecond()
        {
            var r = ClashResolver.Resolve(PlayerSleight(10, false), EnemyZero(11));
            Assert.AreEqual(ClashOutcome.BWins, r.Outcome);
            Assert.IsTrue(r.BrokeByZero);
        }

        [Test]
        public void ZeroProof_IsIgnoredOnNonSleights()
        {
            var card = new AttackPayload(1, Team.Player, 5, false, false, 10, CardShape.Strike, Element.Physical,
                zeroProof: true);
            Assert.IsFalse(card.ZeroProof);
            Assert.AreEqual(ClashOutcome.BWins, ClashResolver.Resolve(card, EnemyZero(11)).Outcome);
        }
    }
}
