namespace DeckArpg.Core
{
    /// <summary>
    /// Counterpuncher trial: how many 0-cards the elite spends to answer an attack (0 = let it through).
    /// Deck cards and pokes cost one; a Sleight is only answered with two, so baiting out a 0 opens a window.
    /// See docs/specs/m3-elite-trials.md.
    /// </summary>
    public static class CounterRules
    {
        public const int SingleCounterCost = 1;
        public const int SleightCounterCost = 2;

        public static int ZerosToSpend(bool isSleight, int zerosReady)
        {
            int cost = isSleight ? SleightCounterCost : SingleCounterCost;
            return zerosReady >= cost ? cost : 0;
        }
    }
}
