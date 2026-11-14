using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Scene-side owner of a combatant's <see cref="DeckCycle"/>. Builds runtime instances from the
    /// read-only starting deck, removes cards broken by a 0 from the current cycle, cracks a partial
    /// Sleight stock on any Card Break, and runs the deck's <see cref="ActiveReload"/>.
    /// </summary>
    public sealed class DeckRuntimeManager : MonoBehaviour
    {
        [SerializeField] private List<CardData> startingDeck = new List<CardData>();
        [Tooltip("0 = random seed each run.")]
        [SerializeField] private int seed;

        private CombatActionController controller;
        private List<RuntimeCardInstance> runDeck;
        private int roomValueMod;
        private Element? roomModElement;

        public DeckCycle Cycle { get; } = new DeckCycle();
        /// <summary>Active Reload for this deck (bar, jam, boost, Overburn). Ticked here on scaled time.</summary>
        public ActiveReload Reload { get; private set; }
        public event Action Changed;

        private void Awake()
        {
            controller = GetComponent<CombatActionController>();
            Cycle.Changed += () => Changed?.Invoke();
            Reload = new ActiveReload(Cycle, new System.Random(seed != 0 ? seed : Environment.TickCount));
            Reinitialize();
        }

        private void Update()
        {
            // Scaled time: hit-stop and the Deadlock freeze pause the bar, the boost and the stock timeout.
            Reload.Tick(Time.deltaTime);
            // Stocking is locked while the bar runs/jams, so a partial stock must not expire meanwhile.
            if (!Reload.LocksCardPlays) Cycle.AdvanceStockTimer(Time.deltaTime);
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

        /// <summary>
        /// Plays a run's persistent card instances (with their Forge upgrades and Premium) instead of fresh
        /// copies of the starting deck. Pass null to go back to the starting deck. Deals a fresh cycle.
        /// </summary>
        public void UseRunDeck(IEnumerable<RuntimeCardInstance> instances, int newSeed = 0)
        {
            runDeck = instances != null ? new List<RuntimeCardInstance>(instances) : null;
            seed = newSeed;
            Reinitialize();
        }

        /// <summary>
        /// Room modifier added to every card (or only cards of <paramref name="onlyElement"/>) as
        /// <see cref="RuntimeCardInstance.TemporaryRoomMod"/>. Applied on every <see cref="Reinitialize"/>.
        /// Zero-breakers ignore it (they never use integer math).
        /// </summary>
        public void SetRoomValueMod(int amount, Element? onlyElement = null)
        {
            roomValueMod = amount;
            roomModElement = onlyElement;
        }

        /// <summary>Fresh cycle from the starting (or run) deck with the room mod; also cancels any reload bar, clears the speed boost and resets the Overburn price.</summary>
        public void Reinitialize()
        {
            Reload?.Cancel();
            Reload?.ResetBoost();
            Reload?.ResetOverburnCost();
            var instances = new List<RuntimeCardInstance>();
            if (runDeck != null)
            {
                foreach (var card in runDeck)
                {
                    if (card == null) continue;
                    card.IsBurnedThisFight = false; // burns last one room
                    instances.Add(card);
                }
            }
            else
            {
                foreach (var data in startingDeck)
                    if (data != null) instances.Add(new RuntimeCardInstance(data));
            }
            foreach (var card in instances)
                card.TemporaryRoomMod = roomModElement == null || card.Data.Element == roomModElement ? roomValueMod : 0;
            Cycle.Initialize(instances, new System.Random(seed != 0 ? seed : Environment.TickCount));
        }

        private void HandleCardBroken(RuntimeCardInstance card, bool brokeByZero)
        {
            if (brokeByZero && card != null) Cycle.DeleteFromCurrentCycle(card);
            // Any Card Break cracks a partial Sleight stock (slot 1 burns unless the Floor of 3 protects it).
            Cycle.InterruptStock();
        }
    }
}
