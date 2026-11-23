using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DeckArpg.UI
{
    /// <summary>
    /// Builds greybox uGUI widgets in code. Uses the built-in legacy font so no TextMeshPro
    /// resources need importing; swap to TMP when real UI art arrives.
    /// </summary>
    internal static class UiFactory
    {
        private static Font font;

        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Canvas CreateOverlayCanvas(Transform parent, string name, int sortingOrder = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>Clickable screens need one EventSystem (with the Input System module); creates it if the scene has none.</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// <summary>Ease-out cubic on [0, 1]: fast start, soft landing (pop-up punch-in).</summary>
        public static float EaseOutCubic(float p) => 1f - (1f - p) * (1f - p) * (1f - p);

        /// <summary>Overlay canvases convert screen points with a null camera; camera-space canvases need theirs.</summary>
        public static Camera CanvasCamera(Canvas canvas) =>
            canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        /// <summary>Screen point -> local point in a canvas rect, with the right camera for its render mode.</summary>
        public static Vector2 ScreenToCanvasLocal(RectTransform canvasRect, Vector3 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen,
                CanvasCamera(canvasRect.GetComponent<Canvas>()), out var local);
            return local;
        }

        public static RectTransform CreateRect(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image CreateImage(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var image = CreateRect(parent, name, anchor, pivot, anchoredPosition, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment, Color color)
        {
            var rt = CreateRect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            return text;
        }

        /// <summary>
        /// Darkens and widens a text's outline so white numbers stay readable on light cards. Reuses the outline
        /// <see cref="CreateText"/> adds; a second one would copy the text mesh again.
        /// </summary>
        public static void ReadableOutline(Text text, float distance)
        {
            var outline = text.GetComponent<Outline>();
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(distance, -distance);
        }
    }
}
