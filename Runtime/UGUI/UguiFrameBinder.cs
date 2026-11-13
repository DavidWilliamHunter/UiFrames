using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Threads.UiFrames.UGUI
{
    /// <summary>
    /// Binds frames under one uGUI root (usually a Canvas): finds <see cref="UiFrameClasses"/> with a
    /// registered marker class, creates their behaviours, resolves their style from
    /// <see cref="UiFrameStyleSheet"/>s plus inline declarations, and watches their geometry each
    /// <see cref="Update"/>. Adds a <see cref="UguiFramePointer"/> to each frame for mouse input when
    /// a <see cref="UguiPointerController"/> is attached. One explicit Startup and Shutdown.
    /// </summary>
    public sealed class UguiFrameBinder : IDisposable
    {
        sealed class Binding
        {
            public UiFrameClasses Classes;
            public IFrameKind Kind;
            public IFrameBehaviour Behaviour;
            public UguiFramePointer Pointer;
            public int LayoutSignature;
        }

        readonly FrameRegistry _registry;
        readonly FrameEnvironment _environment;
        readonly FrameSet _frames;
        readonly List<UiFrameStyleSheet> _sheets;
        readonly Dictionary<GameObject, Binding> _bindings = new Dictionary<GameObject, Binding>();
        readonly Dictionary<Transform, UguiFrameElement> _wrappers = new Dictionary<Transform, UguiFrameElement>();
        readonly HashSet<string> _reported = new HashSet<string>();
        readonly List<UiFrameClasses> _scratch = new List<UiFrameClasses>();
        readonly Dictionary<UiFrameClasses, Action> _subscribed = new Dictionary<UiFrameClasses, Action>();
        UguiPointerController _pointer;
        bool _rescanPending;
        double _nextScan;
        readonly HashSet<UiFrameClasses> _restyle = new HashSet<UiFrameClasses>();

        public UguiFrameBinder(RectTransform root, FrameRegistry registry, FrameEnvironment environment, FrameSet frames,
            IEnumerable<UiFrameStyleSheet> styleSheets = null)
        {
            Root = root ? root : throw new ArgumentNullException(nameof(root));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _sheets = new List<UiFrameStyleSheet>();
            if (styleSheets != null) foreach (var s in styleSheets) if (s) _sheets.Add(s);
        }

        public RectTransform Root { get; }
        public FrameSet Frames => _frames;
        public FrameRegistry Registry => _registry;

        /// <summary>A frame was created for a GameObject (after its first style).</summary>
        public event Action<IFrameBehaviour> FrameBound;
        /// <summary>A frame's custom properties were applied again.</summary>
        public event Action<IFrameBehaviour> FrameStyled;
        /// <summary>A frame is about to be disposed.</summary>
        public event Action<IFrameBehaviour> FrameUnbound;
        public FrameEnvironment Environment => _environment;
        public bool IsRunning { get; private set; }
        /// <summary>dp per canvas unit (canvas scale and screen density).</summary>
        public float DpPerUnit { get; private set; } = 1f;
        /// <summary>Give elements that get a navigation state class (uf-selected, uf-entered,
        /// uf-editing) a <see cref="UiFrameStateVisual"/> with the package look, unless they have one.</summary>
        public bool DefaultStateVisuals { get; set; } = true;

        /// <summary>The mouse controller for this root, set by <see cref="UguiPointerController"/>.</summary>
        public UguiPointerController Pointer
        {
            get => _pointer;
            internal set
            {
                _pointer = value;
                foreach (var b in _bindings.Values) AttachPointer(b);
            }
        }

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            _registry.Changed += OnRegistryChanged;
            foreach (var s in _sheets) s.Changed += OnSheetChanged;
            UpdateScale();
            Rescan();
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _registry.Changed -= OnRegistryChanged;
            foreach (var s in _sheets) if (s) s.Changed -= OnSheetChanged;
            foreach (var kv in _subscribed) if (kv.Key) kv.Key.Changed -= kv.Value;
            _subscribed.Clear();
            foreach (var b in new List<Binding>(_bindings.Values)) Unbind(b);
            _wrappers.Clear();
        }

        public void Dispose() => Shutdown();

        /// <summary>Call once per frame: applies pending rescans, looks for UI added at run time every
        /// <see cref="FrameEnvironment.AutoRescanSeconds"/>, and notices layout changes.</summary>
        public void Update()
        {
            if (!IsRunning) return;
            var interval = _environment.AutoRescanSeconds;
            var now = _environment.Clock.Now;
            if (interval > 0f && now >= _nextScan) { _nextScan = now + interval; _rescanPending = true; }
            if (_rescanPending) Rescan();
            if (_restyle.Count > 0)
            {
                foreach (var b in _bindings.Values)
                    if (b.Classes && _restyle.Contains(b.Classes)) ApplyStyle(b);
                _restyle.Clear();
            }
            var scaleChanged = UpdateScale();
            foreach (var b in _bindings.Values)
            {
                if (!b.Classes) { _rescanPending = true; continue; }
                var sig = Signature(b.Classes.transform);
                if (!scaleChanged && sig == b.LayoutSignature) continue;
                b.LayoutSignature = sig;
                b.Behaviour.OnLayoutChanged();
            }
        }

        public void RequestRescan() => _rescanPending = true;

        void OnClassesChanged(UiFrameClasses classes)
        {
            if (!IsRunning) return;
            _rescanPending = true;     // a marker class may have been added or removed
            _restyle.Add(classes);     // and rules or inline properties may now differ
        }

        /// <summary>Finds tagged GameObjects: binds new ones, rebinds re-tagged ones, drops removed ones.</summary>
        public void Rescan()
        {
            if (!IsRunning) return;
            _rescanPending = false;
            _scratch.Clear();
            Root.GetComponentsInChildren(false, _scratch);
            var seen = new HashSet<GameObject>();
            foreach (var classes in _scratch)
            {
                if (!_subscribed.ContainsKey(classes))
                {
                    Action handler = () => OnClassesChanged(classes);
                    _subscribed.Add(classes, handler);
                    classes.Changed += handler;
                }
                var kind = _registry.Resolve(Wrap(classes.transform));
                if (kind == null) continue;
                var go = classes.gameObject;
                seen.Add(go);
                if (_bindings.TryGetValue(go, out var existing))
                {
                    if (existing.Kind == kind) continue;
                    Unbind(existing);
                }
                Bind(classes, kind);
            }
            foreach (var b in new List<Binding>(_bindings.Values))
                if (!b.Classes || !seen.Contains(b.Classes.gameObject)) Unbind(b);
        }

        /// <summary>The frames from <paramref name="target"/> up to the root, innermost first.
        /// <paramref name="handleOf"/> is the drawer whose handle contains the target, if any.</summary>
        public void CollectFrames(Transform target, List<IFrameBehaviour> into, out IFrameBehaviour handleOf)
        {
            into.Clear();
            handleOf = null;
            var inHandle = false;
            for (var t = target; t != null; t = t == Root ? null : t.parent)
            {
                var classes = t.GetComponent<UiFrameClasses>();
                // .uf-no-drag inside a frame: no frame drags from here (controls with their own
                // drags need no tag in uGUI: the EventSystem sends them the drag first).
                if (into.Count == 0 && classes && classes.HasClass(FrameTags.NoDrag)) { handleOf = null; return; }
                if (classes && classes.HasClass(FrameTags.DrawerHandle)) inHandle = true;
                if (_bindings.TryGetValue(t.gameObject, out var b))
                {
                    if (inHandle && handleOf == null) handleOf = b.Behaviour;
                    into.Add(b.Behaviour);
                }
            }
        }

        public IFrameBehaviour FrameOf(GameObject go) => go && _bindings.TryGetValue(go, out var b) ? b.Behaviour : null;

        internal UguiFrameElement Wrap(Transform t)
        {
            if (!(t is RectTransform rt)) return null;
            if (!_wrappers.TryGetValue(t, out var w))
            {
                w = new UguiFrameElement(rt, this);
                _wrappers.Add(t, w);
            }
            return w;
        }

        internal void Report(string message)
        {
            if (!_reported.Add(message)) return;
            if (_environment.Warn != null) _environment.Warn("UI Frames: " + message);
            else Debug.LogWarning("UI Frames: " + message);
        }

        /// <summary>Warns (once) about frames that cannot work as set up.</summary>
        void CheckSetup(Binding b)
        {
            var go = b.Classes.gameObject;
            var name = go.name;
            if (b.Behaviour is CardFrameBase card)
            {
                if (card.Content == null)
                    Report($"'{name}' ({b.Kind.MarkerClass}) has no child with class uf-cards, so it has nothing to move.");
                if (!go.GetComponent<RectMask2D>() && !go.GetComponent<Mask>())
                    Report($"'{name}' has no RectMask2D, so cards outside it stay visible.");
                if (go.GetComponent<LayoutGroup>())
                    Report($"'{name}' has a LayoutGroup that positions its content and fights the frame; put the layout group on the uf-cards child.");
            }
            if ((b.Behaviour is CardFrameBase || b.Behaviour is DrawerFrame) && !HasRaycastTarget(go))
                Report($"'{name}' has no raycast target (e.g. an Image, alpha 0 is fine), so drags on its empty space do not reach it.");
        }

        static bool HasRaycastTarget(GameObject go)
        {
            var g = go.GetComponent<Graphic>();
            return g && g.raycastTarget;
        }

        void Bind(UiFrameClasses classes, IFrameKind kind)
        {
            var behaviour = kind.Create(new FrameContext(Wrap(classes.transform), _environment, _registry));
            if (behaviour == null) return;
            var b = new Binding { Classes = classes, Kind = kind, Behaviour = behaviour };
            _bindings.Add(classes.gameObject, b);
            _frames.Add(behaviour);
            ApplyStyle(b, notify: false);
            b.LayoutSignature = Signature(classes.transform);
            AttachPointer(b);
            CheckSetup(b);
            FrameBound?.Invoke(behaviour);
        }

        void Unbind(Binding b)
        {
            FrameUnbound?.Invoke(b.Behaviour);
            _bindings.Remove(b.Classes ? b.Classes.gameObject : FindKey(b));
            _frames.Remove(b.Behaviour);
            b.Behaviour.Dispose();
            if (b.Pointer) UnityEngine.Object.Destroy(b.Pointer);
        }

        GameObject FindKey(Binding b)
        {
            foreach (var kv in _bindings) if (kv.Value == b) return kv.Key;
            return null;
        }

        void AttachPointer(Binding b)
        {
            if (_pointer == null || !b.Classes) return;
            if (!b.Pointer) b.Pointer = b.Classes.gameObject.AddComponent<UguiFramePointer>();
            b.Pointer.Init(_pointer, b.Behaviour);
        }

        void OnRegistryChanged()
        {
            _rescanPending = true;
            foreach (var b in _bindings.Values) if (b.Classes) _restyle.Add(b.Classes);
        }

        void OnSheetChanged()
        {
            foreach (var b in _bindings.Values) ApplyStyle(b);
        }

        void ApplyStyle(Binding b, bool notify = true)
        {
            var style = new FrameStyle();
            Func<string, bool> has = b.Classes.HasClass;
            foreach (var sheet in _sheets) if (sheet) sheet.Apply(_registry, has, style, Report);
            foreach (var d in b.Classes.InlineStyle)
            {
                if (string.IsNullOrWhiteSpace(d.property)) continue;
                if (!_registry.TryApply(style, d.property, d.value, out var error)) Report($"'{b.Classes.name}': {error}");
            }
            b.Behaviour.ApplyStyle(style);
            if (notify) FrameStyled?.Invoke(b.Behaviour);
        }

        /// <summary>Changes in the laid-out rects of the frame, its children (content, handle) and
        /// grandchildren (cards). Uses layout rects, which exclude the translation frames apply, so
        /// scrolling and sliding are not mistaken for layout changes.</summary>
        int Signature(Transform frame)
        {
            unchecked
            {
                var e = Wrap(frame);
                var h = 17 * 31 + RectHash(e.LayoutRectDp);
                foreach (var child in e.Children)
                {
                    h = h * 31 + RectHash(child.LayoutRectDp);
                    foreach (var grand in child.Children) h = h * 31 + RectHash(grand.LayoutRectDp);
                }
                return h;
            }
        }

        static int RectHash(FrameRect r)
        {
            unchecked
            {
                return (((Mathf.RoundToInt(r.Width * 10f) * 397) ^ Mathf.RoundToInt(r.Height * 10f)) * 397
                        ^ Mathf.RoundToInt(r.X * 10f)) * 397 ^ Mathf.RoundToInt(r.Y * 10f);
            }
        }

        bool UpdateScale()
        {
            var canvas = Root.GetComponentInParent<Canvas>();
            var scale = canvas ? canvas.rootCanvas.scaleFactor : 1f;
            var dpPerUnit = scale / Mathf.Max(1e-4f, _environment.PixelsPerDp);
            if (Mathf.Abs(dpPerUnit - DpPerUnit) < 1e-5f) return false;
            DpPerUnit = dpPerUnit;
            return true;
        }
    }

    /// <summary>
    /// An <see cref="IFrameElement"/> over a RectTransform. Pointer movement is mapped into the
    /// element's own space through its canvas's camera, so scaled, rotated and world-space UI
    /// follows 1:1; translation is added to <c>anchoredPosition</c> along the element's own axes;
    /// hiding uses a CanvasGroup (which also makes Selectables inside non-interactable).
    /// </summary>
    internal sealed class UguiFrameElement : IFrameElement
    {
        readonly RectTransform _rt;
        readonly UguiFrameBinder _binder;
        readonly List<IFrameElement> _children = new List<IFrameElement>();
        Vector2 _applied; // canvas units currently added to anchoredPosition
        UiFrameClasses _classes;
        CanvasGroup _group;
        bool _hasVisual;

        public UguiFrameElement(RectTransform rt, UguiFrameBinder binder)
        {
            _rt = rt;
            _binder = binder;
        }

        public string Name => _rt ? _rt.name : "(destroyed)";
        public object Native => _rt ? _rt.gameObject : null;
        public IFrameElement Parent => !_rt || _rt == _binder.Root ? null : _binder.Wrap(_rt.parent);

        public IReadOnlyList<IFrameElement> Children
        {
            get
            {
                _children.Clear();
                if (!_rt) return _children;
                foreach (Transform c in _rt)
                    if (c is RectTransform && c.gameObject.activeSelf) _children.Add(_binder.Wrap(c));
                return _children;
            }
        }

        public FrameRect LayoutRectDp
        {
            get
            {
                if (!_rt) return default;
                var k = _binder.DpPerUnit;
                var r = _rt.rect;
                var lp = (Vector2)_rt.localPosition - _applied;
                var parent = _rt.parent as RectTransform;
                float x, y;
                if (parent && _rt != _binder.Root)
                {
                    var pr = parent.rect;
                    x = lp.x + r.xMin - pr.xMin;
                    y = pr.yMax - (lp.y + r.yMax);
                }
                else { x = 0f; y = 0f; }
                return new FrameRect(x * k, y * k, r.width * k, r.height * k);
            }
        }

        UiFrameClasses Classes => _classes ? _classes : _classes = _rt ? _rt.GetComponent<UiFrameClasses>() : null;

        public bool HasClass(string className)
        {
            var c = Classes;
            return c && c.HasClass(className);
        }

        public void SetClass(string className, bool enabled)
        {
            if (!_rt) return;
            var c = Classes;
            if (!c)
            {
                if (!enabled) return;
                c = _classes = _rt.gameObject.AddComponent<UiFrameClasses>();
            }
            if (enabled && _binder.DefaultStateVisuals && !_hasVisual &&
                (className == FrameTags.Selected || className == FrameTags.Entered || className == FrameTags.Editing))
            {
                _hasVisual = true;
                if (!_rt.GetComponent<UiFrameStateVisual>())
                {
                    var v = _rt.gameObject.AddComponent<UiFrameStateVisual>();
                    v.entries.Clear();
                    v.AddDefaults();
                }
            }
            c.SetClass(className, enabled);
        }

        public void SetTranslationDp(float x, float y)
        {
            if (!_rt) return;
            var k = _binder.DpPerUnit;
            var local = new Vector3(x / k, -y / k, 0f); // layout space is y down; uGUI is y up
            // anchoredPosition is in the parent's space: turn the offset by this element's own rotation and scale.
            var parentSpace = (Vector2)(_rt.localRotation * Vector3.Scale(_rt.localScale, local));
            _rt.anchoredPosition += parentSpace - _applied;
            _applied = parentSpace;
        }

        /// <summary>Through a LayoutElement's preferred size when the parent lays the element out,
        /// else the RectTransform's size.</summary>
        public void SetSizeDp(FrameAxis axis, float size)
        {
            if (!_rt) return;
            var units = size / _binder.DpPerUnit;
            var le = _rt.GetComponent<LayoutElement>();
            var laidOut = _rt.parent && _rt.parent.GetComponent<LayoutGroup>();
            if (laidOut || le)
            {
                if (!le) le = _rt.gameObject.AddComponent<LayoutElement>();
                if (axis == FrameAxis.Horizontal) le.preferredWidth = units;
                else le.preferredHeight = units;
                if (laidOut) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_rt.parent);
            }
            if (!laidOut)
                _rt.SetSizeWithCurrentAnchors(axis == FrameAxis.Horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical, units);
        }

        static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>The drawn corners projected to the screen (dp; y negated so it runs down).</summary>
        public FrameRect ScreenRectDp
        {
            get
            {
                if (!_rt) return default;
                var cam = EventCamera();
                var pxPerDp = Mathf.Max(1e-4f, _binder.Environment.PixelsPerDp);
                _rt.GetWorldCorners(s_corners);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var c in s_corners)
                {
                    var p = RectTransformUtility.WorldToScreenPoint(cam, c) / pxPerDp;
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, -p.y); maxY = Mathf.Max(maxY, -p.y);
                }
                return new FrameRect(minX, minY, maxX - minX, maxY - minY);
            }
        }

        Camera EventCamera()
        {
            var canvas = _rt.GetComponentInParent<Canvas>();
            var root = canvas ? canvas.rootCanvas : null;
            return root && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        }

        public (float x, float y) ToLocalDeltaDp(float fromX, float fromY, float toX, float toY)
        {
            if (!_rt) return (0f, 0f);
            var pxPerDp = Mathf.Max(1e-4f, _binder.Environment.PixelsPerDp);
            var cam = EventCamera();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, new Vector2(fromX, fromY) * pxPerDp, cam, out var a) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, new Vector2(toX, toY) * pxPerDp, cam, out var b))
                return (0f, 0f);
            var k = _binder.DpPerUnit;
            var d = b - a;
            return (d.x * k, -d.y * k); // y up → y down
        }

        public void SetVisible(bool visible)
        {
            if (!_rt) return;
            if (!_group)
            {
                if (visible) return;
                _group = _rt.GetComponent<CanvasGroup>();
                if (!_group) _group = _rt.gameObject.AddComponent<CanvasGroup>();
            }
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
        }

        public override string ToString() => Name;
    }
}
