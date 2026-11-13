using System.Collections;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DeckArpg.Tests.PlayMode
{
    public class HandHudViewTests
    {
        private GameObject player;
        private GameObject hud;
        private CardData[] cards;

        [SetUp]
        public void SetUp()
        {
            cards = new[] { CardData.Create("a", 5), CardData.Create("b", 6), CardData.Create("c", 7), CardData.Create("d", 2) };
            player = new GameObject("Player");
            player.AddComponent<CombatantStatus>();
            var deck = player.AddComponent<DeckRuntimeManager>();
            deck.SetStartingDeck(cards, newSeed: 3);
            hud = new GameObject("Hud");
            hud.AddComponent<HandHudView>().Bind(deck, player.GetComponent<CombatantStatus>(), null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(hud);
            Object.Destroy(player);
            foreach (var c in cards) Object.Destroy(c);
        }

        private string SlotValue(int i) =>
            hud.GetComponentsInChildren<Text>().First(t => t.name == "Value" && t.transform.parent.name == $"Slot{i + 1}").text;

        [UnityTest]
        public IEnumerator Slots_ShowHandValues()
        {
            yield return null;
            var cycle = player.GetComponent<DeckRuntimeManager>().Cycle;
            for (int i = 0; i < DeckCycle.HandSize; i++)
                Assert.AreEqual(cycle.Hand[i].GetEffectiveValue().ToString(), SlotValue(i));
        }

        [UnityTest]
        public IEnumerator Slot_UpdatesAfterPlay()
        {
            yield return null;
            var cycle = player.GetComponent<DeckRuntimeManager>().Cycle;
            cycle.TryPlay(0, out _); // draws the 4th card into slot 0
            Assert.AreEqual(cycle.Hand[0].GetEffectiveValue().ToString(), SlotValue(0));
            cycle.TryPlay(0, out _); // queue empty now: slot shows blank
            Assert.AreEqual("", SlotValue(0));
        }
    }
}
