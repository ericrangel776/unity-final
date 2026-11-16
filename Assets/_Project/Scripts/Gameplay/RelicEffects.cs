using System.Collections.Generic;
using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Applies the run's relics to the player in fights. <see cref="RunDirector"/> sets the owned relics and calls
    /// <see cref="BeginRoom"/> after each room's deck is rebuilt. Gilded Quill is pure reward math in <see cref="RunState"/>.
    /// See docs/specs/m3-relics.md.
    /// </summary>
    [RequireComponent(typeof(PlayerCombatInput), typeof(DeckRuntimeManager), typeof(CombatActionController))]
    public sealed class RelicEffects : MonoBehaviour
    {
        private readonly HashSet<RelicId> owned = new HashSet<RelicId>();
        private PlayerCombatInput input;
        private DeckRuntimeManager deck;
        private CombatActionController combat;
        private CombatantStatus status;

        /// <summary>Ashen Sigil stacks this room (Sleight burns).</summary>
        public int AshenStacks { get; private set; }

        private void Awake()
        {
            input = GetComponent<PlayerCombatInput>();
            deck = GetComponent<DeckRuntimeManager>();
            combat = GetComponent<CombatActionController>();
            status = GetComponent<CombatantStatus>();
        }

        private void OnEnable()
        {
            deck.Cycle.CardBurned += HandleCardBurned;
            deck.Reload.Completed += HandleReloadCompleted;
            combat.ClashWon += HandleClashWon;
        }

        private void OnDisable()
        {
            deck.Cycle.CardBurned -= HandleCardBurned;
            deck.Reload.Completed -= HandleReloadCompleted;
            combat.ClashWon -= HandleClashWon;
        }

        public bool Has(RelicId relic) => owned.Contains(relic);

        /// <summary>Replaces the owned set. Call <see cref="BeginRoom"/> afterwards to apply the per-room parts.</summary>
        public void SetRelics(IEnumerable<RelicId> relics)
        {
            owned.Clear();
            if (relics != null) foreach (var r in relics) owned.Add(r);
        }

        /// <summary>Resets per-room relic state: Ashen stacks, Iron Will charge, and the Cinder Lantern free Overburn.</summary>
        public void BeginRoom()
        {
            AshenStacks = 0;
            input.ActionSpeedMultiplier = 1f;
            input.NextSleightZeroProof = false;
            deck.Reload.FreeOverburnsPerRoom = Has(RelicId.CinderLantern) ? RelicRules.CinderLanternFreeOverburns : 0;
            deck.Reload.ResetOverburnCost();
        }

        private void HandleCardBurned(RuntimeCardInstance card, BurnCause cause)
        {
            if (cause != BurnCause.Sleight || !Has(RelicId.AshenSigil)) return;
            AshenStacks++;
            input.ActionSpeedMultiplier = RelicRules.ActionSpeedMultiplier(AshenStacks);
            if (!status.IsDead) status.SetHp(status.Hp + RelicRules.AshenSigilHealPerBurn);
        }

        private void HandleReloadCompleted(ReloadGrade grade)
        {
            if (Has(RelicId.IronWillIdol)) input.NextSleightZeroProof = true;
            if (grade == ReloadGrade.Perfect && Has(RelicId.TemperedCoil)) deck.Cycle.RestoreRandomBurnedToDrawQueue();
        }

        private void HandleClashWon(CombatantStatus loser, bool brokeByZero)
        {
            if (!brokeByZero || !Has(RelicId.ShatteredHourglass)) return;
            deck.Cycle.ReturnRandomSpentToDrawQueue();
            if (loser != null) loser.AddPostureDamage(RelicRules.HourglassPostureDamage);
        }
    }
}
