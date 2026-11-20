using System;
using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// One per scene: builds every <see cref="SfxId"/> clip from <see cref="SfxSynth"/> at startup and plays them from a
    /// small round-robin <see cref="AudioSource"/> pool with ±5% pitch variance. Listens to the game's events (clashes,
    /// Deadlocks, swings, hits, dash, reload, boss states, run end), so gameplay code never calls it. The same sound
    /// within <see cref="RepeatGuardSeconds"/> plays once (multi-hit Sleights don't stack into noise).
    /// Without an audio device (batchmode) it logs once, never throws, and still raises <see cref="Played"/>.
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        public const int PoolSize = 10;
        public const float PitchVariance = 0.05f;
        public const float RepeatGuardSeconds = 0.04f;

        [Range(0f, 1f)] [SerializeField] private float volume = 0.7f;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private RunDirector director;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerCombatInput player;

        private readonly Dictionary<SfxId, AudioClip> clips = new Dictionary<SfxId, AudioClip>();
        private readonly Dictionary<SfxId, float> lastPlayed = new Dictionary<SfxId, float>();
        private AudioSource[] pool;
        private int next;
        private System.Random random = new System.Random(4242);
        private bool warned;
        private bool skipNextDeadlockClash;
        private PlayerMotor motor;
        private DeckRuntimeManager playerDeck;
        private BossEnemy boss;
        private bool started;
        private bool instancesBound;

        /// <summary>The scene's service (the most recently enabled one).</summary>
        public static AudioService Current { get; private set; }
        /// <summary>A sound was requested and passed the repeat guard (raised even when audio is unavailable).</summary>
        public event Action<SfxId> Played;
        public int ClipCount => clips.Count;

        private void Awake()
        {
            if (director == null) director = FindAnyObjectByType<RunDirector>();
            if (player == null) player = FindAnyObjectByType<PlayerCombatInput>();
            BuildClips();
            pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // 2D: the camera is far above the arena
                pool[i] = source;
            }
        }

        private void BuildClips()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : SfxSynth.DefaultSampleRate;
            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                try
                {
                    var samples = SfxSynth.Generate(id, rate);
                    var clip = AudioClip.Create($"sfx_{id}", samples.Length, 1, rate, false);
                    clip.SetData(samples, 0);
                    clips[id] = clip;
                }
                catch (Exception e)
                {
                    WarnOnce($"AudioService: could not build {id}: {e.Message}");
                }
            }
        }

        private void OnEnable()
        {
            Current = this;
            ClashSystem.ClashOccurred += HandleClash;
            DeadlockController.Resolved += HandleDeadlock;
            CombatActionController.AnyHitboxSpawned += HandleHitbox;
            CombatantStatus.AnyHitTaken += HandleHit;
            CombatantStatus.AnyHealed += HandleHealed;
            if (started) BindInstances();
        }

        // Start, not OnEnable: other objects' Awake (the deck's reload bar) may not have run yet when we're enabled.
        private void Start()
        {
            started = true;
            BindInstances();
        }

        private void BindInstances()
        {
            if (instancesBound) return;
            instancesBound = true;
            if (player != null) BindPlayer(player.GetComponent<PlayerMotor>(), player.GetComponent<DeckRuntimeManager>());
            if (director != null)
            {
                director.BossSpawned += BindBoss;
                director.RunEnded += HandleRunEnded;
                if (director.Boss != null) BindBoss(director.Boss);
            }
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            ClashSystem.ClashOccurred -= HandleClash;
            DeadlockController.Resolved -= HandleDeadlock;
            CombatActionController.AnyHitboxSpawned -= HandleHitbox;
            CombatantStatus.AnyHitTaken -= HandleHit;
            CombatantStatus.AnyHealed -= HandleHealed;
            instancesBound = false;
            BindPlayer(null, null);
            if (director != null)
            {
                director.BossSpawned -= BindBoss;
                director.RunEnded -= HandleRunEnded;
            }
            BindBoss(null);
        }

        private void OnDestroy()
        {
            foreach (var clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
        }

        /// <summary>Follows the player's dash and reload bar (either may be null; nulls stop listening).</summary>
        public void BindPlayer(PlayerMotor playerMotor, DeckRuntimeManager deck)
        {
            if (motor != null) motor.Dashed -= HandleDash;
            if (playerDeck != null && playerDeck.Reload != null) playerDeck.Reload.Completed -= HandleReload;
            motor = playerMotor;
            playerDeck = deck;
            if (motor != null) motor.Dashed += HandleDash;
            if (playerDeck != null && playerDeck.Reload != null) playerDeck.Reload.Completed += HandleReload;
        }

        /// <summary>Follows <paramref name="target"/>'s boss events (null stops).</summary>
        public void BindBoss(BossEnemy target)
        {
            if (boss != null)
            {
                boss.SleightWindupStarted -= HandleWindup;
                boss.SleightFired -= HandleBossSleight;
                boss.CycleEnded -= HandleCycleEnded;
                boss.ExhaustedStarted -= HandleExhausted;
            }
            boss = target;
            if (boss != null)
            {
                boss.SleightWindupStarted += HandleWindup;
                boss.SleightFired += HandleBossSleight;
                boss.CycleEnded += HandleCycleEnded;
                boss.ExhaustedStarted += HandleExhausted;
            }
        }

        /// <summary>Plays <paramref name="id"/> unless the same sound played within the repeat guard. Never throws.</summary>
        public void Play(SfxId id)
        {
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(id, out float last) && now - last < RepeatGuardSeconds) return;
            lastPlayed[id] = now;
            Played?.Invoke(id);

            if (!clips.TryGetValue(id, out var clip) || clip == null) return;
            try
            {
                var source = pool[next];
                next = (next + 1) % pool.Length;
                source.pitch = 1f + (float)(random.NextDouble() * 2.0 - 1.0) * PitchVariance;
                source.PlayOneShot(clip, volume);
            }
            catch (Exception e)
            {
                WarnOnce($"AudioService: audio unavailable ({e.Message}); sounds are skipped.");
            }
        }

        private void WarnOnce(string message)
        {
            if (warned) return;
            warned = true;
            Debug.Log(message);
        }

        // ---- Event mapping ----

        internal void HandleClash(ClashEvent e)
        {
            // A Big Deadlock's result already played its own sound.
            if (e.FromDeadlock && skipNextDeadlockClash)
            {
                skipNextDeadlockClash = false;
                return;
            }
            switch (e.Result.Outcome)
            {
                case ClashOutcome.None: return;
                case ClashOutcome.Neutral: Play(SfxId.ClashNeutral); return;
                default: Play(e.Result.BrokeByZero ? SfxId.ZeroBreak : SfxId.CardBreak); return;
            }
        }

        internal void HandleDeadlock(DeadlockResult r)
        {
            if (!r.IsBig) return; // a normal Deadlock sounds like the clash it resolves into
            skipNextDeadlockClash = true;
            Play(SfxId.BigDeadlock);
        }

        private void HandleHitbox(AttackHitbox hb)
        {
            if (hb == null) return;
            // The boss Sleight has its own boom (SleightFired).
            if (hb.Payload.IsSleight && hb.Payload.Team == Team.Enemy) return;
            Play(SfxId.Swing);
        }

        private void HandleHit(CombatantStatus victim, int damage) => Play(SfxId.Hit);
        private void HandleDash() => Play(SfxId.Dash);

        /// <summary>Only the player's heals chime.</summary>
        private void HandleHealed(CombatantStatus who, int amount)
        {
            if (who != null && who.Team == Team.Player) Play(SfxId.Heal);
        }

        private void HandleReload(ReloadGrade grade) =>
            Play(grade == ReloadGrade.Perfect || grade == ReloadGrade.Good ? SfxId.ReloadPerfect : SfxId.ReloadMiss);

        private void HandleWindup(int sum) => Play(SfxId.BossSleightWindup);
        private void HandleBossSleight(AttackPayload payload) => Play(SfxId.BossSleightFire);

        private void HandleCycleEnded(BossCycleEnd end)
        {
            if (end == BossCycleEnd.ReloadWindow) Play(SfxId.BossReload);
        }

        private void HandleExhausted() => Play(SfxId.BossExhausted);

        private void HandleRunEnded(bool victory)
        {
            if (victory) Play(SfxId.Victory);
        }
    }
}
