using System;
using UnityEngine;

namespace DeckArpg.Gameplay
{
    /// <summary>
    /// The arena's exit door (docs/specs/m3-ui.md). While open, it raises <see cref="PlayerEntered"/> once each
    /// time <see cref="Target"/> steps inside <see cref="Radius"/> (horizontal distance, no physics, so tests
    /// are deterministic). Standing inside when it opens doesn't count: the target has to step out and back in.
    /// </summary>
    public sealed class ExitDoor : MonoBehaviour
    {
        [SerializeField] private float radius = 1.5f;
        [SerializeField] private GameObject visuals;

        public Transform Target { get; set; }
        public float Radius => radius;
        public bool IsOpen { get; private set; }
        private bool TargetInside { get; set; }

        public event Action PlayerEntered;

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (visuals != null) visuals.SetActive(open);
            TargetInside = open && IsTargetInside();
        }

        private void Update()
        {
            if (!IsOpen || Target == null) return;
            bool inside = IsTargetInside();
            bool entered = inside && !TargetInside;
            TargetInside = inside;
            if (entered) PlayerEntered?.Invoke();
        }

        private bool IsTargetInside()
        {
            if (Target == null) return false;
            var offset = Target.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }

        /// <summary>Builds a greybox door (two posts, a lintel and a glowing panel) at <paramref name="position"/>, closed.</summary>
        public static ExitDoor Create(Vector3 position, Transform parent = null)
        {
            var root = new GameObject("ExitDoor");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var visuals = new GameObject("Visuals");
            visuals.transform.SetParent(root.transform, false);
            var frame = new Color(0.85f, 0.75f, 0.45f);
            AddPart(visuals.transform, PrimitiveType.Cube, "PostLeft", new Vector3(-1.1f, 1.25f, 0f), new Vector3(0.3f, 2.5f, 0.3f), frame);
            AddPart(visuals.transform, PrimitiveType.Cube, "PostRight", new Vector3(1.1f, 1.25f, 0f), new Vector3(0.3f, 2.5f, 0.3f), frame);
            AddPart(visuals.transform, PrimitiveType.Cube, "Lintel", new Vector3(0f, 2.6f, 0f), new Vector3(2.5f, 0.3f, 0.3f), frame);
            AddPart(visuals.transform, PrimitiveType.Quad, "Glow", new Vector3(0f, 1.25f, 0f), new Vector3(1.9f, 2.5f, 1f),
                new Color(0.55f, 0.85f, 1f));

            var door = root.AddComponent<ExitDoor>();
            door.visuals = visuals;
            door.SetOpen(false);
            return door;
        }

        private static void AddPart(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Color color)
        {
            // Visual only (no collider): the door is entered by distance, and colliders would block the walk-in.
            var part = FxShapes.CreateVisual(type, name, parent, color);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
