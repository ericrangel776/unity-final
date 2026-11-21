using System.Collections.Generic;
using DeckArpg.Core;
using UnityEditor;
using UnityEngine;

namespace DeckArpg.Editor
{
    /// <summary>
    /// Creates the placeholder card assets, decks and run content. Idempotent: existing assets are
    /// reused, never overwritten, so hand-tuned values survive a rebuild.
    /// </summary>
    public static class StarterContentBuilder
    {
        public const string CardsFolder = "Assets/_Project/ScriptableObjects/Cards";
        public const string RunContentPath = "Assets/_Project/ScriptableObjects/RunContent.asset";

        [MenuItem("DeckArpg/Build Starter Cards")]
        public static void BuildMenu()
        {
            BuildPlayerStarterDeck();
            BuildDummyDeck();
            AssetDatabase.SaveAssets();
            Debug.Log($"Starter cards ready in {CardsFolder}");
        }

        /// <summary>
        /// Builds every M3 card (create-if-missing) and rewrites the <see cref="RunContent"/> manifest that lists them.
        /// See docs/specs/m3-card-pool.md.
        /// </summary>
        [MenuItem("DeckArpg/Build Run Content")]
        public static void BuildRunContentMenu() => BuildRunContent();

        public static RunContent BuildRunContent()
        {
            var built = RunContent.Create(BuildPlayerStarterDeck(), BuildDraftPool(), BuildDummyDeck(),
                BuildHexfireDeck(), BuildEliteDeck(), BuildBossDeck(),
                GetOrCreate("enemy_counter_0", 1, CardShape.Strike, Element.Physical, "Counter", zero: true));

            var existing = AssetDatabase.LoadAssetAtPath<RunContent>(RunContentPath);
            RunContent result;
            if (existing != null)
            {
                EditorUtility.CopySerialized(built, existing);
                existing.name = "RunContent";
                Object.DestroyImmediate(built);
                EditorUtility.SetDirty(existing);
                result = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(built, RunContentPath);
                result = built;
            }
            AssetDatabase.SaveAssets();

            var problems = result.Validate();
            if (problems.Count > 0) Debug.LogError("RunContent problems:\n" + string.Join("\n", problems));
            else Debug.Log($"Run content ready at {RunContentPath}");
            return result;
        }

        /// <summary>
        /// 23 draftable cards: 9 Common, 8 Uncommon, 6 Rare (incl. the Null Ward 0-card and Mend 3/5/7 heals). Each element's cards author the
        /// shape that matches its delivery (Fire/Lightning Throw, Ice Arcana, Wind Strike; docs/specs/element-mechanics.md).
        /// </summary>
        public static List<CardData> BuildDraftPool()
        {
            const Rarity C = Rarity.Common, U = Rarity.Uncommon, R = Rarity.Rare;
            return new List<CardData>
            {
                GetOrCreate("cleave_4", 4, CardShape.Strike, Element.Physical, "Cleave 4", rarity: C),
                GetOrCreate("cleave_6", 6, CardShape.Strike, Element.Physical, "Cleave 6", rarity: C),
                GetOrCreate("ember_4", 4, CardShape.Throw, Element.Fire, "Ember 4", rarity: C),
                GetOrCreate("frost_5", 5, CardShape.Arcana, Element.Ice, "Frost 5", rarity: C),
                GetOrCreate("spark_3", 3, CardShape.Throw, Element.Lightning, "Spark 3", rarity: C),
                GetOrCreate("spark_5", 5, CardShape.Throw, Element.Lightning, "Spark 5", rarity: C),
                GetOrCreate("volt_burst_4", 4, CardShape.Throw, Element.Lightning, "Volt Arc 4", rarity: C),
                GetOrCreate("gust_4", 4, CardShape.Strike, Element.Wind, "Gust 4", rarity: C),
                GetOrCreate("mend_3", 3, CardShape.Strike, Element.Physical, "Mend 3", heal: true, rarity: C),

                GetOrCreate("cleave_8", 8, CardShape.Strike, Element.Physical, "Cleave 8", rarity: U),
                GetOrCreate("ember_7", 7, CardShape.Throw, Element.Fire, "Ember 7", rarity: U),
                GetOrCreate("frost_7", 7, CardShape.Arcana, Element.Ice, "Frost 7", rarity: U),
                GetOrCreate("spark_7", 7, CardShape.Throw, Element.Lightning, "Spark 7", rarity: U),
                GetOrCreate("cinder_nova_6", 6, CardShape.Throw, Element.Fire, "Cinder Bolt 6", rarity: U),
                GetOrCreate("rime_lance_6", 6, CardShape.Arcana, Element.Ice, "Rime Burst 6", rarity: U),
                GetOrCreate("gust_6", 6, CardShape.Strike, Element.Wind, "Gust 6", rarity: U),
                GetOrCreate("mend_5", 5, CardShape.Strike, Element.Physical, "Mend 5", heal: true, rarity: U),

                GetOrCreate("cleave_9", 9, CardShape.Strike, Element.Physical, "Cleave 9", rarity: R),
                GetOrCreate("thunderhead_9", 9, CardShape.Throw, Element.Lightning, "Thunderhead 9", rarity: R),
                GetOrCreate("inferno_8", 8, CardShape.Throw, Element.Fire, "Inferno 8", rarity: R),
                GetOrCreate("tempest_8", 8, CardShape.Strike, Element.Wind, "Tempest 8", rarity: R),
                GetOrCreate("mend_7", 7, CardShape.Strike, Element.Physical, "Mend 7", heal: true, rarity: R),
                GetOrCreate("null_ward_0", 1, CardShape.Strike, Element.Physical, "Null Ward", zero: true, rarity: R),
            };
        }

        /// <summary>Hexfire room enemies: elemental mid cards.</summary>
        public static List<CardData> BuildHexfireDeck()
        {
            return new List<CardData>
            {
                GetOrCreate("hex_bolt_3", 3, CardShape.Throw, Element.Fire, "Hex Bolt 3"),
                GetOrCreate("hex_frost_4", 4, CardShape.Arcana, Element.Ice, "Hex Frost 4"),
                GetOrCreate("hex_spark_5", 5, CardShape.Throw, Element.Lightning, "Hex Spark 5"),
                GetOrCreate("hex_flare_6", 6, CardShape.Throw, Element.Fire, "Hex Flare 6"),
                GetOrCreate("hex_arc_7", 7, CardShape.Throw, Element.Lightning, "Hex Arc 7"),
            };
        }

        /// <summary>Purple Elite: heavy hitters.</summary>
        public static List<CardData> BuildEliteDeck()
        {
            var deck = new List<CardData>();
            int[] values = { 5, 6, 7, 7, 8, 9 };
            for (int i = 0; i < values.Length; i++)
                deck.Add(GetOrCreate($"brute_{i + 1}_{values[i]}", values[i], CardShape.Strike, Element.Physical, $"Brute {values[i]}"));
            return deck;
        }

        /// <summary>
        /// The Warden: the 15-card visible boss deck. Three 9s (about 1 in 5); the boss ignores the Intensity bonus and
        /// spaces its 9s so they never come back to back (docs/specs/m4-overview.md).
        /// </summary>
        public static List<CardData> BuildBossDeck()
        {
            var deck = new List<CardData>();
            int[] values = { 4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 9, 9, 9 };
            for (int i = 0; i < values.Length; i++)
                deck.Add(GetOrCreate($"warden_{i + 1:00}_{values[i]}", values[i], CardShape.Strike, Element.Physical, $"Warden {values[i]}"));
            return deck;
        }

        /// <summary>11 cards: Strike 2-7, Ember (Fire Throw) 3 and 5, Frost (Ice Arcana) 4, Mend 3 (heal), one 0-Parry.</summary>
        public static List<CardData> BuildPlayerStarterDeck()
        {
            var deck = new List<CardData>();
            for (int v = 2; v <= 7; v++)
                deck.Add(GetOrCreate($"strike_{v}", v, CardShape.Strike, Element.Physical, $"Strike {v}"));
            deck.Add(GetOrCreate("ember_3", 3, CardShape.Throw, Element.Fire, "Ember 3"));
            deck.Add(GetOrCreate("ember_5", 5, CardShape.Throw, Element.Fire, "Ember 5"));
            deck.Add(GetOrCreate("frost_4", 4, CardShape.Arcana, Element.Ice, "Frost 4"));
            deck.Add(GetOrCreate("mend_3", 3, CardShape.Strike, Element.Physical, "Mend 3", heal: true));
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
            bool zero = false, Rarity rarity = Rarity.Common, bool heal = false)
        {
            EnsureFolder(CardsFolder);
            string path = $"{CardsFolder}/{id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (existing != null)
            {
                SyncAuthoredFields(existing, shape, displayName);
                return existing;
            }

            var card = CardData.Create(id, value, shape, element, zero, heal, displayName: displayName, rarity: rarity);
            AssetDatabase.CreateAsset(card, path);
            return card;
        }

        /// <summary>
        /// Keeps an existing asset's shape and display name in step with the builder (its id, value and element never
        /// change), so a rebuild applies renames like "Cinder Nova" -> "Cinder Bolt" without new GUIDs.
        /// </summary>
        private static void SyncAuthoredFields(CardData card, CardShape shape, string displayName)
        {
            var so = new SerializedObject(card);
            var shapeProp = so.FindProperty("shape");
            var nameProp = so.FindProperty("displayName");
            if (shapeProp.enumValueIndex == (int)shape && nameProp.stringValue == displayName) return;
            shapeProp.enumValueIndex = (int)shape;
            nameProp.stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(card);
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
