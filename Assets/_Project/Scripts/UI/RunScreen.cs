using DeckArpg.Gameplay;
using UnityEngine;

namespace DeckArpg.UI
{
    /// <summary>
    /// One full-screen panel in the run flow (door choice, reward, Shop/Forge/Rest, run end).
    /// <see cref="RunScreens"/> shows at most one at a time and routes keys to it: `1`-`4` -> <see cref="Select"/>,
    /// `Esc` -> <see cref="Back"/>, `Enter` -> <see cref="Confirm"/>. Mouse buttons call the same methods.
    /// </summary>
    public abstract class RunScreen : MonoBehaviour
    {
        protected RunScreens Screens { get; private set; }
        protected RunDirector Director => Screens != null ? Screens.Director : null;

        public bool IsOpen => Screens != null && Screens.Current == this;

        /// <summary>Called once by <see cref="RunScreens"/> before the screen is first shown. Build widgets under <paramref name="root"/>.</summary>
        internal void Attach(RunScreens screens, RectTransform root)
        {
            Screens = screens;
            Build(root);
        }

        protected abstract void Build(RectTransform root);

        /// <summary>Redraw from the run state. Called on show and after every action.</summary>
        public abstract void Refresh();

        /// <summary>
        /// Whether this screen still applies in <paramref name="state"/>. <see cref="RunScreens"/> hides the open screen
        /// when the run moves to a state where it doesn't (e.g. the debug menu or a test finishes a reward directly).
        /// </summary>
        public virtual bool ValidIn(RunDirectorState state) => true;

        /// <summary>Option <paramref name="index"/> (0-based) was chosen.</summary>
        public virtual void Select(int index) { }

        /// <summary>`Esc`: back out, skip or leave, depending on the screen.</summary>
        public virtual void Back() { }

        /// <summary>`Enter`: the screen's main button (Continue / Restart).</summary>
        public virtual void Confirm() { }
    }
}
