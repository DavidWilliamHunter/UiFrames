using System;
using System.Collections.Generic;

namespace Threads.UiFrames.Testing
{
    /// <summary>
    /// An <see cref="IFrameFocusHost"/> over a <see cref="FakeFrameElement"/> tree. Controls are
    /// elements with <see cref="FakeFrameElement.Focusable"/>; frames and cards become focusable
    /// through <see cref="EnsureFocusable"/>. Only visible elements can take focus.
    /// </summary>
    public sealed class FakeFocusHost : IFrameNavigationHost
    {
        readonly FakeFrameElement _root;
        readonly HashSet<IFrameElement> _madeFocusable = new HashSet<IFrameElement>();

        public FakeFocusHost(FakeFrameElement root) { _root = root; }

        public IFrameElement Root => _root;
        public FrameNavigator Navigator { get; set; }
        public IFrameElement Focused { get; private set; }
        public event Action<IFrameElement> FocusChanged;
        /// <summary>Controls currently told to take the arrow keys themselves.</summary>
        public HashSet<IFrameElement> Editing { get; } = new HashSet<IFrameElement>();

        public bool Owns(IFrameElement element) => element != null && FrameGeometry.IsInside(element, _root);

        public bool IsControl(IFrameElement element) =>
            element is FakeFrameElement f && f.Focusable && !_madeFocusable.Contains(f);

        public FrameControlArrows ArrowUse(IFrameElement control) =>
            control is FakeFrameElement f ? f.Arrows : FrameControlArrows.None;

        public void EnsureFocusable(IFrameElement element)
        {
            if (!(element is FakeFrameElement f) || f.Focusable) return;
            f.Focusable = true;
            _madeFocusable.Add(f);
        }

        public void SetEditing(IFrameElement control, bool editing)
        {
            if (editing) Editing.Add(control);
            else Editing.Remove(control);
        }

        public bool CanFocus(IFrameElement element) =>
            element is FakeFrameElement f && f.Focusable && f.VisibleInHierarchy && Owns(f);

        public bool Focus(IFrameElement element)
        {
            if (!CanFocus(element)) return false;
            if (Focused == element) return true;
            Focused = element;
            FocusChanged?.Invoke(element);
            return true;
        }

        /// <summary>As a click or the UI moving focus by itself.</summary>
        public bool MoveFocusTo(IFrameElement element) => Focus(element);

        public IFrameElement FirstFocusable(IFrameElement container, Func<IFrameElement, bool> filter = null)
        {
            foreach (var child in container.Children)
            {
                if (CanFocus(child) && (filter == null || filter(child))) return child;
                var inner = FirstFocusable(child, filter);
                if (inner != null) return inner;
            }
            return null;
        }
    }
}
