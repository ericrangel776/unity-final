using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Creates in-memory cards for a test and destroys them afterwards.</summary>
    internal sealed class TestCards
    {
        private readonly List<CardData> created = new List<CardData>();

        public RuntimeCardInstance Card(int value, CardShape shape = CardShape.Strike, Element element = Element.Physical)
        {
            var data = CardData.Create($"test_{value}_{created.Count}", value, shape, element);
            created.Add(data);
            return new RuntimeCardInstance(data);
        }

        public RuntimeCardInstance Zero()
        {
            var data = CardData.Create($"test_zero_{created.Count}", 1, zeroBreaker: true);
            created.Add(data);
            return new RuntimeCardInstance(data);
        }

        /// <summary>A bare card asset (for run decks, draft pools and shops).</summary>
        public CardData Data(int value, Rarity rarity = Rarity.Common, bool zero = false)
        {
            var data = CardData.Create($"test_data_{value}_{created.Count}", value, zeroBreaker: zero, rarity: rarity);
            created.Add(data);
            return data;
        }

        public List<CardData> DataSet(int count, Rarity rarity = Rarity.Common)
        {
            var list = new List<CardData>();
            for (int i = 0; i < count; i++) list.Add(Data(1 + i % 9, rarity));
            return list;
        }

        public List<RuntimeCardInstance> Numbered(params int[] values)
        {
            var list = new List<RuntimeCardInstance>();
            foreach (var v in values) list.Add(Card(v));
            return list;
        }

        public void DestroyAll()
        {
            foreach (var c in created) if (c != null) Object.DestroyImmediate(c);
            created.Clear();
        }
    }
}
