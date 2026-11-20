using UnityEngine;

namespace DeckArpg.Gameplay
{
    internal static class FlatDirection
    {
        /// <summary>
        /// <paramref name="v"/> projected onto the XZ plane and normalized, or <paramref name="fallback"/> when it has
        /// (almost) no horizontal length.
        /// </summary>
        public static Vector3 Of(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : fallback;
        }
    }
}
