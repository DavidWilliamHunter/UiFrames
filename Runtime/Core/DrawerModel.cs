using System;

namespace Threads.UiFrames
{
    /// <summary>Settings shared by the frame models. Style values (<see cref="FrameStyle"/>) override them.</summary>
    public class FrameMotionOptions
    {
        public float DurationSeconds;
        public FrameEasing Easing = FrameEasing.EaseOut;
        /// <summary>Release speed at or above which a release counts as a fling (dp/s). The touch
        /// controller sets this from TouchSettings; other controllers keep the default.</summary>
        public float FlingThresholdDps = 400f;
        public float RubberBandLimitDp = 60f;

        public virtual void ApplyStyle(FrameStyle style)
        {
            if (style == null) return;
            if (style.TryGet(FrameProperties.Duration, out var d)) DurationSeconds = d;
            if (style.TryGet(FrameProperties.Easing, out var e)) Easing = e;
        }
    }

    public sealed class DrawerOptions : FrameMotionOptions
    {
        public DrawerOptions() { DurationSeconds = 0.22f; }
    }

    /// <summary>
    /// A drawer that slides in from an edge. <see cref="Progress"/> is 0 closed, 1 open, and goes a
    /// little past either end while rubber-banding. It follows a drag 1:1; on release it opens on
    /// a fling towards open, closes on a fling the other way, and otherwise goes to whichever
    /// state is nearer.
    /// </summary>
    public sealed class DrawerModel : IFrameCommandTarget
    {
        readonly AxisMotion _motion;
        float _extent;

        public DrawerModel(IFrameClock clock, DrawerOptions options = null)
        {
            Options = options ?? new DrawerOptions();
            _motion = new AxisMotion(clock) { RubberBandLimitDp = Options.RubberBandLimitDp };
            _motion.Changed += () => ProgressChanged?.Invoke(Progress);
            _motion.Settled += () => Settled?.Invoke();
        }

        public DrawerOptions Options { get; }

        /// <summary>How far the drawer travels from closed to open, in dp (its width or height).</summary>
        public float ExtentDp
        {
            get => _extent;
            set
            {
                _extent = Math.Max(0f, value);
                _motion.SetBounds(0f, _extent);
                // Layout changed (e.g. rotation): keep the open/closed state, retarget any animation.
                if (_motion.State == MotionState.Idle) _motion.JumpTo(IsOpen ? _extent : 0f);
                else if (_motion.State != MotionState.Dragging) _motion.AnimateTo(IsOpen ? _extent : 0f, Options.DurationSeconds, Options.Easing);
            }
        }

        /// <summary>Where the drawer is shown: 0 closed, 1 open.</summary>
        public float Progress => _extent > 0f ? _motion.Position / _extent : IsOpen ? 1f : 0f;
        /// <summary>Offset from the closed position, in dp.</summary>
        public float OffsetDp => _motion.Position;
        /// <summary>The state it is in or going to.</summary>
        public bool IsOpen { get; private set; }
        public bool IsDragging => _motion.State == MotionState.Dragging;
        public bool IsAnimating => _motion.State == MotionState.Animating;

        public event Action<float> ProgressChanged;
        /// <summary>Raised when <see cref="IsOpen"/> changes (at the decision, before the animation ends).</summary>
        public event Action<bool> OpenChanged;
        public event Action<bool> DraggingChanged;
        public event Action Settled;

        public void ApplyStyle(FrameStyle style) => Options.ApplyStyle(style);

        public void Open(bool animate = true) => SetOpen(true, animate);
        public void Close(bool animate = true) => SetOpen(false, animate);
        public void Toggle(bool animate = true) => SetOpen(!IsOpen, animate);

        public void Update() => _motion.Update();

        public bool Execute(in FrameCommand command)
        {
            var id = command.Id;
            if (id == FrameCommandId.Open) Open();
            else if (id == FrameCommandId.Close) Close();
            else if (id == FrameCommandId.Toggle) Toggle();
            else if (id == FrameCommandId.DragBegin) BeginDrag();
            else if (id == FrameCommandId.DragMove)
            {
                BeginDrag();
                _motion.DragBy(command.DeltaDp);
            }
            else if (id == FrameCommandId.DragEnd) EndDrag(command.VelocityDps);
            else if (id == FrameCommandId.DragCancel)
            {
                if (!IsDragging) return true;
                EndDragState();
                SetOpen(IsOpen, true, force: true);
            }
            else return false;
            return true;
        }

        void BeginDrag()
        {
            if (IsDragging) return;
            _motion.BeginDrag();
            DraggingChanged?.Invoke(true);
        }

        void EndDrag(float velocityDps)
        {
            if (!IsDragging) return;
            EndDragState();
            bool open;
            if (velocityDps >= Options.FlingThresholdDps) open = true;
            else if (velocityDps <= -Options.FlingThresholdDps) open = false;
            else open = Progress >= 0.5f;
            SetOpen(open, true, force: true);
        }

        void EndDragState()
        {
            _motion.EndDrag();
            DraggingChanged?.Invoke(false);
        }

        void SetOpen(bool open, bool animate, bool force = false)
        {
            if (IsDragging) return; // A drag owns the drawer; the release decides.
            var changed = open != IsOpen;
            if (!changed && !force && !_motion.IsMoving && _motion.Position == (open ? _extent : 0f)) return;
            IsOpen = open;
            var target = open ? _extent : 0f;
            if (animate) _motion.AnimateTo(target, Options.DurationSeconds, Options.Easing);
            else { _motion.JumpTo(target); Settled?.Invoke(); }
            if (changed) OpenChanged?.Invoke(open);
        }
    }
}
