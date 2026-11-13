using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary>
    /// One <see cref="FramePointerTracker"/> per pointer (mouse, pen, each finger), so several
    /// fingers can drag different frames at once. A frame already dragged by one pointer is not
    /// taken by another.
    /// </summary>
    public sealed class FramePointerTrackers
    {
        readonly FrameSet _frames;
        readonly FrameEnvironment _env;
        readonly FrameInputKinds _kind;
        readonly Dictionary<int, FramePointerTracker> _byPointer = new Dictionary<int, FramePointerTracker>();
        readonly List<FramePointerTracker> _free = new List<FramePointerTracker>();

        public FramePointerTrackers(FrameSet frames, FrameEnvironment environment, FrameInputKinds kind = FrameInputKinds.Pointer)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _env = environment ?? throw new ArgumentNullException(nameof(environment));
            _kind = kind;
            // Wheel events have no pointer of their own.
            Wheel = Make();
        }

        /// <summary>Handles wheel events (no press involved).</summary>
        public FramePointerTracker Wheel { get; }

        /// <summary>The tracker for <paramref name="pointerId"/>, created on first use.</summary>
        public FramePointerTracker For(int pointerId)
        {
            if (_byPointer.TryGetValue(pointerId, out var t)) return t;
            t = _free.Count > 0 ? Take() : Make();
            _byPointer.Add(pointerId, t);
            return t;
        }

        /// <summary>The tracker for <paramref name="pointerId"/> if it is tracking, else null.</summary>
        public FramePointerTracker Active(int pointerId) =>
            _byPointer.TryGetValue(pointerId, out var t) && t.IsTracking ? t : null;

        /// <summary>Returns idle trackers to the pool (call after a release or cancel).</summary>
        public void Release(int pointerId)
        {
            if (!_byPointer.TryGetValue(pointerId, out var t) || t.IsTracking) return;
            _byPointer.Remove(pointerId);
            _free.Add(t);
        }

        public IEnumerable<KeyValuePair<int, FramePointerTracker>> All => _byPointer;

        /// <summary>Cancels every press in progress.</summary>
        public void CancelAll()
        {
            foreach (var kv in new List<KeyValuePair<int, FramePointerTracker>>(_byPointer))
            {
                if (kv.Value.IsTracking) kv.Value.Cancel(kv.Key);
                Release(kv.Key);
            }
        }

        bool IsDragged(IFrameBehaviour frame)
        {
            foreach (var t in _byPointer.Values)
                if (t.DragFrame == frame) return true;
            return false;
        }

        FramePointerTracker Take()
        {
            var t = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);
            return t;
        }

        FramePointerTracker Make() => new FramePointerTracker(_frames, _env, _kind) { Busy = IsDragged };
    }
}
