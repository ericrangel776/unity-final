#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// In-game debug menu (docs/specs/debug-menu.md). F1 or backquote toggles it. It creates itself on play
    /// (Editor and Development builds only), survives scene loads and finds its targets lazily, so it works in any
    /// scene. While open (and not pinned) it blocks gameplay input through <see cref="PlayerCombatInput.DebugInputBlocked"/>.
    /// Commands live in <see cref="DebugCommands"/>.
    /// </summary>
    public sealed class DebugMenu : MonoBehaviour
    {
        public const int SortingOrder = 100;
        public const float RefreshSeconds = 0.25f;
        private const float PanelWidth = 500f, PanelHeight = 910f, PanelTop = 152f, RowHeight = 34f, RowGap = 4f, Pad = 12f;
        private const float ContentTop = 104f;
        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.94f);
        private static readonly Color TabColor = new Color(0.18f, 0.2f, 0.26f, 1f);
        private static readonly Color ActiveTabColor = new Color(0.35f, 0.3f, 0.12f, 1f);

        public static DebugMenu Instance { get; private set; }

        /// <summary>Juice switches, applied to whatever FX components the current scene has.</summary>
        public sealed class JuiceToggles
        {
            public bool Shake = true, Sparks = true, Numbers = true, Sound = true;
            public bool AllOn => Shake && Sparks && Numbers && Sound;
        }

        private readonly List<Row> rows = new List<Row>();
        private Canvas canvas;
        private CanvasGroup group;
        private RectTransform content;
        private Text infoText;
        private Text statusText;
        private Text pinLabel;
        private readonly Dictionary<DebugTab, Image> tabImages = new Dictionary<DebugTab, Image>();
        private float nextRefresh;
        private float nextFind;
        private float fps;
        private float resumeScale = 1f;
        private bool juiceApplied = true;
        private PlayerCombatInput blockedPlayer; // the player we blocked, so we only ever release our own block
        private RunDirector boundDirector;
        private RunState boundRun;
        private List<CardData> allCards;
        private RunContent allCardsFrom;

        public bool IsOpen { get; private set; }
        /// <summary>Pinned: the panel stays up but gameplay input is free (to watch the Info tab while fighting).</summary>
        public bool Pinned { get; private set; }
        public DebugTab CurrentTab { get; private set; } = DebugTab.Run;
        public IReadOnlyList<DebugCommand> Commands { get; private set; }
        public DebugRoomPicker Picker { get; } = new DebugRoomPicker();
        public DebugRunTimer Timer { get; } = new DebugRunTimer();
        public JuiceToggles Juice { get; } = new JuiceToggles();
        public string InfoText => infoText != null ? infoText.text : "";
        public string LastError { get; private set; }
        /// <summary>Rows drawn for the current tab.</summary>
        public int RowCount => rows.Count;
        public bool PanelVisible => canvas != null && canvas.gameObject.activeSelf;

        // ---- targets (found lazily; Unity-null after a scene unload) ----

        public RunDirector Director { get; private set; }
        public PlayerCombatInput PlayerInput { get; private set; }
        public CombatantStatus PlayerStatus => PlayerInput != null ? PlayerInput.GetComponent<CombatantStatus>() : null;
        public DeckRuntimeManager PlayerDeck => PlayerInput != null ? PlayerInput.GetComponent<DeckRuntimeManager>() : null;

        /// <summary>Every distinct card in the run content (starter, pool, enemy decks), for the Add card picker.</summary>
        public IReadOnlyList<CardData> AllCards
        {
            get
            {
                var content = Director != null ? Director.Content : null;
                if (content == null) return Array.Empty<CardData>();
                if (allCards != null && allCardsFrom == content) return allCards;
                allCardsFrom = content;
                allCards = content.StarterDeck.Concat(content.DraftPool).Concat(content.MobDeck).Concat(content.HexfireDeck)
                    .Concat(content.EliteDeck).Concat(content.BossDeck).Concat(content.CasterDeck).Concat(content.RusherDeck)
                    .Append(content.EnemyZero).Where(c => c != null).Distinct().ToList();
                return allCards;
            }
        }

        public List<TrainingDummyEnemy> LivingEnemies() =>
            FindObjectsByType<TrainingDummyEnemy>(FindObjectsSortMode.None)
                .Where(e => e != null && e.Status != null && !e.Status.IsDead).ToList();

        public TrainingDummyEnemy NearestEnemy()
        {
            var from = PlayerStatus != null ? PlayerStatus.transform.position : Vector3.zero;
            return LivingEnemies().OrderBy(e => (e.transform.position - from).sqrMagnitude).FirstOrDefault();
        }

        /// <summary>A ground point <paramref name="distance"/> in front of the player, kept inside the arena.</summary>
        public Vector3 SpawnPointAhead(float distance)
        {
            var t = PlayerInput.transform;
            var forward = t.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var p = t.position + forward.normalized * distance;
            return new Vector3(Mathf.Clamp(p.x, -10f, 10f), 0f, Mathf.Clamp(p.z, -10f, 10f));
        }

        /// <summary>Finds the director and player of the current scene if the cached ones are gone.</summary>
        public void FindTargets()
        {
            if (Director == null) Director = FindAnyObjectByType<RunDirector>();
            if (PlayerInput == null) PlayerInput = FindAnyObjectByType<PlayerCombatInput>();
            BindTimer();
        }

        // ---- lifecycle ----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("DebugMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugMenu>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Commands = DebugCommands.Build(this);
            Build();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ReleaseInput();
            UnbindTimer();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.f1Key.wasPressedThisFrame || keyboard.backquoteKey.wasPressedThisFrame)) Toggle();

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) fps = fps <= 0f ? 1f / dt : Mathf.Lerp(fps, 1f / dt, 0.1f);

            float now = Time.unscaledTime;
            if (now >= nextFind && (Director == null || PlayerInput == null))
            {
                nextFind = now + (IsOpen ? RefreshSeconds : 1f);
                FindTargets();
            }
            UpdateInputBlock();
            if (IsOpen && now >= nextRefresh) Refresh();
        }

        // ---- open / close / tabs ----

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            FindTargets();
            EnsureEventSystem();
            IsOpen = true;
            SetVisible(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            SetVisible(false);
            ReleaseInput();
        }

        /// <summary>Blocks the player while open and not pinned. Only writes the flag when this menu's wish changes.</summary>
        private void UpdateInputBlock()
        {
            if (IsOpen && !Pinned)
            {
                if (PlayerInput == null || blockedPlayer == PlayerInput) return;
                ReleaseInput();
                PlayerInput.DebugInputBlocked = true;
                blockedPlayer = PlayerInput;
            }
            else ReleaseInput();
        }

        private void ReleaseInput()
        {
            if (blockedPlayer != null) blockedPlayer.DebugInputBlocked = false;
            blockedPlayer = null;
        }

        public void SetPinned(bool pinned)
        {
            Pinned = pinned;
            if (group != null) group.alpha = pinned ? 0.8f : 1f;
            if (pinLabel != null) pinLabel.text = pinned ? "Pinned" : "Pin";
        }

        public void SelectTab(DebugTab tab)
        {
            CurrentTab = tab;
            BuildRows();
            Refresh();
        }

        /// <summary>Runs <paramref name="command"/> as a click would. False (and <see cref="LastError"/>) if it threw.</summary>
        public bool RunCommand(DebugCommand command) => Invoke(command, () => command.Run?.Invoke());

        /// <summary>Steps <paramref name="command"/> as its &lt; / &gt; buttons would.</summary>
        public bool StepCommand(DebugCommand command, int dir) => Invoke(command, () => command.Step?.Invoke(dir));

        private bool Invoke(DebugCommand command, Action action)
        {
            FindTargets();
            bool ok = true;
            try
            {
                action();
                LastError = null;
            }
            catch (Exception e)
            {
                ok = false;
                LastError = $"{SafeLabel(command)}: {e.Message}";
                Debug.LogException(e);
            }
            Refresh();
            return ok;
        }

        // ---- time ----

        public void TogglePause()
        {
            if (HitStop.BaseTimeScale == 0f) HitStop.SetBaseTimeScale(resumeScale);
            else
            {
                resumeScale = HitStop.BaseTimeScale;
                HitStop.SetBaseTimeScale(0f);
            }
        }

        public void SetTimeScale(float scale)
        {
            HitStop.SetBaseTimeScale(scale);
            if (scale > 0f) resumeScale = scale;
        }

        // ---- building ----

        private void Build()
        {
            canvas = UiFactory.CreateOverlayCanvas(transform, "DebugMenuCanvas", SortingOrder);
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            group = canvas.gameObject.AddComponent<CanvasGroup>();

            // Below the HP readout and the run strip (both top-left/top-centre), above nothing at the bottom-left.
            var panel = UiFactory.CreateImage(canvas.transform, "Panel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -PanelTop), new Vector2(PanelWidth, PanelHeight), PanelColor);
            panel.raycastTarget = true; // clicks on the panel never reach anything behind it

            var title = RunWidgets.Label(panel.transform, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Pad, -8f), new Vector2(300f, 34f), 24, TextAnchor.MiddleLeft, RunWidgets.Gold);
            title.text = "DEBUG  (F1)";
            RunWidgets.TextButton(panel.transform, "Pin", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad - 90f, -8f),
                new Vector2(84f, 32f), "Pin", () => SetPinned(!Pinned), out pinLabel);
            pinLabel.fontSize = 18;
            RunWidgets.TextButton(panel.transform, "Close", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, -8f),
                new Vector2(84f, 32f), "Close", Close, out var closeLabel);
            closeLabel.fontSize = 18;

            var tabs = (DebugTab[])Enum.GetValues(typeof(DebugTab));
            float tabWidth = (PanelWidth - 2f * Pad - (tabs.Length - 1) * RowGap) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                var button = RunWidgets.TextButton(panel.transform, $"Tab{tab}", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(Pad + i * (tabWidth + RowGap), -50f), new Vector2(tabWidth, 34f), tab.ToString(),
                    () => SelectTab(tab), out var label, TabColor);
                label.fontSize = 18;
                tabImages[tab] = (Image)button.targetGraphic;
            }

            content = UiFactory.CreateRect(panel.transform, "Content", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Pad, -ContentTop), new Vector2(PanelWidth - 2f * Pad, PanelHeight - ContentTop - 60f));

            statusText = RunWidgets.Label(panel.transform, "Status", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(Pad, 10f), new Vector2(PanelWidth - 2f * Pad, 44f), 16, TextAnchor.LowerLeft, RunWidgets.Muted, wrap: true);

            infoText = RunWidgets.Label(content, "Info", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                content.sizeDelta, 17, TextAnchor.UpperLeft, Color.white, wrap: true);
            infoText.verticalOverflow = VerticalWrapMode.Overflow;

            BuildRows();
        }

        private void BuildRows()
        {
            foreach (var row in rows) if (row.Root != null) Destroy(row.Root.gameObject);
            rows.Clear();
            foreach (var pair in tabImages) pair.Value.color = pair.Key == CurrentTab ? ActiveTabColor : Color.white;
            if (infoText != null) infoText.gameObject.SetActive(CurrentTab == DebugTab.Info);
            if (CurrentTab == DebugTab.Info) return;

            float width = content.sizeDelta.x;
            int index = 0;
            foreach (var command in Commands)
            {
                if (command.Tab != CurrentTab) continue;
                var root = UiFactory.CreateRect(content, $"Row{index}", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, -index * (RowHeight + RowGap)), new Vector2(width, RowHeight));
                var row = new Row { Root = root, Command = command };
                float stepWidth = command.Step != null ? 40f : 0f;
                float mainX = command.Step != null ? stepWidth + RowGap : 0f;
                float mainWidth = width - 2f * (command.Step != null ? stepWidth + RowGap : 0f);
                var cmd = command;
                row.Main = RunWidgets.TextButton(root, "Main", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(mainX, 0f), new Vector2(mainWidth, RowHeight), "", () => RunCommand(cmd), out row.Label);
                row.Label.fontSize = 18;
                if (command.Step != null)
                {
                    row.Prev = RunWidgets.TextButton(root, "Prev", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                        new Vector2(stepWidth, RowHeight), "<", () => StepCommand(cmd, -1), out var prev);
                    row.Next = RunWidgets.TextButton(root, "Next", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero,
                        new Vector2(stepWidth, RowHeight), ">", () => StepCommand(cmd, 1), out var next);
                    prev.fontSize = next.fontSize = 20;
                }
                rows.Add(row);
                index++;
            }
        }

        private void SetVisible(bool visible)
        {
            if (canvas != null) canvas.gameObject.SetActive(visible);
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // ---- refresh ----

        /// <summary>Redraws labels, greys out unavailable rows, rewrites the Info tab and re-applies the juice switches.</summary>
        public void Refresh()
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            ApplyJuice();
            foreach (var row in rows)
            {
                bool available = SafeAvailable(row.Command);
                row.Label.text = SafeLabel(row.Command);
                row.Main.interactable = available && row.Command.Run != null;
                row.Label.color = available ? Color.white : RunWidgets.Muted * new Color(1f, 1f, 1f, 0.6f);
                if (row.Prev != null) row.Prev.interactable = available;
                if (row.Next != null) row.Next.interactable = available;
            }
            if (CurrentTab == DebugTab.Info && infoText != null) infoText.text = BuildInfo();
            if (statusText != null) statusText.text = LastError != null ? $"Error: {LastError}" : StatusLine();
        }

        private static bool SafeAvailable(DebugCommand c)
        {
            try { return c.Available(); }
            catch (Exception) { return false; } // a target was destroyed between refreshes
        }

        private static string SafeLabel(DebugCommand c)
        {
            try { return c.Label(); }
            catch (Exception) { return "(unavailable)"; }
        }

        private string StatusLine()
        {
            string scene = Director != null ? "run scene" : PlayerInput != null ? "arena (no RunDirector)" : "no player";
            return $"{scene}  ·  time x{HitStop.BaseTimeScale:0.##}  ·  {(Pinned ? "pinned: input free" : "input blocked while open")}";
        }

        private void ApplyJuice()
        {
            if (Juice.AllOn && juiceApplied) return; // nothing switched off: leave the scene alone
            foreach (var punch in FindObjectsByType<CameraPunch>(FindObjectsSortMode.None))
            {
                punch.enabled = Juice.Shake;
                foreach (var b in punch.GetComponents<Behaviour>())
                    if (b.GetType().Name == "CinemachineImpulseListener") b.enabled = Juice.Shake;
            }
            foreach (var fx in FindObjectsByType<HitImpactFx>(FindObjectsSortMode.None)) fx.enabled = Juice.Sparks;
            foreach (var fx in FindObjectsByType<ClashImpactFx>(FindObjectsSortMode.None)) fx.enabled = Juice.Sparks;
            foreach (var view in FindObjectsByType<DamageNumberView>(FindObjectsSortMode.None)) view.enabled = Juice.Numbers;
            AudioListener.volume = Juice.Sound ? 1f : 0f;
            juiceApplied = Juice.AllOn; // all back on: applied once, then left alone
        }

        /// <summary>Marks the juice switches for re-application (call after changing <see cref="Juice"/>).</summary>
        public void JuiceChanged() => juiceApplied = false;

        private string BuildInfo()
        {
            float now = Time.realtimeSinceStartup;
            var sb = new StringBuilder();
            sb.AppendLine($"FPS {fps:0}   time x{HitStop.BaseTimeScale:0.##}   Freeze AI {(TrainingDummyEnemy.DebugFreezeAI ? "ON" : "off")}");

            var d = Director;
            var run = d != null ? d.Run : null;
            if (run != null)
            {
                string room = d.State == RunDirectorState.ChoosingDoor
                    ? $"door ({string.Join(", ", run.CurrentOffer.Select(c => c.ToString()))})"
                    : run.CurrentRoom.ToString();
                sb.AppendLine($"{d.State}   room {run.RoomNumber}/{RunState.BossRoom}: {room}");
                sb.AppendLine($"Seed {d.LastSeed}   currency {run.Currency}   Momentum {run.Momentum.Stacks}");
                sb.AppendLine($"Relics: {(run.Relics.Count > 0 ? string.Join(", ", run.Relics) : "none")}");
                sb.AppendLine($"Room time {Timer.RoomElapsed(now):0.0}s   run time {Timer.RunElapsed(now):0}s");
                if (Timer.History.Count > 0)
                    sb.AppendLine("Last rooms: " + string.Join("  ", Timer.History.Select(h => $"{h.room} {h.seconds:0.0}s")));
                sb.AppendLine($"Enemies {d.Enemies.Count(e => e != null && !e.Status.IsDead)}/{d.Enemies.Count(e => e != null)}" +
                              $"   mix [{string.Join(", ", d.CurrentMix)}]   waves pending {d.WavesPending}" +
                              (d.IsWaveIncoming ? "   WAVE 2 INCOMING" : ""));
            }
            else
            {
                sb.AppendLine(d == null ? "No RunDirector in this scene (Run tab is off)." : "No run yet.");
                sb.AppendLine($"Enemies alive: {LivingEnemies().Count}");
            }

            var p = PlayerStatus;
            if (p != null)
            {
                var fx = p.GetComponent<ElementStatusEffects>();
                string statuses = fx == null ? "" : (fx.IsBurning ? " BURN" : "") + (fx.IsChilled ? " CHILL" : "");
                sb.AppendLine($"Player HP {p.Hp}/{p.MaxHp}   posture {p.Posture:0}/{p.MaxPosture:0}{statuses}" +
                              (p.GodMode ? "   GOD" : "") + (p.IsStunned ? "   STUNNED" : ""));
            }
            var deck = PlayerDeck;
            if (deck != null && deck.Cycle != null)
            {
                var c = deck.Cycle;
                sb.AppendLine($"Hand [{string.Join(", ", c.Hand.Select(h => h == null ? "-" : h.GetEffectiveValue().ToString()))}]" +
                              $"   draw {c.DrawCount}  discard {c.DiscardCount}  burned {c.BurnedCount}  stock {c.StockCount}");
            }
            return sb.ToString();
        }

        // ---- run timer ----

        private void BindTimer()
        {
            if (Director == boundDirector) return;
            UnbindTimer();
            boundDirector = Director;
            if (boundDirector == null) return;
            boundDirector.RoomStarted += HandleRoomStarted;
            boundDirector.StateChanged += HandleStateChanged;
            boundRun = boundDirector.Run;
            Timer.StartRun(Time.realtimeSinceStartup);
        }

        private void UnbindTimer()
        {
            if (ReferenceEquals(boundDirector, null)) return;
            boundDirector.RoomStarted -= HandleRoomStarted;
            boundDirector.StateChanged -= HandleStateChanged;
            boundDirector = null;
        }

        private void HandleRoomStarted(MapCard room)
        {
            CheckNewRun();
            Timer.RoomStarted(Time.realtimeSinceStartup, room.ToString());
        }

        private void HandleStateChanged(RunDirectorState state)
        {
            CheckNewRun();
            if (state != RunDirectorState.Fighting && state != RunDirectorState.Utility) Timer.RoomEnded(Time.realtimeSinceStartup);
        }

        private void CheckNewRun()
        {
            if (boundDirector == null || boundDirector.Run == boundRun) return;
            boundRun = boundDirector.Run;
            Timer.StartRun(Time.realtimeSinceStartup);
        }

        private sealed class Row
        {
            public RectTransform Root;
            public DebugCommand Command;
            public Button Main, Prev, Next;
            public Text Label;
        }
    }
}
#endif
