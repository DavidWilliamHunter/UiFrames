using System;
using System.Collections.Generic;
using Threads.UiFrames.Toolkit;
using Threads.UiFrames.UGUI;
using Threads.UiFrames.Unity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Threads.UiFrames.Input
{
    /// <summary>
    /// The default wiring: one <see cref="FrameRegistry"/>, <see cref="FrameEnvironment"/> and
    /// <see cref="FrameSet"/>, binders for the listed UI Toolkit documents and uGUI roots, and the
    /// mouse and keyboard/gamepad controllers. It starts on enable and shuts down on disable.
    /// <para>To add your own frame kinds, turn off <see cref="startOnEnable"/> and call
    /// <see cref="Startup(FrameRegistry)"/> from your bootstrap with your registry
    /// (e.g. <c>FrameRegistry.WithBuiltIns().Add(new MyKind())</c>).</para>
    /// </summary>
    [AddComponentMenu("UI Frames/UI Frames Runner")]
    [DefaultExecutionOrder(-50)]
    public sealed class UiFramesRunner : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("UI Toolkit documents whose trees contain frames.")]
        public List<UIDocument> documents = new List<UIDocument>();
        [Tooltip("uGUI roots (usually Canvases) whose children contain frames.")]
        public List<RectTransform> uguiRoots = new List<RectTransform>();
        [Tooltip("Style sheets for uGUI frames, applied in order (later rules win).")]
        public List<UiFrameStyleSheet> uguiStyleSheets = new List<UiFrameStyleSheet>();
        [Tooltip("Added to each document's root: the package's UiFrames.uss defaults.")]
        public StyleSheet toolkitDefaults;
        [Tooltip("Added after the defaults: the look of the state classes (Runtime/Toolkit/UiFrames-Theme.uss). Empty = style them yourself.")]
        public StyleSheet toolkitTheme;
        [Tooltip("uGUI: give frames, cards and controls a UiFrameStateVisual with the package look when the keyboard first selects them (unless they have one).")]
        public bool uguiDefaultStateVisuals = true;

        [Header("Events (element names; the C# events pass the element)")]
        [Tooltip("Enter on a card (or frame) with nothing inside to go to.")]
        public UnityEvent<string> onActivated = new UnityEvent<string>();
        [Tooltip("The keyboard/gamepad selection moved.")]
        public UnityEvent<string> onSelectionChanged = new UnityEvent<string>();
        public UnityEvent<string> onDrawerOpened = new UnityEvent<string>();
        public UnityEvent<string> onDrawerClosed = new UnityEvent<string>();

        [Header("Input")]
        [Tooltip("Mouse and pen: drag, wheel, handle clicks.")]
        public bool pointer = true;
        [Tooltip("Keyboard, gamepad and accessibility: frames follow the UI's own focus and navigation, plus frame commands (page, home/end, toggle, close, cycle).")]
        public bool navigation = true;
        [Tooltip("Leave touch to a touch controller (TouchController) instead of the pointer controllers.")]
        public bool pointerIgnoresTouch;
        [Tooltip("Optional: an action map with the FrameNavigationController action names. Empty = built-in defaults.")]
        public InputActionReference navigationMap;
        [Tooltip("The EventSystem whose selection uGUI frames follow. Empty = the current EventSystem at startup.")]
        public EventSystem eventSystem;

        [Header("Lifetime")]
        public bool startOnEnable = true;

        readonly List<ToolkitFrameBinder> _toolkit = new List<ToolkitFrameBinder>();
        readonly List<ToolkitPointerController> _toolkitPointers = new List<ToolkitPointerController>();
        readonly List<UguiFrameBinder> _ugui = new List<UguiFrameBinder>();
        readonly List<UguiPointerController> _uguiPointers = new List<UguiPointerController>();
        readonly List<UIDocument> _pendingDocuments = new List<UIDocument>();
        readonly List<ToolkitFocusController> _toolkitFocus = new List<ToolkitFocusController>();
        readonly List<UguiFocusController> _uguiFocus = new List<UguiFocusController>();
        readonly List<FrameNavigator> _navigators = new List<FrameNavigator>();
        readonly Dictionary<DrawerFrame, Action<bool>> _drawerHandlers = new Dictionary<DrawerFrame, Action<bool>>();
        FrameNavigationController _navigation;

        /// <summary>Enter on something with nothing inside to go to.</summary>
        public event Action<IFrameElement> Activated;
        /// <summary>The keyboard/gamepad selection of a UI moved (null: cleared).</summary>
        public event Action<IFrameElement> SelectionChanged;
        /// <summary>A drawer opened (true) or closed (false).</summary>
        public event Action<DrawerFrame, bool> DrawerChanged;

        public FrameRegistry Registry { get; private set; }
        public FrameEnvironment Environment { get; private set; }
        public FrameSet Frames { get; private set; }
        public FrameNavigationController Navigation => _navigation;
        /// <summary>One keyboard/gamepad navigator per UIDocument and uGUI root.</summary>
        public IReadOnlyList<FrameNavigator> Navigators => _navigators;
        public bool IsRunning { get; private set; }

        void OnEnable()
        {
            if (startOnEnable) Startup();
        }

        void OnDisable() => Shutdown();

        /// <summary>Starts everything. Pass your own registry to add or override frame kinds.</summary>
        public void Startup(FrameRegistry registry = null)
        {
            if (IsRunning) return;
            IsRunning = true;
            Registry = registry ?? FrameRegistry.WithBuiltIns();
            Environment = UnityFrameHost.CreateEnvironment();
            Frames = new FrameSet();
            Frames.Added += OnFrameAdded;
            Frames.Removed += OnFrameRemoved;

            if (navigation)
            {
                _navigation = new FrameNavigationController(Frames, navigationMap ? navigationMap.action.actionMap : null);
                _navigation.Startup();
            }

            _pendingDocuments.Clear();
            foreach (var d in documents) if (d) _pendingDocuments.Add(d);
            BindPendingDocuments();

            var es = eventSystem ? eventSystem : EventSystem.current;

            foreach (var root in uguiRoots)
            {
                if (!root) continue;
                var binder = new UguiFrameBinder(root, Registry, Environment, Frames, uguiStyleSheets)
                {
                    DefaultStateVisuals = uguiDefaultStateVisuals,
                };
                _ugui.Add(binder);
                binder.Startup();
                if (pointer)
                {
                    var p = new UguiPointerController(binder) { IgnoreTouch = pointerIgnoresTouch };
                    _uguiPointers.Add(p);
                    p.Startup();
                }
                if (navigation)
                {
                    if (!es)
                    {
                        Debug.LogWarning($"UI Frames: no EventSystem, so frames under '{root.name}' cannot follow keyboard/gamepad selection.", this);
                        continue;
                    }
                    var focus = new UguiFocusController(binder, es);
                    _uguiFocus.Add(focus);
                    focus.Startup();
                    AddNavigator(focus, providesTab: false);
                }
            }
        }

        void AddNavigator(IFrameNavigationHost host, bool providesTab)
        {
            var navigator = new FrameNavigator(Frames, host);
            _navigators.Add(navigator);
            navigator.Activated += e =>
            {
                Activated?.Invoke(e);
                onActivated?.Invoke(e.Name);
            };
            navigator.SelectionChanged += () =>
            {
                SelectionChanged?.Invoke(navigator.Selected);
                onSelectionChanged?.Invoke(navigator.Selected?.Name);
            };
            navigator.Startup();
            _navigation?.Navigators.Add(navigator);
            if (!providesTab) _navigation?.TabNavigators.Add(navigator);
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _navigation?.Dispose();
            _navigation = null;
            foreach (var kv in _drawerHandlers) kv.Key.Model.OpenChanged -= kv.Value;
            _drawerHandlers.Clear();
            if (Frames != null)
            {
                Frames.Added -= OnFrameAdded;
                Frames.Removed -= OnFrameRemoved;
            }
            foreach (var n in _navigators) n.Shutdown();
            foreach (var f in _toolkitFocus) f.Shutdown();
            foreach (var f in _uguiFocus) f.Shutdown();
            _navigators.Clear();
            _toolkitFocus.Clear();
            _uguiFocus.Clear();
            foreach (var p in _toolkitPointers) p.Shutdown();
            foreach (var p in _uguiPointers) p.Shutdown();
            foreach (var b in _toolkit) b.Shutdown();
            foreach (var b in _ugui) b.Shutdown();
            _toolkitPointers.Clear();
            _uguiPointers.Clear();
            _toolkit.Clear();
            _ugui.Clear();
            _pendingDocuments.Clear();
        }

        void OnFrameAdded(IFrameBehaviour frame)
        {
            if (!(frame is DrawerFrame drawer) || _drawerHandlers.ContainsKey(drawer)) return;
            Action<bool> handler = open =>
            {
                DrawerChanged?.Invoke(drawer, open);
                (open ? onDrawerOpened : onDrawerClosed)?.Invoke(drawer.Element.Name);
            };
            _drawerHandlers.Add(drawer, handler);
            drawer.Model.OpenChanged += handler;
        }

        void OnFrameRemoved(IFrameBehaviour frame)
        {
            if (!(frame is DrawerFrame drawer) || !_drawerHandlers.TryGetValue(drawer, out var handler)) return;
            drawer.Model.OpenChanged -= handler;
            _drawerHandlers.Remove(drawer);
        }

        /// <summary>Looks for UI added or re-tagged at run time now, rather than at the next periodic
        /// scan (<see cref="FrameEnvironment.AutoRescanSeconds"/>).</summary>
        public void Rescan()
        {
            foreach (var b in _toolkit) b.Rescan();
            foreach (var b in _ugui) b.Rescan();
            foreach (var f in _uguiFocus) f.Refresh();
        }

        void Update()
        {
            if (!IsRunning) return;
            if (_pendingDocuments.Count > 0) BindPendingDocuments();
            foreach (var b in _toolkit) b.Update();
            foreach (var b in _ugui) b.Update();
            Frames.Update();
            foreach (var f in _uguiFocus) f.Update();
            foreach (var n in _navigators) n.Update();
            _navigation?.Update(Environment.Clock.Now);
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus && Environment != null) UnityFrameHost.Refresh(Environment);
        }

        /// <summary>A UIDocument builds its tree in its own OnEnable, which may run after ours.</summary>
        void BindPendingDocuments()
        {
            for (var i = _pendingDocuments.Count - 1; i >= 0; i--)
            {
                var doc = _pendingDocuments[i];
                if (!doc) { _pendingDocuments.RemoveAt(i); continue; }
                var root = doc.rootVisualElement;
                if (root == null || root.panel == null) continue;
                _pendingDocuments.RemoveAt(i);
                if (toolkitDefaults && !root.styleSheets.Contains(toolkitDefaults)) root.styleSheets.Add(toolkitDefaults);
                if (toolkitTheme && !root.styleSheets.Contains(toolkitTheme)) root.styleSheets.Add(toolkitTheme);
                var binder = new ToolkitFrameBinder(root, Registry, Environment, Frames);
                _toolkit.Add(binder);
                binder.Startup();
                if (pointer)
                {
                    var p = new ToolkitPointerController(binder, Environment) { IgnoreTouch = pointerIgnoresTouch };
                    _toolkitPointers.Add(p);
                    p.Startup();
                }
                if (navigation)
                {
                    var focus = new ToolkitFocusController(binder) { KeysFromController = _navigation != null };
                    _toolkitFocus.Add(focus);
                    focus.Startup();
                    AddNavigator(focus, providesTab: true);
                    if (_navigation != null) _navigation.KeyNavigators.Add(_navigators[_navigators.Count - 1]);
                }
            }
        }
    }
}
