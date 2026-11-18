using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    public class DrawQueueStripTests
    {
        private GameObject player;
        private GameObject hud;
        private DeckRuntimeManager deck;
        private DrawQueueStripView strip;
        private readonly List<CardData> cards = new List<CardData>();

        private void Setup(int count)
        {
            for (int i = 0; i < count; i++) cards.Add(CardData.Create($"q{i}", 1 + i % 9));
            player = new GameObject("Player");
            player.AddComponent<CombatantStatus>();
            deck = player.AddComponent<DeckRuntimeManager>();
            deck.SetStartingDeck(cards, newSeed: 11);
            hud = new GameObject("Hud");
            strip = hud.AddComponent<DrawQueueStripView>();
            strip.Bind(deck);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(hud);
            Object.Destroy(player);
            foreach (var c in cards) Object.Destroy(c);
            cards.Clear();
        }

        private List<string> Expected() => deck.Cycle.DrawOrder.Select(c => c.GetEffectiveValue().ToString()).ToList();

        [UnityTest]
        public IEnumerator Chips_MatchTheDrawOrder()
        {
            Setup(9);
            yield return null;
            Assert.AreEqual(6, strip.ChipLabels.Count);
            CollectionAssert.AreEqual(Expected(), strip.ChipLabels);
        }

        [UnityTest]
        public IEnumerator Chips_ConsumeLeftToRightOnPlay()
        {
            Setup(9);
            yield return null;
            var before = strip.ChipLabels.ToList();
            deck.Cycle.TryPlay(0, out _);
            CollectionAssert.AreEqual(before.Skip(1), strip.ChipLabels);
        }

        [UnityTest]
        public IEnumerator Chips_RedrawAfterReloadAndShowReloadWhenEmpty()
        {
            Setup(6);
            yield return null;
            var cycle = deck.Cycle;
            while (cycle.DrawCount > 0) cycle.TryPlay(0, out _);
            Assert.AreEqual(0, strip.ChipLabels.Count);
            Assert.IsTrue(strip.ShowsReload);

            for (int i = 0; i < DeckCycle.HandSize; i++) cycle.TryPlay(i, out _);
            Assert.IsTrue(cycle.Reload());
            Assert.IsFalse(strip.ShowsReload);
            CollectionAssert.AreEqual(Expected(), strip.ChipLabels);
        }

        [UnityTest]
        public IEnumerator Chips_RedrawAfterAShuffle()
        {
            Setup(9);
            yield return null;
            deck.Reinitialize(); // reshuffles with a fresh deal
            CollectionAssert.AreEqual(Expected(), strip.ChipLabels);
        }

        [UnityTest]
        public IEnumerator HugeQueue_CollapsesIntoAPlusChip()
        {
            Setup(DrawQueueStripView.Capacity + 13);
            yield return null;
            int queued = deck.Cycle.DrawCount;
            Assert.Greater(queued, DrawQueueStripView.Capacity);
            Assert.AreEqual(DrawQueueStripView.Capacity, strip.ChipLabels.Count);
            int visibleCards = DrawQueueStripView.Capacity - 1;
            Assert.AreEqual($"+{queued - visibleCards}", strip.ChipLabels.Last());
            CollectionAssert.AreEqual(Expected().Take(visibleCards), strip.ChipLabels.Take(visibleCards));
        }

        [Test]
        public void ChipWidth_ShrinksThenStopsAtTheMinimum()
        {
            Assert.AreEqual(DrawQueueStripView.MaxChipWidth, DrawQueueStripView.ChipWidth(3));
            Assert.Less(DrawQueueStripView.ChipWidth(20), DrawQueueStripView.MaxChipWidth);
            Assert.AreEqual(DrawQueueStripView.MinChipWidth, DrawQueueStripView.ChipWidth(200));
            float used = DrawQueueStripView.Capacity * (DrawQueueStripView.MinChipWidth + DrawQueueStripView.Gap);
            Assert.LessOrEqual(used - DrawQueueStripView.Gap, DrawQueueStripView.StripWidth);
        }
    }
}
