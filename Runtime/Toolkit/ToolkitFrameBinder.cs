using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Threads.UiFrames.Toolkit
{
    /// <summary>
    /// Binds frames in one UI Toolkit tree (usually a UIDocument's root): finds elements carrying a
    /// registered marker class, creates their behaviours, feeds them their <c>--*</c> custom
    /// properties (on <see cref="CustomStyleResolvedEvent"/>) and their geometry (on
    /// <see cref="GeometryChangedEvent"/>). Call <see cref="Rescan"/> after adding or re-tagging
    /// elements at runtime. One explicit <see cref="Startup"/> and <see cref="Shutdown"/>.
    /// </summary>
    public sealed class ToolkitFrameBinder : IDisposable
    {
        sealed class Binding
        {
            public VisualElement Element;
            public IFrameKind Kind;
            public IFrameBehaviour Behaviour;
            public readonly List<VisualElement> Watched = new List<VisualElement>();
        }

        readonly FrameRegistry _registry;
        readonly FrameEnvironment _environment;
        readonly FrameSet _frames;
        readonly Dictionary<VisualElement, Binding> _bindings = new Dictionary<VisualElement, Binding>();
        readonly Dictionary<VisualElement, Binding> _watchers = new Dictionary<VisualElement, Binding>();
        readonly Dictionary<VisualElement, ToolkitFrameElement> _wrappers = new Dictionary<VisualElement, ToolkitFrameElement>();
        readonly List<(FrameProperty property, CustomStyleProperty<string> uss)> _properties = new List<(FrameProperty, CustomStyleProperty<string>)>();
        readonly HashSet<string> _reported = new HashSet<string>();
        // Elements hidden by a frame, and the focusable descendants each one turned off. An element
        // inside several hidden areas (a cut-off card in a closed drawer) counts each of them and is
        // focusable again only when the last one shows.
        readonly Dictionary<VisualElement, List<VisualElement>> _suppressed = new Dictionary<VisualElement, List<VisualElement>>();
        readonly Dictionary<VisualElement, int> _suppressCount = new Dictionary<VisualElement, int>();
        readonly List<VisualElement> _scratch = new List<VisualElement>();
        double _nextScan;

        public ToolkitFrameBinder(VisualElement root, FrameRegistry registry, FrameEnvironment environment, FrameSet frames)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }

        public VisualElement Root { get; }
        public FrameSet Frames => _frames;
        public FrameRegistry Registry => _registry;

        /// <summary>A frame was created for an element (after its first style).</summary>
        public event Action<IFrameBehaviour> FrameBound;
        /// <summary>A frame's custom properties were applied again.</summary>
        public event Action<IFrameBehaviour> FrameStyled;
        /// <summary>A frame is about to be disposed.</summary>
        public event Action<IFrameBehaviour> FrameUnbound;
        public bool IsRunning { get; private set; }
        /// <summary>dp per panel unit (panel scale and screen density).</summary>
        public float DpPerUnit { get; private set; } = 1f;

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            _registry.Changed += RescanAndRestyle;
            UpdateScale();
            Rescan();
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _registry.Changed -= RescanAndRestyle;
            foreach (var b in new List<Binding>(_bindings.Values)) Unbind(b);
            foreach (var hidden in new List<VisualElement>(_suppressed.Keys)) SetFocusSuppressed(hidden, false);
            _wrappers.Clear();
        }

        /// <summary>Whether the element was designed to take focus (true even while a hidden frame
        /// part has turned its focus off).</summary>
        public bool DesignedFocusable(VisualElement ve) => ve.focusable || _suppressCount.ContainsKey(ve);

        /// <summary>Hidden parts of frames (a closed drawer's contents, a cut-off card) must not be
        /// reachable by Tab or navigation: their focusable elements are turned off while hidden.</summary>
        internal void SetFocusSuppressed(VisualElement root, bool suppressed)
        {
            if (suppressed)
            {
                if (_suppressed.ContainsKey(root)) return;
                var list = new List<VisualElement>();
                root.Query<VisualElement>().ForEach(e =>
                {
                    _suppressCount.TryGetValue(e, out var n);
                    if (n == 0 && !e.focusable) return;
                    _suppressCount[e] = n + 1;
                    e.focusable = false;
                    list.Add(e);
                });
                _suppressed.Add(root, list);
                if (Root.panel?.focusController?.focusedElement is VisualElement f && list.Contains(f)) f.Blur();
                return;
            }
            if (!_suppressed.TryGetValue(root, out var off)) return;
            _suppressed.Remove(root);
            foreach (var e in off)
            {
                if (!_suppressCount.TryGetValue(e, out var n)) continue;
                if (n > 1) { _suppressCount[e] = n - 1; continue; }
                _suppressCount.Remove(e);
                e.focusable = true;
            }
        }

        public void Dispose() => Shutdown();

        /// <summary>Call once per frame: follows panel scale and density changes, and every
        /// <see cref="FrameEnvironment.AutoRescanSeconds"/> looks for UI added or re-tagged at run time.</summary>
        public void Update()
        {
            if (!IsRunning) return;
            if (UpdateScale())
                foreach (var b in _bindings.Values) b.Behaviour.OnLayoutChanged();
            var interval = _environment.AutoRescanSeconds;
            var now = _environment.Clock.Now;
            if (interval > 0f && now >= _nextScan)
            {
                _nextScan = now + interval;
                Rescan();
            }
        }

        void RescanAndRestyle()
        {
            if (!IsRunning) return;
            Rescan();
            foreach (var b in _bindings.Values) ApplyStyle(b); // new properties may now be registered
        }

        /// <summary>Finds tagged elements: binds new ones, rebinds re-tagged ones, drops removed ones.
        /// Cheap enough to run often: frames already bound are left alone.</summary>
        public void Rescan()
        {
            if (!IsRunning) return;
            if (_properties.Count != _registry.Properties.Count)
            {
                _properties.Clear();
                foreach (var p in _registry.Properties) _properties.Add((p, new CustomStyleProperty<string>(p.Name)));
            }

            var seen = new HashSet<VisualElement>();
            _scratch.Clear();
            Root.Query<VisualElement>().ForEach(_scratch.Add);
            foreach (var ve in _scratch)
            {
                var kind = _registry.Resolve(Wrap(ve));
                if (kind == null) continue;
                seen.Add(ve);
                if (_bindings.TryGetValue(ve, out var existing))
                {
                    if (existing.Kind == kind) { Rewatch(existing); continue; }
                    Unbind(existing);
                }
                Bind(ve, kind);
            }
            foreach (var b in new List<Binding>(_bindings.Values))
                if (!seen.Contains(b.Element)) Unbind(b);
        }

        /// <summary>The frames under <paramref name="target"/>, innermost first. Empty if the target
        /// is inside a control that owns its own drags (slider, scroller, scroll view, text field,
        /// list view, <c>.uf-no-drag</c>). <paramref name="handleOf"/> is the drawer whose handle
        /// contains the target, if any.</summary>
        public void CollectFrames(VisualElement target, List<IFrameBehaviour> into, out IFrameBehaviour handleOf)
        {
            into.Clear();
            handleOf = null;
            var inHandle = false;
            for (var ve = target; ve != null; ve = ve == Root ? null : ve.parent)
            {
                if (into.Count == 0 && IsDragOwner(ve)) { into.Clear(); handleOf = null; return; }
                if (ve.ClassListContains(FrameTags.DrawerHandle)) inHandle = true;
                if (_bindings.TryGetValue(ve, out var b))
                {
                    if (inHandle && handleOf == null) handleOf = b.Behaviour;
                    into.Add(b.Behaviour);
                }
            }
        }

        /// <summary>Controls that handle their own drags; a press inside one never drags a frame.</summary>
        public static bool IsDragOwner(VisualElement ve)
        {
            if (ve.ClassListContains(FrameTags.NoDrag)) return true;
            if (ve is Scroller || ve is ScrollView || ve is MinMaxSlider || ve is BaseVerticalCollectionView || ve is TextField) return true;
            for (var t = ve.GetType(); t != null && t != typeof(VisualElement); t = t.BaseType)
            {
                if (!t.IsGenericType) continue;
                var g = t.GetGenericTypeDefinition();
                if (g == typeof(BaseSlider<>) || g == typeof(TextInputBaseField<>)) return true;
            }
            return false;
        }

        /// <summary>The binding's frame for a VisualElement, or null.</summary>
        public IFrameBehaviour FrameOf(VisualElement ve) => ve != null && _bindings.TryGetValue(ve, out var b) ? b.Behaviour : null;

        internal ToolkitFrameElement Wrap(VisualElement ve)
        {
            if (ve == null) return null;
            if (!_wrappers.TryGetValue(ve, out var w))
            {
                w = new ToolkitFrameElement(ve, this);
                _wrappers.Add(ve, w);
            }
            return w;
        }

        void Bind(VisualElement ve, IFrameKind kind)
        {
            var behaviour = kind.Create(new FrameContext(Wrap(ve), _environment, _registry));
            if (behaviour == null) return;
            var b = new Binding { Element = ve, Kind = kind, Behaviour = behaviour };
            _bindings.Add(ve, b);
            _frames.Add(behaviour);
            ve.RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
            Rewatch(b);
            ApplyStyle(b, notify: false);
            CheckSetup(b);
            FrameBound?.Invoke(behaviour);
        }

        /// <summary>Warns (once) about frames that cannot work as set up.</summary>
        void CheckSetup(Binding b)
        {
            var name = string.IsNullOrEmpty(b.Element.name) ? b.Element.GetType().Name : b.Element.name;
            if (b.Behaviour is CardFrameBase && b.Element.Q(className: FrameTags.Cards) == null)
                Report($"UI Frames: '{name}' ({b.Kind.MarkerClass}) has no child with class uf-cards, so it has nothing to move.");
            if (b.Behaviour is DrawerFrame && b.Element.resolvedStyle.position != Position.Absolute && b.Element.panel != null &&
                !float.IsNaN(b.Element.layout.width))
                Report($"UI Frames: drawer '{name}' is not position: absolute, so it takes up space in the layout (add UiFrames.uss or set it).");
        }

        void Unbind(Binding b)
        {
            FrameUnbound?.Invoke(b.Behaviour);
            b.Element.UnregisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
            Unwatch(b);
            _bindings.Remove(b.Element);
            _frames.Remove(b.Behaviour);
            b.Behaviour.Dispose();
        }

        /// <summary>Geometry of the frame, its <c>.uf-cards</c> content and its handle drive layout.</summary>
        void Rewatch(Binding b)
        {
            var content = b.Element.Q(className: FrameTags.Cards);
            var handle = b.Element.Q(className: FrameTags.DrawerHandle);
            if (b.Watched.Count > 0 && b.Watched.Contains(b.Element) &&
                (content == null || b.Watched.Contains(content)) && (handle == null || b.Watched.Contains(handle)) &&
                b.Watched.Count == 1 + (content != null ? 1 : 0) + (handle != null ? 1 : 0))
                return;
            if (b.Watched.Count > 0) b.Behaviour.OnLayoutChanged(); // content or handle replaced
            Unwatch(b);
            Watch(b, b.Element);
            Watch(b, b.Element.Q(className: FrameTags.Cards));
            Watch(b, b.Element.Q(className: FrameTags.DrawerHandle));
        }

        void Watch(Binding b, VisualElement ve)
        {
            if (ve == null || _watchers.ContainsKey(ve)) return;
            _watchers.Add(ve, b);
            b.Watched.Add(ve);
            ve.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        void Unwatch(Binding b)
        {
            foreach (var ve in b.Watched)
            {
                ve.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _watchers.Remove(ve);
            }
            b.Watched.Clear();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.currentTarget is VisualElement ve && _watchers.TryGetValue(ve, out var b))
                b.Behaviour.OnLayoutChanged();
        }

        void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.currentTarget is VisualElement ve && _bindings.TryGetValue(ve, out var b)) ApplyStyle(b);
        }

        void ApplyStyle(Binding b, bool notify = true)
        {
            var style = new FrameStyle();
            var custom = b.Element.customStyle;
            foreach (var (property, uss) in _properties)
            {
                if (!custom.TryGetValue(uss, out var raw)) continue;
                if (!style.TryApply(property, raw, out var error)) Report($"UI Frames: '{b.Element.name}': {error}");
            }
            b.Behaviour.ApplyStyle(style);
            if (notify) FrameStyled?.Invoke(b.Behaviour);
        }

        void Report(string message)
        {
            if (!_reported.Add(message)) return;
            if (_environment.Warn != null) _environment.Warn(message);
            else Debug.LogWarning(message);
        }

        bool UpdateScale()
        {
            var pxPerUnit = 1f;
            if (Root.panel != null && Root.panel.contextType == ContextType.Player)
            {
                var a = RuntimePanelUtils.ScreenToPanel(Root.panel, Vector2.zero);
                var c = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(100f, 0f));
                var units = Mathf.Abs(c.x - a.x);
                if (units > 1e-4f) pxPerUnit = 100f / units;
            }
            var dpPerUnit = pxPerUnit / Mathf.Max(1e-4f, _environment.PixelsPerDp);
            if (Mathf.Abs(dpPerUnit - DpPerUnit) < 1e-5f) return false;
            DpPerUnit = dpPerUnit;
            return true;
        }
    }

    /// <summary>An <see cref="IFrameElement"/> over a VisualElement (dp = panel units × <see cref="ToolkitFrameBinder.DpPerUnit"/>).</summary>
    internal sealed class ToolkitFrameElement : IFrameElement
    {
        readonly VisualElement _ve;
        readonly ToolkitFrameBinder _binder;
        readonly List<IFrameElement> _children = new List<IFrameElement>();

        public ToolkitFrameElement(VisualElement ve, ToolkitFrameBinder binder)
        {
            _ve = ve;
            _binder = binder;
        }

        public string Name => string.IsNullOrEmpty(_ve.name) ? _ve.GetType().Name : _ve.name;
        public object Native => _ve;
        public IFrameElement Parent => _ve == _binder.Root ? null : _binder.Wrap(_ve.parent);

        public IReadOnlyList<IFrameElement> Children
        {
            get
            {
                _children.Clear();
                foreach (var c in _ve.Children()) _children.Add(_binder.Wrap(c));
                return _children;
            }
        }

        public FrameRect LayoutRectDp
        {
            get
            {
                var r = _ve.layout;
                var k = _binder.DpPerUnit;
                return new FrameRect(Finite(r.x) * k, Finite(r.y) * k, Finite(r.width) * k, Finite(r.height) * k);
            }
        }

        public bool HasClass(string className) => _ve.ClassListContains(className);
        public void SetClass(string className, bool enabled) => _ve.EnableInClassList(className, enabled);

        /// <summary>Along the element's own axes: its rotate and scale are applied to the offset,
        /// because UI Toolkit applies translate in the parent's space.</summary>
        public void SetTranslationDp(float x, float y)
        {
            var k = _binder.DpPerUnit;
            var v = new Vector2(x / k, y / k);
            var scale = _ve.resolvedStyle.scale.value;
            v = new Vector2(v.x * scale.x, v.y * scale.y);
            var degrees = _ve.resolvedStyle.rotate.angle.ToDegrees();
            if (Mathf.Abs(degrees) > 1e-3f)
            {
                var r = degrees * Mathf.Deg2Rad;
                float c = Mathf.Cos(r), s = Mathf.Sin(r);
                v = new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
            }
            _ve.style.translate = new Translate(v.x, v.y);
        }

        /// <summary>Screen dp (y up) → panel → this element's local space, so every scale and
        /// rotation above it is undone; the pair difference cancels the element's own position.</summary>
        public (float x, float y) ToLocalDeltaDp(float fromX, float fromY, float toX, float toY)
        {
            var k = _binder.DpPerUnit;
            var a = _ve.WorldToLocal(new Vector2(fromX / k, -fromY / k));
            var b = _ve.WorldToLocal(new Vector2(toX / k, -toY / k));
            var d = b - a;
            return (Finite(d.x) * k, Finite(d.y) * k);
        }

        public void SetVisible(bool visible)
        {
            _ve.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
            _binder.SetFocusSuppressed(_ve, !visible);
        }

        public void SetSizeDp(FrameAxis axis, float size)
        {
            var units = size / _binder.DpPerUnit;
            if (axis == FrameAxis.Horizontal) _ve.style.width = units;
            else _ve.style.height = units;
        }

        public FrameRect ScreenRectDp
        {
            get
            {
                var r = _ve.worldBound;
                var k = _binder.DpPerUnit;
                return new FrameRect(Finite(r.x) * k, Finite(r.y) * k, Finite(r.width) * k, Finite(r.height) * k);
            }
        }

        static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        public override string ToString() => Name;
    }
}
