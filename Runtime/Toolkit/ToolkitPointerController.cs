using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Threads.UiFrames.Toolkit
{
    /// <summary>
    /// Mouse, pen and touch for one UI Toolkit tree: drags frames, scrolls them with the wheel,
    /// toggles drawers from their handles, and focuses the frame pressed. Each pointer (each
    /// finger) is tracked on its own, so several fingers can drag different frames at once. A drag
    /// that starts on a button still moves its frame; once the frame takes the pointer, the button
    /// loses it and does not click.
    /// <para>Routing: presses are seen at the root (trickle-down). While a press lasts, its moves and
    /// release are also watched on every element along the pressed path, because UI Toolkit sends
    /// a captured pointer's events to the capturing element (a Button's Clickable captures on press)
    /// without passing the root. Each event is handled once: at the root if it is aimed elsewhere,
    /// at its target if that is on the path.</para>
    /// </summary>
    public sealed class ToolkitPointerController : IDisposable
    {
        sealed class Press
        {
            public int PointerId;
            public FramePointerTracker Tracker;
            public readonly HashSet<VisualElement> Path = new HashSet<VisualElement>();
            public VisualElement Captured;
        }

        readonly ToolkitFrameBinder _binder;
        readonly FramePointerTrackers _trackers;
        readonly List<IFrameBehaviour> _candidates = new List<IFrameBehaviour>();
        readonly Dictionary<int, Press> _presses = new Dictionary<int, Press>();
        // Path elements are shared by fingers pressing in the same area: count the presses watching each.
        readonly Dictionary<VisualElement, int> _watching = new Dictionary<VisualElement, int>();

        public ToolkitPointerController(ToolkitFrameBinder binder, FrameEnvironment environment)
        {
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _trackers = new FramePointerTrackers(binder.Frames, environment);
        }

        /// <summary>Leave touch to a touch controller instead of handling it here.</summary>
        public bool IgnoreTouch { get; set; }
        public bool IsRunning { get; private set; }
        /// <summary>One tracker per pointer.</summary>
        public FramePointerTrackers Trackers => _trackers;

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            var root = _binder.Root;
            root.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(OnRootMove, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(OnRootUp, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(OnRootCancel, TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            var root = _binder.Root;
            root.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerMoveEvent>(OnRootMove, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerUpEvent>(OnRootUp, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerCancelEvent>(OnRootCancel, TrickleDown.TrickleDown);
            root.UnregisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            foreach (var press in new List<Press>(_presses.Values))
            {
                if (press.Tracker.IsTracking) press.Tracker.Cancel(press.PointerId);
                End(press);
            }
        }

        public void Dispose() => Shutdown();

        bool Ignore(IPointerEvent evt) => IgnoreTouch && evt.pointerType == UnityEngine.UIElements.PointerType.touch;

        // Panel space is y down; the tracker works in dp, y up.
        Vector2 ToDp(Vector3 panelPosition) =>
            new Vector2(panelPosition.x * _binder.DpPerUnit, -panelPosition.y * _binder.DpPerUnit);

        // ---- Press ----

        void OnDown(PointerDownEvent evt)
        {
            if (Ignore(evt) || evt.button > 0) return;
            if (_presses.TryGetValue(evt.pointerId, out var old))
            {
                if (old.Tracker.IsTracking) old.Tracker.Cancel(old.PointerId);
                End(old);
            }
            var target = evt.target as VisualElement;
            _binder.CollectFrames(target, _candidates, out var handleOf);
            var p = ToDp(evt.position);
            var tracker = _trackers.For(evt.pointerId);
            tracker.Down(evt.pointerId, p.x, p.y, _candidates, handleOf);
            if (!tracker.IsTracking) { _trackers.Release(evt.pointerId); return; }
            var press = new Press { PointerId = evt.pointerId, Tracker = tracker };
            _presses.Add(evt.pointerId, press);
            for (var ve = target; ve != null; ve = ve == _binder.Root ? null : ve.parent)
            {
                press.Path.Add(ve);
                _watching.TryGetValue(ve, out var n);
                _watching[ve] = n + 1;
                if (n > 0) continue;
                ve.RegisterCallback<PointerMoveEvent>(OnPathMove);
                ve.RegisterCallback<PointerUpEvent>(OnPathUp);
                ve.RegisterCallback<PointerCancelEvent>(OnPathCancel);
                ve.RegisterCallback<PointerCaptureOutEvent>(OnPathCaptureOut);
            }
        }

        void End(Press press)
        {
            foreach (var ve in press.Path)
            {
                if (!_watching.TryGetValue(ve, out var n)) continue;
                if (n > 1) { _watching[ve] = n - 1; continue; }
                _watching.Remove(ve);
                ve.UnregisterCallback<PointerMoveEvent>(OnPathMove);
                ve.UnregisterCallback<PointerUpEvent>(OnPathUp);
                ve.UnregisterCallback<PointerCancelEvent>(OnPathCancel);
                ve.UnregisterCallback<PointerCaptureOutEvent>(OnPathCaptureOut);
            }
            press.Path.Clear();
            var c = press.Captured;
            press.Captured = null;
            if (c != null && c.HasPointerCapture(press.PointerId)) c.ReleasePointer(press.PointerId);
            _presses.Remove(press.PointerId);
            _trackers.Release(press.PointerId);
        }

        // ---- Routing: once per event ----

        Press PressOf(int pointerId) => _presses.TryGetValue(pointerId, out var p) ? p : null;
        static bool AimedAtPath(Press press, EventBase evt) => evt.target is VisualElement t && press.Path.Contains(t);
        static bool AtTarget(EventBase evt) => evt.currentTarget == evt.target;

        void OnRootMove(PointerMoveEvent evt)
        {
            var press = PressOf(evt.pointerId);
            if (press != null && !AimedAtPath(press, evt)) Move(press, evt);
        }

        void OnPathMove(PointerMoveEvent evt)
        {
            var press = PressOf(evt.pointerId);
            if (press != null && AtTarget(evt) && AimedAtPath(press, evt)) Move(press, evt);
        }

        void OnRootUp(PointerUpEvent evt)
        {
            var press = PressOf(evt.pointerId);
            if (press != null && !AimedAtPath(press, evt)) Up(press, evt);
        }

        void OnPathUp(PointerUpEvent evt)
        {
            var press = PressOf(evt.pointerId);
            if (press != null && AtTarget(evt) && AimedAtPath(press, evt)) Up(press, evt);
        }

        void OnRootCancel(PointerCancelEvent evt)
        {
            var press = PressOf(evt.pointerId);
            if (press != null && !AimedAtPath(press, evt)) Cancel(press);
        }

        void OnPathCancel(PointerCancelEvent evt)
        {
            var press = PressOf(evt.pointerId);
            if (press != null && AtTarget(evt) && AimedAtPath(press, evt)) Cancel(press);
        }

        void OnPathCaptureOut(PointerCaptureOutEvent evt)
        {
            // Our frame lost the pointer to someone else: stop dragging.
            var press = PressOf(evt.pointerId);
            if (press == null || !AtTarget(evt) || press.Captured == null || evt.target != press.Captured) return;
            press.Captured = null;
            Cancel(press);
        }

        // ---- Handling ----

        void Move(Press press, PointerMoveEvent evt)
        {
            var tracker = press.Tracker;
            if (!tracker.IsTracking) return;
            var p = ToDp(evt.position);
            if (tracker.Move(evt.pointerId, p.x, p.y))
            {
                // The frame takes the pointer: whatever had it (a button's Clickable) gets
                // PointerCaptureOut and will not click on release.
                press.Captured = (VisualElement)tracker.DragFrame.Element.Native;
                press.Captured.CapturePointer(evt.pointerId);
            }
            if (tracker.IsDragging) evt.StopPropagation();
            else if (!tracker.IsTracking) End(press); // nothing moves this way: leave it to the UI
        }

        void Up(Press press, PointerUpEvent evt)
        {
            var tracker = press.Tracker;
            var dragging = tracker.IsDragging;
            if (tracker.IsTracking)
            {
                var p = ToDp(evt.position);
                tracker.Up(evt.pointerId, p.x, p.y);
            }
            End(press);
            if (dragging) evt.StopPropagation();
        }

        void Cancel(Press press)
        {
            if (press.Tracker.IsTracking) press.Tracker.Cancel(press.PointerId);
            End(press);
        }

        void OnWheel(WheelEvent evt)
        {
            _binder.CollectFrames(evt.target as VisualElement, _candidates, out _);
            var perTick = WheelEvent.scrollDeltaPerTick;
            if (perTick <= 0f) perTick = 1f;
            if (_trackers.Wheel.Wheel(_candidates, evt.delta.x / perTick, evt.delta.y / perTick)) evt.StopPropagation();
        }
    }
}
