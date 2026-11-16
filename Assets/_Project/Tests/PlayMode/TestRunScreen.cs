using DeckArpg.UI;
using UnityEngine;

namespace DeckArpg.Tests.PlayMode
{
    /// <summary>A bare <see cref="RunScreen"/> that records what the shell routes to it. Select closes it, like a real choice.</summary>
    internal sealed class TestRunScreen : RunScreen
    {
        public int Selected = -1, Backs, Confirms, Refreshes;
        protected override void Build(RectTransform root) { }
        public override void Refresh() => Refreshes++;
        public override void Select(int index) { Selected = index; Screens.Hide(); }
        public override void Back() => Backs++;
        public override void Confirm() => Confirms++;

        /// <summary>Adds one to <paramref name="screens"/>' GameObject and registers it.</summary>
        public static TestRunScreen AddTo(RunScreens screens)
        {
            var screen = screens.gameObject.AddComponent<TestRunScreen>();
            screens.Attach(screen);
            return screen;
        }
    }
}
