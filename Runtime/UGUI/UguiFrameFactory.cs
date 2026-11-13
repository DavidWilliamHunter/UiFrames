using UnityEngine;
using UnityEngine.UI;

namespace Threads.UiFrames.UGUI
{
    /// <summary>
    /// Builds correctly set-up uGUI frames: the hierarchy, mask, raycast target, layout group,
    /// content size fitter and classes each frame needs. Used by the GameObject > UI Frames menu,
    /// and by code that builds UI at run time (the runner picks new frames up by itself).
    /// Sizes are in canvas units.
    /// </summary>
    public static class UguiFrameFactory
    {
        /// <summary>A strip (<c>uf-card-strip</c>) of cards along <paramref name="axis"/>: an empty
        /// raycast-target Image, a RectMask2D, and a <c>uf-cards</c> child with a layout group and a
        /// content size fitter. Add cards with <see cref="AddCard"/>.</summary>
        public static RectTransform CardStrip(Transform parent, Vector2 size, FrameAxis axis = FrameAxis.Horizontal, float spacing = 12f, string name = "Card Strip")
        {
            var frame = Frame(parent, name, size, FrameTags.CardStrip);
            if (axis == FrameAxis.Vertical) frame.GetComponent<UiFrameClasses>().SetInline(FrameProperties.Axis.Name, "vertical");
            Content(frame, axis, spacing);
            return frame;
        }

        /// <summary>A deck (<c>uf-card-deck</c>): like a strip, but it steps card by card and rests on
        /// whole cards. Make the frame exactly N cards (plus gaps) long to show N at a time.</summary>
        public static RectTransform CardDeck(Transform parent, Vector2 size, FrameAxis axis = FrameAxis.Horizontal, float spacing = 12f, string name = "Card Deck")
        {
            var frame = Frame(parent, name, size, FrameTags.CardDeck);
            if (axis == FrameAxis.Vertical) frame.GetComponent<UiFrameClasses>().SetInline(FrameProperties.Axis.Name, "vertical");
            Content(frame, axis, spacing);
            return frame;
        }

        /// <summary>Adds a card (an Image with a LayoutElement of <paramref name="size"/>) to a strip or deck.</summary>
        public static RectTransform AddCard(RectTransform frame, Vector2 size, Color? color = null, string name = null)
        {
            var content = FindContent(frame) ?? frame;
            var card = New(name ?? $"Card {content.childCount + 1}", content);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0, 1);
            card.sizeDelta = size;
            card.gameObject.AddComponent<Image>().color = color ?? new Color32(58, 92, 140, 255);
            var le = card.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            return card;
        }

        /// <summary>A drawer (<c>uf-drawer</c>) against <paramref name="edge"/> of
        /// <paramref name="parent"/> (usually the canvas), <paramref name="depth"/> deep, with a
        /// handle (<c>uf-drawer-handle</c>) sticking out on the open side. It is laid out open;
        /// the frame slides it off when closed. Put its contents under the returned transform.</summary>
        public static RectTransform Drawer(Transform parent, FrameEdge edge, float depth, out RectTransform handle, string name = "Drawer")
        {
            if (edge == FrameEdge.Auto) edge = FrameEdge.Left;
            var drawer = New(name, parent);
            Vector2 min, max, pivot, handlePivot, handleAnchor;
            switch (edge)
            {
                case FrameEdge.Right: min = new Vector2(1, 0); max = new Vector2(1, 1); pivot = new Vector2(1, 0.5f); handleAnchor = new Vector2(0, 0.5f); handlePivot = new Vector2(1, 0.5f); break;
                case FrameEdge.Top: min = new Vector2(0, 1); max = new Vector2(1, 1); pivot = new Vector2(0.5f, 1); handleAnchor = new Vector2(0.5f, 0); handlePivot = new Vector2(0.5f, 1); break;
                case FrameEdge.Bottom: min = new Vector2(0, 0); max = new Vector2(1, 0); pivot = new Vector2(0.5f, 0); handleAnchor = new Vector2(0.5f, 1); handlePivot = new Vector2(0.5f, 0); break;
                default: min = new Vector2(0, 0); max = new Vector2(0, 1); pivot = new Vector2(0, 0.5f); handleAnchor = new Vector2(1, 0.5f); handlePivot = new Vector2(0, 0.5f); break;
            }
            drawer.anchorMin = min;
            drawer.anchorMax = max;
            drawer.pivot = pivot;
            drawer.anchoredPosition = Vector2.zero;
            var horizontal = edge == FrameEdge.Left || edge == FrameEdge.Right;
            drawer.sizeDelta = horizontal ? new Vector2(depth, 0) : new Vector2(0, depth);
            drawer.gameObject.AddComponent<Image>().color = new Color32(38, 44, 58, 255);
            var classes = drawer.gameObject.AddComponent<UiFrameClasses>();
            classes.SetClasses(FrameTags.Drawer);
            classes.SetInline(FrameProperties.Edge.Name, edge.ToString().ToLowerInvariant());

            handle = New("Handle", drawer);
            handle.anchorMin = handle.anchorMax = handleAnchor;
            handle.pivot = handlePivot;
            handle.anchoredPosition = Vector2.zero;
            handle.sizeDelta = horizontal ? new Vector2(44, 64) : new Vector2(64, 44);
            handle.gameObject.AddComponent<Image>().color = new Color32(38, 44, 58, 255);
            handle.gameObject.AddComponent<UiFrameClasses>().SetClasses(FrameTags.DrawerHandle);
            return drawer;
        }

        /// <summary>The <c>uf-cards</c> child of a strip or deck, or null.</summary>
        public static RectTransform FindContent(RectTransform frame)
        {
            foreach (var c in frame.GetComponentsInChildren<UiFrameClasses>(true))
                if (c.transform != frame && c.HasClass(FrameTags.Cards)) return (RectTransform)c.transform;
            return null;
        }

        static RectTransform Frame(Transform parent, string name, Vector2 size, string marker)
        {
            var frame = New(name, parent);
            frame.sizeDelta = size;
            // An Image so presses on empty space reach the frame (almost transparent, not invisible:
            // a fully transparent Image is still a raycast target, but this shows the frame's area).
            frame.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            frame.gameObject.AddComponent<RectMask2D>();
            frame.gameObject.AddComponent<UiFrameClasses>().SetClasses(marker);
            return frame;
        }

        static void Content(RectTransform frame, FrameAxis axis, float spacing)
        {
            var content = New("Cards", frame);
            if (axis == FrameAxis.Horizontal)
            {
                content.anchorMin = new Vector2(0, 0);
                content.anchorMax = new Vector2(0, 1);
                content.pivot = new Vector2(0, 1);
                content.sizeDelta = Vector2.zero;
                var g = content.gameObject.AddComponent<HorizontalLayoutGroup>();
                g.spacing = spacing;
                g.childControlWidth = g.childControlHeight = true;
                g.childForceExpandWidth = g.childForceExpandHeight = false;
                content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = new Vector2(1, 1);
                content.pivot = new Vector2(0.5f, 1);
                content.sizeDelta = Vector2.zero;
                var g = content.gameObject.AddComponent<VerticalLayoutGroup>();
                g.spacing = spacing;
                g.childControlWidth = g.childControlHeight = true;
                g.childForceExpandWidth = true;
                g.childForceExpandHeight = false;
                content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            content.anchoredPosition = Vector2.zero;
            content.gameObject.AddComponent<UiFrameClasses>().SetClasses(FrameTags.Cards);
        }

        static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }
    }
}
