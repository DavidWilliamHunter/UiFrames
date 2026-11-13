using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Threads.UiFrames.UGUI
{
    /// <summary>
    /// uGUI keyboard/gamepad navigation for frames: the <see cref="IFrameFocusHost"/> of a
    /// <see cref="FrameNavigator"/>, through the EventSystem's selection.
    /// <list type="bullet">
    /// <item>Every Selectable under the root gets a <see cref="UguiNavItem"/>, which hands the
    /// EventSystem's Move, Submit and Cancel to the navigator, and its own automatic navigation
    /// is turned off (restored on shutdown), so the UI does not also move the selection.</item>
    /// <item>Frames and cards the keyboard selects get a <see cref="UguiNavProxy"/> so they can be selected.</item>
    /// <item>A slider keeps its arrows off until Enter starts adjusting it; an InputField does not
    /// start editing on selection, only on Enter.</item>
    /// <item>A drawer handle without a Selectable gets a <see cref="UguiDrawerHandle"/> so a click toggles it.</item>
    /// </list>
    /// uGUI has no Tab: <c>FrameNavigationController</c> sends Tab to the navigator. Call
    /// <see cref="Update"/> once per frame (uGUI has no selection-changed event).
    /// </summary>
    public sealed class UguiFocusController : IFrameNavigationHost, IDisposable
    {
        readonly UguiFrameBinder _binder;
        readonly EventSystem _eventSystem;
        readonly List<Component> _added = new List<Component>();
        readonly Dictionary<Selectable, Navigation> _originalNavigation = new Dictionary<Selectable, Navigation>();
        readonly Dictionary<InputField, bool> _originalActivate = new Dictionary<InputField, bool>();
        readonly Dictionary<Button, UnityEngine.Events.UnityAction> _hooked = new Dictionary<Button, UnityEngine.Events.UnityAction>();
        readonly List<Selectable> _scratch = new List<Selectable>();
        GameObject _lastSelected;
        double _nextRefresh;

        public UguiFocusController(UguiFrameBinder binder, EventSystem eventSystem)
        {
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _eventSystem = eventSystem ? eventSystem : throw new ArgumentNullException(nameof(eventSystem));
        }

        public bool IsRunning { get; private set; }
        public FrameNavigator Navigator { get; set; }
        public event Action<IFrameElement> FocusChanged;
        public EventSystem EventSystem => _eventSystem;
        public IFrameElement Root => _binder.Wrap(_binder.Root);

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            _binder.FrameBound += OnFrameChanged;
            _binder.FrameStyled += OnFrameChanged;
            _binder.FrameUnbound += OnFrameUnbound;
            Refresh();
            _lastSelected = _eventSystem.currentSelectedGameObject;
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _binder.FrameBound -= OnFrameChanged;
            _binder.FrameStyled -= OnFrameChanged;
            _binder.FrameUnbound -= OnFrameUnbound;
            foreach (var kv in _originalNavigation) if (kv.Key) kv.Key.navigation = kv.Value;
            _originalNavigation.Clear();
            foreach (var kv in _originalActivate) if (kv.Key) kv.Key.shouldActivateOnSelect = kv.Value;
            _originalActivate.Clear();
            foreach (var c in _added) if (c) UnityEngine.Object.Destroy(c);
            _added.Clear();
            foreach (var kv in _hooked) if (kv.Key) kv.Key.onClick.RemoveListener(kv.Value);
            _hooked.Clear();
        }

        public void Dispose() => Shutdown();

        /// <summary>Takes over Selectables added since startup (call after building UI at run time).</summary>
        public void Refresh()
        {
            if (!IsRunning) return;
            _scratch.Clear();
            _binder.Root.GetComponentsInChildren(true, _scratch);
            foreach (var s in _scratch) TakeOver(s);
            foreach (var f in _binder.Frames.Frames)
                if (Owns(f.Element)) HookHandle(f);
        }

        /// <summary>Notices selection changes (call once per frame).</summary>
        public void Update()
        {
            if (!IsRunning || !_eventSystem) return;
            var interval = _binder.Environment.AutoRescanSeconds;
            var now = _binder.Environment.Clock.Now;
            if (interval > 0f && now >= _nextRefresh)
            {
                _nextRefresh = now + interval;
                Refresh(); // Selectables added at run time
            }
            var selected = _eventSystem.currentSelectedGameObject;
            if (selected == _lastSelected) return;
            _lastSelected = selected;
            if (selected && selected.transform.IsChildOf(_binder.Root)) FocusChanged?.Invoke(_binder.Wrap(selected.transform));
        }

        // ---- IFrameFocusHost ----

        public IFrameElement Focused
        {
            get
            {
                var go = _eventSystem ? _eventSystem.currentSelectedGameObject : null;
                return go && go.transform.IsChildOf(_binder.Root) ? _binder.Wrap(go.transform) : null;
            }
        }

        public bool Owns(IFrameElement element) =>
            element?.Native is GameObject go && go && go.transform.IsChildOf(_binder.Root);

        public bool IsControl(IFrameElement element)
        {
            if (!(element?.Native is GameObject go) || !go || !go.activeInHierarchy) return false;
            var s = go.GetComponent<Selectable>();
            return s && !(s is UguiNavProxy) && !(s is UguiDrawerHandle) && s.enabled && s.interactable;
        }

        public FrameControlArrows ArrowUse(IFrameElement control)
        {
            if (!(control?.Native is GameObject go) || !go) return FrameControlArrows.None;
            var s = go.GetComponent<Selectable>();
            if (s is Slider || s is Scrollbar) return FrameControlArrows.Adjust;
            if (s is InputField || (s && s.GetType().Name.Contains("InputField"))) return FrameControlArrows.Text;
            return FrameControlArrows.None;
        }

        public void EnsureFocusable(IFrameElement element)
        {
            if (!(element?.Native is GameObject go) || !go || go.GetComponent<Selectable>()) return;
            var proxy = go.AddComponent<UguiNavProxy>();
            _added.Add(proxy);
            TakeOver(proxy);
        }

        public void SetEditing(IFrameElement control, bool editing)
        {
            if (!(control?.Native is GameObject go) || !go) return;
            var s = go.GetComponent<Selectable>();
            if (!s) return;
            if (s is InputField field)
            {
                if (editing) field.ActivateInputField();
                else field.DeactivateInputField();
                return;
            }
            // A slider adjusts on Move only when it has nowhere to navigate to.
            s.navigation = editing ? new Navigation { mode = Navigation.Mode.None } : SelfNavigation(s);
        }

        public bool CanFocus(IFrameElement element) =>
            element?.Native is GameObject go && go && CanSelect(go.GetComponent<Selectable>());

        public bool Focus(IFrameElement element)
        {
            if (!(element?.Native is GameObject go) || !go || !CanSelect(go.GetComponent<Selectable>())) return false;
            if (_eventSystem.currentSelectedGameObject != go) _eventSystem.SetSelectedGameObject(go);
            Update(); // report at once rather than next frame
            return _eventSystem.currentSelectedGameObject == go;
        }

        public IFrameElement FirstFocusable(IFrameElement container, Func<IFrameElement, bool> filter = null)
        {
            if (!(container?.Native is GameObject root) || !root) return null;
            _scratch.Clear();
            root.GetComponentsInChildren(false, _scratch);
            foreach (var s in _scratch)
            {
                if (s.gameObject == root || !CanSelect(s)) continue;
                var w = _binder.Wrap(s.transform);
                if (filter == null || filter(w)) return w;
            }
            return null;
        }

        static bool CanSelect(Selectable s) => s && s.isActiveAndEnabled && s.IsInteractable();

        // ---- Taking over Selectables ----

        void TakeOver(Selectable s)
        {
            if (!s || _originalNavigation.ContainsKey(s)) return;
            _originalNavigation.Add(s, s.navigation);
            s.navigation = s is Slider || s is Scrollbar ? SelfNavigation(s) : new Navigation { mode = Navigation.Mode.None };
            if (s is InputField field && !_originalActivate.ContainsKey(field))
            {
                _originalActivate.Add(field, field.shouldActivateOnSelect);
                field.shouldActivateOnSelect = false;
            }
            if (!s.GetComponent<UguiNavItem>())
            {
                var item = s.gameObject.AddComponent<UguiNavItem>();
                item.Init(this);
                _added.Add(item);
            }
        }

        /// <summary>Navigation that always lands on itself: Move is "used" without leaving, so a
        /// slider does not adjust, and the navigator decides where to go.</summary>
        static Navigation SelfNavigation(Selectable s) => new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = s, selectOnRight = s, selectOnUp = s, selectOnDown = s,
        };

        void OnFrameChanged(IFrameBehaviour frame)
        {
            if (!Owns(frame.Element)) return;
            if (frame.Element.Native is GameObject go && go)
            {
                _scratch.Clear();
                go.GetComponentsInChildren(true, _scratch);
                foreach (var s in _scratch) TakeOver(s);
            }
            HookHandle(frame);
        }

        void HookHandle(IFrameBehaviour frame)
        {
            if (!(frame is DrawerFrame drawer) || !(drawer.Handle?.Native is GameObject handle) || !handle) return;
            var button = handle.GetComponent<Button>();
            if (button)
            {
                if (_hooked.ContainsKey(button)) return;
                UnityEngine.Events.UnityAction toggle = () => { if (drawer.Style.Accepts(FrameInputKinds.Pointer)) drawer.Execute(FrameCommand.Toggle); };
                button.onClick.AddListener(toggle);
                _hooked.Add(button, toggle);
            }
            else if (!handle.GetComponent<Selectable>())
            {
                var h = handle.AddComponent<UguiDrawerHandle>();
                h.Init(drawer);
                _added.Add(h);
                TakeOver(h);
            }
        }

        void OnFrameUnbound(IFrameBehaviour frame)
        {
            if (!(frame is DrawerFrame drawer) || !(drawer.Handle?.Native is GameObject handle) || !handle) return;
            var button = handle.GetComponent<Button>();
            if (button && _hooked.TryGetValue(button, out var toggle))
            {
                button.onClick.RemoveListener(toggle);
                _hooked.Remove(button);
            }
        }
    }

    /// <summary>Hands the EventSystem's Move, Submit and Cancel on a selected object to the
    /// <see cref="FrameNavigator"/>. Added at run time by <see cref="UguiFocusController"/>.</summary>
    [AddComponentMenu("")]
    public sealed class UguiNavItem : MonoBehaviour, IMoveHandler, ISubmitHandler, ICancelHandler
    {
        UguiFocusController _host;

        internal void Init(UguiFocusController host) => _host = host;

        FrameNavigator Navigator => _host != null && _host.IsRunning ? _host.Navigator : null;

        public void OnMove(AxisEventData eventData)
        {
            var nav = Navigator;
            if (nav == null) return;
            FrameNavDirection d;
            switch (eventData.moveDir)
            {
                case MoveDirection.Left: d = FrameNavDirection.Left; break;
                case MoveDirection.Right: d = FrameNavDirection.Right; break;
                case MoveDirection.Up: d = FrameNavDirection.Up; break;
                case MoveDirection.Down: d = FrameNavDirection.Down; break;
                default: return;
            }
            if (nav.Move(d)) eventData.Use();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (Navigator != null && Navigator.Submit()) eventData.Use();
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (Navigator != null && Navigator.Cancel()) eventData.Use();
        }
    }

    /// <summary>Lets a frame or card be selected by the keyboard (added at run time; no visuals of
    /// its own: style the <c>uf-selected</c> class).</summary>
    [AddComponentMenu("")]
    public sealed class UguiNavProxy : Selectable
    {
        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None;
            navigation = new Navigation { mode = Navigation.Mode.None };
        }
    }

    /// <summary>A clickable drawer handle (added at run time when the handle has no Selectable):
    /// a click toggles the drawer. With the keyboard, the drawer itself is selected and entered.</summary>
    [AddComponentMenu("")]
    public sealed class UguiDrawerHandle : Selectable, IPointerClickHandler
    {
        DrawerFrame _drawer;

        internal void Init(DrawerFrame drawer)
        {
            _drawer = drawer;
            if (!targetGraphic) targetGraphic = GetComponent<Graphic>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_drawer == null || eventData.dragging || eventData.button != PointerEventData.InputButton.Left) return;
            if (_drawer.Style.Accepts(FrameInputKinds.Pointer)) _drawer.Execute(FrameCommand.Toggle);
        }
    }
}
