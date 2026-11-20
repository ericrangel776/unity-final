using System.Collections;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>Effect damage, burn/chill/chain/Physical/Wind statuses and resistances (docs/specs/element-mechanics.md).</summary>
    public class ElementStatusTests
    {
        private const float Eps = 0.01f;
        // Far from other fixtures' leftovers so chains never find strangers.
        private static readonly Vector3 Origin = new Vector3(-300f, 0f, 0f);

        private readonly List<Object> spawned = new List<Object>();
        private ElementSystem system;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            var go = new GameObject("ElementSystem");
            spawned.Add(go);
            system = go.AddComponent<ElementSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private CombatantStatus MakeBody(Team team, Vector3 offset, bool withEffects = true)
        {
            var go = new GameObject($"{team}_body");
            spawned.Add(go);
            go.transform.position = Origin + offset;
            var status = go.AddComponent<CombatantStatus>();
            status.Team = team;
            if (withEffects) go.AddComponent<ElementStatusEffects>();
            return status;
        }

        private TrainingDummyEnemy MakeEnemy(Vector3 offset)
        {
            var go = new GameObject("Enemy");
            spawned.Add(go);
            go.transform.position = Origin + offset;
            // Like the prefab: at batchmode frame rates each step is tiny and the default 0.001 would drop it.
            go.AddComponent<CharacterController>().minMoveDistance = 0f;
            go.AddComponent<CombatantStatus>();
            go.AddComponent<ElementStatusEffects>();
            var enemy = go.AddComponent<TrainingDummyEnemy>();
            enemy.RespawnOnDeath = false;
            enemy.Deck.SetStartingDeck(Deck(), newSeed: 3);
            return enemy;
        }

        private List<CardData> Deck()
        {
            var cards = new List<CardData>();
            for (int v = 3; v <= 7; v++)
            {
                var data = CardData.Create($"elem_{v}_{spawned.Count}", v, CardShape.Strike, Element.Physical);
                cards.Add(data);
                spawned.Add(data);
            }
            return cards;
        }

        private static AttackPayload Card(Element element, int value = 4, Team team = Team.Player, bool sleight = false) =>
            new AttackPayload(1, team, value, false, sleight, 0, CardShape.Strike, element);

        private static ElementStatusEffects Fx(Component c) => c.GetComponent<ElementStatusEffects>();

        // ---- effect damage ----

        [Test]
        public void EffectDamage_RaisesAnyHitLanded_AsAnEffect()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            var hits = new List<HitInfo>();
            System.Action<HitInfo> handler = h => hits.Add(h);
            CombatantStatus.AnyHitLanded += handler;
            try
            {
                Assert.IsTrue(body.TakeEffectDamage(5, Element.Fire, Card(Element.Fire)));
            }
            finally
            {
                CombatantStatus.AnyHitLanded -= handler;
            }
            Assert.AreEqual(1, hits.Count);
            Assert.IsTrue(hits[0].IsEffect);
            Assert.AreEqual(5, hits[0].Damage);
            Assert.AreEqual(body.MaxHp - 5, body.Hp);
            Assert.AreEqual(Element.Fire, body.LastHitElement);
        }

        [Test]
        public void EffectDamage_NoFlinchNoKnockback_NoStatus()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            body.TakeEffectDamage(10, Element.Fire, Card(Element.Fire));
            Assert.IsFalse(body.IsStunned);
            Assert.AreEqual(Vector3.zero, body.KnockbackVelocity);
            Assert.IsFalse(Fx(body).IsBurning, "effect damage never applies a status");
        }

        [Test]
        public void EffectDamage_RespectsIFramesAndTeams_AndCanKill()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            body.IsInvulnerable = true;
            Assert.IsFalse(body.TakeEffectDamage(10, Element.Fire, Card(Element.Fire)));
            body.IsInvulnerable = false;
            Assert.IsFalse(body.TakeEffectDamage(10, Element.Fire, Card(Element.Fire, team: Team.Enemy)), "same team");
            Assert.AreEqual(body.MaxHp, body.Hp);

            bool died = false;
            body.Died += () => died = true;
            Assert.IsTrue(body.TakeEffectDamage(body.MaxHp, Element.Fire, Card(Element.Fire)));
            Assert.IsTrue(died);
        }

        [UnityTest]
        public IEnumerator EffectDamage_NeverHitStops()
        {
            var hs = new GameObject("HitStop");
            spawned.Add(hs);
            hs.AddComponent<HitStop>();
            yield return null;
            var body = MakeBody(Team.Player, Vector3.zero);
            body.TakeEffectDamage(10, Element.Fire, Card(Element.Fire, team: Team.Enemy));
            Assert.AreEqual(1f, Time.timeScale, "burn ticks must never freeze the game");
        }

        // ---- Fire ----

        [UnityTest]
        public IEnumerator FireHit_BurnsForBurnTotal_OverThreeSeconds()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            body.TakeHit(Card(Element.Fire, 4)); // 12 damage -> burn 5
            int afterHit = body.Hp;
            Assert.AreEqual(body.MaxHp - 12, afterHit);
            Assert.IsTrue(Fx(body).IsBurning);
            Assert.AreEqual(3f, Fx(body).BurnRemaining, Eps);
            yield return new WaitForSeconds(1.6f);
            Assert.Greater(afterHit, body.Hp, "burning ticks");
            Assert.Less(afterHit - body.Hp, 5, "not all at once");
            yield return new WaitForSeconds(1.7f);
            Assert.AreEqual(afterHit - ElementRules.BurnTotal(12), body.Hp);
            Assert.IsFalse(Fx(body).IsBurning);
        }

        [Test]
        public void FireHit_LandsOnThePlayerToo()
        {
            var player = MakeBody(Team.Player, Vector3.zero);
            player.TakeHit(Card(Element.Fire, 4, Team.Enemy));
            Assert.IsTrue(Fx(player).IsBurning);
        }

        [Test]
        public void PokeAndZero_ApplyNoStatus()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            body.TakeHit(AttackPayload.Poke(1, Team.Player, 0));
            body.TakeHit(new AttackPayload(1, Team.Player, 0, true, false, 0, CardShape.Strike, Element.Fire));
            Assert.IsFalse(Fx(body).IsBurning);
        }

        [Test]
        public void LethalFireHit_AppliesNoBurn()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            body.SetMaxHp(5);
            body.TakeHit(Card(Element.Fire, 9));
            Assert.IsTrue(body.IsDead);
            Assert.IsFalse(Fx(body).IsBurning);
        }

        // ---- Ice ----

        [Test]
        public void IceHit_ChillsTheVictim_SleightLonger()
        {
            var a = MakeBody(Team.Enemy, Vector3.zero);
            var b = MakeBody(Team.Enemy, new Vector3(20f, 0f, 0f));
            a.TakeHit(Card(Element.Ice));
            b.TakeHit(Card(Element.Ice, 12, sleight: true));
            Assert.AreEqual(2.5f, Fx(a).ChillRemaining, Eps);
            Assert.AreEqual(4f, Fx(b).ChillRemaining, Eps);
            Assert.AreEqual(0.7f, Fx(a).SpeedMultiplier, Eps);
        }

        [UnityTest]
        public IEnumerator Chill_StretchesTheEnemyTelegraph()
        {
            // A far target: the AI only ticks with one, and it never gets in range.
            var target = new GameObject("FarTarget");
            spawned.Add(target);
            target.transform.position = Origin + new Vector3(10f, 0f, 80f);
            var normal = MakeEnemy(Vector3.zero);
            var chilled = MakeEnemy(new Vector3(20f, 0f, 0f));
            normal.Target = target.transform;
            chilled.Target = target.transform;
            yield return null; // Start
            Fx(chilled).ApplyChill(true);
            normal.ForceTelegraph();
            chilled.ForceTelegraph();
            yield return new WaitForSeconds(0.35f);
            float ratio = chilled.TelegraphProgress / normal.TelegraphProgress;
            Assert.AreEqual(0.7f, ratio, 0.08f, "the wind-up ticks at x0.7");
        }

        [UnityTest]
        public IEnumerator Chill_SlowsEnemyMovement()
        {
            var target = new GameObject("Target");
            spawned.Add(target);
            target.transform.position = Origin + new Vector3(0f, 0f, 60f);
            // A floor: an airborne CharacterController's SimpleMove makes no horizontal progress.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            spawned.Add(ground);
            ground.transform.position = Origin + new Vector3(10f, 0f, 30f);
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            var normal = MakeEnemy(new Vector3(0f, 1.1f, 0f));
            var chilled = MakeEnemy(new Vector3(20f, 1.1f, 0f));
            normal.Target = target.transform;
            chilled.Target = target.transform;
            yield return null;
            Fx(chilled).ApplyChill(true); // 4 s: covers both recover phases and the measured window
            yield return new WaitForSeconds(2f); // both are approaching by now (1.2 s and 1.2/0.7 s recover)
            float n0 = normal.transform.position.z, c0 = chilled.transform.position.z;
            yield return new WaitForSeconds(1f);
            float ratio = (chilled.transform.position.z - c0) / (normal.transform.position.z - n0);
            Assert.AreEqual(0.7f, ratio, 0.08f);
        }

        [UnityTest]
        public IEnumerator ChilledPlayer_WalksAtSeventyPercent_AndKeepsTheReloadBoostRule()
        {
            var go = new GameObject("Player");
            spawned.Add(go);
            go.transform.position = Origin;
            go.AddComponent<CharacterController>();
            go.AddComponent<CombatantStatus>().Team = Team.Player;
            go.AddComponent<CombatActionController>();
            go.AddComponent<DeckRuntimeManager>().SetStartingDeck(Deck(), newSeed: 3);
            var motor = go.AddComponent<PlayerMotor>();
            go.AddComponent<PlayerCombatInput>();
            var fx = go.AddComponent<ElementStatusEffects>();
            yield return null;
            Assert.AreEqual(1f, motor.MoveSpeedMultiplier, Eps);
            fx.ApplyChill(false);
            yield return null;
            Assert.AreEqual(0.7f, motor.MoveSpeedMultiplier, Eps);
            Assert.IsTrue(motor.TryDash(), "dash is untouched by chill");
        }

        // ---- Lightning ----

        [Test]
        public void LightningHit_ChainsToFiveOfSixClustered_NotTheStragglerOrThePlayer()
        {
            var victim = MakeBody(Team.Enemy, Vector3.zero);
            var cluster = new[]
            {
                MakeBody(Team.Enemy, new Vector3(1f, 0f, 0f)),
                MakeBody(Team.Enemy, new Vector3(2f, 0f, 0f)),
                MakeBody(Team.Enemy, new Vector3(3f, 0f, 0f)),
                MakeBody(Team.Enemy, new Vector3(4f, 0f, 0f)),
                MakeBody(Team.Enemy, new Vector3(5f, 0f, 0f)),
                MakeBody(Team.Enemy, new Vector3(6f, 0f, 0f)),
            };
            var straggler = MakeBody(Team.Enemy, new Vector3(0f, 0f, -7f));
            var player = MakeBody(Team.Player, new Vector3(0f, 0f, 1f));

            Vector3[] line = null;
            System.Action<Vector3[]> handler = p => line = p;
            ElementSystem.ChainFired += handler;
            try
            {
                victim.TakeHit(Card(Element.Lightning, 4)); // 12 -> chains ceil(7.2) = 8
            }
            finally
            {
                ElementSystem.ChainFired -= handler;
            }

            for (int i = 0; i < 5; i++) Assert.AreEqual(cluster[i].MaxHp - 8, cluster[i].Hp, $"link {i}");
            Assert.AreEqual(cluster[5].MaxHp, cluster[5].Hp, "max 5 links");
            Assert.AreEqual(straggler.MaxHp, straggler.Hp, "7 u from the victim, farther from every link: out of reach");
            Assert.AreEqual(player.MaxHp, player.Hp, "never jumps to the attacker's team");
            Assert.IsNotNull(line);
            Assert.AreEqual(6, line.Length, "victim + 5 links");
            Assert.IsTrue(system.IsChainLineVisible);
        }

        [Test]
        public void ChainLinks_AreJolted_HalvedOnToughEnemies()
        {
            var victim = MakeBody(Team.Enemy, Vector3.zero);
            var mob = MakeBody(Team.Enemy, new Vector3(1f, 0f, 0f));
            var elite = MakeBody(Team.Enemy, new Vector3(2f, 0f, 0f));
            Fx(elite).DurationScale = ElementRules.EliteBossDurationScale;
            var noFx = MakeBody(Team.Enemy, new Vector3(3f, 0f, 0f), withEffects: false);

            victim.TakeHit(Card(Element.Lightning, 4));

            Assert.AreEqual(ElementRules.ChainJoltSeconds, mob.StunRemaining, Eps);
            Assert.AreEqual(ElementRules.ChainJoltSeconds * 0.5f, elite.StunRemaining, Eps);
            Assert.AreEqual(ElementRules.ChainJoltSeconds, noFx.StunRemaining, Eps, "no effects component: full jolt");
        }

        [Test]
        public void ChainJolt_NeverShortensALongerStun()
        {
            var victim = MakeBody(Team.Enemy, Vector3.zero);
            var link = MakeBody(Team.Enemy, new Vector3(1f, 0f, 0f));
            link.ApplyBreak(); // 0.8 s break stun
            float before = link.StunRemaining;
            Assert.Greater(before, ElementRules.ChainJoltSeconds);
            victim.TakeHit(Card(Element.Lightning, 4));
            Assert.AreEqual(before, link.StunRemaining, Eps);
        }

        [Test]
        public void ChainLink_KilledByTheJump_IsNotJolted()
        {
            var victim = MakeBody(Team.Enemy, Vector3.zero);
            var link = MakeBody(Team.Enemy, new Vector3(1f, 0f, 0f));
            link.SetHp(1);
            victim.TakeHit(Card(Element.Lightning, 4));
            Assert.IsTrue(link.IsDead);
            Assert.IsFalse(link.IsStunned);
        }

        [Test]
        public void LightningHit_WithNobodyNear_FiresNoChain()
        {
            var victim = MakeBody(Team.Enemy, Vector3.zero);
            bool fired = false;
            System.Action<Vector3[]> handler = _ => fired = true;
            ElementSystem.ChainFired += handler;
            try
            {
                victim.TakeHit(Card(Element.Lightning));
            }
            finally
            {
                ElementSystem.ChainFired -= handler;
            }
            Assert.IsFalse(fired);
        }

        // ---- Physical ----

        [UnityTest]
        public IEnumerator PhysicalWin_DealsOneAndAHalfTimesBreakPosture()
        {
            float physical = 0f, fire = 0f;
            yield return BreakPosture(Element.Physical, Vector3.zero, p => physical = p);
            yield return BreakPosture(Element.Fire, new Vector3(0f, 0f, 30f), p => fire = p);
            Assert.Greater(fire, 0f);
            Assert.AreEqual(fire * 1.5f, physical, 0.5f);
        }

        private IEnumerator BreakPosture(Element winnerElement, Vector3 offset, System.Action<float> result)
        {
            var playerGo = new GameObject("Winner");
            spawned.Add(playerGo);
            playerGo.transform.position = Origin + offset;
            playerGo.AddComponent<CombatantStatus>().Team = Team.Player;
            var winner = playerGo.AddComponent<CombatActionController>();
            var enemyGo = new GameObject("Loser");
            spawned.Add(enemyGo);
            enemyGo.transform.position = Origin + offset + new Vector3(0f, 0f, 2.2f);
            enemyGo.AddComponent<CombatantStatus>().Team = Team.Enemy;
            var loser = enemyGo.AddComponent<CombatActionController>();

            // The loser swings a Physical Strike; the winner's attack (a Strike box or a fireball) overlaps it at once.
            loser.Spawn(new AttackPayload(loser.Status.OwnerId, Team.Enemy, 5, false, false, 1, CardShape.Strike,
                Element.Physical), Vector3.back);
            winner.Spawn(new AttackPayload(winner.Status.OwnerId, Team.Player, 7, false, false, 2, CardShape.Strike,
                winnerElement), Vector3.forward);
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            Assert.IsTrue(loser.Status.IsStunned, $"{winnerElement} 7 should break 5");
            result(loser.Status.Posture);
        }

        // ---- Wind ----

        [Test]
        public void WindHit_PushesThreeTimesAsHard_BossHalvesIt()
        {
            var a = MakeBody(Team.Enemy, Vector3.zero);
            var b = MakeBody(Team.Enemy, new Vector3(20f, 0f, 0f));
            var boss = MakeBody(Team.Enemy, new Vector3(40f, 0f, 0f));
            boss.KnockbackTakenMultiplier = ElementRules.BossWindKnockbackScale;
            a.TakeHit(Card(Element.Physical, 5), Vector3.forward);
            b.TakeHit(Card(Element.Wind, 5), Vector3.forward);
            boss.TakeHit(Card(Element.Wind, 5), Vector3.forward);
            float baseSpeed = a.KnockbackVelocity.magnitude;
            Assert.AreEqual(baseSpeed * 3f, b.KnockbackVelocity.magnitude, Eps);
            Assert.AreEqual(baseSpeed * 3f * 0.5f, boss.KnockbackVelocity.magnitude, Eps);
        }

        [UnityTest]
        public IEnumerator WindPush_OutlastsANormalPush()
        {
            var a = MakeBody(Team.Enemy, Vector3.zero);
            var b = MakeBody(Team.Enemy, new Vector3(20f, 0f, 0f));
            a.TakeHit(Card(Element.Physical, 6), Vector3.forward);
            b.TakeHit(Card(Element.Wind, 6), Vector3.forward);
            // Scaled time: knockback decays with Time.deltaTime.
            float start = Time.time, end = Time.realtimeSinceStartup + 2f;
            while (Time.time - start < 0.2f && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(Vector3.zero, a.KnockbackVelocity, "a normal push is gone after 0.15 s");
            Assert.Greater(b.KnockbackVelocity.magnitude, ElementRules.WindSlamMinSpeed, "a Wind push still carries slam speed");
        }

        [Test]
        public void BossKnockbackResistance_AppliesToWindOnly()
        {
            var a = MakeBody(Team.Enemy, Vector3.zero);
            var boss = MakeBody(Team.Enemy, new Vector3(20f, 0f, 0f));
            boss.KnockbackTakenMultiplier = 0.5f;
            a.TakeHit(Card(Element.Physical, 5), Vector3.forward);
            boss.TakeHit(Card(Element.Physical, 5), Vector3.forward);
            Assert.AreEqual(a.KnockbackVelocity.magnitude, boss.KnockbackVelocity.magnitude, Eps);
        }

        [UnityTest]
        public IEnumerator WindHit_IntoAWall_AddsSlamPostureOnce()
        {
            var enemy = MakeEnemy(Vector3.zero);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(wall);
            wall.transform.position = Origin + new Vector3(1.5f, 0f, 0f);
            wall.transform.localScale = new Vector3(1f, 4f, 6f);
            yield return null;
            Physics.SyncTransforms();

            var gust = Card(Element.Wind, 5);
            enemy.Status.TakeHit(gust, Vector3.right);
            int hpAfterHit = enemy.Status.Hp;
            Assert.IsTrue(Fx(enemy).IsSlamWindowOpen);
            float end = Time.realtimeSinceStartup + 1f;
            while (enemy.Status.Posture <= 0f && Time.realtimeSinceStartup < end) yield return null;
            float slam = ElementRules.WindSlamPostureFraction * enemy.Status.MaxPosture;
            Assert.AreEqual(slam, enemy.Status.Posture, 2f, "50% of max posture (minus a frame of decay)");
            Assert.AreEqual(hpAfterHit - ElementRules.SlamDamage(CombatMath.Damage(gust)), enemy.Status.Hp,
                "the slam deals half the gust's hit");
            Assert.IsFalse(Fx(enemy).IsSlamWindowOpen, "once per gust hit");
            yield return new WaitForSeconds(0.2f);
            Assert.LessOrEqual(enemy.Status.Posture, slam, "no second slam from further bumps");
        }

        [UnityTest]
        public IEnumerator NonWindHit_IntoAWall_IsNoSlam()
        {
            var enemy = MakeEnemy(Vector3.zero);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(wall);
            wall.transform.position = Origin + new Vector3(1.1f, 0f, 0f);
            wall.transform.localScale = new Vector3(1f, 4f, 6f);
            yield return null;
            Physics.SyncTransforms();
            enemy.Status.TakeHit(Card(Element.Physical, 9), Vector3.right);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0f, enemy.Status.Posture);
        }

        // ---- resistances ----

        [Test]
        public void HalvedDurationScale_HalvesBurnAndChill()
        {
            var elite = MakeBody(Team.Enemy, Vector3.zero);
            Fx(elite).DurationScale = ElementRules.EliteBossDurationScale;
            elite.TakeHit(Card(Element.Fire, 4));
            elite.TakeHit(Card(Element.Ice, 4));
            Assert.AreEqual(1.5f, Fx(elite).BurnRemaining, Eps);
            Assert.AreEqual(1.25f, Fx(elite).ChillRemaining, Eps);
        }

        [Test]
        public void Death_ClearsStatuses()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            Fx(body).ApplyChill(true);
            Fx(body).ApplyBurn(10, true, Card(Element.Fire));
            body.TakeEffectDamage(body.MaxHp, Element.Physical, Card(Element.Physical));
            Assert.IsFalse(Fx(body).IsBurning);
            Assert.IsFalse(Fx(body).IsChilled);
        }

        [Test]
        public void Revive_ClearsStatuses()
        {
            var body = MakeBody(Team.Enemy, Vector3.zero);
            Fx(body).ApplyChill(true);
            body.Revive();
            Assert.IsFalse(Fx(body).IsChilled);
        }
    }
}
