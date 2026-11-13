namespace DeckArpg.Core
{
    /// <summary>
    /// Single home for the numeric card range. Zero-breakers are a flag, never a value,
    /// so every numeric card stays within [Min, Max] no matter how many mods stack.
    /// </summary>
    public static class CardValue
    {
        public const int Min = 1;
        public const int Max = 9;

        public static int Clamp(int value)
        {
            if (value < Min) return Min;
            if (value > Max) return Max;
            return value;
        }
    }
}
