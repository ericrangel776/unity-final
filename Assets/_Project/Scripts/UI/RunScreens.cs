using System.Collections.Generic;
using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Owns the M3 run screens (docs/specs/m3-ui.md): shows at most one <see cref="RunScreen"/>, routes the menu keys
    /// to it, and is the <b>only writer</b> of <see cref="PlayerCombatInput.InputBlocked"/>.
    /// The block turns on as soon as a screen shows, but turns off only in <c>LateUpdate</c>: the number keys are
    /// also the card slots, so a `1` that closes a screen must not be read as a card play later in the same frame.
    /// </summary>
    public sealed class RunScreens : MonoBehaviour
    {
        public const int SortingOrder = 10;

        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private RunDirector director;

        private readonly List<RunScreen> attached = new List<RunScreen>();
        private readonly Dictionary<RunScreen, RectTransform> roots = new Dictionary<RunScreen, RectTransform>();
        private Canvas canvas;
        private RectTransform dim;

        public RunDirector Director => director;
        public RunScreen Current { get; private set; }
        public Canvas Canvas => canvas;
        public bool InputBlocked => director != null && director.Player != null && director.Player.InputBlocked;

        private void Awake()
        {
            if (director == null) director = FindAnyObjectByType<RunDirector>();
            EnsureEventSystem();
            canvas = UiFactory.CreateOverlayCanvas(transform, "RunScreensCanvas", SortingOrder);
            // Dims the arena behind an open screen and catches clicks that miss a button.
            var dimImage = UiFactory.CreateImage(canvas.transform, "Dim", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.6f));
            dimImage.raycastTarget = true;
            dim = dimImage.rectTransform;
            Stretch(dim);
            dim.gameObject.SetActive(false);

            foreach (var screen in GetComponents<RunScreen>()) Attach(screen);
            if (director != null) director.StateChanged += HandleStateChanged;
        }

        private void OnDestroy()
        {
            if (director != null) director.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(RunDirectorState state)
        {
            if (Current != null && !Current.ValidIn(state)) Hide();
        }

        /// <summary>Registers a screen added after Awake (tests). Screens on this GameObject are attached automatically.</summary>
        public void Attach(RunScreen screen)
        {
            if (screen == null || attached.Contains(screen)) return;
            var root = UiFactory.CreateRect(canvas.transform, screen.GetType().Name, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(root);
            screen.Attach(this, root);
            root.gameObject.SetActive(false);
            attached.Add(screen);
            roots[screen] = root;
        }

        /// <summary>Shows <paramref name="screen"/> (closing any other) and blocks player input right away.</summary>
        public void Show(RunScreen screen)
        {
            if (screen == null) return;
            Attach(screen);
            if (Current != null && Current != screen) roots[Current].gameObject.SetActive(false);
            Current = screen;
            dim.gameObject.SetActive(true);
            roots[screen].gameObject.SetActive(true);
            SetBlocked(true);
            screen.Refresh();
        }

        /// <summary>Closes the open screen. Player input is released at the end of this frame (see class summary).</summary>
        public void Hide()
        {
            if (Current == null) return;
            roots[Current].gameObject.SetActive(false);
            dim.gameObject.SetActive(false);
            Current = null;
        }

        /// <summary>Routes a number key: <paramref name="index"/> 0 = key `1`.</summary>
        public void HandleKey(int index) { if (Current != null) Current.Select(index); }
        public void HandleBack() { if (Current != null) Current.Back(); }
        public void HandleConfirm() { if (Current != null) Current.Confirm(); }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Current == null) return;
            // `1`-`9` (top row or numpad). Key.Digit1..Digit9 and Key.Numpad1..Numpad9 are contiguous in the enum.
            for (int i = 0; i < 9; i++)
            {
                if (!keyboard[Key.Digit1 + i].wasPressedThisFrame && !keyboard[Key.Numpad1 + i].wasPressedThisFrame) continue;
                HandleKey(i);
                return;
            }
            if (keyboard.escapeKey.wasPressedThisFrame) HandleBack();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) HandleConfirm();
        }

        private void LateUpdate()
        {
            // Release only after every Update this frame has run (see class summary).
            if (Current == null && InputBlocked) SetBlocked(false);
        }

        private void OnDisable()
        {
            if (director != null && director.Player != null) director.Player.InputBlocked = false;
        }

        private void SetBlocked(bool blocked)
        {
            if (director != null && director.Player != null) director.Player.InputBlocked = blocked;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }
    }
}
