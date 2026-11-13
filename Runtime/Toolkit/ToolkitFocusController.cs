using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Threads.UiFrames.Toolkit
{
    /// <summary>
    /// UI Toolkit keyboard/gamepad navigation for frames: the <see cref="IFrameFocusHost"/> of a
    /// <see cref="FrameNavigator"/>. Frames and cards are made focusable when the keyboard selects
    /// them; a slider does not take the arrows until Enter starts adjusting it.
    /// <para>Keys reach the navigator one of two ways. With <see cref="KeysFromController"/> (what
    /// <c>UiFramesRunner</c> uses), an Input System controller reads the keys and calls the
    /// navigator, and the panel's own navigation events are swallowed, so each key acts once
    /// whether or not the panel delivers them (it does not in every setup); keys the navigator
    /// leaves to a control are passed on with <see cref="SendNative"/>. Without it, the panel's
    /// navigation events are caught at the root and given to the navigator.</para>
    /// </summary>
    public sealed class ToolkitFocusController : IFrameNavigationHost, IDisposable
    {
        readonly ToolkitFrameBinder _binder;
        readonly Dictionary<VisualElement, bool> _madeFocusable = new Dictionary<VisualElement, bool>();
        VisualElement _editing;
        bool _forwarding;

        /// <summary>Keys come from an Input System controller rather than the panel's navigation events.</summary>
        public bool KeysFromController { get; set; }

        /// <summary>The kinds of navigation event <see cref="SendNative"/> can pass to the focused control.</summary>
        public enum NativeKey { Left, Right, Up, Down, Submit, Cancel }

        /// <summary>Passes a key the navigator did not use to the focused control as the panel's own
        /// navigation event (a button presses on Submit; an adjusting slider moves).</summary>
        public void SendNative(NativeKey key)
        {
            var panel = RootVe.panel;
            if (panel == null || !(Controller?.focusedElement is VisualElement focused)) return;
            if (key <= NativeKey.Down && StepSlider(focused, key == NativeKey.Right || key == NativeKey.Up ? 1 : -1)) return;
            EventBase evt;
            switch (key)
            {
                case NativeKey.Left: evt = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Left); break;
                case NativeKey.Right: evt = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right); break;
                case NativeKey.Up: evt = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Up); break;
                case NativeKey.Down: evt = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Down); break;
                case NativeKey.Submit: evt = NavigationSubmitEvent.GetPooled(); break;
                default: evt = NavigationCancelEvent.GetPooled(); break;
            }
            _forwarding = true;
            try
            {
                using (evt) panel.visualTree.SendEvent(evt);
            }
            finally { _forwarding = false; }
        }

        public ToolkitFocusController(ToolkitFrameBinder binder)
        {
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        }

        public bool IsRunning { get; private set; }
        public FrameNavigator Navigator { get; set; }
        public event Action<IFrameElement> FocusChanged;
        public IFrameElement Root => _binder.Wrap(_binder.Root);

        VisualElement RootVe => _binder.Root;
        FocusController Controller => RootVe.panel?.focusController;

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            RootVe.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
            RootVe.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            RootVe.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            RootVe.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);
            RootVe.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            RootVe.UnregisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
            RootVe.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            RootVe.UnregisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            RootVe.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);
            RootVe.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            foreach (var kv in _madeFocusable) kv.Key.focusable = kv.Value;
            _madeFocusable.Clear();
            _editing = null;
        }

        public void Dispose() => Shutdown();

        /// <summary>Moves a slider being adjusted by <c>--uf-arrow-step</c> of its range (from the
        /// frame around it; 10 % by default, as uGUI). Returns false for other controls.</summary>
        bool StepSlider(VisualElement focused, int direction)
        {
            var target = _editing != null && (_editing == focused || _editing.Contains(focused)) ? _editing : null;
            var step = 0.1f;
            for (var p = target?.parent; p != null; p = p.parent)
            {
                var frame = _binder.FrameOf(p);
                if (frame == null || !frame.Style.TryGet(FrameProperties.ArrowStep, out var s)) continue;
                step = s;
                break;
            }
            switch (target)
            {
                case Slider f:
                    f.value = UnityEngine.Mathf.Clamp(f.value + direction * step * (f.highValue - f.lowValue), UnityEngine.Mathf.Min(f.lowValue, f.highValue), UnityEngine.Mathf.Max(f.lowValue, f.highValue));
                    return true;
                case SliderInt i:
                    var delta = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(step * UnityEngine.Mathf.Abs(i.highValue - i.lowValue)));
                    i.value = UnityEngine.Mathf.Clamp(i.value + direction * delta, UnityEngine.Mathf.Min(i.lowValue, i.highValue), UnityEngine.Mathf.Max(i.lowValue, i.highValue));
                    return true;
                default:
                    return false;
            }
        }

        // ---- IFrameFocusHost ----

        public IFrameElement Focused =>
            Controller?.focusedElement is VisualElement ve && Contains(ve) ? _binder.Wrap(ve) : null;

        public bool Owns(IFrameElement element) => element?.Native is VisualElement ve && Contains(ve);

        public bool IsControl(IFrameElement element) =>
            element?.Native is VisualElement ve && ve != RootVe && !_madeFocusable.ContainsKey(ve) &&
            _binder.DesignedFocusable(ve) && ve.enabledInHierarchy && ve.resolvedStyle.display != DisplayStyle.None &&
            !InsideControl(ve);

        public FrameControlArrows ArrowUse(IFrameElement control)
        {
            if (!(control?.Native is VisualElement ve)) return FrameControlArrows.None;
            if (ve is TextField || IsGeneric(ve, typeof(TextInputBaseField<>))) return FrameControlArrows.Text;
            if (ve is Scroller || ve is MinMaxSlider || IsGeneric(ve, typeof(BaseSlider<>))) return FrameControlArrows.Adjust;
            return FrameControlArrows.None;
        }

        public void EnsureFocusable(IFrameElement element)
        {
            if (!(element?.Native is VisualElement ve) || ve.focusable) return;
            _madeFocusable[ve] = false;
            ve.focusable = true;
        }

        public void SetEditing(IFrameElement control, bool editing) =>
            _editing = editing ? control?.Native as VisualElement : null;

        public bool CanFocus(IFrameElement element) => element?.Native is VisualElement ve && CanFocus(ve);

        public bool Focus(IFrameElement element)
        {
            if (!(element?.Native is VisualElement ve) || !CanFocus(ve)) return false;
            if (HasFocus(ve)) return true;
            ve.Focus();
            return HasFocus(ve);
        }

        public IFrameElement FirstFocusable(IFrameElement container, Func<IFrameElement, bool> filter = null)
        {
            if (!(container?.Native is VisualElement root)) return null;
            IFrameElement found = null;
            root.Query<VisualElement>().ForEach(ve =>
            {
                if (found != null || ve == root || !CanFocus(ve)) return;
                var w = _binder.Wrap(ve);
                if (filter == null || filter(w)) found = w;
            });
            return found;
        }

        bool Contains(VisualElement ve) => ve == RootVe || RootVe.Contains(ve);

        /// <summary>Focus is on the element or, for fields that pass focus to an inner input, inside it.</summary>
        bool HasFocus(VisualElement ve) =>
            Controller?.focusedElement is VisualElement f && (f == ve || (ve.delegatesFocus && ve.Contains(f)));

        static bool CanFocus(VisualElement ve)
        {
            if (ve.panel == null || !ve.focusable || !ve.canGrabFocus || !ve.enabledInHierarchy) return false;
            for (var e = ve; e != null; e = e.parent)
            {
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
                if (e.style.visibility.keyword == StyleKeyword.Undefined && e.style.visibility.value == Visibility.Hidden) return false;
            }
            return true;
        }

        /// <summary>Parts of a control (a slider's dragger, a field's text input) are not items.</summary>
        bool InsideControl(VisualElement ve)
        {
            for (var p = ve.parent; p != null && p != RootVe; p = p.parent)
            {
                if (_binder.FrameOf(p) != null) return false;
                if (!_madeFocusable.ContainsKey(p) && _binder.DesignedFocusable(p)) return true;
            }
            return false;
        }

        static bool IsGeneric(VisualElement ve, Type definition)
        {
            for (var t = ve.GetType(); t != null && t != typeof(VisualElement); t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == definition) return true;
            return false;
        }

        // ---- Events ----

        void OnFocusIn(FocusInEvent evt)
        {
            if (evt.target is VisualElement ve && Contains(ve)) FocusChanged?.Invoke(_binder.Wrap(ve));
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (_forwarding) return;
            var nav = Navigator;
            if (nav == null) return;
            if (KeysFromController) { Swallow(evt); return; }
            bool used;
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Left: used = nav.Move(FrameNavDirection.Left); break;
                case NavigationMoveEvent.Direction.Right: used = nav.Move(FrameNavDirection.Right); break;
                case NavigationMoveEvent.Direction.Up: used = nav.Move(FrameNavDirection.Up); break;
                case NavigationMoveEvent.Direction.Down: used = nav.Move(FrameNavDirection.Down); break;
                case NavigationMoveEvent.Direction.Next: used = nav.Tab(1); break;
                case NavigationMoveEvent.Direction.Previous: used = nav.Tab(-1); break;
                default: used = false; break;
            }
            if (used) Swallow(evt);
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            if (_forwarding || Navigator == null) return;
            if (KeysFromController || Navigator.Submit()) Swallow(evt);
        }

        void OnNavigationCancel(NavigationCancelEvent evt)
        {
            if (_forwarding || Navigator == null) return;
            if (KeysFromController || Navigator.Cancel()) Swallow(evt);
        }

        /// <summary>Controls that read arrow keys directly (a slider) get them only while adjusting.</summary>
        void OnKeyDown(KeyDownEvent evt)
        {
            if (Navigator == null) return;
            var arrow = evt.keyCode == UnityEngine.KeyCode.LeftArrow || evt.keyCode == UnityEngine.KeyCode.RightArrow ||
                        evt.keyCode == UnityEngine.KeyCode.UpArrow || evt.keyCode == UnityEngine.KeyCode.DownArrow;
            if (!arrow || !(evt.target is VisualElement target)) return;
            if (_editing != null && (_editing == target || _editing.Contains(target))) return;
            if (ArrowUse(_binder.Wrap(target)) == FrameControlArrows.Adjust || InsideAdjustControl(target)) evt.StopPropagation();
        }

        bool InsideAdjustControl(VisualElement ve)
        {
            for (var p = ve.parent; p != null && p != RootVe; p = p.parent)
                if (ArrowUse(_binder.Wrap(p)) == FrameControlArrows.Adjust) return true;
            return false;
        }

        void Swallow(EventBase evt)
        {
            evt.StopPropagation();
            Controller?.IgnoreEvent(evt);
        }
    }
}
