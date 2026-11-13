using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Threads.UiFrames.UGUI
{
    /// <summary>
    /// Mouse and pen (and, without a touch controller, touch) for one uGUI root, through the
    /// EventSystem: drags frames (a drag that starts on a button moves the frame and the button
    /// does not click), scrolls them with the wheel, toggles drawers from their handles and
    /// focuses the frame pressed. Controls with their own drags (Slider, ScrollRect, InputField…)
    /// keep them, because uGUI sends a drag to the nearest drag handler.
    /// </summary>
    public sealed class UguiPointerController : IDisposable
    {
        readonly UguiFrameBinder _binder;
        readonly FramePointerTracker _tracker;
        readonly List<IFrameBehaviour> _candidates = new List<IFrameBehaviour>();
        bool _passedOn;

        public UguiPointerController(UguiFrameBinder binder)
        {
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _tracker = new FramePointerTracker(binder.Frames, binder.Environment);
        }

        /// <summary>Leave touch to a touch controller (TouchController) instead of handling it here.</summary>
        public bool IgnoreTouch { get; set; }
        public bool IsRunning { get; private set; }
        public FramePointerTracker Tracker => _tracker;

        public void Startup()
        {
            if (IsRunning) return;
            IsRunning = true;
            _binder.Pointer = this;
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            if (_tracker.IsTracking) _tracker.Cancel(_tracker.PointerId);
            if (_binder.Pointer == this) _binder.Pointer = null;
        }

        public void Dispose() => Shutdown();

        bool Ignore(PointerEventData e) =>
            !IsRunning || e.button != PointerEventData.InputButton.Left ||
            (IgnoreTouch && e is ExtendedPointerEventData x && x.pointerType == UIPointerType.Touch);

        Vector2 ToDp(Vector2 screen)
        {
            var k = Mathf.Max(1e-4f, _binder.Environment.PixelsPerDp);
            return screen / k;
        }

        internal void PointerDown(PointerEventData e)
        {
            if (Ignore(e)) return;
            _binder.CollectFrames(Target(e.pointerPressRaycast.gameObject), _candidates, out _);
            foreach (var f in _candidates)
            {
                if (!f.Style.Accepts(FrameInputKinds.Pointer)) continue;
                if (f.Style.Accepts(FrameInputKinds.Navigation)) _binder.Frames.Focus(f);
                break;
            }
        }

        internal void PointerClick(PointerEventData e)
        {
            if (Ignore(e) || e.dragging) return;
            _binder.CollectFrames(Target(e.pointerPressRaycast.gameObject), _candidates, out var handleOf);
            if (handleOf == null || !handleOf.Style.Accepts(FrameInputKinds.Pointer)) return;
            // A handle with its own Selectable (UguiDrawerHandle, a Button) toggles itself.
            if (handleOf is DrawerFrame d && d.Handle?.Native is GameObject h && h && h.GetComponent<Selectable>()) return;
            handleOf.Execute(FrameCommand.Toggle);
        }

        internal void BeginDrag(UguiFramePointer from, PointerEventData e)
        {
            if (Ignore(e)) return;
            _passedOn = false;
            _binder.CollectFrames(Target(e.pointerPressRaycast.gameObject) ?? from.transform, _candidates, out _);
            var press = ToDp(e.pressPosition);
            _tracker.Down(e.pointerId, press.x, press.y, _candidates);
            if (!_tracker.IsTracking) { PassOn(from, e); return; }
            Drag(from, e);
        }

        internal void Drag(UguiFramePointer from, PointerEventData e)
        {
            if (_passedOn || !_tracker.IsTracking || e.pointerId != _tracker.PointerId) return;
            var p = ToDp(e.position);
            _tracker.Move(e.pointerId, p.x, p.y);
            // No frame moves this way: hand the drag to an outer drag handler (e.g. a ScrollRect).
            if (!_tracker.IsTracking) PassOn(from, e);
        }

        internal void EndDrag(PointerEventData e)
        {
            if (_passedOn || !_tracker.IsTracking || e.pointerId != _tracker.PointerId) return;
            var p = ToDp(e.position);
            _tracker.Up(e.pointerId, p.x, p.y);
        }

        internal void Scroll(UguiFramePointer from, PointerEventData e)
        {
            var used = false;
            if (IsRunning)
            {
                var perTick = e.currentInputModule is InputSystemUIInputModule m && Mathf.Abs(m.scrollDeltaPerTick) > 1e-4f
                    ? m.scrollDeltaPerTick : 1f;
                _binder.CollectFrames(Target(e.pointerCurrentRaycast.gameObject) ?? from.transform, _candidates, out _);
                // uGUI: +y is wheel up (earlier content).
                used = _tracker.Wheel(_candidates, e.scrollDelta.x / perTick, -e.scrollDelta.y / perTick);
            }
            if (!used && from.transform.parent)
                ExecuteEvents.ExecuteHierarchy(from.transform.parent.gameObject, e, ExecuteEvents.scrollHandler);
        }

        void PassOn(UguiFramePointer from, PointerEventData e)
        {
            _passedOn = true;
            var parent = from.transform.parent;
            var handler = parent ? ExecuteEvents.GetEventHandler<IBeginDragHandler>(parent.gameObject) : null;
            if (!handler) return;
            e.pointerDrag = handler;
            ExecuteEvents.Execute(handler, e, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(handler, e, ExecuteEvents.beginDragHandler);
        }

        static Transform Target(GameObject go) => go ? go.transform : null;
    }

    /// <summary>Forwards uGUI pointer events on a frame to its <see cref="UguiPointerController"/>.
    /// Added at run time by <see cref="UguiFrameBinder"/>.</summary>
    [AddComponentMenu("")]
    public sealed class UguiFramePointer : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IScrollHandler, IPointerDownHandler, IPointerClickHandler
    {
        UguiPointerController _controller;

        public IFrameBehaviour Frame { get; private set; }

        internal void Init(UguiPointerController controller, IFrameBehaviour frame)
        {
            _controller = controller;
            Frame = frame;
        }

        bool Active => _controller != null && _controller.IsRunning;

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = true;
        public void OnBeginDrag(PointerEventData e) { if (Active) _controller.BeginDrag(this, e); }
        public void OnDrag(PointerEventData e) { if (Active) _controller.Drag(this, e); }
        public void OnEndDrag(PointerEventData e) { if (Active) _controller.EndDrag(e); }
        public void OnPointerDown(PointerEventData e) { if (Active) _controller.PointerDown(e); }
        public void OnPointerClick(PointerEventData e) { if (Active) _controller.PointerClick(e); }

        public void OnScroll(PointerEventData e)
        {
            if (Active) _controller.Scroll(this, e);
            else if (transform.parent) ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, e, ExecuteEvents.scrollHandler);
        }
    }
}
