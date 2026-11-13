using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Threads.UiFrames.Toolkit
{
    /// <summary>
    /// Mouse and pen (and, without a touch controller, touch) for one UI Toolkit tree: drags frames,
    /// scrolls them with the wheel, toggles drawers from their handles, and focuses the frame
    /// pressed. A drag that starts on a button still moves its frame; once the frame takes the
    /// pointer, the button loses it and does not click.
    /// <para>Routing: presses are seen at the root (trickle-down). While a press lasts, its moves and
    /// release are also watched on every element along the pressed path, because UI Toolkit sends
    /// a captured pointer's events to the capturing element (a Button's Clickable captures on press)
    /// without passing the root. Each event is handled once: at the root if it is aimed elsewhere,
    /// at its target if that is on the path.</para>
    /// </summary>
    public sealed class ToolkitPointerController : IDisposable
    {
        readonly ToolkitFrameBinder _binder;
        readonly FramePointerTracker _tracker;
        readonly List<IFrameBehaviour> _candidates = new List<IFrameBehaviour>();
        readonly List<VisualElement> _path = new List<VisualElement>();
        readonly HashSet<VisualElement> _pathSet = new HashSet<VisualElement>();
        VisualElement _captured;

        public ToolkitPointerController(ToolkitFrameBinder binder, FrameEnvironment environment)
        {
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _tracker = new FramePointerTracker(binder.Frames, environment);
        }

        /// <summary>Leave touch to a touch controller (TouchController) instead of handling it here.</summary>
        public bool IgnoreTouch { get; set; }
        public bool IsRunning { get; private set; }
        public FramePointerTracker Tracker => _tracker;

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
            if (_tracker.IsTracking) _tracker.Cancel(_tracker.PointerId);
            EndPress();
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
            if (_tracker.IsTracking) { _tracker.Cancel(_tracker.PointerId); EndPress(); }
            var target = evt.target as VisualElement;
            _binder.CollectFrames(target, _candidates, out var handleOf);
            var p = ToDp(evt.position);
            _tracker.Down(evt.pointerId, p.x, p.y, _candidates, handleOf);
            if (_tracker.IsTracking) WatchPath(target);
        }

        void WatchPath(VisualElement target)
        {
            for (var ve = target; ve != null; ve = ve == _binder.Root ? null : ve.parent)
            {
                _path.Add(ve);
                _pathSet.Add(ve);
                ve.RegisterCallback<PointerMoveEvent>(OnPathMove);
                ve.RegisterCallback<PointerUpEvent>(OnPathUp);
                ve.RegisterCallback<PointerCancelEvent>(OnPathCancel);
                ve.RegisterCallback<PointerCaptureOutEvent>(OnPathCaptureOut);
            }
        }

        void EndPress()
        {
            foreach (var ve in _path)
            {
                ve.UnregisterCallback<PointerMoveEvent>(OnPathMove);
                ve.UnregisterCallback<PointerUpEvent>(OnPathUp);
                ve.UnregisterCallback<PointerCancelEvent>(OnPathCancel);
                ve.UnregisterCallback<PointerCaptureOutEvent>(OnPathCaptureOut);
            }
            _path.Clear();
            _pathSet.Clear();
            var c = _captured;
            _captured = null;
            if (c != null && c.HasPointerCapture(_tracker.PointerId)) c.ReleasePointer(_tracker.PointerId);
        }

        // ---- Routing: once per event ----

        bool AimedAtPath(EventBase evt) => evt.target is VisualElement t && _pathSet.Contains(t);
        static bool AtTarget(EventBase evt) => evt.currentTarget == evt.target;

        void OnRootMove(PointerMoveEvent evt) { if (!AimedAtPath(evt)) Move(evt); }
        void OnPathMove(PointerMoveEvent evt) { if (AtTarget(evt)) Move(evt); }
        void OnRootUp(PointerUpEvent evt) { if (!AimedAtPath(evt)) Up(evt); }
        void OnPathUp(PointerUpEvent evt) { if (AtTarget(evt)) Up(evt); }
        void OnRootCancel(PointerCancelEvent evt) { if (!AimedAtPath(evt)) Cancel(evt.pointerId); }
        void OnPathCancel(PointerCancelEvent evt) { if (AtTarget(evt)) Cancel(evt.pointerId); }

        void OnPathCaptureOut(PointerCaptureOutEvent evt)
        {
            // Our frame lost the pointer to someone else: stop dragging.
            if (!AtTarget(evt) || _captured == null || evt.target != _captured) return;
            _captured = null;
            Cancel(evt.pointerId);
        }

        // ---- Handling ----

        void Move(PointerMoveEvent evt)
        {
            if (!_tracker.IsTracking || evt.pointerId != _tracker.PointerId) return;
            var p = ToDp(evt.position);
            if (_tracker.Move(evt.pointerId, p.x, p.y))
            {
                // The frame takes the pointer: whatever had it (a button's Clickable) gets
                // PointerCaptureOut and will not click on release.
                _captured = (VisualElement)_tracker.DragFrame.Element.Native;
                _captured.CapturePointer(evt.pointerId);
            }
            if (_tracker.IsDragging) evt.StopPropagation();
            else if (!_tracker.IsTracking) EndPress(); // nothing moves this way: leave it to the UI
        }

        void Up(PointerUpEvent evt)
        {
            if (!_tracker.IsTracking || evt.pointerId != _tracker.PointerId) return;
            var dragging = _tracker.IsDragging;
            var p = ToDp(evt.position);
            _tracker.Up(evt.pointerId, p.x, p.y);
            EndPress();
            if (dragging) evt.StopPropagation();
        }

        void Cancel(int pointerId)
        {
            if (!_tracker.IsTracking || pointerId != _tracker.PointerId) return;
            _tracker.Cancel(pointerId);
            EndPress();
        }

        void OnWheel(WheelEvent evt)
        {
            _binder.CollectFrames(evt.target as VisualElement, _candidates, out _);
            var perTick = WheelEvent.scrollDeltaPerTick;
            if (perTick <= 0f) perTick = 1f;
            if (_tracker.Wheel(_candidates, evt.delta.x / perTick, evt.delta.y / perTick)) evt.StopPropagation();
        }
    }
}
