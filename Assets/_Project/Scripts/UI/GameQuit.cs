using System;
using UnityEngine;

namespace DeckArpg.UI
{
    /// <summary>
    /// The one place the UI quits the game from (title screen, run summary). In the Editor, `Application.Quit` does
    /// nothing, so it logs instead. Tests set <see cref="Override"/> so a Quit button can be pressed without quitting.
    /// </summary>
    public static class GameQuit
    {
        /// <summary>When set, called instead of quitting (tests).</summary>
        public static Action Override;

        public static void Quit()
        {
            if (Override != null) { Override(); return; }
#if UNITY_EDITOR
            Debug.Log("Quit requested (ignored in the Editor).");
#else
            Application.Quit();
#endif
        }
    }
}
