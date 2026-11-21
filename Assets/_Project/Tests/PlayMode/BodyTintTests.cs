using System.Collections;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>BodyTint: one owner for a body's colour override, highest layer wins.</summary>
    public class BodyTintTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private GameObject body;
        private Renderer renderer;
        private BodyTint tint;

        [SetUp]
        public void SetUp()
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            renderer = body.GetComponent<Renderer>();
            tint = BodyTint.For(body);
        }

        [TearDown]
        public void TearDown() => Object.Destroy(body);

        private Color Shown()
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor(BaseColorId);
        }

        // LateUpdate applies the layers; yield twice so it has run after the change.
        private static IEnumerator Frames() { yield return null; yield return null; }

        [Test]
        public void For_ReturnsTheSameComponentEachTime()
        {
            Assert.AreSame(tint, BodyTint.For(body));
        }

        [UnityTest]
        public IEnumerator Flash_ShowsOverStatus_AndEndingItRestoresTheStatusTint()
        {
            tint.Set(BodyTint.Layer.Status, Color.red);
            yield return Frames();
            Assert.AreEqual(Color.red, Shown());

            tint.Set(BodyTint.Layer.Flash, Color.white);
            yield return Frames();
            Assert.AreEqual(Color.white, Shown(), "the flash is the top layer");

            tint.Clear(BodyTint.Layer.Flash);
            yield return Frames();
            Assert.AreEqual(Color.red, Shown(), "the status tint comes back instead of being wiped");
        }

        [UnityTest]
        public IEnumerator ClearingTheLastLayer_RemovesTheOverride()
        {
            tint.Set(BodyTint.Layer.State, Color.cyan);
            yield return Frames();
            Assert.IsTrue(renderer.HasPropertyBlock());

            tint.Clear(BodyTint.Layer.State);
            yield return Frames();
            Assert.IsFalse(renderer.HasPropertyBlock());
        }

        [UnityTest]
        public IEnumerator LerpFromWhite_IgnoresTheMaterialColour()
        {
            tint.Set(BodyTint.Layer.State, Color.blue, 0.5f, lerpFromWhite: true);
            yield return Frames();
            Assert.AreEqual(Color.Lerp(Color.white, Color.blue, 0.5f), Shown());
        }

        [UnityTest]
        public IEnumerator BaseColor_ShowsWithNoLayerActive()
        {
            tint.SetBaseColor(Color.magenta);
            Assert.AreEqual(Color.magenta, Shown(), "applied at once");
            yield return Frames();
            Assert.AreEqual(Color.magenta, Shown(), "and kept while no layer is active");
        }

        [UnityTest]
        public IEnumerator Flash_WinsOverBaseColor_ThenReturnsToBase_NotTheMaterial()
        {
            tint.SetBaseColor(Color.magenta);
            tint.Set(BodyTint.Layer.Flash, Color.white);
            yield return Frames();
            Assert.AreEqual(Color.white, Shown());

            tint.Clear(BodyTint.Layer.Flash);
            yield return Frames();
            Assert.IsTrue(renderer.HasPropertyBlock());
            Assert.AreEqual(Color.magenta, Shown(), "back to the variant colour");
        }

        [UnityTest]
        public IEnumerator LerpFromOwnColour_StartsFromTheBaseColor()
        {
            tint.SetBaseColor(Color.black);
            tint.Set(BodyTint.Layer.Status, Color.white, 0.25f);
            yield return Frames();
            AssertColor(Color.Lerp(Color.black, Color.white, 0.25f), Shown());
        }

        [UnityTest]
        public IEnumerator DisablingWithABaseColor_KeepsTheBaseColor()
        {
            tint.SetBaseColor(Color.magenta);
            tint.Set(BodyTint.Layer.Flash, Color.white);
            yield return Frames();

            tint.enabled = false;
            Assert.AreEqual(Color.magenta, Shown());
        }

        // A property-block colour round-trip isn't bit-exact for non-dyadic values.
        private static void AssertColor(Color expected, Color actual)
        {
            Assert.That(Vector4.Distance(expected, actual), Is.LessThan(1e-3f), $"expected {expected}, was {actual}");
        }
    }
}
