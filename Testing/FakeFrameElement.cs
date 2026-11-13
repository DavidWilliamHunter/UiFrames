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

        public override string ToString() => Name;
    }
}
