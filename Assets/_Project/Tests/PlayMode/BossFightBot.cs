using System;
using System.Collections.Generic;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>
    /// A scripted "decent player" for measurements (BossFightMeasurement, RunTimingMeasurement). It drives the player
    /// through <see cref="PlayerCombatInput.Execute"/> and <see cref="PlayerMotor"/> (the input component is disabled),
    /// with a human-ish reaction delay and imperfect timing. Tactics: beat a telegraph with a higher card, otherwise
    /// 0-parry the swing, otherwise dash; 0-parry or dash boss Sleights; go all-in (Sleights first) during the reload
    /// window and Exhausted; play the lowest card for chip damage and keep the highest for answers. It's a proxy, not a
    /// human. Boss mode fights one boss; run mode (docs/specs/run-timing.md) fights the nearest live enemy of a
    /// <see cref="RunDirector"/> room and switches to the boss tactics when the room has a boss.
    /// </summary>
    internal sealed class BossFightBot : MonoBehaviour
    {
        public float ReactionSeconds = 0.15f;
        /// <summary>Std-dev of the reload/Deadlock press error, as a fraction of the bar / in seconds.</summary>
        public float TimingError = 0.05f;
        public int Seed = 1;
        /// <summary>Neutral heals only below this fraction of max HP.</summary>
        public float HealBelow = 0.6f;

        private PlayerCombatInput input;
        private PlayerMotor motor;
        private DeckRuntimeManager deck;
        private CombatantStatus status;
        private RunDirector director;           // run mode only
        private TrainingDummyEnemy body;        // the current target
        private BossEnemy boss;                 // set when the target is the boss
        private BossEnemy sleightWatched;       // the boss whose SleightFired we're subscribed to
        private readonly Dictionary<TrainingDummyEnemy, Action<RuntimeCardInstance>> swingWatch =
            new Dictionary<TrainingDummyEnemy, Action<RuntimeCardInstance>>();
        private System.Random rng;
        private float recovery;
        private float parryAt = -1f;
        private float reloadPressTarget = -1f;
        private bool deadlockSubmitted;
        private float deadlockPressAt = -1f;

        // Stats
        public int CardsPlayed, Parries, Dashes, SleightsFired, ReloadsPerfect, ReloadsOther;

        /// <summary>Boss mode: fights <paramref name="target"/> only.</summary>
        public void Init(PlayerCombatInput player, BossEnemy target)
        {
            Bind(player);
            SetBoss(target);
        }

        /// <summary>Run mode: each frame fights the room's boss if alive, else the nearest live enemy.</summary>
        public void Init(PlayerCombatInput player, RunDirector runDirector)
        {
            Bind(player);
            director = runDirector;
        }

        private void Bind(PlayerCombatInput player)
        {
            input = player;
            motor = player.GetComponent<PlayerMotor>();
            deck = player.GetComponent<DeckRuntimeManager>();
            status = player.GetComponent<CombatantStatus>();
            rng = new System.Random(Seed);
            input.enabled = false; // the bot is the input now
        }

        private void OnDestroy()
        {
            foreach (var pair in swingWatch)
                if (pair.Key != null) pair.Key.Swung -= pair.Value;
            swingWatch.Clear();
            if (sleightWatched != null) sleightWatched.SleightFired -= HandleBossSleight;
            sleightWatched = null;
        }

        private void SetBoss(BossEnemy target)
        {
            boss = target;
            body = target.Body;
            Watch(body);
            if (sleightWatched != target)
            {
                if (sleightWatched != null) sleightWatched.SleightFired -= HandleBossSleight;
                sleightWatched = target;
                target.SleightFired += HandleBossSleight;
            }
        }

        /// <summary>Subscribes to an enemy's swings the first time it is seen (wave 2 spawns mid-room).</summary>
        private void Watch(TrainingDummyEnemy enemy)
        {
            if (enemy == null || swingWatch.ContainsKey(enemy)) return;
            Action<RuntimeCardInstance> handler = _ => HandleSwung(enemy);
            swingWatch.Add(enemy, handler);
            enemy.Swung += handler;
        }

        private void HandleSwung(TrainingDummyEnemy enemy)
        {
            // Couldn't beat the telegraph: answer the live swing with a 0 after reacting. A mob's swing only matters
            // when it's close enough to land; the boss's always does.
            bool isBoss = boss != null && enemy == boss.Body;
            if (!isBoss && enemy != null)
            {
                var offset = enemy.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude > Reach(enemy) + 1f) return;
            }
            if (deck.Cycle.ZerosReady > 0 && parryAt < 0f) parryAt = Time.time + ReactionSeconds * 0.6f;
        }

        private void HandleBossSleight(AttackPayload payload)
        {
            if (deck.Cycle.ZerosReady > 0 && parryAt < 0f) parryAt = Time.time + ReactionSeconds;
        }

        private float Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        /// <summary>Run mode: the live boss if there is one, else the nearest live enemy (null if none).</summary>
        private void Retarget()
        {
            var roomBoss = director.Boss;
            if (roomBoss != null && !roomBoss.Status.IsDead)
            {
                SetBoss(roomBoss);
                return;
            }
            boss = null;
            body = null;
            float best = float.MaxValue;
            foreach (var e in director.Enemies)
            {
                if (e == null || e.Status.IsDead) continue;
                Watch(e);
                float d = (e.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; body = e; }
            }
        }

        private static float Reach(TrainingDummyEnemy enemy) => 1.6f + 0.5f * enemy.transform.localScale.x; // strike reach plus the body radius

        private void Update()
        {
            if (status == null) return;
            if (director != null && !status.IsDead) Retarget();
            if (body == null || status.IsDead || body.Status.IsDead)
            {
                motor.SetMoveInput(Vector2.zero);
                if (director != null && !status.IsDead) Idle();
                return;
            }
            if (HandleDeadlock()) return;

            var toTarget = body.transform.position - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            motor.SetAimDirection(toTarget);
            if (recovery > 0f) recovery -= Time.deltaTime;
            motor.MoveSpeedMultiplier = deck.Reload.SpeedMultiplier;
            HandleReloadPress();

            bool canAct = CanAct();
            var cycle = deck.Cycle;
            float reach = Reach(body);

            // 1) A scheduled 0-parry (after a swing / Sleight activation).
            if (parryAt >= 0f && Time.time >= parryAt)
            {
                parryAt = -1f;
                if (canAct && Act(CombatCommand.Parry)) { Parries++; return; }
            }

            if (boss != null)
            {
                // 2) Boss Sleight wind-up: keep a 0 for after it fires, else dash out at the last moment.
                if (boss.State == BossState.SleightWindup)
                {
                    Move(-toTarget, dist < 4f);
                    if (cycle.ZerosReady == 0 && boss.StateRemaining < 0.2f + ReactionSeconds && motor.TryDash()) Dashes++;
                    return;
                }

                // 3) Punish windows: all in.
                bool punish = boss.State == BossState.ReloadWindow || boss.State == BossState.Exhausted;
                if (punish)
                {
                    Move(toTarget, dist > reach * 0.8f);
                    if (!canAct || dist > reach) return;
                    // A Sleight while Exhausted (2x damage): keep stocking once started, start only with time left.
                    bool stocking = deck.Cycle.StockCount > 0;
                    if (boss.State == BossState.Exhausted && (stocking || (boss.StateRemaining > 1.2f && FullHand())) && StockOne())
                        return;
                    if (PlayExtreme(highest: true)) return;
                    if (cycle.CanReload) StartReload();
                    else Act(CombatCommand.Poke);
                    return;
                }
            }

            // 4) Normal swing telegraph.
            var telegraph = body.CurrentTelegraph;
            if (telegraph != null)
            {
                int v = telegraph.GetEffectiveValue();
                int slot = BestAbove(v);
                if (slot >= 0)
                {
                    Move(toTarget, dist > reach * 0.8f);
                    if (canAct && dist <= reach && Act(CombatCommand.PlaySlot1 + slot)) CardsPlayed++;
                    return;
                }
                if (cycle.ZerosReady > 0) { Move(toTarget, dist > reach); return; } // stay close, parry the swing
                Move(-toTarget, true);
                if (body.TelegraphProgress > 0.75f && motor.TryDash()) Dashes++;
                return;
            }

            // 5) Neutral: heal when hurt, reload when out of attacks, else chip with the lowest card (keep the best
            //    for answers).
            if (canAct && status.Hp <= status.MaxHp * HealBelow && PlayHeal()) return;
            if (OutOfCards())
            {
                Move(-toTarget, dist < 3f);
                if (!canAct) return;
                if (cycle.CanReload) StartReload();
                else if (dist <= reach) Act(CombatCommand.Poke); // e.g. only a heal left at full HP: poke, never idle
                return;
            }
            Move(toTarget, dist > reach * 0.8f);
            if (!canAct || dist > reach) return;
            if (HandCount() >= 2 ? PlayExtreme(highest: false) : PlayExtreme(highest: true)) return;
        }

        /// <summary>Plays a heal card from the hand (the game refuses one at full HP).</summary>
        private bool PlayHeal()
        {
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var c = deck.Cycle.Hand[i];
                if (c != null && c.IsHeal && Act(CombatCommand.PlaySlot1 + i)) return true;
            }
            return false;
        }

        /// <summary>A one-line state dump for stalled rooms (measurement TIMEOUTs, the smoke test's failure message).</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"bot hp {status.Hp}/{status.MaxHp} pos {transform.position} recovery {recovery:0.00} stunned {status.IsStunned} ");
            sb.Append($"reload {deck.Reload.State} canReload {deck.Cycle.CanReload} zeros {deck.Cycle.ZerosReady} hand [");
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var c = deck.Cycle.Hand[i];
                sb.Append(c == null ? "-" : (c.IsHeal ? "H" : "") + c.GetEffectiveValue()).Append(i < DeckCycle.HandSize - 1 ? " " : "");
            }
            sb.Append($"] cards {CardsPlayed} parries {Parries} dashes {Dashes}");
            if (body != null)
            {
                var d = body.transform.position - transform.position;
                d.y = 0f;
                sb.Append($" | target {body.name} pos {body.transform.position} dist {d.magnitude:0.0} hp {body.Status.Hp} " +
                          $"telegraph {(body.CurrentTelegraph != null ? body.CurrentTelegraph.GetEffectiveValue().ToString() : "-")}");
            }
            else sb.Append(" | no target");
            if (director != null)
                foreach (var e in director.Enemies)
                    if (e != null && !e.Status.IsDead) sb.Append($" | {e.name} at {e.transform.position} hp {e.Status.Hp}");
            return sb.ToString();
        }

        /// <summary>Run mode with no live target (wave delay, cleared room): stand still, finish/start a reload.</summary>
        private void Idle()
        {
            if (recovery > 0f) recovery -= Time.deltaTime;
            HandleReloadPress();
            if (OutOfCards() && CanAct() && deck.Cycle.CanReload) StartReload();
        }

        private bool CanAct() => Time.timeScale > 0f && !status.IsStunned && !motor.IsDashing && recovery <= 0f;

        private bool HandleDeadlock()
        {
            var d = DeadlockController.Current;
            if (d == null || !d.IsActive) { deadlockSubmitted = false; deadlockPressAt = -1f; return false; }
            motor.SetMoveInput(Vector2.zero);
            if (deadlockSubmitted) return true;
            if (deadlockPressAt < 0f) deadlockPressAt = Mathf.Max(0.05f, d.TargetTime + Gaussian() * TimingError * 2f);
            if (d.Elapsed >= deadlockPressAt)
            {
                deadlockSubmitted = true;
                // Commit the best hand card when there is one (adds its value), else push on timing alone.
                int best = BestAbove(0);
                d.SubmitPlayer(best >= 0 ? DeadlockChoice.Slot1 + best : DeadlockChoice.Poke);
            }
            return true;
        }

        private void HandleReloadPress()
        {
            var reload = deck.Reload;
            if (reload.State != ReloadState.Running) { reloadPressTarget = -1f; return; }
            if (reloadPressTarget < 0f) reloadPressTarget = Mathf.Clamp01(reload.Target01 + Gaussian() * TimingError);
            if (reload.Progress01 >= reloadPressTarget)
            {
                var grade = reload.Press();
                if (grade == ReloadGrade.Perfect) ReloadsPerfect++; else ReloadsOther++;
                reloadPressTarget = -1f;
            }
        }

        private void StartReload()
        {
            if (Act(CombatCommand.Reload)) reloadPressTarget = -1f;
        }

        private bool Act(CombatCommand command)
        {
            if (!input.Execute(command)) return false;
            recovery = input.ActionRecoverySeconds;
            return true;
        }

        /// <summary>One stock press (a Sleight takes three, each with its own recovery, like a human).</summary>
        private bool StockOne()
        {
            int slot = FirstCard();
            if (slot < 0) return false;
            bool fires = deck.Cycle.StockCount == SleightRules.StockSize - 1;
            if (!Act(CombatCommand.StockSlot1 + slot)) return false;
            if (fires) SleightsFired++;
            return true;
        }

        private bool PlayExtreme(bool highest)
        {
            int pick = -1;
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var c = deck.Cycle.Hand[i];
                if (c == null || c.IsHeal) continue;
                if (pick < 0) { pick = i; continue; }
                int a = c.GetEffectiveValue(), b = deck.Cycle.Hand[pick].GetEffectiveValue();
                if (highest ? a > b : a < b) pick = i;
            }
            if (pick < 0 || !Act(CombatCommand.PlaySlot1 + pick)) return false;
            CardsPlayed++;
            return true;
        }

        private int BestAbove(int value)
        {
            int pick = -1;
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var c = deck.Cycle.Hand[i];
                if (c == null || c.IsHeal || c.GetEffectiveValue() <= value) continue;
                // The smallest card that still wins, so the big ones stay for later.
                if (pick < 0 || c.GetEffectiveValue() < deck.Cycle.Hand[pick].GetEffectiveValue()) pick = i;
            }
            return pick;
        }

        private int FirstCard()
        {
            for (int i = 0; i < DeckCycle.HandSize; i++) if (deck.Cycle.Hand[i] != null) return i;
            return -1;
        }

        /// <summary>Attack cards in hand (heals excluded: they can't answer a telegraph or chip).</summary>
        private int HandCount()
        {
            int n = 0;
            for (int i = 0; i < DeckCycle.HandSize; i++)
            {
                var c = deck.Cycle.Hand[i];
                if (c != null && !c.IsHeal) n++;
            }
            return n;
        }

        private bool FullHand() => HandCount() == DeckCycle.HandSize;
        private bool OutOfCards() => HandCount() == 0;

        /// <summary>Walks along <paramref name="worldDirection"/> (converted to the motor's camera-relative input).</summary>
        private void Move(Vector3 worldDirection, bool go)
        {
            if (!go || worldDirection.sqrMagnitude < 0.0001f) { motor.SetMoveInput(Vector2.zero); return; }
            var cam = Camera.main;
            var dir = worldDirection.normalized;
            if (cam == null) { motor.SetMoveInput(new Vector2(dir.x, dir.z)); return; }
            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            motor.SetMoveInput(new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, forward)));
        }
    }
}
