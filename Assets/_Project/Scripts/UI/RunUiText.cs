using System.Collections.Generic;
using System.Linq;
using System.Text;
using DeckArpg.Core;

namespace DeckArpg.UI
{
    /// <summary>Pure text helpers for the run screens and HUD strip (no Unity objects, so EditMode-testable).</summary>
    public static class RunUiText
    {
        public const string PipOn = "●";
        public const string PipOff = "○";

        /// <summary>"Room 3/6", or "Room 6/6 · Boss" on the boss room.</summary>
        public static string Room(int roomNumber) =>
            $"Room {roomNumber}/{RunState.BossRoom}" + (roomNumber == RunState.BossRoom ? " · Boss" : "");

        /// <summary><paramref name="filled"/> of <paramref name="total"/> pips, e.g. "●●○". Clamped to [0, total].</summary>
        public static string Pips(int filled, int total)
        {
            if (total <= 0) return "";
            if (filled < 0) filled = 0;
            if (filled > total) filled = total;
            var sb = new StringBuilder(total);
            for (int i = 0; i < total; i++) sb.Append(i < filled ? PipOn : PipOff);
            return sb.ToString();
        }

        public static string Momentum(int stacks) => "Momentum " + Pips(stacks, Core.Momentum.MaxStacks);

        // ---- Map Cards (door choice) ----

        public static string MapCardName(MapCardKind kind)
        {
            switch (kind)
            {
                case MapCardKind.Vanguard: return "Vanguard";
                case MapCardKind.Hexfire: return "Hexfire";
                case MapCardKind.KindledGround: return "Kindled Ground";
                case MapCardKind.BountyWind: return "Bounty Wind";
                case MapCardKind.Shop: return "Wayfarer's Stall";
                case MapCardKind.Forge: return "Ember Forge";
                case MapCardKind.Rest: return "Still Waters";
                case MapCardKind.EliteTrial: return "Elite Trial";
                case MapCardKind.Boss: return "The Warden";
                default: return kind.ToString();
            }
        }

        /// <summary>What the corner number means: Intensity on fights, Quality on Blue rooms.</summary>
        public static string NumberLabel(in MapCard card) => card.IsCombat ? "INTENSITY" : "QUALITY";

        public static string TierName(RewardTier tier) => tier.ToString();

        /// <summary>
        /// The reward line: "Fierce ●●●○" (tier name + 1-4 pips) for fights, "Final fight" for the boss, empty for Blue
        /// rooms (they have no combat reward).
        /// </summary>
        public static string TierLine(in MapCard card)
        {
            if (!card.IsCombat) return "";
            if (card.Kind == MapCardKind.Boss) return "Final fight";
            var tier = RewardRules.Tier(card.Number);
            return $"{TierName(tier)} {Pips((int)tier + 1, TierCount)}";
        }

        private const int TierCount = 4;

        /// <summary>A short description of what the room holds, with its scaled numbers.</summary>
        public static string MapCardBlurb(in MapCard card)
        {
            switch (card.Kind)
            {
                case MapCardKind.Vanguard:
                    return $"{Enemies(card)}, Strike-heavy.\nDraft one rarity higher.";
                case MapCardKind.Hexfire:
                    return $"{Enemies(card)} with stronger elemental cards.\nDraft one rarity higher.";
                case MapCardKind.KindledGround:
                    return $"{Enemies(card)}.\nYour {card.BuffElement} cards +{RoomIntensity.PlayerElementBuff(card)}. Enemies faster.";
                case MapCardKind.BountyWind:
                    return $"{Enemies(card)}.\nCurrency +{RoomIntensity.BountyCurrencyPercent(card) - 100}%. Enemies tankier.";
                case MapCardKind.Shop:
                    return $"Buy from {ShopRules.StockCount(card.Number)} cards,\nor pay to purge one.";
                case MapCardKind.Forge:
                    return $"Upgrade a card +{ForgeRules.UpgradeAmount(card.Number)}\n" +
                           (ForgeRules.GrantsBoth(card.Number) ? "and make another Premium." : "or make one Premium.");
                case MapCardKind.Rest:
                    return $"Heal {RestRules.HealPercent(card.Number)}% HP,\nor Meditate: +1 Momentum.";
                case MapCardKind.EliteTrial:
                    return $"One elite: {card.Trial}.\n{TrialHint(card.Trial)}\nDrops a relic choice.";
                case MapCardKind.Boss:
                    return "The floor's guardian.\nBeat it to win the run.";
                default:
                    return "";
            }
        }

        public static string TrialHint(EliteTrial trial)
        {
            switch (trial)
            {
                case EliteTrial.Veiled: return "Its cards stay hidden until the swing.";
                case EliteTrial.Counterpuncher: return "Answers your plays with 0s.";
                case EliteTrial.Hexer: return "Your broken cards burn.";
                default: return "";
            }
        }

        /// <summary>Regular fights always have 2+ enemies (Elite/Boss blurbs don't use this).</summary>
        private static string Enemies(in MapCard card) => $"{RoomIntensity.EnemyCount(card)} enemies";

        // ---- run end ----

        /// <summary>Rooms finished: every entered room, minus the one the player died in (still in progress).</summary>
        public static int RoomsCleared(RunState run) =>
            run == null ? 0 : run.History.Count - (run.Phase == RunPhase.InRoom ? 1 : 0);

        /// <summary>"5.3", or "-" when no fight was entered (AverageIntensity is 0 then).</summary>
        public static string AverageIntensity(RunState run) =>
            run == null || run.AverageIntensity <= 0f ? "-" : run.AverageIntensity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The rooms taken, in order: "Vanguard 2 > Ember Forge 4 > ...".</summary>
        public static string Path(RunState run)
        {
            if (run == null || run.History.Count == 0) return "";
            return string.Join("  >  ", run.History.Select(r => $"{MapCardName(r.Kind)} {r.Number}"));
        }

        // ---- cards (draft, shop, deck lists) ----

        /// <summary>The value a card shows: "0" for a 0-card, else base + <paramref name="upgrade"/> clamped to [1, 9].</summary>
        public static string CardValue(CardData card, int upgrade = 0) =>
            card.IsZeroBreaker ? "0" : Core.CardValue.Clamp(card.BaseValue + upgrade).ToString();

        /// <summary>"Strike · Fire", plus "Premium (+2, burns on play)" and "Pre-upgraded +N" lines when they apply.</summary>
        public static string CardDetails(CardData card, int upgrade = 0)
        {
            var sb = new StringBuilder(card.IsZeroBreaker ? "0-card · breaks anything played first" : $"{card.Shape} · {card.Element}");
            if (card.IsPremium) sb.Append($"\nPremium (+{RuntimeCardInstance.PremiumBonus}, burns on play)");
            if (upgrade > 0 && !card.IsZeroBreaker) sb.Append($"\nPre-upgraded +{upgrade}");
            return sb.ToString();
        }

        /// <summary>
        /// One deck card for a list: its name (which already carries the base value, e.g. "Strike 2"), plus "+N" for
        /// run upgrades and "P" for Premium.
        /// </summary>
        public static string DeckCardLabel(RuntimeCardInstance card)
        {
            string tags = (card.PermanentRunUpgradeMod > 0 ? $"  +{card.PermanentRunUpgradeMod}" : "") +
                          (card.IsPremium ? "  P" : "");
            return card.Data.DisplayName + tags;
        }

        // ---- Blue rooms (docs/specs/m3-blue-polish.md) ----

        /// <summary>The card's value outside a fight: base + run upgrades + Premium, clamped to [1, 9]. 0-cards are 0.</summary>
        public static int DeckValue(RuntimeCardInstance card, int extraUpgrade = 0, bool asPremium = false)
        {
            if (card.IsZeroBreaker) return 0;
            int premium = card.IsPremium || asPremium ? RuntimeCardInstance.PremiumBonus : 0;
            return Core.CardValue.Clamp(card.Data.BaseValue + card.PermanentRunUpgradeMod + extraUpgrade + premium);
        }

        /// <summary>A deck-list cell: "6  Strike 4  +2  ·  Strike · Physical", plus "  (reason)" when it can't be chosen.</summary>
        public static string DeckCell(RuntimeCardInstance card, string blockedReason = null)
        {
            string kind = card.IsZeroBreaker ? "0-card" : $"{card.Data.Shape} · {card.Data.Element}";
            string text = $"{DeckValue(card)}   {DeckCardLabel(card)}   ·   {kind}";
            return string.IsNullOrEmpty(blockedReason) ? text : $"{text}   ({blockedReason})";
        }

        /// <summary>"Strike 4: 4 -> 6".</summary>
        public static string UpgradePreview(RuntimeCardInstance card, int amount) =>
            $"{card.Data.DisplayName}: {DeckValue(card)} -> {DeckValue(card, amount)}";

        /// <summary>"Frost 4: 4 -> 6, burns when played".</summary>
        public static string PremiumPreview(RuntimeCardInstance card) =>
            $"{card.Data.DisplayName}: {DeckValue(card)} -> {DeckValue(card, asPremium: true)}, burns when played";

        /// <summary>"Remove Ember 3: -40 currency, deck 10 -> 9".</summary>
        public static string PurgePreview(RuntimeCardInstance card, int cost, int deckCount) =>
            $"Remove {card.Data.DisplayName}: -{cost} currency, deck {deckCount} -> {deckCount - 1}";

        /// <summary>"Need 30 more currency" (empty when affordable).</summary>
        public static string Shortfall(int price, int currency) =>
            currency >= price ? "" : $"Need {price - currency} more currency";

        /// <summary>Why a card can't be upgraded (null if it can).</summary>
        public static string UpgradeBlockedReason(RuntimeCardInstance card)
        {
            if (card.IsZeroBreaker) return "0-cards can't change";
            return ForgeRules.CanUpgrade(card) ? null : $"already at {Core.CardValue.Max}";
        }

        /// <summary>Why a card can't be made Premium (null if it can).</summary>
        public static string PremiumBlockedReason(RuntimeCardInstance card)
        {
            if (card.IsZeroBreaker) return "0-cards can't change";
            return card.IsPremium ? "already Premium" : null;
        }

        // ---- relics ----

        public static string RelicName(RelicId relic)
        {
            switch (relic)
            {
                case RelicId.ShatteredHourglass: return "Shattered Hourglass";
                case RelicId.AshenSigil: return "Ashen Sigil";
                case RelicId.IronWillIdol: return "Iron Will Idol";
                case RelicId.TemperedCoil: return "Tempered Coil";
                case RelicId.CinderLantern: return "Cinder Lantern";
                case RelicId.GildedQuill: return "Gilded Quill";
                default: return relic.ToString();
            }
        }

        /// <summary>The effect in one or two lines (numbers from <see cref="RelicRules"/> where they exist).</summary>
        public static string RelicDescription(RelicId relic)
        {
            switch (relic)
            {
                case RelicId.ShatteredHourglass: return $"Your 0-breaks return a spent card\nand deal {RelicRules.HourglassPostureDamage:0} posture damage.";
                case RelicId.AshenSigil: return $"Each Sleight burn: +10% swing speed\nfor the room, heal {RelicRules.AshenSigilHealPerBurn} HP.";
                case RelicId.IronWillIdol: return "The first Sleight after each reload\ncan't be broken by a 0.";
                case RelicId.TemperedCoil: return "A Perfect reload restores\n1 burned card.";
                case RelicId.CinderLantern: return "The first Overburn each room\ncosts no HP.";
                case RelicId.GildedQuill: return $"Drafts offer +{RelicRules.GildedQuillExtraOptions} card.\nSkipping pays +{RelicRules.GildedQuillExtraSkipBonus} more.";
                default: return "";
            }
        }

        public static string RelicShortName(RelicId relic)
        {
            switch (relic)
            {
                case RelicId.ShatteredHourglass: return "Hourglass";
                case RelicId.AshenSigil: return "Ashen";
                case RelicId.IronWillIdol: return "Iron Will";
                case RelicId.TemperedCoil: return "Coil";
                case RelicId.CinderLantern: return "Lantern";
                case RelicId.GildedQuill: return "Quill";
                default: return relic.ToString();
            }
        }

        /// <summary>
        /// Relic short names in a fixed (enum) order, joined with " · ". Ashen shows its room stacks ("Ashen x2")
        /// when above 0. "No relics" when the set is empty or null.
        /// </summary>
        public static string Relics(IEnumerable<RelicId> relics, int ashenStacks)
        {
            var owned = relics?.OrderBy(r => (int)r).ToList();
            if (owned == null || owned.Count == 0) return "No relics";
            return string.Join(" · ", owned.Select(r =>
                r == RelicId.AshenSigil && ashenStacks > 0 ? $"{RelicShortName(r)} x{ashenStacks}" : RelicShortName(r)));
        }

        // ---- Boss HUD (M4) ----

        /// <summary>
        /// The boss's 15-pip deck counter, in display order: cards still to play (hand + queue), stocked toward a Sleight,
        /// spent this cycle, 0-deleted this cycle, burned for the fight. Negative counts are treated as 0.
        /// </summary>
        public static List<BossPip> BossPips(int left, int stocked, int spent, int deleted, int burned)
        {
            var pips = new List<BossPip>();
            void Add(BossPip pip, int count) { for (int i = 0; i < count; i++) pips.Add(pip); }
            Add(BossPip.Left, left);
            Add(BossPip.Stocked, stocked);
            Add(BossPip.Spent, spent);
            Add(BossPip.Deleted, deleted);
            Add(BossPip.Burned, burned);
            return pips;
        }

        /// <summary>
        /// "9 left · 3 spent · 2 deleted (2 more = EXHAUSTED)". Parts that are 0 are left out, except "left". The Exhausted
        /// hint is only shown mid-cycle (<paramref name="showHint"/>): once the cycle has ended it no longer applies.
        /// </summary>
        public static string BossCounter(int left, int stocked, int spent, int deleted, int burned, int exhaustThreshold,
            bool showHint = true)
        {
            var parts = new List<string> { $"{left} left" };
            if (stocked > 0) parts.Add($"{stocked} stocked");
            if (spent > 0) parts.Add($"{spent} spent");
            if (deleted > 0 && !showHint) parts.Add($"{deleted} deleted");
            else if (deleted > 0)
            {
                int more = exhaustThreshold - deleted;
                parts.Add(more > 0 ? $"{deleted} deleted ({more} more = EXHAUSTED)" : $"{deleted} deleted: EXHAUSTED at reload");
            }
            if (burned > 0) parts.Add($"{burned} burned");
            return string.Join(" · ", parts);
        }

        /// <summary>The boss state label: "SLEIGHT 15", "RELOADING 1.2s", "EXHAUSTED 3.4s", or "GUARDED" while it just fights
        /// (it takes reduced damage outside its punish windows).</summary>
        public static string BossState(Gameplay.BossState state, float remaining, int sleightValue)
        {
            if (remaining < 0f) remaining = 0f;
            switch (state)
            {
                case Gameplay.BossState.SleightWindup: return $"SLEIGHT {sleightValue}";
                case Gameplay.BossState.ReloadWindow: return $"RELOADING {remaining:0.0}s";
                case Gameplay.BossState.Exhausted: return $"EXHAUSTED {remaining:0.0}s";
                default: return "GUARDED";
            }
        }
    }

    /// <summary>One pip of the boss deck counter.</summary>
    public enum BossPip { Left, Stocked, Spent, Deleted, Burned }
}
