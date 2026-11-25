using DeckArpg.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Title screen for the early build: Start (`1`/Enter), Controls (`2`, toggles a controls panel) and Quit
    /// (`3`/Esc). The run is already dealt behind it, and <see cref="RunScreens"/> blocks player input while it shows.
    /// It opens once per app session when the run scene loads (<see cref="OpenOnBoot"/>), except in batchmode, so
    /// scene tests still start straight in the run. A full front-end (intro, pause, options) is M5.7.
    /// </summary>
    public sealed class TitleView : RunScreen
    {
        public const string GameTitle = "DECK-BUILDER ARPG";

        /// <summary>The controls table from the README, shown by the Controls button.</summary>
        public const string ControlsText =
            "WASD / mouse   Move / aim\n" +
            "1, 2, 3   Play that hand card\n" +
            "Q   Parry with the 0 card (play it after the enemy swings)\n" +
            "Left mouse   Value-1 poke (always available)\n" +
            "Space   Dash (i-frames)\n" +
            "Shift + 1/2/3/Q   Stock a card into the Sleight (the 3rd card fires it)\n" +
            "R, then R in the sweet spot   Reload (sweet spot = instant + speed boost)\n" +
            "Shift + R   Overburn reload: pay HP to get burned cards back";

        private static bool shownThisSession;

        private Text controls, intro;
        private bool openPending;

        public bool ControlsShown => controls != null && controls.gameObject.activeSelf;

        protected override void Build(RectTransform root)
        {
            var centre = new Vector2(0.5f, 0.5f);
            var title = RunWidgets.Label(root, "Title", centre, centre, new Vector2(0f, 300f), new Vector2(1400f, 120f), 96,
                TextAnchor.MiddleCenter, RunWidgets.Gold);
            title.text = GameTitle;
            intro = RunWidgets.Label(root, "Intro", centre, centre, new Vector2(0f, 200f), new Vector2(1300f, 80f), 28,
                TextAnchor.MiddleCenter, RunWidgets.Muted, wrap: true);
            intro.text = "Every attack is a card. Clear six rooms and beat the boss.\n" +
                         "Early build (v" + Application.version + "): greybox art.";
            controls = RunWidgets.Label(root, "Controls", centre, centre, new Vector2(0f, -20f), new Vector2(1300f, 320f), 26,
                TextAnchor.MiddleCenter, Color.white, wrap: true);
            controls.text = ControlsText;
            controls.gameObject.SetActive(false);

            RunWidgets.TextButton(root, "Start", centre, centre, new Vector2(-380f, -260f), new Vector2(320f, 70f),
                "1  Start", () => Select(0), out _);
            RunWidgets.TextButton(root, "ControlsButton", centre, centre, new Vector2(0f, -260f), new Vector2(320f, 70f),
                "2  Controls", () => Select(1), out _);
            RunWidgets.TextButton(root, "Quit", centre, centre, new Vector2(380f, -260f), new Vector2(320f, 70f),
                "3  Quit", () => Select(2), out _);
            RunWidgets.Hint(root).text = "Enter: start   ·   Esc: quit";
        }

        // Editor Play with domain reload off keeps statics between plays; reset so each Play shows the title.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => shownThisSession = false;

        /// <summary>Adds the title to the run scene's screens on load (player builds and Editor Play, not batchmode).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OpenOnBoot()
        {
            if (shownThisSession || Application.isBatchMode) return;
            var screens = FindAnyObjectByType<RunScreens>();
            if (screens == null) return;
            shownThisSession = true;
            var view = screens.GetComponent<TitleView>();
            if (view == null) view = screens.gameObject.AddComponent<TitleView>();
            screens.Attach(view);
            view.openPending = true; // shown in Update, after every Start ran (the director deals the run in Start)
        }

        private void Update()
        {
            if (!openPending) return;
            openPending = false;
            if (Director != null && ValidIn(Director.State)) Screens.Show(this);
        }

        public override bool ValidIn(RunDirectorState state) => state == RunDirectorState.ChoosingDoor;

        public override void Refresh() { }

        public override void Select(int index)
        {
            switch (index)
            {
                case 0: Confirm(); break;
                case 1:
                    controls.gameObject.SetActive(!controls.gameObject.activeSelf);
                    intro.gameObject.SetActive(!controls.gameObject.activeSelf);
                    break;
                case 2: GameQuit.Quit(); break;
            }
        }

        /// <summary>Start: close the title; the first door is already on offer.</summary>
        public override void Confirm()
        {
            if (IsOpen) Screens.Hide();
        }

        public override void Back() => GameQuit.Quit();
    }
}
