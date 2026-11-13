using System.Collections.Generic;
using DeckArpg.Core;
using UnityEditor;
using UnityEngine;

namespace DeckArpg.Editor
{
    /// <summary>
    /// Creates the placeholder card assets for the M1 prototype. Idempotent: existing assets are
    /// reused, never overwritten, so hand-tuned values survive a rebuild.
    /// </summary>
    public static class StarterContentBuilder
    {
        public const string CardsFolder = "Assets/_Project/ScriptableObjects/Cards";

        [MenuItem("DeckArpg/Build Starter Cards")]
        public static void BuildMenu()
        {
            BuildPlayerStarterDeck();
            BuildDummyDeck();
            AssetDatabase.SaveAssets();
            Debug.Log($"Starter cards ready in {CardsFolder}");
        }

        /// <summary>10 cards: Strike 2-7, Ember (Fire Throw) 3 and 5, Frost (Ice Arcana) 4, one 0-Parry.</summary>
        public static List<CardData> BuildPlayerStarterDeck()
        {
            var deck = new List<CardData>();
            for (int v = 2; v <= 7; v++)
                deck.Add(GetOrCreate($"strike_{v}", v, CardShape.Strike, Element.Physical, $"Strike {v}"));
            deck.Add(GetOrCreate("ember_3", 3, CardShape.Throw, Element.Fire, "Ember 3"));
            deck.Add(GetOrCreate("ember_5", 5, CardShape.Throw, Element.Fire, "Ember 5"));
            deck.Add(GetOrCreate("frost_4", 4, CardShape.Arcana, Element.Ice, "Frost 4"));
            deck.Add(GetOrCreate("parry_0", 1, CardShape.Strike, Element.Physical, "Parry", zero: true));
            return deck;
        }

        /// <summary>Training dummy: mostly mid cards plus one 9 to practise 0-parries on.</summary>
        public static List<CardData> BuildDummyDeck()
        {
            return new List<CardData>
            {
                GetOrCreate("dummy_slam_3", 3, CardShape.Strike, Element.Physical, "Slam 3"),
                GetOrCreate("dummy_slam_4", 4, CardShape.Strike, Element.Physical, "Slam 4"),
                GetOrCreate("dummy_slam_5", 5, CardShape.Strike, Element.Physical, "Slam 5"),
                GetOrCreate("dummy_slam_6", 6, CardShape.Strike, Element.Physical, "Slam 6"),
                GetOrCreate("dummy_slam_9", 9, CardShape.Strike, Element.Physical, "Slam 9"),
            };
        }

        private static CardData GetOrCreate(string id, int value, CardShape shape, Element element, string displayName,
            bool zero = false)
        {
            EnsureFolder(CardsFolder);
            string path = $"{CardsFolder}/{id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (existing != null) return existing;

            var card = CardData.Create(id, value, shape, element, zero, displayName: displayName);
            AssetDatabase.CreateAsset(card, path);
            return card;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
