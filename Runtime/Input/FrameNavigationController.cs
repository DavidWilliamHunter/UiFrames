using System;
using System.Collections.Generic;
using Threads.UiFrames.Toolkit;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Threads.UiFrames.Input
{
    /// <summary>
    /// Keyboard and gamepad input for the <see cref="FrameNavigator"/>s:
    /// <list type="bullet">
    /// <item>For navigators in <see cref="KeyNavigators"/> (UI Toolkit): every key (arrows, Tab,
    /// Enter, Esc), read here rather than from the panel, whose navigation events are not
    /// delivered in every setup. Keys the navigator leaves to a control (Enter on a button, arrows
    /// on a slider being adjusted) are passed to the control.</item>
    /// <item>For the others (uGUI, whose EventSystem delivers Move/Submit/Cancel): Tab, which uGUI
    /// lacks (<see cref="TabNavigators"/>), and arrows/Enter while nothing is selected yet.</item>
    /// <item>Frame commands: Page Up/Down (shoulders), Home/End, toggle the drawer (gamepad Y),
    /// jump to the next frame (F6 / Select).</item>
    /// </list>
    /// Uses <see cref="CreateDefaultActions"/> unless you pass a map with the same action names.
    /// </summary>
    public sealed class FrameNavigationController : IDisposable
    {
        public const string NavigateAction = "Navigate";
        public const string SubmitAction = "Submit";
        public const string CancelAction = "Cancel";
        public const string TabAction = "Tab";
        public const string PageAction = "Page";
        public const string HomeAction = "Home";
        public const string EndAction = "End";
        public const string ToggleAction = "Toggle";
        public const string CloseAction = "Close";
        public const string CycleFrameAction = "CycleFrame";
        /// <summary>Held: Tab and CycleFrame go backwards (Shift by default).</summary>
        public const string ReverseAction = "Reverse";

        readonly FrameSet _frames;
        readonly bool _ownsActions;
        readonly List<(InputAction action, Action<InputAction.CallbackContext> handler)> _subscriptions =
            new List<(InputAction, Action<InputAction.CallbackContext>)>();
        bool _enabledActions;
        Vector2Int _held;
        double _repeatAt = double.MaxValue;

        public FrameNavigationController(FrameSet frames, InputActionMap actions = null)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _ownsActions = actions == null;
            Actions = actions ?? CreateDefaultActions();
        }

        public InputActionMap Actions { get; }
        public bool IsRunning { get; private set; }
        /// <summary>One navigator per UI root.</summary>
        public List<FrameNavigator> Navigators { get; } = new List<FrameNavigator>();
        /// <summary>Navigators whose UI has no Tab of its own (uGUI).</summary>
        public List<FrameNavigator> TabNavigators { get; } = new List<FrameNavigator>();
        /// <summary>Navigators that get every key from here (UI Toolkit).</summary>
        public List<FrameNavigator> KeyNavigators { get; } = new List<FrameNavigator>();

        /// <summary>Holding a direction repeats it after this long (seconds), as uGUI's input module does.</summary>
        public float RepeatDelay { get; set; } = 0.5f;
        /// <summary>Then repeats every this long (seconds).</summary>
        public float RepeatRate { get; set; } = 0.1f;

        /// <summary>Call once per frame with the current time (seconds): repeats a held direction for
        /// the navigators fed from here. (uGUI repeats by itself.)</summary>
        public void Update(double now)
        {
            if (!IsRunning || _held == Vector2Int.zero) return;
            if (_repeatAt == double.MaxValue) { _repeatAt = now + RepeatDelay; return; }
            if (now < _repeatAt) return;
            _repeatAt = now + Math.Max(0.01f, RepeatRate);
            var target = KeyTarget();
            if (target != null) Step(target, Direction(_held));
        }

        /// <summary>
        /// The default bindings (the same as the shipped <c>UiFramesNavigation.inputactions</c>):
        /// Navigate (arrows, d-pad, left stick), Submit (Enter, Space, gamepad A), Cancel (Esc,
        /// gamepad B), Tab, Page (Page Up/Down, shoulders), Home/End, Toggle drawer (gamepad Y),
        /// CycleFrame (F6, gamepad Select), Reverse (Shift: held, Tab and CycleFrame go back).
        /// Close has no default binding. For uGUI, Navigate/Submit/Cancel are only used while
        /// nothing is selected; the EventSystem's input module delivers them otherwise.
        /// </summary>
        public static InputActionMap CreateDefaultActions()
        {
            var map = new InputActionMap("UiFrames");
            var nav = map.AddAction(NavigateAction, InputActionType.Value, expectedControlLayout: "Vector2");
            nav.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            nav.AddBinding("<Gamepad>/dpad");
            nav.AddBinding("<Gamepad>/leftStick");
            var submit = map.AddAction(SubmitAction, InputActionType.Button, "<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/numpadEnter");
            submit.AddBinding("<Keyboard>/space");
            submit.AddBinding("<Gamepad>/buttonSouth");
            var cancel = map.AddAction(CancelAction, InputActionType.Button, "<Keyboard>/escape");
            cancel.AddBinding("<Gamepad>/buttonEast");
            map.AddAction(TabAction, InputActionType.Button, "<Keyboard>/tab");

            var page = map.AddAction(PageAction, InputActionType.Value, expectedControlLayout: "Axis");
            page.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/pageUp").With("Positive", "<Keyboard>/pageDown");
            page.AddCompositeBinding("1DAxis").With("Negative", "<Gamepad>/leftShoulder").With("Positive", "<Gamepad>/rightShoulder");
            map.AddAction(HomeAction, InputActionType.Button, "<Keyboard>/home");
            map.AddAction(EndAction, InputActionType.Button, "<Keyboard>/end");
            map.AddAction(ToggleAction, InputActionType.Button, "<Gamepad>/buttonNorth");
            map.AddAction(CloseAction, InputActionType.Button);
            var cycle = map.AddAction(CycleFrameAction, InputActionType.Button, "<Keyboard>/f6");
            cycle.AddBinding("<Gamepad>/select");
            map.AddAction(ReverseAction, InputActionType.Button, "<Keyboard>/shift");
            return map;
        }

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            On(NavigateAction, OnNavigate);
            On(NavigateAction, _ => { _held = Vector2Int.zero; _repeatAt = double.MaxValue; }, canceled: true);
            On(SubmitAction, OnSubmit);
            On(CancelAction, OnCancel);
            On(TabAction, OnTab);
            On(PageAction, OnPage);
            On(HomeAction, _ => Send(FrameCommand.Home));
            On(EndAction, _ => Send(FrameCommand.End));
            On(ToggleAction, _ => Toggle());
            On(CloseAction, _ => Close());
            On(CycleFrameAction, OnCycle);
            if (!Actions.enabled) { Actions.Enable(); _enabledActions = true; }
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            foreach (var (action, handler) in _subscriptions)
            {
                action.performed -= handler;
                action.canceled -= handler;
            }
            _subscriptions.Clear();
            if (_enabledActions) { Actions.Disable(); _enabledActions = false; }
        }

        public void Dispose()
        {
            Shutdown();
            if (_ownsActions) Actions.Dispose();
        }

        void On(string name, Action<InputAction.CallbackContext> handler, bool canceled = false)
        {
            var action = Actions.FindAction(name);
            if (action == null) return;
            if (canceled) action.canceled += handler;
            else action.performed += handler;
            _subscriptions.Add((action, handler));
        }

        // ---- Commands (public so tests and custom bindings can call them) ----

        /// <summary>Sends a command to the focused frame, bubbling to outer frames.</summary>
        public bool Send(in FrameCommand command) =>
            _frames.Dispatch(_frames.Focused, command, FrameInputKinds.Navigation) != null;

        /// <summary>Toggles the drawer that has focus (or contains it), else the first drawer.</summary>
        public bool Toggle()
        {
            for (var f = _frames.Focused; f != null; f = _frames.OuterOf(f))
                if (f is DrawerFrame d && d.Style.Accepts(FrameInputKinds.Navigation)) return d.Execute(FrameCommand.Toggle);
            foreach (var f in _frames.Frames)
                if (f is DrawerFrame d && d.Style.Accepts(FrameInputKinds.Navigation)) return d.Execute(FrameCommand.Toggle);
            return false;
        }

        /// <summary>Closes the drawer that has focus (or contains it), else every open drawer.</summary>
        public bool Close()
        {
            for (var f = _frames.Focused; f != null; f = _frames.OuterOf(f))
                if (f is DrawerFrame d && d.IsOpen && d.Style.Accepts(FrameInputKinds.Navigation)) return d.Execute(FrameCommand.Close);
            var any = false;
            foreach (var f in _frames.Frames)
                if (f is DrawerFrame d && d.IsOpen && d.Style.Accepts(FrameInputKinds.Navigation)) any |= d.Execute(FrameCommand.Close);
            return any;
        }

        /// <summary>The navigator of the UI holding the focused frame, else the first.</summary>
        FrameNavigator Active
        {
            get
            {
                var frame = _frames.Focused;
                foreach (var n in Navigators)
                    if (frame != null && n.Host.Owns(frame.Element)) return n;
                return Navigators.Count > 0 ? Navigators[0] : null;
            }
        }

        void OnNavigate(InputAction.CallbackContext c)
        {
            var v = c.ReadValue<Vector2>();
            var dir = v.sqrMagnitude < 0.25f ? Vector2Int.zero
                : Mathf.Abs(v.x) >= Mathf.Abs(v.y) ? new Vector2Int(v.x > 0f ? 1 : -1, 0) : new Vector2Int(0, v.y > 0f ? 1 : -1);
            if (dir == _held) return;
            _held = dir;
            _repeatAt = double.MaxValue; // the next Update starts the repeat delay
            if (dir == Vector2Int.zero) return;
            var d = Direction(dir);
            var target = KeyTarget();
            if (target != null)
            {
                Step(target, d);
                return;
            }
            // Other UIs deliver arrows themselves once something is selected; this only starts them.
            var start = Unfocused();
            if (start != null) start.Move(d);
        }

        void OnSubmit(InputAction.CallbackContext c)
        {
            var target = KeyTarget();
            if (target != null)
            {
                if (!target.Submit()) Native(target, ToolkitFocusController.NativeKey.Submit);
                return;
            }
            var start = Unfocused();
            if (start != null) start.Submit();
        }

        void OnCancel(InputAction.CallbackContext c)
        {
            var target = KeyTarget();
            if (target != null && !target.Cancel()) Native(target, ToolkitFocusController.NativeKey.Cancel);
        }

        /// <summary>The key navigator to get keys now: none while another UI (uGUI) has the selection;
        /// else the one whose UI has focus, the one holding the focused frame, or the first.</summary>
        FrameNavigator KeyTarget()
        {
            if (KeyNavigators.Count == 0) return null;
            foreach (var n in Navigators)
                if (!KeyNavigators.Contains(n) && n.Host.Focused != null) return null;
            foreach (var n in KeyNavigators)
                if (n.Host.Focused != null) return n;
            var active = Active;
            return active != null && KeyNavigators.Contains(active) ? active : KeyNavigators[0];
        }

        /// <summary>A navigator (not fed keys from here) whose UI has nothing selected yet.</summary>
        FrameNavigator Unfocused()
        {
            foreach (var n in Navigators)
                if (!KeyNavigators.Contains(n) && n.Host.Focused == null) return n;
            return null;
        }

        static FrameNavDirection Direction(Vector2Int dir) =>
            dir.x > 0 ? FrameNavDirection.Right : dir.x < 0 ? FrameNavDirection.Left : dir.y > 0 ? FrameNavDirection.Up : FrameNavDirection.Down;

        /// <summary>One step for a navigator fed from here. Arrows it leaves alone belong to a slider
        /// being adjusted (passed on); a text field reads its arrow keys itself.</summary>
        static void Step(FrameNavigator target, FrameNavDirection d)
        {
            if (target.Move(d) || !target.IsEditing || target.Host.ArrowUse(target.Editing) != FrameControlArrows.Adjust) return;
            Native(target, d == FrameNavDirection.Left ? ToolkitFocusController.NativeKey.Left
                : d == FrameNavDirection.Right ? ToolkitFocusController.NativeKey.Right
                : d == FrameNavDirection.Up ? ToolkitFocusController.NativeKey.Up : ToolkitFocusController.NativeKey.Down);
        }

        static void Native(FrameNavigator navigator, ToolkitFocusController.NativeKey key)
        {
            if (navigator.Host is ToolkitFocusController toolkit) toolkit.SendNative(key);
        }

        /// <summary>The Reverse action is held (Shift by default; rebind it like any action).</summary>
        bool Reversed
        {
            get
            {
                var reverse = Actions.FindAction(ReverseAction);
                return reverse != null && reverse.IsPressed();
            }
        }

        void OnTab(InputAction.CallbackContext c)
        {
            var back = Reversed;
            var key = KeyTarget();
            if (key != null) { key.Tab(back ? -1 : 1); return; }
            var active = Active;
            foreach (var n in TabNavigators)
                if (n == active || n.Host.Focused != null) { n.Tab(back ? -1 : 1); return; }
            if (TabNavigators.Count > 0 && active == null) TabNavigators[0].Tab(back ? -1 : 1);
        }

        void OnPage(InputAction.CallbackContext c)
        {
            var v = c.ReadValue<float>();
            if (v > 0.5f) Send(FrameCommand.PageNext);
            else if (v < -0.5f) Send(FrameCommand.PagePrevious);
        }

        void OnCycle(InputAction.CallbackContext c)
        {
            var back = Reversed;
            Active?.CycleFrame(back ? -1 : 1);
        }
    }
}
