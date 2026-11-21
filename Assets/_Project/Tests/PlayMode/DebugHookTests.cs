using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Gameplay hooks behind the debug menu (docs/specs/debug-menu.md).</summary>
    public class DebugHookTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [SetUp]
        public void SetUp() => Time.timeScale = 1f;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            HitStop.Disabled = false;
            HitStop.SetBaseTimeScale(1f);
            TrainingDummyEnemy.DebugFreezeAI = false;
            CombatActionController.DebugShowHitboxes = null;
            Time.timeScale = 1f;
        }

        private static IEnumerator Realtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private CombatantStatus MakeStatus(Team team)
        {
            var go = new GameObject($"{team}_debug");
            spawned.Add(go);
            var s = go.AddComponent<CombatantStatus>();
            s.Team = team;
            return s;
        }

        private static AttackPayload Lethal(Team from) =>
            new AttackPayload(4242, from, 999, false, true, Time.frameCount, CardShape.Strike, Element.Physical);

        // ---- god mode ----

        [Test]
        public void GodMode_LethalHitLands_ButHpDoesNotDrop()
        {
            var player = MakeStatus(Team.Player);
            player.GodMode = true;
            int hp = player.Hp;
            int landed = 0;
            void Count(HitInfo h) { if (h.Victim == player) landed++; }
            CombatantStatus.AnyHitLanded += Count;
            try
            {
                Assert.IsTrue(player.TakeHit(Lethal(Team.Enemy)), "the hit still lands");
            }
            finally { CombatantStatus.AnyHitLanded -= Count; }

            Assert.AreEqual(hp, player.Hp);
            Assert.IsFalse(player.IsDead);
            Assert.AreEqual(1, landed, "hit feedback still fires");
        }

        [Test]
        public void GodMode_EffectDamageDoesNotDrop()
        {
            var player = MakeStatus(Team.Player);
            player.GodMode = true;
            int hp = player.Hp;
            player.TakeEffectDamage(999, Element.Fire, Lethal(Team.Enemy));
            Assert.AreEqual(hp, player.Hp);
        }

        [Test]
        public void GodModeOff_LethalHitKills()
        {
            var player = MakeStatus(Team.Player);
            player.TakeHit(Lethal(Team.Enemy));
            Assert.IsTrue(player.IsDead, "control");
        }

        // ---- hit-stop base scale ----

        private HitStop MakeHitStop()
        {
            var go = new GameObject("DebugHitStop");
            spawned.Add(go);
            return go.AddComponent<HitStop>();
        }

        [UnityTest]
        public IEnumerator BaseTimeScale_SurvivesAFreeze()
        {
            var stop = MakeHitStop();
            HitStop.SetBaseTimeScale(0.5f);
            Assert.AreEqual(0.5f, Time.timeScale);

            stop.Freeze(0.05f);
            Assert.AreEqual(0f, Time.timeScale);
            yield return Realtime(0.2f);
            Assert.AreEqual(0.5f, Time.timeScale, "the freeze restores the debug scale");
        }

        [UnityTest]
        public IEnumerator BaseTimeScale_SetDuringAFreeze_AppliesWhenItEnds()
        {
            var stop = MakeHitStop();
            stop.Freeze(0.1f);
            HitStop.SetBaseTimeScale(0.25f);
            Assert.AreEqual(0f, Time.timeScale, "still frozen");
            yield return Realtime(0.3f);
            Assert.AreEqual(0.25f, Time.timeScale);
        }

        [Test]
        public void Disabled_FreezeDoesNothing()
        {
            var stop = MakeHitStop();
            HitStop.Disabled = true;
            stop.Freeze(1f);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(stop.IsFrozen);
        }

        // ---- frozen AI ----

        [UnityTest]
        public IEnumerator FrozenAI_NeverTelegraphs()
        {
            var target = new GameObject("DebugTarget");
            spawned.Add(target);
            var go = new GameObject("FrozenDummy");
            spawned.Add(go);
            go.transform.position = new Vector3(0f, 0f, 1.5f); // already in swing range
            go.AddComponent<CharacterController>().minMoveDistance = 0f;
            var enemy = go.AddComponent<TrainingDummyEnemy>();
            enemy.Target = target.transform;
            var cards = new List<CardData>();
            for (int i = 0; i < 4; i++)
            {
                var c = CardData.Create($"frozen_{i}", 4, CardShape.Strike, Element.Physical);
                cards.Add(c);
                spawned.Add(c);
            }
            enemy.Deck.SetStartingDeck(cards, newSeed: 2);
            int telegraphs = 0;
            enemy.TelegraphStarted += _ => telegraphs++;

            TrainingDummyEnemy.DebugFreezeAI = true;
            yield return Realtime(2.2f); // recover 1.2 s + plenty
            Assert.AreEqual(0, telegraphs, "frozen");

            TrainingDummyEnemy.DebugFreezeAI = false;
            yield return Realtime(1.6f);
            Assert.Greater(telegraphs, 0, "control: unfrozen, it winds up");
        }

        // ---- hitbox override ----

        [Test]
        public void DebugShowHitboxes_OverridesTheVisual()
        {
            var owner = MakeStatus(Team.Player).gameObject.AddComponent<CombatActionController>();
            var payload = new AttackPayload(owner.Status.OwnerId, Team.Player, 4, false, false, Time.frameCount,
                CardShape.Strike, Element.Physical);

            CombatActionController.DebugShowHitboxes = false;
            var hidden = owner.Spawn(payload, Vector3.forward);
            Assert.IsNull(hidden.GetComponentInChildren<MeshRenderer>(), "no visual while forced off");

            CombatActionController.DebugShowHitboxes = true;
            var shown = owner.Spawn(payload, Vector3.forward);
            Assert.IsNotNull(shown.GetComponentInChildren<MeshRenderer>(), "a visual while forced on");
        }
    }
}
