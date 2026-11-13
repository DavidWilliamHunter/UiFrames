using System;

namespace Threads.UiFrames
{
    /// <summary>Time source for frame animation, in seconds. Adapters pass one that reads Unity's
    /// time; tests pass a clock they step by hand. Nothing in the core reads a global clock.</summary>
    public interface IFrameClock
    {
        double Now { get; }
    }

    /// <summary>Easing curves (cubic).</summary>
    public static class Easing
    {
        public static float Apply(FrameEasing easing, float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            switch (easing)
            {
                case FrameEasing.Linear: return t;
                case FrameEasing.EaseIn: return t * t * t;
                case FrameEasing.EaseOut: { var u = 1f - t; return 1f - u * u * u; }
                case FrameEasing.EaseInOut:
                    return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f;
                default: return t;
            }
        }
    }

    /// <summary>The iOS-style rubber band: an overshoot <c>x</c> is shown as
    /// <c>L·(1 − 1/(x·c/L + 1))</c>, which never reaches the limit <c>L</c>.</summary>
    public static class RubberBand
    {
        public const float Coefficient = 0.55f;

        public static float Apply(float overshoot, float limit)
        {
            if (overshoot <= 0f || limit <= 0f) return 0f;
            return limit * (1f - 1f / (overshoot * Coefficient / limit + 1f));
        }

        public static float Inverse(float shown, float limit)
        {
            if (shown <= 0f || limit <= 0f) return 0f;
            var y = Math.Min(shown, limit * 0.999f);
            return limit / Coefficient * (1f / (1f - y / limit) - 1f);
        }
    }

    /// <summary>What an <see cref="AxisMotion"/> is doing.</summary>
    public enum MotionState
    {
        Idle,
        /// <summary>Following direct manipulation 1:1 (rubber-banded past the bounds).</summary>
        Dragging,
        /// <summary>Tweening to <see cref="AxisMotion.Target"/>.</summary>
        Animating,
        /// <summary>Moving on with decaying velocity after a release.</summary>
        Coasting,
    }

    /// <summary>
    /// A position on one axis, in dp, between <see cref="Min"/> and <see cref="Max"/>:
    /// followed 1:1 while dragging (rubber-banded at the ends), then tweened or coasted.
    /// The frame models are built from this. It never animates during a drag.
    /// </summary>
    public sealed class AxisMotion
    {
        /// <summary>Below this speed a coast stops.</summary>
        public const float MinCoastVelocityDps = 2f;

        readonly IFrameClock _clock;
        float _raw;
        double _startTime;
        float _from, _duration;
        FrameEasing _easing;
        float _velocity, _rate;

        public AxisMotion(IFrameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public float Min { get; private set; }
        public float Max { get; private set; }
        public float Position { get; private set; }
        public MotionState State { get; private set; }
        /// <summary>Where an animation is going; the position otherwise.</summary>
        public float Target { get; private set; }
        /// <summary>The largest distance the rubber band shows past an end.</summary>
        public float RubberBandLimitDp { get; set; } = 60f;
        public bool IsMoving => State != MotionState.Idle;

        /// <summary>Raised whenever <see cref="Position"/> changes.</summary>
        public event Action Changed;
        /// <summary>Raised when an animation or coast ends by itself.</summary>
        public event Action Settled;

        /// <summary>Sets the range. When idle the position is clamped into it.</summary>
        public void SetBounds(float min, float max)
        {
            if (max < min) max = min;
            Min = min;
            Max = max;
            if (State == MotionState.Idle) Set(Clamp(Position));
        }

        public float Clamp(float p) => p < Min ? Min : p > Max ? Max : p;

        /// <summary>Moves at once and stops any motion.</summary>
        public void JumpTo(float position)
        {
            State = MotionState.Idle;
            Target = position;
            Set(position);
        }

        /// <summary>Stops where it is.</summary>
        public void Stop()
        {
            State = MotionState.Idle;
            Target = Position;
        }

        public void BeginDrag()
        {
            if (State == MotionState.Dragging) return;
            // Grabbing a frame mid-animation continues from what is shown, including any overshoot.
            _raw = Unband(Position);
            State = MotionState.Dragging;
            Target = Position;
        }

        public void DragBy(float deltaDp)
        {
            if (State != MotionState.Dragging) BeginDrag();
            _raw += deltaDp;
            Target = Band(_raw);
            Set(Target);
        }

        /// <summary>Ends a drag without moving. The owner then calls <see cref="AnimateTo"/> or <see cref="Coast"/>.</summary>
        public void EndDrag()
        {
            if (State == MotionState.Dragging) State = MotionState.Idle;
        }

        public void AnimateTo(float target, float durationSeconds, FrameEasing easing)
        {
            Target = target;
            if (durationSeconds <= 0f || Math.Abs(target - Position) < 0.001f)
            {
                State = MotionState.Idle;
                Set(target);
                Settled?.Invoke();
                return;
            }
            _from = Position;
            _duration = durationSeconds;
            _easing = easing;
            _startTime = _clock.Now;
            State = MotionState.Animating;
        }

        /// <summary>Moves on with <paramref name="velocityDps"/>, keeping <paramref name="decelerationRate"/>
        /// of the speed each second. Stops at the bounds. Out of bounds, it springs back instead.</summary>
        public void Coast(float velocityDps, float decelerationRate, float settleDuration, FrameEasing easing)
        {
            if (Position < Min || Position > Max)
            {
                AnimateTo(Clamp(Position), settleDuration, easing);
                return;
            }
            if (Math.Abs(velocityDps) < MinCoastVelocityDps || decelerationRate <= 0f)
            {
                State = MotionState.Idle;
                Target = Position;
                Settled?.Invoke();
                return;
            }
            _from = Position;
            _velocity = velocityDps;
            _rate = Math.Min(decelerationRate, 0.9999f);
            _startTime = _clock.Now;
            Target = Clamp(ProjectedRest(velocityDps, decelerationRate));
            State = MotionState.Coasting;
        }

        /// <summary>Where a coast from here with this velocity would stop, ignoring bounds.</summary>
        public float ProjectedRest(float velocityDps, float decelerationRate)
        {
            if (decelerationRate <= 0f) return Position;
            var rate = Math.Min(decelerationRate, 0.9999f);
            return Position - velocityDps / (float)Math.Log(rate);
        }

        /// <summary>Advances animation and coasting to the clock's time.</summary>
        public void Update()
        {
            if (State == MotionState.Animating)
            {
                var t = (float)((_clock.Now - _startTime) / _duration);
                if (t >= 1f)
                {
                    State = MotionState.Idle;
                    Set(Target);
                    Settled?.Invoke();
                    return;
                }
                Set(_from + (Target - _from) * Easing.Apply(_easing, t));
            }
            else if (State == MotionState.Coasting)
            {
                var t = (float)(_clock.Now - _startTime);
                var decay = (float)Math.Pow(_rate, t);
                var p = _from + _velocity * (decay - 1f) / (float)Math.Log(_rate);
                var stopped = Math.Abs(_velocity * decay) < MinCoastVelocityDps;
                if (p <= Min || p >= Max || stopped)
                {
                    State = MotionState.Idle;
                    Set(Clamp(p));
                    Target = Position;
                    Settled?.Invoke();
                    return;
                }
                Set(p);
            }
        }

        float Band(float raw)
        {
            if (raw < Min) return Min - RubberBand.Apply(Min - raw, RubberBandLimitDp);
            if (raw > Max) return Max + RubberBand.Apply(raw - Max, RubberBandLimitDp);
            return raw;
        }

        float Unband(float shown)
        {
            if (shown < Min) return Min - RubberBand.Inverse(Min - shown, RubberBandLimitDp);
            if (shown > Max) return Max + RubberBand.Inverse(shown - Max, RubberBandLimitDp);
            return shown;
        }

        void Set(float p)
        {
            if (p == Position) return;
            Position = p;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Converts screen motion (dp, y up, the TouchController and Input System convention) into a
    /// model's positive direction. UI Toolkit panel coordinates are y down: flip y first.
    /// </summary>
    public static class FrameAxisMath
    {
        /// <summary>Drawer: positive = opening. A left drawer opens to the right, a bottom one upwards.</summary>
        public static float DrawerDelta(FrameEdge edge, float dx, float dy) => edge switch
        {
            FrameEdge.Left => dx,
            FrameEdge.Right => -dx,
            FrameEdge.Bottom => dy,
            FrameEdge.Top => -dy,
            _ => 0f,
        };

        /// <summary>Strip/deck: positive = later content. Dragging left or up reveals what follows
        /// (content runs left to right, and top to bottom).</summary>
        public static float ScrollDelta(FrameAxis axis, float dx, float dy) =>
            axis == FrameAxis.Horizontal ? -dx : dy;

        /// <summary>The axis a drawer moves along.</summary>
        public static FrameAxis DrawerAxis(FrameEdge edge) =>
            edge == FrameEdge.Top || edge == FrameEdge.Bottom ? FrameAxis.Vertical : FrameAxis.Horizontal;

        /// <summary>
        /// <c>--uf-edge: auto</c>: the handle sticks out on the side the drawer opens towards, so the
        /// drawer is attached to the opposite edge. <paramref name="dx"/>, <paramref name="dy"/> are
        /// the handle centre minus the drawer centre (y up).
        /// </summary>
        public static FrameEdge InferEdge(float dx, float dy)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0f ? FrameEdge.Left : FrameEdge.Right;
            return dy >= 0f ? FrameEdge.Bottom : FrameEdge.Top;
        }
    }
}
