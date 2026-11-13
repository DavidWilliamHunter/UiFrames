#if THREADS_UF_TOUCHCONTROLLER
using UnityEngine;
using UnityEngine.UIElements;

namespace Threads.UiFrames.TouchInput
{
    /// <summary>
    /// Frames' elements in TouchController's screen space (pixels or dp, origin bottom-left), for
    /// hit-testing touches and placing joysticks. Uses each UI system's own mapping, so canvas
    /// cameras and panel scaling are respected.
    /// </summary>
    public static class FrameScreen
    {
        /// <summary>Whether the element is shown and the screen point (px, origin bottom-left) is on it.</summary>
        public static bool Contains(IFrameElement element, Vector2 screenPx, Vector2 screenSizePx)
        {
            switch (element?.Native)
            {
                case GameObject go when go && go.activeInHierarchy:
                    var rt = (RectTransform)go.transform;
                    return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPx, CameraOf(rt));
                case VisualElement ve when ve.panel != null && Shown(ve):
                    var p = RuntimePanelUtils.ScreenToPanel(ve.panel, new Vector2(screenPx.x, screenSizePx.y - screenPx.y));
                    return ve.worldBound.Contains(p);
                default:
                    return false;
            }
        }

        /// <summary>The element's drawn bounds in screen pixels (origin bottom-left).</summary>
        public static Rect RectPx(IFrameElement element, Vector2 screenSizePx)
        {
            switch (element?.Native)
            {
                case GameObject go when go:
                {
                    var rt = (RectTransform)go.transform;
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    var cam = CameraOf(rt);
                    Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
                    foreach (var c in corners)
                    {
                        var s = RectTransformUtility.WorldToScreenPoint(cam, c);
                        min = Vector2.Min(min, s);
                        max = Vector2.Max(max, s);
                    }
                    return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                }
                case VisualElement ve when ve.panel != null:
                {
                    // Panel → screen: invert ScreenToPanel (an axis-aligned scale and offset).
                    var o = RuntimePanelUtils.ScreenToPanel(ve.panel, Vector2.zero);
                    var u = RuntimePanelUtils.ScreenToPanel(ve.panel, new Vector2(100f, 100f));
                    var sx = Mathf.Abs(u.x - o.x) > 1e-4f ? 100f / (u.x - o.x) : 1f;
                    var sy = Mathf.Abs(u.y - o.y) > 1e-4f ? 100f / (u.y - o.y) : 1f;
                    var b = ve.worldBound;
                    var x0 = (b.xMin - o.x) * sx;
                    var x1 = (b.xMax - o.x) * sx;
                    // Screen y from the top for ScreenToPanel; flip to bottom-left.
                    var yTop = (b.yMin - o.y) * sy;
                    var yBottom = (b.yMax - o.y) * sy;
                    return Rect.MinMaxRect(Mathf.Min(x0, x1), screenSizePx.y - Mathf.Max(yTop, yBottom),
                                           Mathf.Max(x0, x1), screenSizePx.y - Mathf.Min(yTop, yBottom));
                }
                default:
                    return default;
            }
        }

        public static Rect RectDp(IFrameElement element, float pixelsPerDp, Vector2 screenSizePx)
        {
            var r = RectPx(element, screenSizePx);
            var k = Mathf.Max(1e-4f, pixelsPerDp);
            return new Rect(r.x / k, r.y / k, r.width / k, r.height / k);
        }

        static Camera CameraOf(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            var root = canvas ? canvas.rootCanvas : null;
            return root && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        }

        static bool Shown(VisualElement ve)
        {
            for (var e = ve; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }
    }
}
#endif
