using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// Runtime-built visuals (exit door, hitbox cubes, pips) must use URP Lit. A copy of the primitive's own material
    /// rendered magenta in the v0.1.0 player build (built-in Standard shader), while the Editor looked fine.
    /// </summary>
    public class FxShapesTests
    {
        [Test]
        public void CreateVisual_UsesUrpLit_WithTheRequestedColour()
        {
            var parent = new GameObject("FxShapesTestParent");
            try
            {
                var colour = new Color(0.12f, 0.34f, 0.56f, 1f);
                var visual = FxShapes.CreateVisual(PrimitiveType.Cube, "Probe", parent.transform, colour);
                var material = visual.GetComponent<Renderer>().sharedMaterial;
                Assert.AreEqual(FxShapes.SolidShaderName, material.shader.name);
                for (int i = 0; i < 4; i++) Assert.AreEqual(colour[i], material.color[i], 1e-4f, $"colour channel {i}");
                Assert.IsNull(visual.GetComponent<Collider>(), "visuals are collider-less");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
