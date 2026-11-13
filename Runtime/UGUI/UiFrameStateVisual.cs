using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Threads.UiFrames.UGUI
{
    /// <summary>
    /// Shows a frame's (or card's, or control's) state classes, the uGUI counterpart of styling
    /// <c>.uf-selected</c>, <c>.uf-entered</c>… in USS. Each entry reacts to one class with an
    /// outline, a tint of a Graphic, or by showing a GameObject. The binder adds one with
    /// <see cref="AddDefaults"/> to elements that get a state class and have none (turn that off with
    /// <see cref="UguiFrameBinder.DefaultStateVisuals"/>); add your own to choose the looks.
    /// </summary>
    [AddComponentMenu("UI Frames/UI Frame State Visual")]
    [RequireComponent(typeof(UiFrameClasses))]
    public sealed class UiFrameStateVisual : MonoBehaviour
    {
        public enum Kind { Outline, Tint, Show }

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The state class, e.g. uf-selected, uf-entered, uf-editing, uf-open, uf-dragging.")]
            public string className = FrameTags.Selected;
            public Kind kind = Kind.Outline;
            public Color color = new Color32(255, 196, 64, 255);
            [Tooltip("Outline thickness (canvas units).")]
            public float width = 4f;
            [Tooltip("Tint: the Graphic to recolour (empty = this object's Graphic).")]
            public Graphic tintTarget;
            [Tooltip("Show: the object made active while the class is set.")]
            public GameObject showTarget;

            [NonSerialized] internal GameObject outline;
            [NonSerialized] internal Color original;
            [NonSerialized] internal bool tinted;
        }

        public List<Entry> entries = new List<Entry>();

        UiFrameClasses _classes;

        /// <summary>The package look: a bright outline for the keyboard selection, a faint one for
        /// frames and cards the keyboard is inside, an orange one for a control being adjusted.</summary>
        public void AddDefaults()
        {
            entries.Add(new Entry { className = FrameTags.Selected, kind = Kind.Outline, color = new Color32(255, 196, 64, 255), width = 4f });
            entries.Add(new Entry { className = FrameTags.Entered, kind = Kind.Outline, color = new Color(1f, 0.77f, 0.25f, 0.45f), width = 2f });
            entries.Add(new Entry { className = FrameTags.Editing, kind = Kind.Outline, color = new Color32(255, 128, 32, 255), width = 4f });
        }

        void Reset() => AddDefaults();

        void OnEnable()
        {
            _classes = GetComponent<UiFrameClasses>();
            _classes.ClassChanged += OnClassChanged;
            foreach (var e in entries) Apply(e, _classes.HasClass(e.className));
        }

        void OnDisable()
        {
            if (_classes) _classes.ClassChanged -= OnClassChanged;
            foreach (var e in entries) Apply(e, false);
        }

        void OnClassChanged(string className, bool on)
        {
            foreach (var e in entries)
                if (e.className == className) Apply(e, on);
        }

        void Apply(Entry e, bool on)
        {
            switch (e.kind)
            {
                case Kind.Outline:
                    if (on && !e.outline) e.outline = MakeOutline(e);
                    if (e.outline)
                    {
                        e.outline.SetActive(on);
                        if (on) e.outline.transform.SetAsLastSibling(); // drawn over the content
                    }
                    break;
                case Kind.Tint:
                    var g = e.tintTarget ? e.tintTarget : GetComponent<Graphic>();
                    if (!g) break;
                    if (on && !e.tinted) { e.original = g.color; g.color = e.color; e.tinted = true; }
                    else if (!on && e.tinted) { g.color = e.original; e.tinted = false; }
                    break;
                case Kind.Show:
                    if (e.showTarget) e.showTarget.SetActive(on);
                    break;
            }
        }

        GameObject MakeOutline(Entry e)
        {
            var box = new GameObject($"UF {e.className}", typeof(RectTransform), typeof(LayoutElement));
            box.hideFlags = HideFlags.DontSave;
            var rt = (RectTransform)box.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            box.GetComponent<LayoutElement>().ignoreLayout = true;
            Bar(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, e.width), e.color);
            Bar(rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, e.width), e.color);
            Bar(rt, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(e.width, 0), e.color);
            Bar(rt, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(e.width, 0), e.color);
            return box;
        }

        static void Bar(RectTransform parent, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size, Color color)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.hideFlags = HideFlags.DontSave;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        void OnDestroy()
        {
            foreach (var e in entries)
                if (e.outline) Destroy(e.outline);
        }
    }
}
