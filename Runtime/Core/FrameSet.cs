using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary>
    /// The live frames of one app: what the binders created, which one has keyboard/gamepad focus,
    /// and command dispatch that bubbles to outer frames. Owned by your bootstrap (or
    /// <c>UiFramesRunner</c>) and shared by binders and controllers.
    /// </summary>
    public sealed class FrameSet
    {
        readonly List<IFrameBehaviour> _frames = new List<IFrameBehaviour>();
        readonly Dictionary<object, IFrameBehaviour> _byNative = new Dictionary<object, IFrameBehaviour>();
        // The key each frame was added under: a destroyed GameObject reports a null Native later.
        readonly Dictionary<IFrameBehaviour, object> _keys = new Dictionary<IFrameBehaviour, object>();

        public IReadOnlyList<IFrameBehaviour> Frames => _frames;
        /// <summary>The frame keyboard and gamepad commands go to (it carries <c>uf-focused</c>).</summary>
        public IFrameBehaviour Focused { get; private set; }

        public event Action<IFrameBehaviour> Added;
        public event Action<IFrameBehaviour> Removed;
        public event Action<IFrameBehaviour> FocusChanged;

        public void Add(IFrameBehaviour frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var key = frame.Element.Native ?? throw new ArgumentException("The frame's element has no native object.", nameof(frame));
            if (_byNative.ContainsKey(key)) throw new InvalidOperationException($"'{frame.Element.Name}' already has a frame.");
            _frames.Add(frame);
            _byNative.Add(key, frame);
            _keys.Add(frame, key);
            Added?.Invoke(frame);
        }

        /// <summary>Removes (does not dispose) a frame.</summary>
        public bool Remove(IFrameBehaviour frame)
        {
            if (frame == null || !_keys.TryGetValue(frame, out var key)) return false;
            _keys.Remove(frame);
            _byNative.Remove(key);
            _frames.Remove(frame);
            if (Focused == frame) Focus(null);
            Removed?.Invoke(frame);
            return true;
        }

        /// <summary>The frame created for a VisualElement or GameObject, or null.</summary>
        public IFrameBehaviour Find(object native) =>
            native != null && _byNative.TryGetValue(native, out var f) ? f : null;

        /// <summary>The nearest frame that contains <paramref name="frame"/> (its outer frame), or null.</summary>
        public IFrameBehaviour OuterOf(IFrameBehaviour frame)
        {
            for (var e = frame?.Element.Parent; e != null; e = e.Parent)
            {
                var f = Find(e.Native);
                if (f != null) return f;
            }
            return null;
        }

        public void Focus(IFrameBehaviour frame)
        {
            if (frame == Focused) return;
            Focused?.Element.SetClass(FrameTags.Focused, false);
            Focused = frame;
            Focused?.Element.SetClass(FrameTags.Focused, true);
            FocusChanged?.Invoke(frame);
        }

        /// <summary>Moves focus to the next (or previous) frame that accepts <paramref name="kind"/>, wrapping.</summary>
        public void CycleFocus(int direction, FrameInputKinds kind = FrameInputKinds.Navigation)
        {
            if (_frames.Count == 0) return;
            var start = Focused == null ? (direction > 0 ? -1 : 0) : _frames.IndexOf(Focused);
            for (var n = 1; n <= _frames.Count; n++)
            {
                var i = ((start + direction * n) % _frames.Count + _frames.Count) % _frames.Count;
                if (_frames[i].Style.Accepts(kind)) { Focus(_frames[i]); return; }
            }
        }

        /// <summary>Sends a command to <paramref name="frame"/>; if it does not handle it (or does not
        /// accept <paramref name="kind"/>), to its outer frames in turn. Returns the frame that handled it.</summary>
        public IFrameBehaviour Dispatch(IFrameBehaviour frame, in FrameCommand command, FrameInputKinds kind)
        {
            for (var f = frame; f != null; f = OuterOf(f))
                if (f.Style.Accepts(kind) && f.Execute(command)) return f;
            return null;
        }

        public void Update()
        {
            for (var i = 0; i < _frames.Count; i++) _frames[i].Update();
        }
    }

    /// <summary>Estimates release velocity from recent samples (dp, seconds), like Android's VelocityTracker.</summary>
    public sealed class VelocityTracker
    {
        /// <summary>Only samples this recent count.</summary>
        public const float WindowSeconds = 0.1f;
        /// <summary>No movement for this long means the pointer was held still: velocity 0.</summary>
        public const float StillSeconds = 0.04f;

        readonly double[] _t = new double[16];
        readonly float[] _x = new float[16];
        readonly float[] _y = new float[16];
        int _count, _head;

        public void Clear() { _count = 0; _head = 0; }

        public void Add(double time, float x, float y)
        {
            _head = (_head + 1) % _t.Length;
            _t[_head] = time; _x[_head] = x; _y[_head] = y;
            if (_count < _t.Length) _count++;
        }

        /// <summary>Velocity at <paramref name="now"/> in dp/s.</summary>
        public (float x, float y) Velocity(double now)
        {
            if (_count < 2) return (0f, 0f);
            var newest = _head;
            if (now - _t[newest] > StillSeconds) return (0f, 0f);
            var oldest = newest;
            for (var n = 1; n < _count; n++)
            {
                var i = (newest - n + _t.Length) % _t.Length;
                if (_t[newest] - _t[i] > WindowSeconds) break;
                oldest = i;
            }
            var dt = (float)(_t[newest] - _t[oldest]);
            if (dt <= 1e-4f) return (0f, 0f);
            return ((_x[newest] - _x[oldest]) / dt, (_y[newest] - _y[oldest]) / dt);
        }
    }

    /// <summary>Which frame takes a drag: shared by the pointer and touch controllers of both UI systems.</summary>
    public static class FrameDragRouting
    {
        /// <summary>
        /// The innermost frame that accepts <paramref name="kind"/> and for which the drag runs
        /// mainly along its axis, measured in that frame's own space by
        /// <paramref name="localDelta"/> (so a rotated frame judges by its own axes). A drag across
        /// a frame's axis passes to an outer frame (axis lock). <paramref name="innermostFirst"/>
        /// lists the frames under the press from the inside out. Returns null if none fits.
        /// </summary>
        public static IFrameBehaviour Choose(IReadOnlyList<IFrameBehaviour> innermostFirst,
            Func<IFrameBehaviour, (float x, float y)> localDelta, FrameInputKinds kind)
        {
            for (var i = 0; i < innermostFirst.Count; i++)
            {
                var f = innermostFirst[i];
                if (!f.Style.Accepts(kind)) continue;
                var (dx, dy) = localDelta(f);
                var axis = Math.Abs(dx) >= Math.Abs(dy) ? FrameAxis.Horizontal : FrameAxis.Vertical;
                if (f.DragAxis == axis) return f;
            }
            return null;
        }
    }
}
