using System;
using System.Collections.Generic;

namespace Threads.UiFrames.Testing
{
    /// <summary>
    /// An in-memory <see cref="IFrameElement"/> for testing frame behaviours (built-in or from your
    /// own package) without UI Toolkit or uGUI. Build a tree with <see cref="Add"/> and read back
    /// translations, visibility and classes.
    /// </summary>
    public sealed class FakeFrameElement : IFrameElement
    {
        readonly List<IFrameElement> _children = new List<IFrameElement>();
        readonly HashSet<string> _classes = new HashSet<string>();

        public FakeFrameElement(string name, FrameRect rect, params string[] classes)
        {
            Name = name;
            LayoutRectDp = rect;
            foreach (var c in classes) _classes.Add(c);
        }

        public FakeFrameElement(string name, float x, float y, float width, float height, params string[] classes)
            : this(name, new FrameRect(x, y, width, height), classes) { }

        public string Name { get; }
        public object Native => this;
        public IFrameElement Parent { get; private set; }
        public IReadOnlyList<IFrameElement> Children => _children;
        public FrameRect LayoutRectDp { get; set; }
        public IReadOnlyCollection<string> Classes => _classes;
        public float TranslationX { get; private set; }
        public float TranslationY { get; private set; }
        public bool Visible { get; private set; } = true;
        /// <summary>Rotation of this element relative to its parent, degrees (clockwise on screen).</summary>
        public float RotationDeg { get; set; }
        /// <summary>Uniform scale of this element relative to its parent.</summary>
        public float Scale { get; set; } = 1f;
        /// <summary>A control a <see cref="FakeFocusHost"/> may focus (as designed).</summary>
        public bool Focusable { get; set; }
        /// <summary>How the control uses the arrow keys (sliders: Adjust; fields: Text).</summary>
        public FrameControlArrows Arrows { get; set; }

        /// <summary>Layout rects and translations summed up the tree (rotation and scale ignored).</summary>
        public FrameRect ScreenRectDp
        {
            get
            {
                float x = 0f, y = 0f;
                for (IFrameElement e = this; e != null; e = e.Parent)
                {
                    var r = e.LayoutRectDp;
                    x += r.X;
                    y += r.Y;
                    if (e is FakeFrameElement f) { x += f.TranslationX; y += f.TranslationY; }
                }
                return new FrameRect(x, y, LayoutRectDp.Width, LayoutRectDp.Height);
            }
        }

        /// <summary>Visible and every ancestor visible.</summary>
        public bool VisibleInHierarchy
        {
            get
            {
                for (IFrameElement e = this; e != null; e = e.Parent)
                    if (e is FakeFrameElement f && !f.Visible) return false;
                return true;
            }
        }

        public FakeFrameElement Add(FakeFrameElement child)
        {
            child.Parent = this;
            _children.Add(child);
            return child;
        }

        public bool HasClass(string className) => _classes.Contains(className);

        public void SetClass(string className, bool enabled)
        {
            if (enabled) _classes.Add(className);
            else _classes.Remove(className);
        }

        public void SetTranslationDp(float x, float y)
        {
            TranslationX = x;
            TranslationY = y;
        }

        public void SetVisible(bool visible) => Visible = visible;

        /// <summary>Resizes at once (a real UI lays out later).</summary>
        public void SetSizeDp(FrameAxis axis, float size)
        {
            var r = LayoutRectDp;
            LayoutRectDp = axis == FrameAxis.Horizontal ? new FrameRect(r.X, r.Y, size, r.Height) : new FrameRect(r.X, r.Y, r.Width, size);
        }

        /// <summary>Screen (dp, y up) to local (y down): undoes the rotation and scale of this element
        /// and its ancestors. The screen is the root's space at scale 1.</summary>
        public (float x, float y) ToLocalDeltaDp(float fromX, float fromY, float toX, float toY)
        {
            float x = toX - fromX, y = -(toY - fromY);
            float rotation = 0f, scale = 1f;
            for (IFrameElement e = this; e != null; e = e.Parent)
            {
                if (!(e is FakeFrameElement f)) continue;
                rotation += f.RotationDeg;
                scale *= f.Scale;
            }
            var a = -rotation * (float)Math.PI / 180f;
            var c = (float)Math.Cos(a);
            var s = (float)Math.Sin(a);
            return ((x * c - y * s) / scale, (x * s + y * c) / scale);
        }

        public override string ToString() => Name;
    }
}
