using System;
using System.Collections.Generic;

namespace DeckArpg.Core
{
    /// <summary>The 6 relics. Rules in <see cref="RelicRules"/>; fight effects in Gameplay's RelicEffects (docs/specs/m3-relics.md).</summary>
    public enum RelicId
    {
        ShatteredHourglass,
        AshenSigil,
        IronWillIdol,
        TemperedCoil,
        CinderLantern,
        GildedQuill
    }

    public enum RunPhase
    {
        ChoosingDoor,
        InRoom,
        Complete
    }

    /// <summary>
    /// Everything that persists across rooms in one run: deck, currency, HP, Momentum, relics and floor progress.
    /// Flow: <see cref="DealOffer"/> -> <see cref="EnterRoom"/> -> <see cref="CompleteRoom"/>, repeated for rooms 1-6.
    /// Room 6 is always the boss; clearing it completes the run.
    /// </summary>
    public sealed class RunState
    {
        public const int BossRoom = 6;
        public const int BossIntensity = 7;

        private readonly List<RuntimeCardInstance> deck = new List<RuntimeCardInstance>();
        private readonly HashSet<RelicId> relics = new HashSet<RelicId>();
        private readonly List<MapCard> history = new List<MapCard>();
        private List<MapCard> currentOffer;

        public RunState(IEnumerable<CardData> startingDeck, int maxHp, int seed)
        {
            if (startingDeck == null) throw new ArgumentNullException(nameof(startingDeck));
            if (maxHp < 1) throw new ArgumentOutOfRangeException(nameof(maxHp));
            foreach (var card in startingDeck) if (card != null) deck.Add(new RuntimeCardInstance(card));
            MaxHp = maxHp;
            Hp = maxHp;
            Rng = new Random(seed);
        }

        public IReadOnlyList<RuntimeCardInstance> Deck => deck;
        public int Currency { get; private set; }
        public int MaxHp { get; }
        public int Hp { get; private set; }
        public Momentum Momentum { get; } = new Momentum();
        public IReadOnlyCollection<RelicId> Relics => relics;
        public int PurgeCount { get; internal set; }
        /// <summary>Shared seeded RNG for this run's rolls (offers, drafts, shops).</summary>
        public Random Rng { get; }

        public RunPhase Phase { get; private set; } = RunPhase.ChoosingDoor;
        /// <summary>The room being chosen or played (1-6).</summary>
        public int RoomNumber { get; private set; } = 1;
        public MapCard CurrentRoom { get; private set; }
        public IReadOnlyList<MapCard> CurrentOffer => currentOffer;
        public IReadOnlyList<MapCard> History => history;
        public bool PurpleOffered { get; private set; }
        public bool IsBossRoom => RoomNumber == BossRoom;

        /// <summary>
        /// Average Intensity of the combat rooms the player chose (0 if none). The boss room is fixed at
        /// <see cref="BossIntensity"/>, not chosen, so it's left out (M3 review).
        /// </summary>
        public float AverageIntensity
        {
            get
            {
                int sum = 0, count = 0;
                foreach (var room in history)
                {
                    if (!room.IsCombat || room.Kind == MapCardKind.Boss) continue;
                    sum += room.Number;
                    count++;
                }
                return count == 0 ? 0f : (float)sum / count;
            }
        }

        // ---- floor flow ----

        /// <summary>Deals (once per door) the cards to choose from. Room 6 offers only the boss.</summary>
        public IReadOnlyList<MapCard> DealOffer()
        {
            RequirePhase(RunPhase.ChoosingDoor);
            if (currentOffer != null) return currentOffer;

            currentOffer = IsBossRoom
                ? new List<MapCard> { new MapCard(MapCardKind.Boss, BossIntensity) }
                : MapOfferGenerator.Deal(RoomNumber, PurpleOffered, Momentum, Rng);
            foreach (var card in currentOffer)
                if (card.Kind == MapCardKind.EliteTrial) PurpleOffered = true;
            return currentOffer;
        }

        public MapCard EnterRoom(int offerIndex)
        {
            RequirePhase(RunPhase.ChoosingDoor);
            if (currentOffer == null) throw new InvalidOperationException("Deal the offer before entering a room.");
            if (offerIndex < 0 || offerIndex >= currentOffer.Count) throw new ArgumentOutOfRangeException(nameof(offerIndex));

            CurrentRoom = currentOffer[offerIndex];
            currentOffer = null;
            history.Add(CurrentRoom);
            Phase = RunPhase.InRoom;
            return CurrentRoom;
        }

        /// <summary>
        /// Finishes the current room. Combat rooms pay currency (using the streak held before this clear),
        /// update Momentum and return the draft reward; Blue rooms return null.
        /// </summary>
        public CombatReward CompleteRoom(IReadOnlyList<CardData> pool)
        {
            RequirePhase(RunPhase.InRoom);
            CombatReward reward = null;
            if (CurrentRoom.IsCombat)
            {
                if (pool == null) throw new ArgumentNullException(nameof(pool));
                bool quill = HasRelic(RelicId.GildedQuill);
                reward = RewardRoller.RollCombatReward(CurrentRoom, pool, Momentum, Rng,
                    quill ? RelicRules.GildedQuillExtraOptions : 0, quill ? RelicRules.GildedQuillExtraSkipBonus : 0, relics);
                AddCurrency(reward.Currency);
                Momentum.RecordCombatClear(CurrentRoom.Number);
            }

            if (CurrentRoom.Kind == MapCardKind.Boss) Phase = RunPhase.Complete;
            else
            {
                RoomNumber++;
                Phase = RunPhase.ChoosingDoor;
            }
            return reward;
        }

        // ---- resources ----

        public void AddCurrency(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Currency += amount;
        }

        /// <summary>Spends if affordable. Currency never goes negative.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount > Currency) return false;
            Currency -= amount;
            return true;
        }

        /// <summary>Syncs HP from the player after a fight. Clamped to [0, MaxHp].</summary>
        public void SetHp(int hp) => Hp = Math.Max(0, Math.Min(MaxHp, hp));

        public void Heal(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            SetHp(Hp + amount);
        }

        public RuntimeCardInstance AddCard(CardData data, int permanentUpgrade = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (permanentUpgrade < 0) throw new ArgumentOutOfRangeException(nameof(permanentUpgrade));
            var card = new RuntimeCardInstance(data) { PermanentRunUpgradeMod = permanentUpgrade };
            deck.Add(card);
            return card;
        }

        public bool RemoveCard(RuntimeCardInstance card) => card != null && deck.Remove(card);
        public bool OwnsCard(RuntimeCardInstance card) => card != null && deck.Contains(card);

        /// <summary>False if already owned.</summary>
        public bool AddRelic(RelicId relic) => relics.Add(relic);
        public bool HasRelic(RelicId relic) => relics.Contains(relic);

        private void RequirePhase(RunPhase phase)
        {
            if (Phase != phase) throw new InvalidOperationException($"Run is in {Phase}, expected {phase}.");
        }
    }
}
