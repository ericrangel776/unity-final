using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    public class CameraPunchTests
    {
        private const float BaseFov = 30f;
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        private CameraPunch MakeCamera()
        {
            var go = new GameObject("VCam");
            spawned.Add(go);
            var vcam = go.AddComponent<CinemachineCamera>();
            var lens = vcam.Lens;
            lens.FieldOfView = BaseFov;
            vcam.Lens = lens;
            return go.AddComponent<CameraPunch>();
        }

        private CombatActionController MakeCombatant(Team team, Vector3 position)
        {
            var go = new GameObject($"{team}_combatant");
            spawned.Add(go);
            go.transform.position = position;
            go.AddComponent<CombatantStatus>().Team = team;
            return go.AddComponent<CombatActionController>();
        }

        private static AttackPayload Payload(CombatActionController c, int value, int tick, bool zero = false) =>
            new AttackPayload(c.Status.OwnerId, c.Status.Team, value, zero, false, tick, CardShape.Strike, Element.Physical);

        /// <summary>
        /// Runs a clash and returns the FOV right after the punch handled it (the punch subscribed
        /// first, so it runs first) - immune to frame-time easing.
        /// </summary>
        private IEnumerator ClashAndReadFov(CameraPunch punch, int enemyValue, int playerValue, bool playerZero,
            System.Action<float> result, float originX = 0f)
        {
            // originX keeps separate clashes in one test from touching each other's live hitboxes.
            var player = MakeCombatant(Team.Player, new Vector3(originX, 0f, 0f));
            var enemy = MakeCombatant(Team.Enemy, new Vector3(originX, 0f, 2.2f));
            float? fov = null;
            System.Action<ClashEvent> probe = _ => fov = punch.CurrentFov;
            ClashSystem.ClashOccurred += probe;
            try
            {
                enemy.Spawn(Payload(enemy, enemyValue, tick: 1), Vector3.back);
                player.Spawn(Payload(player, playerValue, tick: 2, playerZero), Vector3.forward);
                float end = Time.realtimeSinceStartup + 1f;
                while (!fov.HasValue && Time.realtimeSinceStartup < end) yield return null;
            }
            finally
            {
                ClashSystem.ClashOccurred -= probe;
            }
            Assert.IsTrue(fov.HasValue, "No clash happened.");
            result(fov.Value);
        }

        private static IEnumerator WaitUntilRealtime(System.Func<bool> done, float timeout = 1.5f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest]
        public IEnumerator CardBreak_ZoomsIn_ThenEasesBackToBase()
        {
            var punch = MakeCamera();
            float fov = 0f;
            yield return ClashAndReadFov(punch, enemyValue: 3, playerValue: 8, playerZero: false, f => fov = f);

            Assert.Less(fov, BaseFov, "A Card Break should zoom in.");
            yield return WaitUntilRealtime(() => Mathf.Approximately(punch.CurrentFov, BaseFov));
            Assert.AreEqual(BaseFov, punch.CurrentFov, 0.001f, "Zoom must return to the base FOV.");
        }

        [UnityTest]
        public IEnumerator ZeroBreak_ZoomsHarderThanCardBreak()
        {
            var cardPunch = MakeCamera();
            float cardFov = 0f;
            yield return ClashAndReadFov(cardPunch, 3, 8, false, f => cardFov = f);
            cardPunch.enabled = false;

            var zeroPunch = MakeCamera();
            float zeroFov = 0f;
            yield return ClashAndReadFov(zeroPunch, 9, 0, true, f => zeroFov = f, originX: 50f);

            Assert.Less(zeroFov, cardFov);
        }

        [UnityTest]
        public IEnumerator NeutralClash_DoesNotZoom()
        {
            var punch = MakeCamera();
            float fov = 0f;
            yield return ClashAndReadFov(punch, 5, 5, false, f => fov = f);

            Assert.AreEqual(BaseFov, fov, 0.001f);
        }

        [UnityTest]
        public IEnumerator HoldsZoom_WhileTimeIsFrozen()
        {
            var punch = MakeCamera();
            Time.timeScale = 0f;
            punch.Punch(3f);

            // Well past the minimum hold + ease-out, but time is still frozen.
            float end = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(BaseFov - 3f, punch.CurrentFov, 0.001f, "Zoom should hold through the freeze.");

            Time.timeScale = 1f;
            yield return WaitUntilRealtime(() => Mathf.Approximately(punch.CurrentFov, BaseFov));
            Assert.AreEqual(BaseFov, punch.CurrentFov, 0.001f);
        }

        [UnityTest]
        public IEnumerator PunchDuringPunch_KeepsStrongerZoom()
        {
            var punch = MakeCamera();
            punch.Punch(5f);
            punch.Punch(3f);

            Assert.AreEqual(BaseFov - 5f, punch.CurrentFov, 0.001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisablingMidPunch_RestoresBaseFov()
        {
            var punch = MakeCamera();
            punch.Punch(5f);
            punch.enabled = false;

            Assert.AreEqual(BaseFov, punch.CurrentFov, 0.001f);
            yield return null;
        }
    }
}
