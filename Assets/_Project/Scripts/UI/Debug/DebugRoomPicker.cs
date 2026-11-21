#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using DeckArpg.Core;

namespace DeckArpg.UI
{
    /// <summary>
    /// The debug menu's room picker: kind, Intensity/Quality, Elite trial and KindledGround element, always forming a
    /// valid <see cref="MapCard"/>. See docs/specs/debug-menu.md.
    /// </summary>
    public sealed class DebugRoomPicker
    {
        private static readonly EliteTrial[] Trials = { EliteTrial.Veiled, EliteTrial.Counterpuncher, EliteTrial.Hexer };
        private static readonly MapCardKind[] Kinds = (MapCardKind[])Enum.GetValues(typeof(MapCardKind));
        private static readonly Element[] Elements = (Element[])Enum.GetValues(typeof(Element));

        private int kind;
        private int trial;
        private int element;

        public MapCardKind Kind => Kinds[kind];
        public int Number { get; private set; } = 5;
        public EliteTrial Trial => Kind == MapCardKind.EliteTrial ? Trials[trial] : EliteTrial.None;
        public Element BuffElement => Elements[element];

        public MapCard Card => new MapCard(Kind, Number, Trial, BuffElement);

        public void StepKind(int dir) => kind = Wrap(kind + dir, Kinds.Length);
        public void StepNumber(int dir) => Number = Math.Max(CardValue.Min, Math.Min(CardValue.Max, Number + dir));
        public void StepTrial(int dir) => trial = Wrap(trial + dir, Trials.Length);
        public void StepElement(int dir) => element = Wrap(element + dir, Elements.Length);

        public string KindLabel => $"Room: {Kind}";
        public string NumberLabel => $"{(MapCard.IsCombatKind(Kind) ? "Intensity" : "Quality")}: {Number}";
        public string ExtraLabel =>
            Kind == MapCardKind.EliteTrial ? $"Trial: {Trial}"
            : Kind == MapCardKind.KindledGround ? $"Buff element: {BuffElement}"
            : "(no trial/element)";

        /// <summary>Steps whichever extra applies to the current kind (trial or element).</summary>
        public void StepExtra(int dir)
        {
            if (Kind == MapCardKind.EliteTrial) StepTrial(dir);
            else if (Kind == MapCardKind.KindledGround) StepElement(dir);
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;
    }
}
#endif
