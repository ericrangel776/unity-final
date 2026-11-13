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
