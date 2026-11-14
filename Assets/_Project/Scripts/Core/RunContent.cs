using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeckArpg.Core
{
    /// <summary>
    /// Everything a run draws its cards from: the starter deck, the draft pool and the enemy decks.
    /// Read-only at runtime like <see cref="CardData"/>; the Editor content builder authors it.
    /// </summary>
    [CreateAssetMenu(fileName = "RunContent", menuName = "DeckArpg/Run Content")]
    public sealed class RunContent : ScriptableObject
    {
        public const int MinDraftPoolSize = 15;
        public const int MinEnemyDeckSize = 4;
        public const int BossDeckSize = 15;

        [SerializeField] private List<CardData> starterDeck = new List<CardData>();
        [SerializeField] private List<CardData> draftPool = new List<CardData>();
        [Tooltip("Vanguard and Green rooms.")]
        [SerializeField] private List<CardData> mobDeck = new List<CardData>();
        [SerializeField] private List<CardData> hexfireDeck = new List<CardData>();
        [SerializeField] private List<CardData> eliteDeck = new List<CardData>();
        [SerializeField] private List<CardData> bossDeck = new List<CardData>();
        [Tooltip("0-card copies the Counterpuncher trial adds to the elite deck.")]
        [SerializeField] private CardData enemyZero;

        public IReadOnlyList<CardData> StarterDeck => starterDeck;
        public IReadOnlyList<CardData> DraftPool => draftPool;
        public IReadOnlyList<CardData> MobDeck => mobDeck;
        public IReadOnlyList<CardData> HexfireDeck => hexfireDeck;
        public IReadOnlyList<CardData> EliteDeck => eliteDeck;
        public IReadOnlyList<CardData> BossDeck => bossDeck;
        public CardData EnemyZero => enemyZero;

        /// <summary>Enemy deck for a combat room kind.</summary>
        public IReadOnlyList<CardData> EnemyDeckFor(MapCardKind kind)
        {
            switch (kind)
            {
                case MapCardKind.Hexfire: return hexfireDeck;
                case MapCardKind.EliteTrial: return eliteDeck;
                case MapCardKind.Boss: return bossDeck;
                case MapCardKind.Vanguard:
                case MapCardKind.KindledGround:
                case MapCardKind.BountyWind: return mobDeck;
                default: throw new ArgumentException($"{kind} has no enemies.", nameof(kind));
            }
        }

        /// <summary>
        /// Builds an in-memory content set. Public so the Editor builder and tests can author it;
        /// gameplay code must never call this to "edit" existing content.
        /// </summary>
        public static RunContent Create(IEnumerable<CardData> starter, IEnumerable<CardData> pool,
            IEnumerable<CardData> mob, IEnumerable<CardData> hexfire, IEnumerable<CardData> elite,
            IEnumerable<CardData> boss, CardData zero)
        {
            var content = CreateInstance<RunContent>();
            content.starterDeck = new List<CardData>(starter ?? Array.Empty<CardData>());
            content.draftPool = new List<CardData>(pool ?? Array.Empty<CardData>());
            content.mobDeck = new List<CardData>(mob ?? Array.Empty<CardData>());
            content.hexfireDeck = new List<CardData>(hexfire ?? Array.Empty<CardData>());
            content.eliteDeck = new List<CardData>(elite ?? Array.Empty<CardData>());
            content.bossDeck = new List<CardData>(boss ?? Array.Empty<CardData>());
            content.enemyZero = zero;
            content.name = "RunContent";
            return content;
        }

        /// <summary>Content rules (see docs/specs/m3-card-pool.md). Empty list = valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            CheckNoNulls(problems, "StarterDeck", starterDeck);
            CheckNoNulls(problems, "DraftPool", draftPool);
            CheckNoNulls(problems, "MobDeck", mobDeck);
            CheckNoNulls(problems, "HexfireDeck", hexfireDeck);
            CheckNoNulls(problems, "EliteDeck", eliteDeck);
            CheckNoNulls(problems, "BossDeck", bossDeck);

            if (starterDeck.Count < ShopRules.MinDeckSize)
                problems.Add($"StarterDeck has {starterDeck.Count} cards; needs at least {ShopRules.MinDeckSize}.");

            CheckDraftPool(problems);

            CheckEnemyDeck(problems, "MobDeck", mobDeck, MinEnemyDeckSize);
            CheckEnemyDeck(problems, "HexfireDeck", hexfireDeck, MinEnemyDeckSize);
            CheckEnemyDeck(problems, "EliteDeck", eliteDeck, MinEnemyDeckSize);
            CheckEnemyDeck(problems, "BossDeck", bossDeck, 0); // size checked exactly below
            if (bossDeck.Count != BossDeckSize)
                problems.Add($"BossDeck has {bossDeck.Count} cards; needs exactly {BossDeckSize}.");

            if (enemyZero == null) problems.Add("EnemyZero is missing.");
            else if (!enemyZero.IsZeroBreaker) problems.Add("EnemyZero is not a 0-card.");

            CheckUniqueIds(problems);
            return problems;
        }

        private void CheckDraftPool(List<string> problems)
        {
            var distinct = new HashSet<CardData>();
            foreach (var c in draftPool) if (c != null) distinct.Add(c);
            if (distinct.Count != CountNonNull(draftPool)) problems.Add("DraftPool lists the same card twice.");
            if (distinct.Count < MinDraftPoolSize)
                problems.Add($"DraftPool has {distinct.Count} cards; needs at least {MinDraftPoolSize}.");

            bool hasZero = false;
            var rarities = new HashSet<Rarity>();
            var shapes = new HashSet<CardShape>();
            var elements = new HashSet<Element>();
            foreach (var c in distinct)
            {
                rarities.Add(c.Rarity);
                if (c.IsZeroBreaker) { hasZero = true; continue; }
                shapes.Add(c.Shape);
                elements.Add(c.Element);
            }

            foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
                if (!rarities.Contains(r)) problems.Add($"DraftPool has no {r} card.");
            foreach (CardShape s in Enum.GetValues(typeof(CardShape)))
                if (!shapes.Contains(s)) problems.Add($"DraftPool has no {s} card.");
            foreach (Element e in Enum.GetValues(typeof(Element)))
                if (!elements.Contains(e)) problems.Add($"DraftPool has no {e} card.");
            if (!hasZero) problems.Add("DraftPool has no 0-card.");
        }

        private static void CheckEnemyDeck(List<string> problems, string label, List<CardData> deck, int minSize)
        {
            if (deck.Count < minSize) problems.Add($"{label} has {deck.Count} cards; needs at least {minSize}.");
            foreach (var c in deck)
                if (c != null && c.IsZeroBreaker) problems.Add($"{label} contains 0-card {c.CardId}; enemy zeros come from trials.");
        }

        /// <summary>One asset per id. The same asset may sit in several lists (e.g. a starter card in a deck).</summary>
        private void CheckUniqueIds(List<string> problems)
        {
            var byId = new Dictionary<string, CardData>();
            var all = new List<CardData>();
            all.AddRange(starterDeck);
            all.AddRange(draftPool);
            all.AddRange(mobDeck);
            all.AddRange(hexfireDeck);
            all.AddRange(eliteDeck);
            all.AddRange(bossDeck);
            all.Add(enemyZero);

            foreach (var c in all)
            {
                if (c == null) continue;
                if (string.IsNullOrEmpty(c.CardId)) { problems.Add($"Card '{c.name}' has no id."); continue; }
                if (byId.TryGetValue(c.CardId, out var other) && other != c)
                    problems.Add($"Card id '{c.CardId}' is used by two different assets.");
                else byId[c.CardId] = c;
            }

            var enemy = new HashSet<CardData>();
            enemy.UnionWith(mobDeck);
            enemy.UnionWith(hexfireDeck);
            enemy.UnionWith(eliteDeck);
            enemy.UnionWith(bossDeck);
            if (enemyZero != null) enemy.Add(enemyZero);
            foreach (var c in draftPool)
                if (c != null && enemy.Contains(c)) problems.Add($"DraftPool card '{c.CardId}' is an enemy card.");
        }

        private static void CheckNoNulls(List<string> problems, string label, List<CardData> list)
        {
            if (CountNonNull(list) != list.Count) problems.Add($"{label} has an empty entry.");
        }

        private static int CountNonNull(List<CardData> list)
        {
            int n = 0;
            foreach (var c in list) if (c != null) n++;
            return n;
        }
    }
}
