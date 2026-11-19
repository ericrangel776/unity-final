using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Juice on plain damaging body hits (docs/specs/hit-juice.md): hit-stop, sparks, impulse, death pop.</summary>
    public class HitJuiceTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        private T Spawn<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go.AddComponent<T>();
        }

        private CombatantStatus Body(Team team)
        {
            var status = Spawn<CombatantStatus>($"{team}_body");
            status.Team = team;
            return status;
        }

        private static AttackPayload Card(int value, Team attacker, Element element = Element.Fire) =>
            new AttackPayload(1, attacker, value, false, false, 0, CardShape.Strike, element);

        private static IEnumerator WaitUntilRealtime(System.Func<bool> done, float timeout = 1f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        /// <summary>Lands the hit and measures how long (real time) the game stayed frozen.</summary>
        private static IEnumerator MeasureFreeze(CombatantStatus victim, AttackPayload payload, System.Action<bool, float> result)
        {
            float start = Time.realtimeSinceStartup;
            victim.TakeHit(payload);
            bool froze = Time.timeScale == 0f;
            yield return WaitUntilRealtime(() => Time.timeScale > 0f);
            result(froze, Time.realtimeSinceStartup - start);
        }

        [UnityTest]
        public IEnumerator PlayerCardHit_FreezesBriefly_ThenRestoresTimeScale()
        {
            Spawn<HitStop>("HitStop");
            var enemy = Body(Team.Enemy);
            bool froze = false;
            float seconds = 0f;

            yield return MeasureFreeze(enemy, Card(9, Team.Player), (f, s) => { froze = f; seconds = s; });

            Assert.IsTrue(froze, "A damaging card hit should freeze time.");
            Assert.AreEqual(1f, Time.timeScale, "Time scale must be restored after the freeze.");
            Assert.GreaterOrEqual(seconds, HitJuiceRules.MaxCardHitStop * 0.9f);
        }

        [UnityTest]
        public IEnumerator Poke_NeverFreezes()
        {
            Spawn<HitStop>("HitStop");
            var enemy = Body(Team.Enemy);

            enemy.TakeHit(AttackPayload.Poke(1, Team.Player, 0));

            Assert.AreEqual(1f, Time.timeScale, "Pokes must not freeze.");
            Assert.Less(enemy.Hp, enemy.MaxHp, "The poke should still have dealt damage.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyHitOnPlayer_Freezes()
        {
            Spawn<HitStop>("HitStop");
            var player = Body(Team.Player);
            bool froze = false;
            float seconds = 0f;

            yield return MeasureFreeze(player, Card(2, Team.Enemy), (f, s) => { froze = f; seconds = s; });

            Assert.IsTrue(froze, "Getting hit should freeze time.");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.GreaterOrEqual(seconds, HitJuiceRules.PlayerHurtHitStop * 0.9f);
        }
    }
}
