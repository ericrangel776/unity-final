using System;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Shared helpers for tests that drive the M3 run scene through <see cref="RunDirector"/>.</summary>
    internal static class RunTestHelpers
    {
        /// <summary>
        /// Walks seeded runs (taking door 1 and clearing it) until a door offers a matching card, then enters it.
        /// Returns the room's card. Deterministic: same seeds every time.
        /// </summary>
        public static MapCard EnterRoom(RunDirector director, Func<MapCard, bool> match)
        {
            for (int seed = 1; seed < 400; seed++)
            {
                director.RestartRun(seed);
                for (int room = 1; room <= 5; room++)
                {
                    var offer = director.Run.CurrentOffer;
                    for (int i = 0; i < offer.Count; i++)
                    {
                        if (!match(offer[i])) continue;
                        director.ChooseDoor(i);
                        return director.Run.CurrentRoom;
                    }
                    director.ChooseDoor(0);
                    if (director.State == RunDirectorState.Fighting)
                    {
                        director.DebugDefeatAllEnemies();
                        director.FinishReward();
                    }
                    else director.LeaveUtility();
                }
            }
            Assert.Fail("No seed produced a matching door.");
            return default;
        }

        /// <summary>Teleports a CharacterController-driven object (it overrides direct position writes while enabled).</summary>
        public static void Place(Component who, Vector3 position)
        {
            var body = who.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            who.transform.position = position;
            if (body != null) body.enabled = true;
        }
    }
}
