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

        /// <summary>One deck card for a list: "Ember Strike 6", with "+N" for run upgrades and "P" for Premium.</summary>
        public static string DeckCardLabel(RuntimeCardInstance card)
        {
            string value = card.IsZeroBreaker ? "0" : card.GetEffectiveValue().ToString();
            string tags = (card.PermanentRunUpgradeMod > 0 ? $" +{card.PermanentRunUpgradeMod}" : "") +
                          (card.IsPremium ? " P" : "");
            return $"{card.Data.DisplayName}  {value}{tags}";
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
    }
}
