using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary>
    /// Turns one pointer's presses, moves and releases into frame commands. The UI adapters feed
    /// it events (positions in screen dp, y up) together with the frames under the press, innermost
    /// first. Each frame converts the movement into its own local space
    /// (<see cref="IFrameElement.ToLocalDeltaDp"/>), so scaled, rotated and world-space frames
    /// follow the pointer 1:1; the slop is measured on screen.
    /// <list type="bullet">
    /// <item>A press focuses the innermost frame under it.</item>
    /// <item>Past <see cref="FrameEnvironment.DragSlopDp"/>, the drag goes to the innermost frame
    /// moving along the drag's main axis (<see cref="FrameDragRouting"/>); the adapter should then
    /// take the pointer from any control under it, so a button does not click.</item>
    /// <item>A release sends <c>drag-end</c> with the release velocity; a release without a drag on
    /// a drawer handle toggles that drawer.</item>
    /// </list>
    /// </summary>
    public sealed class FramePointerTracker
    {
        readonly FrameSet _frames;
        readonly FrameEnvironment _env;
        readonly VelocityTracker _velocity = new VelocityTracker();
        readonly List<IFrameBehaviour> _candidates = new List<IFrameBehaviour>();
        IFrameBehaviour _handleOf;
        float _startX, _startY, _lastX, _lastY;

        public FramePointerTracker(FrameSet frames, FrameEnvironment environment, FrameInputKinds kind = FrameInputKinds.Pointer)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _env = environment ?? throw new ArgumentNullException(nameof(environment));
            Kind = kind;
        }

        public FrameInputKinds Kind { get; }
        public bool IsTracking { get; private set; }
        public int PointerId { get; private set; }
        /// <summary>The frame being dragged, or null.</summary>
        public IFrameBehaviour DragFrame { get; private set; }
        public bool IsDragging => DragFrame != null;

        /// <summary>A press. <paramref name="candidates"/>: frames under it, innermost first (leave
        /// it empty if the press is on a control that owns its own drags, such as a slider).
        /// <paramref name="handleOf"/>: the drawer whose handle was pressed, if any.</summary>
        public void Down(int pointerId, float xDp, float yDp, IReadOnlyList<IFrameBehaviour> candidates, IFrameBehaviour handleOf = null)
        {
            if (IsTracking) Cancel(PointerId);
            _candidates.Clear();
            if (candidates != null)
                foreach (var c in candidates) if (c.Style.Accepts(Kind)) _candidates.Add(c);
            if (_candidates.Count == 0 && handleOf == null) return;
            IsTracking = true;
            PointerId = pointerId;
            _handleOf = handleOf;
            _startX = _lastX = xDp;
            _startY = _lastY = yDp;
            _velocity.Clear();
            _velocity.Add(_env.Clock.Now, xDp, yDp);
            var focus = _candidates.Count > 0 ? _candidates[0] : handleOf;
            if (focus != null && focus.Style.Accepts(FrameInputKinds.Navigation)) _frames.Focus(focus);
        }

        /// <summary>A move. Returns true on the move that starts a frame drag, so the adapter can
        /// capture the pointer and cancel the press on any control under it.</summary>
        public bool Move(int pointerId, float xDp, float yDp)
        {
            if (!IsTracking || pointerId != PointerId) return false;
            _velocity.Add(_env.Clock.Now, xDp, yDp);
            if (IsDragging)
            {
                DragFrame.Execute(FrameCommand.DragMove(CommandDelta(DragFrame, _lastX, _lastY, xDp, yDp)));
                _lastX = xDp; _lastY = yDp;
                return false;
            }
            var dx = xDp - _startX;
            var dy = yDp - _startY;
            if (dx * dx + dy * dy < _env.DragSlopDp * _env.DragSlopDp) return false;
            var sx = _startX;
            var sy = _startY;
            var frame = FrameDragRouting.Choose(_candidates, f => f.Element.ToLocalDeltaDp(sx, sy, xDp, yDp), Kind);
            if (frame == null)
            {
                // Nothing moves this way: leave the pointer to the UI (e.g. a scroll view outside).
                IsTracking = false;
                return false;
            }
            DragFrame = frame;
            _handleOf = null;
            frame.Execute(FrameCommand.DragBegin);
            // Follow the pointer 1:1 from where it was pressed.
            frame.Execute(FrameCommand.DragMove(CommandDelta(frame, _startX, _startY, xDp, yDp)));
            _lastX = xDp; _lastY = yDp;
            return true;
        }

        public void Up(int pointerId, float xDp, float yDp)
        {
            if (!IsTracking || pointerId != PointerId) return;
            Move(pointerId, xDp, yDp);
            if (IsDragging)
            {
                var (vx, vy) = _velocity.Velocity(_env.Clock.Now);
                // Map a short step at the release point (not a far point), so perspective cameras
                // (world-space canvases) convert velocity correctly.
                const float step = 0.01f;
                var v = CommandDelta(DragFrame, _lastX, _lastY, _lastX + vx * step, _lastY + vy * step) / step;
                DragFrame.Execute(FrameCommand.DragEnd(v));
            }
            else if (_handleOf != null)
            {
                _handleOf.Execute(FrameCommand.Toggle);
            }
            Reset();
        }

        public void Cancel(int pointerId)
        {
            if (!IsTracking || pointerId != PointerId) return;
            if (IsDragging) DragFrame.Execute(FrameCommand.DragCancel);
            Reset();
        }

        /// <summary>A mouse wheel: <paramref name="ticksX"/>/<paramref name="ticksY"/> in notches,
        /// positive = towards later content (right / down). The innermost frame that scrolls takes
        /// it (a horizontal strip also takes a vertical wheel). Returns true if a frame used it.</summary>
        public bool Wheel(IReadOnlyList<IFrameBehaviour> innermostFirst, float ticksX, float ticksY)
        {
            if (innermostFirst == null) return false;
            for (var i = 0; i < innermostFirst.Count; i++)
            {
                var f = innermostFirst[i];
                if (!f.Style.Accepts(Kind)) continue;
                var ticks = f.DragAxis == FrameAxis.Horizontal ? (ticksX != 0f ? ticksX : ticksY) : ticksY;
                if (ticks == 0f) continue;
                if (f.Execute(FrameCommand.ScrollBy(ticks * _env.WheelStepDp)))
                {
                    if (f.Style.Accepts(FrameInputKinds.Navigation)) _frames.Focus(f);
                    return true;
                }
            }
            return false;
        }

        static float CommandDelta(IFrameBehaviour frame, float fromX, float fromY, float toX, float toY)
        {
            var (lx, ly) = frame.Element.ToLocalDeltaDp(fromX, fromY, toX, toY);
            return frame.ToCommandDelta(lx, ly);
        }

        void Reset()
        {
            IsTracking = false;
            DragFrame = null;
            _handleOf = null;
            _candidates.Clear();
        }
    }
}
