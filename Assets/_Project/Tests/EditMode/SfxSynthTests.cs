using System;
using System.Linq;
using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class SfxSynthTests
    {
        private static SfxId[] AllIds => (SfxId[])Enum.GetValues(typeof(SfxId));

        [TestCaseSource(nameof(AllIds))]
        public void Clip_HasTheRightLengthAndIsNotSilent(SfxId id)
        {
            var samples = SfxSynth.Generate(id, 22050);
            Assert.AreEqual((int)(SfxSynth.Duration(id) * 22050), samples.Length);
            Assert.Greater(samples.Max(Math.Abs), 0.1f);
        }

        [TestCaseSource(nameof(AllIds))]
        public void Clip_IsFiniteAndNormalized(SfxId id)
        {
            var samples = SfxSynth.Generate(id, 22050);
            Assert.IsTrue(samples.All(s => !float.IsNaN(s) && !float.IsInfinity(s)));
            Assert.AreEqual(SfxSynth.Peak, samples.Max(Math.Abs), 1e-4f);
        }

        [TestCaseSource(nameof(AllIds))]
        public void Clip_StartsAndEndsQuietly(SfxId id)
        {
            // Attack/fade envelopes: no click at either end.
            var samples = SfxSynth.Generate(id, 22050);
            Assert.Less(Math.Abs(samples[0]), 0.05f);
            Assert.Less(Math.Abs(samples[samples.Length - 1]), 0.05f);
        }

        [Test]
        public void Generate_IsDeterministic() =>
            CollectionAssert.AreEqual(SfxSynth.Generate(SfxId.ZeroBreak, 22050), SfxSynth.Generate(SfxId.ZeroBreak, 22050));

        [Test]
        public void Generate_DifferentSoundsDiffer() =>
            CollectionAssert.AreNotEqual(SfxSynth.Generate(SfxId.CardBreak, 22050), SfxSynth.Generate(SfxId.ZeroBreak, 22050));

        [Test]
        public void Generate_RejectsTinySampleRates() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => SfxSynth.Generate(SfxId.Hit, 100));

        [Test]
        public void Generate_RejectsUnknownIds() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => SfxSynth.Generate((SfxId)999, 22050));
    }
}
