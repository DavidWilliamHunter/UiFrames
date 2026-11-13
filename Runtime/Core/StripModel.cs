using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary>
    /// The measured layout of a strip or deck along its axis, in dp: the visible length, the
    /// content length, and where each card starts and how long it is. Adapters measure it from
    /// the UI and pass it in again when the layout changes.
    /// </summary>
    public sealed class CardLayout
    {
        public static readonly CardLayout Empty = new CardLayout(0f, 0f, Array.Empty<float>(), Array.Empty<float>());

        readonly float[] _starts;
        readonly float[] _sizes;

        public CardLayout(float viewportDp, float contentDp, IReadOnlyList<float> cardStarts, IReadOnlyList<float> cardSizes)
        {
            if (cardStarts == null) throw new ArgumentNullException(nameof(cardStarts));
            if (cardSizes == null) throw new ArgumentNullException(nameof(cardSizes));
            if (cardStarts.Count != cardSizes.Count) throw new ArgumentException("One size per card start.");
            ViewportDp = Math.Max(0f, viewportDp);
            ContentDp = Math.Max(0f, contentDp);
            _starts = new float[cardStarts.Count];
            _sizes = new float[cardSizes.Count];
            for (var i = 0; i < _starts.Length; i++) { _starts[i] = cardStarts[i]; _sizes[i] = cardSizes[i]; }
        }

        /// <summary>Equal cards with a gap between them.</summary>
        public static CardLayout Uniform(float viewportDp, int count, float cardDp, float gapDp = 0f)
        {
            var starts = new float[count];
            var sizes = new float[count];
            for (var i = 0; i < count; i++) { starts[i] = i * (cardDp + gapDp); sizes[i] = cardDp; }
            var content = count > 0 ? count * cardDp + (count - 1) * gapDp : 0f;
            return new CardLayout(viewportDp, content, starts, sizes);
        }

        public float ViewportDp { get; }
        public float ContentDp { get; }
        public int Count => _starts.Length;
        public float MaxOffset => Math.Max(0f, ContentDp - ViewportDp);

        /// <summary>Same measurements (within 0.01 dp).</summary>
        public bool SameAs(CardLayout other)
        {
            if (other == null || other.Count != Count) return false;
            if (Math.Abs(other.ViewportDp - ViewportDp) > 0.01f || Math.Abs(other.ContentDp - ContentDp) > 0.01f) return false;
            for (var i = 0; i < Count; i++)
                if (Math.Abs(other._starts[i] - _starts[i]) > 0.01f || Math.Abs(other._sizes[i] - _sizes[i]) > 0.01f) return false;
            return true;
        }
        public float Start(int i) => _starts[i];
        public float Size(int i) => _sizes[i];
        public float End(int i) => _starts[i] + _sizes[i];

        /// <summary>True if card <paramref name="i"/> is wholly inside the viewport at <paramref name="offset"/>.</summary>
        public bool IsFullyVisible(int i, float offset) =>
            _starts[i] >= offset - 0.5f && End(i) <= offset + ViewportDp + 0.5f;

        /// <summary>The card whose start is nearest to <paramref name="offset"/>, among 0..<paramref name="last"/>.</summary>
        public int Nearest(float offset, int last)
        {
            var best = 0;
            var bestDist = float.MaxValue;
            for (var i = 0; i <= last && i < Count; i++)
            {
                var d = Math.Abs(_starts[i] - offset);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }

    public sealed class StripOptions : FrameMotionOptions
    {
        public FrameSnap Snap = FrameSnap.None;
        /// <summary>Fraction of inertial speed left after one second (uGUI ScrollRect's default is 0.135).</summary>
        public float DecelerationRate = 0.135f;

        public StripOptions() { DurationSeconds = 0.16f; }

        public override void ApplyStyle(FrameStyle style)
        {
            base.ApplyStyle(style);
            if (style == null) return;
            if (style.TryGet(FrameProperties.Snap, out var snap)) Snap = snap;
            if (style.TryGet(FrameProperties.Deceleration, out var rate)) DecelerationRate = rate;
        }
    }

    /// <summary>
    /// A row or column of cards longer than its container. It follows a drag 1:1, then moves on
    /// with inertia and rubber-banded ends. With <c>--uf-snap: card</c> it settles on the card
    /// nearest to where the inertia would have stopped.
    /// </summary>
    public sealed class StripModel : IFrameCommandTarget
    {
        readonly AxisMotion _motion;

        public StripModel(IFrameClock clock, StripOptions options = null)
        {
            Options = options ?? new StripOptions();
            _motion = new AxisMotion(clock) { RubberBandLimitDp = Options.RubberBandLimitDp };
            _motion.Changed += () => OffsetChanged?.Invoke(OffsetDp);
            _motion.Settled += () => Settled?.Invoke();
        }

        public StripOptions Options { get; }
        public CardLayout Layout { get; private set; } = CardLayout.Empty;
        /// <summary>How far the content is scrolled, in dp (0 = start).</summary>
        public float OffsetDp => _motion.Position;
        public float MaxOffsetDp => Layout.MaxOffset;
        public bool IsDragging => _motion.State == MotionState.Dragging;
        public bool IsMoving => _motion.IsMoving;

        public event Action<float> OffsetChanged;
        public event Action<bool> DraggingChanged;
        public event Action Settled;

        public void ApplyStyle(FrameStyle style) => Options.ApplyStyle(style);

        public void SetLayout(CardLayout layout)
        {
            Layout = layout ?? CardLayout.Empty;
            _motion.SetBounds(0f, Layout.MaxOffset);
        }

        public void Update() => _motion.Update();

        /// <summary>Whether a step (+1 / −1) would move the strip.</summary>
        public bool CanStep(int direction)
        {
            var b = Base;
            return direction > 0 ? b < Layout.MaxOffset - 0.5f : direction < 0 && b > 0.5f;
        }

        /// <summary>Whether content from <paramref name="startDp"/> to <paramref name="endDp"/> is in view now.</summary>
        public bool IsRangeShown(float startDp, float endDp) =>
            startDp >= OffsetDp - 0.5f && endDp <= OffsetDp + Layout.ViewportDp + 0.5f;

        /// <summary>Scrolls the least distance that shows content from <paramref name="startDp"/> to
        /// <paramref name="endDp"/> (its start, if it is longer than the view).</summary>
        public void Reveal(float startDp, float endDp)
        {
            if (IsDragging) return;
            var b = Base;
            var target = b;
            if (startDp < b || endDp - startDp > Layout.ViewportDp) target = startDp;
            else if (endDp > b + Layout.ViewportDp) target = endDp - Layout.ViewportDp;
            target = _motion.Clamp(target);
            if (Math.Abs(target - b) > 0.5f) AnimateTo(target);
        }

        public bool Execute(in FrameCommand command)
        {
            var id = command.Id;
            if (id == FrameCommandId.DragBegin) BeginDrag();
            else if (id == FrameCommandId.DragMove)
            {
                BeginDrag();
                _motion.DragBy(command.DeltaDp);
            }
            else if (id == FrameCommandId.DragEnd) EndDrag(command.VelocityDps);
            else if (id == FrameCommandId.DragCancel) EndDrag(0f);
            else if (id == FrameCommandId.ScrollBy) ScrollBy(command.DeltaDp);
            else if (id == FrameCommandId.Next) Step(+1, page: false);
            else if (id == FrameCommandId.Previous) Step(-1, page: false);
            else if (id == FrameCommandId.PageNext) Step(+1, page: true);
            else if (id == FrameCommandId.PagePrevious) Step(-1, page: true);
            else if (id == FrameCommandId.Home) AnimateTo(0f);
            else if (id == FrameCommandId.End) AnimateTo(Layout.MaxOffset);
            else if (id == FrameCommandId.GoTo)
            {
                if (Layout.Count == 0) return false;
                AnimateTo(Layout.Start(Math.Max(0, Math.Min(command.Index, Layout.Count - 1))));
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
            _motion.EndDrag();
            DraggingChanged?.Invoke(false);
            if (Options.Snap == FrameSnap.Card && Layout.Count > 0)
            {
                var rest = _motion.Clamp(_motion.ProjectedRest(velocityDps, Options.DecelerationRate));
                AnimateTo(SnapPoint(rest));
            }
            else
            {
                _motion.Coast(velocityDps, Options.DecelerationRate, Options.DurationSeconds, Options.Easing);
            }
        }

        void ScrollBy(float deltaDp)
        {
            if (IsDragging || deltaDp == 0f) return;
            var from = Base;
            var target = _motion.Clamp(from + deltaDp);
            if (Options.Snap == FrameSnap.Card && Layout.Count > 0)
            {
                var snapped = SnapPoint(target);
                // A small wheel step must still move a snapping strip by one card.
                if (Math.Abs(snapped - from) < 0.5f) { Step(Math.Sign(deltaDp), page: false); return; }
                target = snapped;
            }
            AnimateTo(target);
        }

        void Step(int direction, bool page)
        {
            if (IsDragging) return;
            var from = Base;
            if (page || Layout.Count == 0)
            {
                AnimateTo(_motion.Clamp(from + direction * Layout.ViewportDp));
                return;
            }
            // The next card start beyond the current position, in that direction.
            float target = direction > 0 ? Layout.MaxOffset : 0f;
            if (direction > 0)
            {
                for (var i = 0; i < Layout.Count; i++)
                    if (Layout.Start(i) > from + 0.5f) { target = Layout.Start(i); break; }
            }
            else
            {
                for (var i = Layout.Count - 1; i >= 0; i--)
                    if (Layout.Start(i) < from - 0.5f) { target = Layout.Start(i); break; }
            }
            AnimateTo(_motion.Clamp(target));
        }

        /// <summary>Where the strip is going: the animation target, or where it is.</summary>
        float Base => _motion.State == MotionState.Animating ? _motion.Target : _motion.Position;

        float SnapPoint(float offset)
        {
            var i = Layout.Nearest(offset, Layout.Count - 1);
            // The end of the content is a rest point too, so the last cards can be reached.
            var cardPoint = _motion.Clamp(Layout.Start(i));
            return Math.Abs(Layout.MaxOffset - offset) < Math.Abs(cardPoint - offset) ? Layout.MaxOffset : cardPoint;
        }

        void AnimateTo(float target) => _motion.AnimateTo(target, Options.DurationSeconds, Options.Easing);
    }
}
