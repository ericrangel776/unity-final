using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Hand-heal channel and Sleight heals on a runtime-built player rig (heal rework, step 5).</summary>
    public class HealChannelTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private readonly List<AttackHitbox> hitboxes = new List<AttackHitbox>();
        private GameObject player;
        private PlayerCombatInput input;
        private DeckRuntimeManager deck;
        private PlayerMotor motor;
        private CombatantStatus status;
        private HealChannel channel;
        private int interrupted;
        private readonly List<int> completed = new List<int>();

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            spawned.Add(ground);
            interrupted = 0;
            completed.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            hitboxes.Clear();
            Time.timeScale = 1f;
        }

        /// <summary>Player rig like the prefab. Each entry is a card value; negative values are heal cards (Mend).</summary>
        private void MakePlayer(params int[] values)
        {
            player = new GameObject("HealTestPlayer");
            spawned.Add(player);
            player.transform.position = new Vector3(0f, 1.1f, 0f);
            player.AddComponent<CharacterController>();
            status = player.AddComponent<CombatantStatus>();
            status.Team = Team.Player;
            var combat = player.AddComponent<CombatActionController>();
            deck = player.AddComponent<DeckRuntimeManager>();
            motor = player.AddComponent<PlayerMotor>();
            input = player.AddComponent<PlayerCombatInput>();
            combat.HitboxSpawned += hb => hitboxes.Add(hb);
            channel = player.GetComponent<HealChannel>();
            Assert.IsNotNull(channel, "PlayerCombatInput adds a HealChannel when the rig lacks one.");
            channel.Interrupted += () => interrupted++;
            channel.Completed += completed.Add;

            var cards = new List<CardData>();
            foreach (var v in values)
            {
                bool heal = v < 0;
                var data = CardData.Create($"heal_test_{cards.Count}", Mathf.Abs(v), heal: heal);
                cards.Add(data);
                spawned.Add(data);
            }
            deck.SetStartingDeck(cards, newSeed: 42);
            motor.SetAimDirection(Vector3.forward);
        }

        private int HealSlot()
        {
            for (int i = 0; i < DeckCycle.HandSize; i++)
                if (deck.Cycle.Hand[i] != null && deck.Cycle.Hand[i].IsHeal) return i;
            Assert.Fail("No heal card in hand.");
            return -1;
        }

        private static CombatCommand Play(int slot) => CombatCommand.PlaySlot1 + slot;

        /// <summary>Hurts the player to 50 HP and starts a Mend 3 channel from the hand.</summary>
        private RuntimeCardInstance StartChannel()
        {
            status.SetHp(50);
            int slot = HealSlot();
            var card = deck.Cycle.Hand[slot];
            Assert.IsTrue(input.Execute(Play(slot)));
            Assert.IsTrue(channel.IsChannelling);
            return card;
        }

        private static IEnumerator WaitUntil(System.Func<bool> done, float seconds = 3f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private static AttackPayload EnemyHit(int value, bool poke = false) =>
            new AttackPayload(999, Team.Enemy, value, false, false, 0, CardShape.Strike, Element.Physical, isPoke: poke);

        private void AssertCancelledWithNoHeal(RuntimeCardInstance card, int hpBefore)
        {
            Assert.IsFalse(channel.IsChannelling);
            Assert.AreEqual(1, interrupted, "Interrupted fires once");
            CollectionAssert.IsEmpty(completed);
            Assert.AreEqual(hpBefore, status.Hp, "no heal");
            CollectionAssert.Contains(deck.Cycle.DiscardPile, card, "the interrupted card is spent");
        }

        // ---- completion ----

        [UnityTest]
        public IEnumerator HandHeal_CompletesAfterHalfSecond_HealsThreeTimesValue()
        {
            MakePlayer(-3, -3, -3, -3);
            var card = StartChannel();
            Assert.AreEqual(9, channel.PendingAmount);
            CollectionAssert.Contains(deck.Cycle.DiscardPile, card, "TryPlay discards on play");
            CollectionAssert.IsEmpty(hitboxes, "a heal has no hitbox");

            float start = Time.time;
            yield return WaitUntil(() => !channel.IsChannelling);

            Assert.GreaterOrEqual(Time.time - start, HealRules.ChannelSeconds - 0.02f);
            Assert.AreEqual(59, status.Hp);
            CollectionAssert.AreEqual(new[] { 9 }, completed);
            Assert.AreEqual(0, interrupted);
            Assert.AreEqual(0f, channel.Progress01);
        }

        [UnityTest]
        public IEnumerator Progress_RisesWhileChannelling()
        {
            MakePlayer(-3, -3, -3, -3);
            StartChannel();
            Assert.AreEqual(0f, channel.Progress01, 1e-4f);
            yield return WaitRealtime(0.2f);
            Assert.Greater(channel.Progress01, 0f);
            Assert.Less(channel.Progress01, 1f);
        }

        [UnityTest]
        public IEnumerator Channel_PausesWhileTimeIsFrozen()
        {
            MakePlayer(-3, -3, -3, -3);
            StartChannel();
            Time.timeScale = 0f; // hit-stop
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            Assert.IsTrue(channel.IsChannelling, "scaled time: hit-stop pauses the channel");
            Assert.AreEqual(50, status.Hp);
            Time.timeScale = 1f;
            yield return WaitUntil(() => !channel.IsChannelling);
            Assert.AreEqual(59, status.Hp);
        }

        // ---- interruptions ----

        [UnityTest]
        public IEnumerator DirectHit_CancelsWithNoHeal()
        {
            MakePlayer(-3, -3, -3, -3);
            var card = StartChannel();
            status.TakeHit(EnemyHit(2), Vector3.forward);
            int hp = status.Hp;
            Assert.Less(hp, 50);
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            AssertCancelledWithNoHeal(card, hp);
        }

        [UnityTest]
        public IEnumerator PokeHit_AlsoCancels()
        {
            MakePlayer(-3, -3, -3, -3);
            var card = StartChannel();
            status.TakeHit(EnemyHit(1, poke: true), Vector3.forward);
            int hp = status.Hp;
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            AssertCancelledWithNoHeal(card, hp);
        }

        [UnityTest]
        public IEnumerator CardBreak_CancelsWithNoHeal()
        {
            MakePlayer(-3, -3, -3, -3);
            var card = StartChannel();
            status.ApplyBreak();
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            AssertCancelledWithNoHeal(card, 50);
        }

        [UnityTest]
        public IEnumerator EffectStun_CancelsWithNoHeal()
        {
            MakePlayer(-3, -3, -3, -3);
            var card = StartChannel();
            status.ApplyStun(0.1f); // Lightning jolt: no event, caught by the channel's own check
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            AssertCancelledWithNoHeal(card, 50);
        }

        [UnityTest]
        public IEnumerator Dash_CancelsWithNoHeal_AndStillDashes()
        {
            MakePlayer(-3, -3, -3, -3);
            var card = StartChannel();
            Assert.IsTrue(motor.TryDash());
            Assert.IsTrue(motor.IsDashing);
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            AssertCancelledWithNoHeal(card, 50);
        }

        [UnityTest]
        public IEnumerator Death_CancelsWithNoHeal()
        {
            MakePlayer(-3, -3, -3, -3);
            input.RespawnOnDeath = false;
            StartChannel();
            while (!status.IsDead) status.TakeHit(EnemyHit(9), Vector3.forward);
            Assert.IsFalse(channel.IsChannelling);
            Assert.AreEqual(1, interrupted);
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            CollectionAssert.IsEmpty(completed);
            Assert.AreEqual(0, status.Hp);
        }

        [UnityTest]
        public IEnumerator ResetForNewRoom_CancelsWithNoHeal()
        {
            MakePlayer(-3, -3, -3, -3);
            var card = StartChannel();
            input.ResetForNewRoom();
            yield return WaitRealtime(HealRules.ChannelSeconds + 0.2f);
            AssertCancelledWithNoHeal(card, 50);
        }

        [UnityTest]
        public IEnumerator BurnTick_DoesNotCancel()
        {
            MakePlayer(-3, -3, -3, -3);
            StartChannel();
            Assert.IsTrue(status.TakeEffectDamage(3, Element.Fire, EnemyHit(1)));
            Assert.AreEqual(47, status.Hp);
            Assert.IsTrue(channel.IsChannelling, "effect damage doesn't interrupt");
            yield return WaitUntil(() => !channel.IsChannelling);
            Assert.AreEqual(0, interrupted);
            Assert.AreEqual(56, status.Hp);
        }

        // ---- rejections ----

        [UnityTest]
        public IEnumerator EveryOtherCommand_RejectedWhileChannelling()
        {
            MakePlayer(-3, -3, -3, -3, -3, -3);
            StartChannel();
            int handCards = 0;
            for (int i = 0; i < DeckCycle.HandSize; i++) if (deck.Cycle.Hand[i] != null) handCards++;

            foreach (var command in new[]
                     {
                         CombatCommand.PlaySlot1, CombatCommand.PlaySlot2, CombatCommand.PlaySlot3, CombatCommand.Parry,
                         CombatCommand.Poke, CombatCommand.Reload, CombatCommand.StockSlot1, CombatCommand.StockSlot2,
                         CombatCommand.StockSlot3, CombatCommand.StockParry, CombatCommand.Overburn
                     })
                Assert.IsFalse(input.Execute(command), $"{command} must wait out the channel");

            CollectionAssert.IsEmpty(hitboxes);
            Assert.AreEqual(0, deck.Cycle.StockCount);
            int after = 0;
            for (int i = 0; i < DeckCycle.HandSize; i++) if (deck.Cycle.Hand[i] != null) after++;
            Assert.AreEqual(handCards, after, "nothing else left the hand");
            Assert.IsTrue(channel.IsChannelling);
            yield return WaitUntil(() => !channel.IsChannelling);
            Assert.IsTrue(input.Execute(CombatCommand.Poke), "commands work again after the channel");
        }

        [UnityTest]
        public IEnumerator HandHeal_RejectedAtFullHp_CardStaysInHand()
        {
            MakePlayer(-3, -3, -3, -3);
            int slot = HealSlot();
            var card = deck.Cycle.Hand[slot];
            Assert.AreEqual(status.MaxHp, status.Hp);

            Assert.IsFalse(input.Execute(Play(slot)));

            Assert.AreSame(card, deck.Cycle.Hand[slot]);
            Assert.IsFalse(channel.IsChannelling);
            Assert.AreEqual(0, deck.Cycle.DiscardCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NonHealCard_StillAttacks()
        {
            MakePlayer(4, 4, 4, 4);
            status.SetHp(50);
            Assert.IsTrue(input.Execute(CombatCommand.PlaySlot1));
            Assert.IsFalse(channel.IsChannelling);
            Assert.AreEqual(1, hitboxes.Count);
            yield return null;
        }

        // ---- Sleight heal ----

        [UnityTest]
        public IEnumerator SleightWithHealCards_HealsOnFire_AndStillAttacks()
        {
            MakePlayer(-3, -3, -3, -3, -3, -3);
            status.SetHp(50);
            int healed = 0;
            CombatantStatus.AnyHealed += Record;
            try
            {
                Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));
                Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));
                Assert.AreEqual(50, status.Hp, "nothing heals before the Sleight fires");
                Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));
            }
            finally { CombatantStatus.AnyHealed -= Record; }

            Assert.AreEqual(50 + 3 * 9, status.Hp, "three Mend 3s heal 27 on the fire frame");
            Assert.AreEqual(27, healed);
            Assert.IsFalse(channel.IsChannelling, "a Sleight heal doesn't channel");
            Assert.AreEqual(1, hitboxes.Count, "the Sleight still attacks");
            Assert.IsTrue(hitboxes[0].Payload.IsSleight);
            Assert.AreEqual(9, hitboxes[0].Payload.Value);
            yield return null;

            void Record(CombatantStatus who, int amount)
            {
                if (who == status) healed += amount;
            }
        }

        [UnityTest]
        public IEnumerator SleightWithHealCard_AtFullHp_FiresNormally_HealsZero()
        {
            MakePlayer(-3, -3, -3, -3, -3, -3);
            int events = 0;
            System.Action<CombatantStatus, int> record = (who, _) => { if (who == status) events++; };
            CombatantStatus.AnyHealed += record;
            try
            {
                for (int i = 0; i < SleightRules.StockSize; i++) Assert.IsTrue(input.Execute(CombatCommand.StockSlot1));
            }
            finally { CombatantStatus.AnyHealed -= record; }

            Assert.AreEqual(status.MaxHp, status.Hp);
            Assert.AreEqual(0, events, "no heal event when nothing was healed");
            Assert.AreEqual(1, hitboxes.Count);
            yield return null;
        }

        // ---- feedback ring (step 8) ----

        [UnityTest]
        public IEnumerator Ring_VisibleOnlyWhileChannelling_FillTracksProgress()
        {
            MakePlayer(-3, -3, -3, -3);
            var fx = player.AddComponent<HealChannelFx>();
            yield return null;
            yield return null;
            Assert.IsFalse(fx.RingVisible, "hidden when idle");

            StartChannel();
            yield return WaitRealtime(0.2f);
            Time.timeScale = 0f; // freeze the channel so the drawn fill and the progress agree
            yield return null;
            yield return null;
            Assert.IsTrue(fx.RingVisible);
            float progress = channel.Progress01;
            Assert.Greater(progress, 0f);
            Assert.Less(progress, 1f);
            Assert.AreEqual(progress, fx.ShownFill, 1e-5f);

            // The arc's last point sits at fill x 360 degrees around the feet, clockwise from the front.
            var ring = fx.Ring;
            var last = ring.GetPosition(ring.positionCount - 1);
            var first = ring.GetPosition(0);
            var center = first - Vector3.forward * HealChannelFx.Radius;
            float angle = Mathf.Atan2(last.x - center.x, last.z - center.z);
            if (angle < 0f) angle += Mathf.PI * 2f;
            Assert.AreEqual(progress * Mathf.PI * 2f, angle, 0.02f);
            Assert.AreEqual(player.transform.position.y - 1f + HealChannelFx.Height, first.y, 1e-3f, "at the feet");

            Time.timeScale = 1f;
            yield return WaitUntil(() => !channel.IsChannelling);
            yield return null;
            yield return null;
            Assert.IsFalse(fx.RingVisible, "hidden after completing");
            Assert.AreEqual(0f, fx.ShownFill);
        }

        [UnityTest]
        public IEnumerator Ring_HidesWhenInterrupted()
        {
            MakePlayer(-3, -3, -3, -3);
            var fx = player.AddComponent<HealChannelFx>();
            StartChannel();
            yield return null;
            yield return null;
            Assert.IsTrue(fx.RingVisible);
            status.ApplyBreak();
            yield return null;
            yield return null;
            Assert.IsFalse(fx.RingVisible);
        }

        // ---- movement ----

        [UnityTest]
        public IEnumerator MoveSpeed_HalvedWhileChannelling_ThenRestoredToPreviousMultiplier()
        {
            MakePlayer(-3, -3, -3, -3);
            var chill = player.AddComponent<ElementStatusEffects>();
            chill.ApplyChill(false);
            Time.timeScale = 0f; // freeze the chill timer and the channel; Update still runs
            yield return null;
            yield return null;
            float previous = motor.MoveSpeedMultiplier;
            Assert.AreEqual(chill.SpeedMultiplier, previous, 1e-4f);
            Assert.Less(previous, 1f, "chilled, so restoring to a hard 1 would be wrong");

            StartChannel();
            yield return null;
            yield return null;
            Assert.AreEqual(previous * HealRules.ChannelMoveMultiplier, motor.MoveSpeedMultiplier, 1e-4f);

            channel.Cancel();
            yield return null;
            yield return null;
            Assert.AreEqual(previous, motor.MoveSpeedMultiplier, 1e-4f);
            Time.timeScale = 1f;
        }
    }
}
