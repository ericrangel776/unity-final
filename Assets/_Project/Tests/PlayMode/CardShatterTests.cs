using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using DeckArpg.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    public class CardShatterTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
        }

        private CardShatterView MakeView()
        {
            var go = new GameObject("Shatter");
            spawned.Add(go);
            return go.AddComponent<CardShatterView>();
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
            new AttackPayload(c.Status.OwnerId, c.Status.Team, value, zero, false, tick, CardShape.Strike, Element.Ice);

        /// <summary>Enemy plays first (tick 1), player second (tick 2); returns the view's state at clash time.</summary>
        private IEnumerator Clash(CardShatterView view, int enemyValue, int playerValue, bool playerZero,
            System.Action<string, bool> result, bool freezeOnClash = false)
        {
            var player = MakeCombatant(Team.Player, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 2.2f));
            bool clashed = false;
            string shown = null;
            bool showing = false;
            System.Action<ClashEvent> probe = _ =>
            {
                clashed = true;
                shown = view.ShownValue;
                showing = view.IsShowing;
                if (freezeOnClash) Time.timeScale = 0f; // like HitStop, but held until the test releases it
            };
            ClashSystem.ClashOccurred += probe;
            try
            {
                enemy.Spawn(Payload(enemy, enemyValue, tick: 1), Vector3.back);
                player.Spawn(Payload(player, playerValue, tick: 2, playerZero), Vector3.forward);
                float end = Time.realtimeSinceStartup + 1f;
                while (!clashed && Time.realtimeSinceStartup < end) yield return null;
            }
            finally
            {
                ClashSystem.ClashOccurred -= probe;
            }
            Assert.IsTrue(clashed, "No clash happened.");
            result(shown, showing);
        }

        private static IEnumerator WaitUntilRealtime(System.Func<bool> done, float timeout = 1.5f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest]
        public IEnumerator CardBreak_ShowsTheLosersValue()
        {
            var view = MakeView();
            string shown = null;
            yield return Clash(view, enemyValue: 3, playerValue: 8, playerZero: false, (s, _) => shown = s);

            Assert.AreEqual("3", shown, "The broken (losing) card is shown, not the winner.");
        }

        [UnityTest]
        public IEnumerator ZeroBreak_ShowsTheBrokenCard()
        {
            var view = MakeView();
            string shown = null;
            yield return Clash(view, enemyValue: 9, playerValue: 0, playerZero: true, (s, _) => shown = s);

            Assert.AreEqual("9", shown);
        }

        [UnityTest]
        public IEnumerator BrokenZeroCard_ShowsZero()
        {
            // The player's 0 is played first (tick 1 < tick 2), so the enemy's 2 breaks it.
            var view = MakeView();
            var player = MakeCombatant(Team.Player, Vector3.zero);
            var enemy = MakeCombatant(Team.Enemy, new Vector3(0f, 0f, 2.2f));
            player.Spawn(Payload(player, 0, tick: 1, zero: true), Vector3.forward);
            enemy.Spawn(Payload(enemy, 2, tick: 2), Vector3.back);

            yield return WaitUntilRealtime(() => view.ShownValue != null, 1f);

            Assert.AreEqual("0", view.ShownValue);
        }

        [UnityTest]
        public IEnumerator NeutralClash_ShowsNothing()
        {
            var view = MakeView();
            bool showing = true;
            yield return Clash(view, enemyValue: 5, playerValue: 5, playerZero: false, (_, s) => showing = s);

            Assert.IsFalse(showing);
        }

        [UnityTest]
        public IEnumerator HoldsWholeWhileFrozen_ThenShattersAndClears()
        {
            var view = MakeView();
            yield return Clash(view, 3, 8, false, (_, __) => { }, freezeOnClash: true);

            float end = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(view.IsShowing);
            Assert.AreEqual(0, view.ActiveShardCount, "Card stays whole during the freeze.");

            Time.timeScale = 1f;
            yield return WaitUntilRealtime(() => view.ActiveShardCount > 0, 0.5f);
            Assert.Greater(view.ActiveShardCount, 0, "Card should shatter once time resumes.");

            yield return WaitUntilRealtime(() => !view.IsShowing, 1.5f);
            Assert.IsFalse(view.IsShowing, "Shards should clear themselves.");
            Assert.IsNull(view.ShownValue);
        }
    }
}
