using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Scene-side owner of a combatant's <see cref="DeckCycle"/>. Builds runtime instances from the
    /// read-only starting deck and removes cards broken by a 0 from the current cycle.
    /// </summary>
    public sealed class DeckRuntimeManager : MonoBehaviour
    {
        [SerializeField] private List<CardData> startingDeck = new List<CardData>();
        [Tooltip("0 = random seed each run.")]
        [SerializeField] private int seed;

        private CombatActionController controller;

        public DeckCycle Cycle { get; } = new DeckCycle();
        public event Action Changed;

        private void Awake()
        {
            controller = GetComponent<CombatActionController>();
            Cycle.Changed += () => Changed?.Invoke();
            Reinitialize();
        }

        private void OnEnable()
        {
            if (controller != null) controller.CardBroken += HandleCardBroken;
        }

        private void OnDisable()
        {
            if (controller != null) controller.CardBroken -= HandleCardBroken;
        }

        /// <summary>Replaces the starting deck and deals a fresh cycle (used by tests and builders).</summary>
        public void SetStartingDeck(IEnumerable<CardData> cards, int newSeed = 0)
        {
            startingDeck = new List<CardData>(cards);
            seed = newSeed;
            Reinitialize();
        }

        public void Reinitialize()
        {
            var instances = new List<RuntimeCardInstance>();
            foreach (var data in startingDeck)
                if (data != null) instances.Add(new RuntimeCardInstance(data));
            Cycle.Initialize(instances, new System.Random(seed != 0 ? seed : Environment.TickCount));
        }

        private void HandleCardBroken(RuntimeCardInstance card, bool brokeByZero)
        {
            if (brokeByZero && card != null) Cycle.DeleteFromCurrentCycle(card);
        }
    }
}
