using DeckArpg.Core;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// Greybox look of an enemy variant: a base body tint and a body scale (docs/specs/enemy-variants.md). The colours
    /// stay clear of the element/status tints: Fire/Burn orange, Lightning yellow, Ice blue, Wind slate and the yellow
    /// break flash.
    /// </summary>
    public static class EnemyVariantLook
    {
        public static readonly Color CasterColor = new Color(0.55f, 0.30f, 0.85f);
        /// <summary>Teal, not the spec's amber: amber read as Burn/Lightning/break-flash next to them.</summary>
        public static readonly Color RusherColor = new Color(0.15f, 0.70f, 0.60f);
        public static readonly Vector3 CasterScale = new Vector3(0.85f, 1.2f, 0.85f);
        public static readonly Vector3 RusherScale = new Vector3(0.8f, 0.7f, 0.8f);

        public static Color ColorOf(EnemyVariant v) =>
            v == EnemyVariant.Caster ? CasterColor : v == EnemyVariant.Rusher ? RusherColor : Color.clear;

        public static Vector3 ScaleOf(EnemyVariant v) =>
            v == EnemyVariant.Caster ? CasterScale : v == EnemyVariant.Rusher ? RusherScale : Vector3.one;

        /// <summary>Tints and scales <paramref name="enemy"/>. A Brute is left as it is.</summary>
        public static void Apply(TrainingDummyEnemy enemy, EnemyVariant v)
        {
            if (enemy == null || v == EnemyVariant.Brute) return;
            var scale = ScaleOf(v);
            enemy.transform.localScale = Vector3.Scale(enemy.transform.localScale, scale);
            BodyTint.For(enemy.gameObject).SetBaseColor(ColorOf(v));
            var pop = enemy.GetComponent<DeathPop>();
            if (pop != null) pop.Scale = (scale.x + scale.y + scale.z) / 3f;
        }
    }
}
