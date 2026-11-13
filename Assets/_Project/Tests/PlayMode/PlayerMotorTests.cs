using System.Collections;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    public class PlayerMotorTests
    {
        private GameObject player;
        private GameObject ground;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 1.1f, 0f);
            player.AddComponent<CombatantStatus>().Team = DeckArpg.Core.Team.Player;
            player.AddComponent<PlayerMotor>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(player);
            Object.Destroy(ground);
        }

        [UnityTest]
        public IEnumerator Dash_GrantsIFrames_ThenEndsThem()
        {
            var motor = player.GetComponent<PlayerMotor>();
            var status = player.GetComponent<CombatantStatus>();

            Assert.IsTrue(motor.TryDash());
            Assert.IsTrue(status.IsInvulnerable);

            float end = Time.time + 1f;
            while (motor.IsDashing && Time.time < end) yield return null;

            Assert.IsFalse(motor.IsDashing);
            Assert.IsFalse(status.IsInvulnerable, "I-frames must end with the dash.");
        }

        [UnityTest]
        public IEnumerator Dash_MovesAlongAimDirection_WhenNoMoveInput()
        {
            var motor = player.GetComponent<PlayerMotor>();
            motor.SetAimDirection(Vector3.right);
            float startX = player.transform.position.x;

            motor.TryDash();
            float end = Time.time + 1f;
            while (motor.IsDashing && Time.time < end) yield return null;

            Assert.Greater(player.transform.position.x, startX + 1f);
        }

        [Test]
        public void Dash_IsBlockedDuringCooldown()
        {
            var motor = player.GetComponent<PlayerMotor>();
            Assert.IsTrue(motor.TryDash());
            Assert.IsFalse(motor.TryDash());
        }

        [Test]
        public void Dash_IsBlockedWhileStunned()
        {
            player.GetComponent<CombatantStatus>().ApplyBreak();
            Assert.IsFalse(player.GetComponent<PlayerMotor>().TryDash());
        }
    }
}
