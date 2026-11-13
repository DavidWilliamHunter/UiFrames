using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary>A direction from the arrow keys, d-pad or stick.</summary>
    public enum FrameNavDirection { Left, Right, Up, Down }

    /// <summary>
    /// Keyboard and gamepad navigation in levels, one navigator per UI root (a UIDocument, a Canvas):
    /// <list type="number">
    /// <item><b>Frames.</b> Arrows (spatially) and Tab move the selection between frames and the
    /// controls outside frames. Enter goes into the selected frame.</item>
    /// <item><b>Cards.</b> Arrows along the frame's axis and Tab move between its cards; the frame
    /// scrolls or steps to show the selected card. Enter goes into a card.</item>
    /// <item><b>Controls.</b> Arrows and Tab move between the card's controls. Enter presses a
    /// button; on a slider or text field it starts adjusting or editing (arrows then belong to
    /// the control) and Enter or Esc ends it.</item>
    /// </list>
    /// Esc always goes back one level (closing a drawer when leaving it). Levels with nothing to choose
    /// are skipped: a card with one control goes straight to it; a card with none raises
    /// <see cref="Activated"/>. A frame with <c>--uf-navigation: flat</c> is not a level: its controls
    /// belong to the level around it. A drawer is a frame whose level is its controls.
    /// <para>The selection carries <c>uf-selected</c>; entered frames and cards carry
    /// <c>uf-entered</c>; a control being adjusted carries <c>uf-editing</c>. The UI's own focus
    /// follows the selection (frames and cards are made focusable), and the selection follows the
    /// UI's focus when it moves another way (a click). When a frame moves (mouse, wheel, page keys)
    /// so that its selected card is out of view, the selection moves to a card in view.</para>
    /// </summary>
    public sealed class FrameNavigator : IDisposable
    {
        enum ScopeKind { Root, Cards, Container }

        sealed class Scope
        {
            public ScopeKind Kind;
            public IFrameElement Container;
            public IFrameBehaviour Frame;
            public IFrameElement Selected;
            /// <summary>Entered automatically because the level above had only this to choose.</summary>
            public bool AutoEntered;
        }

        readonly FrameSet _frames;
        readonly IFrameNavigationHost _host;
        readonly List<Scope> _stack = new List<Scope>();
        readonly Dictionary<IFrameElement, IFrameElement> _remembered = new Dictionary<IFrameElement, IFrameElement>();
        IFrameElement _marked;
        IFrameElement _editing;
        IFrameElement _pendingFocus;
        bool _moving; // we are moving UI focus ourselves

        public FrameNavigator(FrameSet frames, IFrameNavigationHost host)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public IFrameNavigationHost Host => _host;
        public bool IsRunning { get; private set; }
        /// <summary>The selected frame, card or control, or null.</summary>
        public IFrameElement Selected => _stack.Count > 0 ? Current.Selected : null;
        /// <summary>0 at the top level; +1 for each frame or card entered.</summary>
        public int Depth => _stack.Count - 1;
        /// <summary>The control taking the arrow keys itself, or null.</summary>
        public IFrameElement Editing => _editing;
        public bool IsEditing => _editing != null;

        /// <summary>Enter on something with nothing inside to go to (e.g. a picture card): the game
        /// decides what that does.</summary>
        public event Action<IFrameElement> Activated;
        public event Action SelectionChanged;

        Scope Current => _stack[_stack.Count - 1];

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            _stack.Clear();
            _stack.Add(new Scope { Kind = ScopeKind.Root, Container = _host.Root });
            _host.Navigator = this;
            _host.FocusChanged += OnHostFocus;
            _frames.FocusChanged += OnFrameFocus;
            _frames.Removed += OnFrameRemoved;
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _host.FocusChanged -= OnHostFocus;
            _frames.FocusChanged -= OnFrameFocus;
            _frames.Removed -= OnFrameRemoved;
            if (_editing != null) StopEditing();
            for (var i = 1; i < _stack.Count; i++) _stack[i].Container.SetClass(FrameTags.Entered, false);
            _marked?.SetClass(FrameTags.Selected, false);
            _marked = null;
            _stack.Clear();
            _remembered.Clear();
            if (_host.Navigator == this) _host.Navigator = null;
        }

        public void Dispose() => Shutdown();

        // ---------------- Input ----------------

        /// <summary>An arrow, d-pad or stick direction. Returns true if used (the UI should then not
        /// move focus itself).</summary>
        public bool Move(FrameNavDirection direction)
        {
            if (!IsRunning || _editing != null) return false;
            var s = Current;
            var items = Items(s);
            if (s.Selected == null || !items.Contains(s.Selected)) return SelectFirst(items);
            if (s.Kind == ScopeKind.Cards)
            {
                var step = Step(s.Frame.DragAxis, direction);
                if (step == 0)
                {
                    // Across the axis: the frame may use it (a menu row changes its value).
                    var across = direction == FrameNavDirection.Right || direction == FrameNavDirection.Down ? 1 : -1;
                    s.Frame.Execute(FrameCommand.Adjust(across, s.Selected));
                    return true;
                }
                var j = items.IndexOf(s.Selected) + step;
                if (j >= 0 && j < items.Count) Select(items[j]);
                return true; // at an end: stay in this level
            }
            var best = Nearest(items, s.Selected, direction);
            if (best != null) Select(best);
            return true;
        }

        /// <summary>Tab (+1) or Shift+Tab (−1): the next item in this level, wrapping.</summary>
        public bool Tab(int direction)
        {
            if (!IsRunning || _editing != null) return false;
            var items = Items(Current);
            if (items.Count == 0) return false;
            var i = Current.Selected == null ? -1 : items.IndexOf(Current.Selected);
            var j = i < 0 ? (direction >= 0 ? 0 : items.Count - 1) : ((i + Math.Sign(direction == 0 ? 1 : direction)) % items.Count + items.Count) % items.Count;
            Select(items[j]);
            return true;
        }

        /// <summary>Enter / Space / gamepad A. Returns false when the selected control should handle
        /// it itself (a button press, a text field's Enter).</summary>
        public bool Submit()
        {
            if (!IsRunning) return false;
            if (_editing != null)
            {
                var use = _host.ArrowUse(_editing);
                StopEditing();
                return use == FrameControlArrows.Adjust;
            }
            var item = Current.Selected;
            if (item == null) return SelectFirst(Items(Current));
            if (_host.IsControl(item))
            {
                var use = _host.ArrowUse(item);
                if (use == FrameControlArrows.None) return false;
                StartEditing(item);
                return use == FrameControlArrows.Adjust;
            }
            if (EnterItem(item, auto: false)) return true;
            Activated?.Invoke(item);
            return true;
        }

        /// <summary>Esc / gamepad B: stop adjusting a control, else go back one level.</summary>
        public bool Cancel()
        {
            if (!IsRunning) return false;
            if (_editing != null) { StopEditing(); return true; }
            if (_stack.Count <= 1)
            {
                // At the top: Esc closes the selected drawer if something else opened it.
                if (FrameOf(Current.Selected) is DrawerFrame open && open.IsOpen) return open.Execute(FrameCommand.Close);
                return false;
            }
            Scope popped;
            do popped = Pop();
            while (popped.AutoEntered && _stack.Count > 1);
            Select(popped.Container);
            return true;
        }

        /// <summary>Back to the top level and on to the next (−1: previous) frame.</summary>
        public void CycleFrame(int direction)
        {
            if (!IsRunning) return;
            if (_editing != null) StopEditing();
            IFrameElement from = null;
            while (_stack.Count > 1) from = Pop().Container;
            from = from ?? Current.Selected;
            var frames = Items(Current).FindAll(e => IsLevelsFrame(FrameOf(e)));
            if (frames.Count == 0) return;
            var i = from == null ? -1 : frames.IndexOf(from);
            var j = i < 0 ? 0 : ((i + (direction >= 0 ? 1 : -1)) % frames.Count + frames.Count) % frames.Count;
            Select(frames[j]);
        }

        /// <summary>Selects <paramref name="element"/> as if the user had navigated to it (entering
        /// the frames and cards around it).</summary>
        public void SelectElement(IFrameElement element)
        {
            if (!IsRunning || element == null || !_host.Owns(element)) return;
            Sync(element, focus: true);
        }

        /// <summary>Call once per frame.</summary>
        public void Update()
        {
            if (!IsRunning) return;

            if (_pendingFocus != null)
            {
                // e.g. a drawer that just opened: its controls can take focus once shown.
                var e = _pendingFocus;
                if (!_host.Owns(e)) _pendingFocus = null;
                else if (FocusHost(e)) _pendingFocus = null;
            }

            for (var i = 1; i < _stack.Count; i++)
            {
                var s = _stack[i];
                // A drawer closed some other way (mouse, code): leave it.
                if (s.Frame is DrawerFrame d && !d.IsOpen)
                {
                    while (_stack.Count > i) Pop();
                    Select(s.Container);
                    return;
                }
                // A frame moved its selected card out of view (mouse, wheel, page keys): select one in view.
                if (s.Kind == ScopeKind.Cards && s.Selected != null && s.Frame is IFrameNavigable nav && !nav.IsMoving && !nav.IsShown(s.Selected))
                {
                    var shown = Items(s).Find(nav.IsShown);
                    if (shown == null) continue;
                    while (_stack.Count > i + 1) Pop();
                    Select(shown);
                    return;
                }
            }

            if (Current.Selected != null && !_host.Owns(Current.Selected)) Reset();
        }

        // ---------------- Levels ----------------

        List<IFrameElement> Items(Scope s)
        {
            var items = new List<IFrameElement>();
            if (s.Kind == ScopeKind.Cards)
            {
                var content = (s.Frame as CardFrameBase)?.Content;
                if (content != null) items.AddRange(content.Children);
                return items;
            }
            Collect(s.Container, s, items);
            return items;
        }

        void Collect(IFrameElement parent, Scope s, List<IFrameElement> into)
        {
            foreach (var c in parent.Children)
            {
                if (s.Frame is DrawerFrame && c.HasClass(FrameTags.DrawerHandle)) continue;
                if (c.HasClass(FrameTags.PointerOnly)) continue; // mouse and touch only
                if (IsLevelsFrame(FrameOf(c))) { into.Add(c); continue; }
                if (_host.IsControl(c)) { into.Add(c); continue; }
                Collect(c, s, into);
            }
        }

        static bool IsLevelsFrame(IFrameBehaviour f) =>
            f != null && f.Style.Accepts(FrameInputKinds.Navigation) &&
            f.Style.Get(FrameProperties.Navigation, FrameNavigationMode.Levels) == FrameNavigationMode.Levels;

        IFrameBehaviour FrameOf(IFrameElement e) => e == null ? null : _frames.Find(e.Native);

        /// <summary>The level <paramref name="item"/> opens, or null if it is not a frame or card.</summary>
        Scope ScopeFor(IFrameElement item, Scope from)
        {
            var f = FrameOf(item);
            if (IsLevelsFrame(f) && item != from.Container)
            {
                if (f is CardFrameBase) return new Scope { Kind = ScopeKind.Cards, Container = item, Frame = f };
                return new Scope { Kind = ScopeKind.Container, Container = item, Frame = f };
            }
            if (from.Kind == ScopeKind.Cards) return new Scope { Kind = ScopeKind.Container, Container = item };
            return null;
        }

        bool EnterItem(IFrameElement item, bool auto)
        {
            var scope = ScopeFor(item, Current);
            if (scope == null) return false;
            if (scope.Frame is DrawerFrame drawer && !drawer.IsOpen) drawer.Execute(FrameCommand.Open);
            var items = Items(scope);
            if (items.Count == 0)
            {
                if (auto) return false; // skipping a trivial level never acts on what is in it
                Current.Frame?.Execute(FrameCommand.Activate(item)); // the frame may act on it (a menu row)
                Activated?.Invoke(item);
                return true;
            }
            scope.AutoEntered = auto;
            Push(scope);
            var target = items.Count == 1 ? items[0] : Remembered(scope, items) ?? items.Find(IsShownInFrames) ?? items[0];
            Select(target);
            if (items.Count == 1 && ScopeFor(target, scope) != null) EnterItem(target, auto: true);
            return true;
        }

        IFrameElement Remembered(Scope s, List<IFrameElement> items) =>
            _remembered.TryGetValue(s.Container, out var r) && items.Contains(r) ? r : null;

        void Push(Scope s)
        {
            _stack.Add(s);
            s.Container.SetClass(FrameTags.Entered, true);
        }

        Scope Pop()
        {
            var s = Current;
            _stack.RemoveAt(_stack.Count - 1);
            s.Container.SetClass(FrameTags.Entered, false);
            if (s.Frame is DrawerFrame d && d.IsOpen) d.Execute(FrameCommand.Close);
            return s;
        }

        void Reset()
        {
            if (_editing != null) StopEditing();
            while (_stack.Count > 1) Pop();
            Current.Selected = null;
            Mark(null);
        }

        bool SelectFirst(List<IFrameElement> items)
        {
            if (items.Count == 0) return false;
            Select(Remembered(Current, items) ?? items.Find(IsShownInFrames) ?? items[0]);
            return true;
        }

        // ---------------- Selection ----------------

        void Select(IFrameElement item, bool focus = true)
        {
            var s = Current;
            s.Selected = item;
            _remembered[s.Container] = item;
            Mark(item);

            // Bring it into view in every frame around it; a strip or deck shows its whole card, so
            // the card stays the selection of that level.
            for (var e = item.Parent; e != null; e = e.Parent)
            {
                var f = FrameOf(e);
                if (f == null || !f.Style.Accepts(FrameInputKinds.Navigation)) continue;
                f.Execute(FrameCommand.Reveal(CardOf(f, item) ?? item));
            }

            _moving = true;
            try
            {
                var frame = FrameOf(item) ?? InnermostFrame(item);
                if (frame != null) _frames.Focus(frame);
                if (focus)
                {
                    if (!_host.IsControl(item)) _host.EnsureFocusable(item);
                    _pendingFocus = FocusHost(item) ? null : item;
                }
            }
            finally { _moving = false; }
            SelectionChanged?.Invoke();
        }

        bool FocusHost(IFrameElement e)
        {
            _moving = true;
            try { return _host.Focus(e); }
            finally { _moving = false; }
        }

        void Mark(IFrameElement item)
        {
            if (_marked == item) return;
            _marked?.SetClass(FrameTags.Selected, false);
            _marked = item;
            _marked?.SetClass(FrameTags.Selected, true);
        }

        /// <summary>The card of <paramref name="frame"/> that contains <paramref name="e"/>, or null.</summary>
        static IFrameElement CardOf(IFrameBehaviour frame, IFrameElement e)
        {
            var content = (frame as CardFrameBase)?.Content;
            if (content == null) return null;
            for (var x = e; x != null; x = x.Parent)
                if (x.Parent == content) return x;
            return null;
        }

        IFrameBehaviour InnermostFrame(IFrameElement e)
        {
            for (var x = e.Parent; x != null; x = x.Parent)
            {
                var f = FrameOf(x);
                if (f != null) return f;
            }
            return null;
        }

        bool IsShownInFrames(IFrameElement element)
        {
            for (var e = element.Parent; e != null; e = e.Parent)
                if (FrameOf(e) is IFrameNavigable nav && !nav.IsShown(element)) return false;
            return true;
        }

        void StartEditing(IFrameElement control)
        {
            _editing = control;
            control.SetClass(FrameTags.Editing, true);
            _host.SetEditing(control, true);
        }

        void StopEditing()
        {
            var c = _editing;
            _editing = null;
            c.SetClass(FrameTags.Editing, false);
            _host.SetEditing(c, false);
        }

        // ---------------- Following focus moved another way ----------------

        void OnHostFocus(IFrameElement element)
        {
            if (_moving || element == null || !IsRunning) return;
            var selected = Current.Selected;
            if (element == selected) return;
            // A field passing focus to its inner input is still the same selection.
            if (selected != null && _host.IsControl(selected) && FrameGeometry.IsInside(element, selected)) return;
            Sync(element, focus: false); // also ends any editing
        }

        void OnFrameFocus(IFrameBehaviour frame)
        {
            // A press on a frame (pointer). Keep the levels if the frame is already entered.
            if (_moving || frame == null || !IsRunning || !_host.Owns(frame.Element)) return;
            foreach (var s in _stack)
                if (s.Frame == frame || s.Container == frame.Element || s.Selected == frame.Element) return;
            if (IsLevelsFrame(frame)) Sync(frame.Element, focus: true);
        }

        void OnFrameRemoved(IFrameBehaviour frame)
        {
            foreach (var s in _stack)
                if (s.Frame == frame) { Reset(); return; }
            if (Current.Selected == frame.Element) Reset();
        }

        /// <summary>Rebuilds the levels so that <paramref name="element"/> (or the nearest item around
        /// it) is selected, entering the frames and cards that contain it.</summary>
        void Sync(IFrameElement element, bool focus)
        {
            var chain = new List<IFrameElement>();
            for (var x = element; x != null && x != _host.Root; x = x.Parent) chain.Add(x);
            chain.Reverse();

            if (_editing != null) StopEditing();
            // Leave levels without closing drawers: the user moved on another way.
            while (_stack.Count > 1)
            {
                var s = Current;
                _stack.RemoveAt(_stack.Count - 1);
                s.Container.SetClass(FrameTags.Entered, false);
            }

            IFrameElement last = null;
            foreach (var x in chain)
            {
                var s = Current;
                if (!Items(s).Contains(x)) continue;
                last = x;
                if (x == element) break;
                var inner = ScopeFor(x, s);
                if (inner == null) break;
                s.Selected = x;
                _remembered[s.Container] = x;
                Push(inner);
            }
            if (last == null) return;
            // The element was not an item itself (e.g. a drawer's handle): do not stay inside the
            // level opened for the last item found; select that item instead.
            if (_stack.Count > 1 && Current.Container == last)
            {
                var s = Current;
                _stack.RemoveAt(_stack.Count - 1);
                s.Container.SetClass(FrameTags.Entered, false);
            }
            Select(last, focus);
        }

        // ---------------- Geometry ----------------

        static int Step(FrameAxis axis, FrameNavDirection d)
        {
            if (axis == FrameAxis.Horizontal) return d == FrameNavDirection.Right ? 1 : d == FrameNavDirection.Left ? -1 : 0;
            return d == FrameNavDirection.Down ? 1 : d == FrameNavDirection.Up ? -1 : 0;
        }

        /// <summary>The item nearest in <paramref name="d"/>: mostly along it, penalising sideways distance.</summary>
        static IFrameElement Nearest(List<IFrameElement> items, IFrameElement from, FrameNavDirection d)
        {
            var a = from.ScreenRectDp;
            IFrameElement best = null;
            var bestScore = float.MaxValue;
            foreach (var c in items)
            {
                if (c == from) continue;
                var b = c.ScreenRectDp;
                float along, across;
                switch (d)
                {
                    case FrameNavDirection.Right: along = b.X - (a.X + a.Width); across = Gap(a.Y, a.Height, b.Y, b.Height); if (b.CentreX <= a.CentreX) continue; break;
                    case FrameNavDirection.Left: along = a.X - (b.X + b.Width); across = Gap(a.Y, a.Height, b.Y, b.Height); if (b.CentreX >= a.CentreX) continue; break;
                    case FrameNavDirection.Down: along = b.Y - (a.Y + a.Height); across = Gap(a.X, a.Width, b.X, b.Width); if (b.CentreY <= a.CentreY) continue; break;
                    default: along = a.Y - (b.Y + b.Height); across = Gap(a.X, a.Width, b.X, b.Width); if (b.CentreY >= a.CentreY) continue; break;
                }
                var score = Math.Max(0f, along) + across * 2f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        /// <summary>Distance between two ranges on the other axis (0 if they overlap).</summary>
        static float Gap(float aStart, float aSize, float bStart, float bSize)
        {
            if (bStart + bSize < aStart) return aStart - (bStart + bSize);
            if (bStart > aStart + aSize) return bStart - (aStart + aSize);
            return 0f;
        }
    }
}
