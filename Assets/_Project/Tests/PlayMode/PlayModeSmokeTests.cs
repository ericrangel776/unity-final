using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Confirms the PlayMode runner works; real gameplay tests go alongside this.</summary>
    public class PlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator PlayMode_AdvancesFrames()
        {
            int start = Time.frameCount;
            yield return null;
            Assert.Greater(Time.frameCount, start);
        }
    }
}
