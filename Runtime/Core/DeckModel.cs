using System;

namespace Threads.UiFrames
{
    public sealed class DeckOptions : FrameMotionOptions
    {
        public FrameDeckStep Step = FrameDeckStep.Card;

        public DeckOptions() { DurationSeconds = 0.16f; }

        public override void ApplyStyle(FrameStyle style)
        {
            base.ApplyStyle(style);
            if (style != null && style.TryGet(FrameProperties.DeckStep, out var step)) Step = step;
        }
    }

    /// <summary>
    /// A row of cards that always comes to rest on a card start, so it rests showing whole cards
    /// only (adapters hide a card that would be cut off at the far end while
    /// <see cref="IsMoving"/> is false; see <see cref="IsCardShownAtRest"/>). It follows a drag;
    /// on release a fling steps by a card or a page (<c>--uf-deck-step</c>) and a slow drag snaps
    /// to the nearest card.
    /// </summary>
    public sealed class DeckModel : IFrameCommandTarget
    {
        readonly AxisMotion _motion;

        public DeckModel(IFrameClock clock, DeckOptions options = null)
        {
            Options = options ?? new DeckOptions();
            _motion = new AxisMotion(clock) { RubberBandLimitDp = Options.RubberBandLimitDp };
            _motion.Changed += () => OffsetChanged?.Invoke(OffsetDp);
            _motion.Settled += () => Settled?.Invoke();
        }

        public DeckOptions Options { get; }
        public CardLayout Layout { get; private set; } = CardLayout.Empty;
        public int Count => Layout.Count;
        /// <summary>The card the deck rests on, or is going to.</summary>
        public int Index { get; private set; }
        /// <summary>The highest index the deck can rest on: past it, the cards that are left all fit.</summary>
        public int LastIndex { get; private set; }
        public float OffsetDp => _motion.Position;
        public bool IsDragging => _motion.State == MotionState.Dragging;
        public bool IsMoving => _motion.IsMoving;

        public event Action<int> IndexChanged;
        public event Action<float> OffsetChanged;
        public event Action<bool> DraggingChanged;
        public event Action Settled;

        public void ApplyStyle(FrameStyle style) => Options.ApplyStyle(style);

        public void SetLayout(CardLayout layout)
        {
            Layout = layout ?? CardLayout.Empty;
            LastIndex = 0;
            for (var i = 0; i < Layout.Count; i++)
            {
                LastIndex = i;
                if (Layout.Start(i) >= Layout.MaxOffset - 0.5f) break;
            }
            // Bounds run to the last rest point, which may leave a gap at the end rather than cut
            // a card at the start.
            _motion.SetBounds(0f, Layout.Count > 0 ? Layout.Start(LastIndex) : 0f);
            var index = Math.Min(Index, LastIndex);
            if (!IsDragging) _motion.JumpTo(RestPoint(index));
            SetIndex(index);
        }

        /// <summary>Cards shown while resting at <see cref="Index"/>: those wholly in view.</summary>
        public int VisibleCount
        {
            get
            {
                var n = 0;
                for (var i = Index; i < Layout.Count; i++)
                {
                    if (!Layout.IsFullyVisible(i, RestPoint(Index))) break;
                    n++;
                }
                return Math.Max(1, n);
            }
        }

        /// <summary>Whether card <paramref name="i"/> should be shown: always while moving (cards are
        /// only cut off during an animation); at rest, unless the view would cut it off. Cards wholly
        /// outside the view stay shown (they are clipped anyway) so keyboard and gamepad navigation
        /// can still move focus to them, which steps the deck.</summary>
        public bool IsCardShownAtRest(int i)
        {
            if (IsMoving || Layout.IsFullyVisible(i, OffsetDp)) return true;
            var viewEnd = OffsetDp + Layout.ViewportDp;
            return Layout.End(i) <= OffsetDp + 0.5f || Layout.Start(i) >= viewEnd - 0.5f;
        }

        public void GoTo(int index, bool animate = true)
        {
            if (Layout.Count == 0 || IsDragging) return;
            index = Math.Max(0, Math.Min(index, LastIndex));
            SetIndex(index);
            if (animate) _motion.AnimateTo(RestPoint(index), Options.DurationSeconds, Options.Easing);
            else { _motion.JumpTo(RestPoint(index)); Settled?.Invoke(); }
        }

        /// <summary>Whether a step (+1 / −1) would move the deck.</summary>
        public bool CanStep(int direction) => direction > 0 ? Index < LastIndex : direction < 0 && Index > 0;

        /// <summary>Steps the least distance that shows card <paramref name="card"/> whole.</summary>
        public void Reveal(int card)
        {
            if (card < 0 || card >= Count || IsDragging) return;
            if (Layout.IsFullyVisible(card, RestPoint(Index))) return;
            if (card < Index) { GoTo(card); return; }
            for (var j = Index + 1; j <= LastIndex; j++)
            {
                if (!Layout.IsFullyVisible(card, RestPoint(j))) continue;
                GoTo(j);
                return;
            }
            GoTo(Math.Min(card, LastIndex));
        }

        public void Next() => GoTo(Index + StepSize);
        public void Previous() => GoTo(Index - StepSize);

        public void Update() => _motion.Update();

        public bool Execute(in FrameCommand command)
        {
            var id = command.Id;
            if (id == FrameCommandId.Next) Next();
            else if (id == FrameCommandId.Previous) Previous();
            else if (id == FrameCommandId.PageNext) GoTo(Index + VisibleCount);
            else if (id == FrameCommandId.PagePrevious) GoTo(Index - VisibleCount);
            else if (id == FrameCommandId.Home) GoTo(0);
            else if (id == FrameCommandId.End) GoTo(LastIndex);
            else if (id == FrameCommandId.GoTo) GoTo(command.Index);
            else if (id == FrameCommandId.ScrollBy)
            {
                // A wheel notch or scroll key steps the deck; it never rests between cards.
                if (command.DeltaDp > 0f) Next();
                else if (command.DeltaDp < 0f) Previous();
            }
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
                _motion.EndDrag();
                DraggingChanged?.Invoke(false);
                GoTo(Index);
            }
            else return false;
            return true;
        }

        int StepSize => Options.Step == FrameDeckStep.Page ? VisibleCount : 1;

        float RestPoint(int index) => Layout.Count == 0 ? 0f : Layout.Start(index);

        void BeginDrag()
        {
            if (IsDragging) return;
            _motion.BeginDrag();
            DraggingChanged?.Invoke(true);
        }

        void EndDrag(float velocityDps)
        {
            if (!IsDragging) return;
            var step = StepSize; // measured at the index the drag started from
            _motion.EndDrag();
            DraggingChanged?.Invoke(false);
            if (Layout.Count == 0) return;
            var p = _motion.Position;
            int target;
            if (velocityDps >= Options.FlingThresholdDps) target = Floor(p) + step;
            else if (velocityDps <= -Options.FlingThresholdDps) target = Ceil(p) - step;
            else target = Layout.Nearest(p, LastIndex);
            GoTo(target);
        }

        /// <summary>The last rest point at or before <paramref name="p"/>.</summary>
        int Floor(float p)
        {
            var r = 0;
            for (var i = 0; i <= LastIndex; i++) if (RestPoint(i) <= p + 0.5f) r = i;
            return r;
        }

        /// <summary>The first rest point at or after <paramref name="p"/>.</summary>
        int Ceil(float p)
        {
            for (var i = 0; i <= LastIndex; i++) if (RestPoint(i) >= p - 0.5f) return i;
            return LastIndex;
        }

        void SetIndex(int index)
        {
            if (index == Index) return;
            Index = index;
            IndexChanged?.Invoke(index);
        }
    }
}
