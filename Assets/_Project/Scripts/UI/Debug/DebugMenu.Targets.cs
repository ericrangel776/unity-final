#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using DeckArpg.Core;
using DeckArpg.Gameplay;
using UnityEngine;

namespace DeckArpg.UI
{
    // Targets: the director, player and enemies the commands act on, found lazily (Unity-null after a scene unload).
    public sealed partial class DebugMenu
    {
        public RunDirector Director { get; private set; }
        public PlayerCombatInput PlayerInput { get; private set; }
        public CombatantStatus PlayerStatus => PlayerPart(ref playerStatus);
        public DeckRuntimeManager PlayerDeck => PlayerPart(ref playerDeck);

        // Components of the current PlayerInput, cached because rows read them many times per refresh.
        private CombatantStatus playerStatus;
        private DeckRuntimeManager playerDeck;
        private PlayerCombatInput partsOf;

        private T PlayerPart<T>(ref T cached) where T : Component
        {
            if (PlayerInput == null) return null;
            if (partsOf != PlayerInput)
            {
                partsOf = PlayerInput;
                playerStatus = null;
                playerDeck = null;
            }
            if (cached == null) cached = PlayerInput.GetComponent<T>();
            return cached;
        }

        /// <summary>Every distinct card in the run content (starter, pool, enemy decks), for the Add card picker.</summary>
        public IReadOnlyList<CardData> AllCards
        {
            get
            {
                var content = Director != null ? Director.Content : null;
                if (content == null) return Array.Empty<CardData>();
                if (allCards != null && allCardsFrom == content) return allCards;
                allCardsFrom = content;
                allCards = content.StarterDeck.Concat(content.DraftPool).Concat(content.MobDeck).Concat(content.HexfireDeck)
                    .Concat(content.EliteDeck).Concat(content.BossDeck).Concat(content.CasterDeck).Concat(content.RusherDeck)
                    .Append(content.EnemyZero).Where(c => c != null).Distinct().ToList();
                return allCards;
            }
        }

        private List<TrainingDummyEnemy> refreshEnemies; // non-null only while Refresh redraws the rows

        public List<TrainingDummyEnemy> LivingEnemies() =>
            refreshEnemies != null ? new List<TrainingDummyEnemy>(refreshEnemies)
                : FindObjectsByType<TrainingDummyEnemy>(FindObjectsSortMode.None)
                    .Where(e => e != null && e.Status != null && !e.Status.IsDead).ToList();

        public TrainingDummyEnemy NearestEnemy()
        {
            var from = PlayerStatus != null ? PlayerStatus.transform.position : Vector3.zero;
            return LivingEnemies().OrderBy(e => (e.transform.position - from).sqrMagnitude).FirstOrDefault();
        }

        /// <summary>A ground point <paramref name="distance"/> in front of the player, kept inside the arena.</summary>
        public Vector3 SpawnPointAhead(float distance)
        {
            var t = PlayerInput.transform;
            var forward = t.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var p = t.position + forward.normalized * distance;
            return new Vector3(Mathf.Clamp(p.x, -10f, 10f), 0f, Mathf.Clamp(p.z, -10f, 10f));
        }

        /// <summary>Finds the director and player of the current scene if the cached ones are gone.</summary>
        public void FindTargets()
        {
            if (Director == null) Director = FindAnyObjectByType<RunDirector>();
            if (PlayerInput == null) PlayerInput = FindAnyObjectByType<PlayerCombatInput>();
            BindTimer();
        }
    }
}
#endif
