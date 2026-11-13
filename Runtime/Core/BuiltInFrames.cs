using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary><c>.uf-drawer</c> (with an optional <c>.uf-drawer-handle</c> child): reads
    /// <c>--uf-edge</c>, <c>--uf-duration</c>, <c>--uf-easing</c>, <c>--uf-input</c>.</summary>
    public sealed class DrawerKind : IFrameKind
    {
        static readonly FrameProperty[] s_properties =
            { FrameProperties.Edge, FrameProperties.Duration, FrameProperties.Easing, FrameProperties.Input };

        public string MarkerClass => FrameTags.Drawer;
        public IReadOnlyList<FrameProperty> Properties => s_properties;
        public IFrameBehaviour Create(FrameContext context) => new DrawerFrame(context);
    }

    /// <summary><c>.uf-card-strip</c> with a <c>.uf-cards</c> content element: reads <c>--uf-axis</c>,
    /// <c>--uf-snap</c>, <c>--uf-deceleration</c>, <c>--uf-duration</c>, <c>--uf-easing</c>, <c>--uf-input</c>.</summary>
    public sealed class StripKind : IFrameKind
    {
        static readonly FrameProperty[] s_properties =
        {
            FrameProperties.Axis, FrameProperties.Snap, FrameProperties.Deceleration, FrameProperties.VisibleCards,
            FrameProperties.Duration, FrameProperties.Easing, FrameProperties.Input,
        };

        public string MarkerClass => FrameTags.CardStrip;
        public IReadOnlyList<FrameProperty> Properties => s_properties;
        public IFrameBehaviour Create(FrameContext context) => new StripFrame(context);
    }

    /// <summary><c>.uf-card-deck</c> with a <c>.uf-cards</c> content element: reads <c>--uf-axis</c>,
    /// <c>--uf-deck-step</c>, <c>--uf-duration</c>, <c>--uf-easing</c>, <c>--uf-input</c>.</summary>
    public sealed class DeckKind : IFrameKind
    {
        static readonly FrameProperty[] s_properties =
        {
            FrameProperties.Axis, FrameProperties.DeckStep, FrameProperties.VisibleCards,
            FrameProperties.Duration, FrameProperties.Easing, FrameProperties.Input,
        };

        public string MarkerClass => FrameTags.CardDeck;
        public IReadOnlyList<FrameProperty> Properties => s_properties;
        public IFrameBehaviour Create(FrameContext context) => new DeckFrame(context);
    }

    /// <summary>
    /// A drawer. Lay the element out in its <b>open</b> position against its edge; when closed it is
    /// moved off by its own width or height (a handle placed outside its bounds stays in view).
    /// While closed and at rest its contents (all children but the handle) are hidden, so they
    /// cannot be focused or pressed off screen. UI focus moving inside opens it.
    /// Sets <c>uf-open</c> and <c>uf-dragging</c>.
    /// </summary>
    public sealed class DrawerFrame : IFrameBehaviour, IFrameNavigable
    {
        public DrawerFrame(FrameContext context)
        {
            Element = context.Element;
            var env = context.Environment;
            Model = new DrawerModel(context.Clock, new DrawerOptions
            {
                FlingThresholdDps = env.FlingThresholdDps,
                RubberBandLimitDp = env.RubberBandLimitDp,
            });
            Model.ProgressChanged += OnProgress;
            Model.OpenChanged += OnOpenChanged;
            Model.DraggingChanged += OnDraggingChanged;
            Model.Settled += UpdateContentVisibility;
            OnLayoutChanged();
        }

        public IFrameElement Element { get; }
        public DrawerModel Model { get; }
        public FrameStyle Style { get; private set; } = new FrameStyle();
        /// <summary>The edge in use (<c>auto</c> resolved).</summary>
        public FrameEdge Edge { get; private set; } = FrameEdge.Left;
        public bool IsOpen => Model.IsOpen;
        public FrameAxis DragAxis => FrameAxisMath.DrawerAxis(Edge);
        /// <summary>The <c>.uf-drawer-handle</c>, or null.</summary>
        public IFrameElement Handle => Element.FindDescendant(FrameTags.DrawerHandle);

        public bool IsMoving => Model.IsDragging || Model.IsAnimating;
        public bool CanStep(int direction) => false;

        /// <summary>The handle is always shown; the rest only while open.</summary>
        public bool IsShown(IFrameElement descendant) => IsInHandle(descendant) || (Model.IsOpen && !IsMoving);

        public float ToCommandDelta(float dx, float dy) => FrameAxisMath.DrawerDelta(Edge, dx, dy);

        /// <summary>Re-applying the same style (UI Toolkit re-resolves styles whenever a state class
        /// changes) does nothing, so animations are not disturbed.</summary>
        public void ApplyStyle(FrameStyle style)
        {
            style = style ?? new FrameStyle();
            if (style.SameAs(Style)) return;
            Style = style.Clone();
            Model.ApplyStyle(Style);
            OnLayoutChanged();
        }

        public void OnLayoutChanged()
        {
            Edge = ResolveEdge();
            Model.ExtentDp = Element.LayoutRectDp.Size(DragAxis);
            Place();
            UpdateContentVisibility();
        }

        public bool Execute(in FrameCommand command)
        {
            if (command.Id == FrameCommandId.Reveal)
            {
                if (!(command.Payload is IFrameElement target) || !FrameGeometry.IsInside(target, Element)) return false;
                if (!IsInHandle(target) && !Model.IsOpen) Model.Open();
                return true;
            }
            return Model.Execute(command);
        }

        public void Update() => Model.Update();

        public void Dispose()
        {
            Model.ProgressChanged -= OnProgress;
            Model.OpenChanged -= OnOpenChanged;
            Model.DraggingChanged -= OnDraggingChanged;
            Model.Settled -= UpdateContentVisibility;
            Element.SetTranslationDp(0f, 0f);
            Element.SetClass(FrameTags.Open, false);
            Element.SetClass(FrameTags.Dragging, false);
            SetContentVisible(true);
        }

        bool IsInHandle(IFrameElement e)
        {
            for (var p = e; p != null && p != Element; p = p.Parent)
                if (p.HasClass(FrameTags.DrawerHandle)) return true;
            return false;
        }

        FrameEdge ResolveEdge()
        {
            var edge = Style.Get(FrameProperties.Edge, FrameEdge.Auto);
            if (edge != FrameEdge.Auto) return edge;
            var handle = Handle;
            if (handle == null) return FrameEdge.Left;
            var r = FrameGeometry.RectIn(handle, Element);
            var size = Element.LayoutRectDp;
            // Layout space is y down; InferEdge takes y up.
            return FrameAxisMath.InferEdge(r.CentreX - size.Width * 0.5f, -(r.CentreY - size.Height * 0.5f));
        }

        void Place()
        {
            var hidden = Model.ExtentDp - Model.OffsetDp; // dp still off screen
            switch (Edge)
            {
                case FrameEdge.Right: Element.SetTranslationDp(hidden, 0f); break;
                case FrameEdge.Top: Element.SetTranslationDp(0f, -hidden); break;
                case FrameEdge.Bottom: Element.SetTranslationDp(0f, hidden); break;
                default: Element.SetTranslationDp(-hidden, 0f); break;
            }
        }

        void UpdateContentVisibility() => SetContentVisible(Model.IsOpen || IsMoving || Model.OffsetDp > 0.5f);

        void SetContentVisible(bool visible)
        {
            foreach (var child in Element.Children)
                if (!child.HasClass(FrameTags.DrawerHandle) && child.FindDescendant(FrameTags.DrawerHandle) == null)
                    child.SetVisible(visible);
        }

        void OnProgress(float _)
        {
            Place();
            if (Model.OffsetDp > 0.5f) SetContentVisible(true);
        }

        void OnOpenChanged(bool open)
        {
            Element.SetClass(FrameTags.Open, open);
            UpdateContentVisibility();
        }

        void OnDraggingChanged(bool dragging)
        {
            Element.SetClass(FrameTags.Dragging, dragging);
            UpdateContentVisibility();
        }
    }

    /// <summary>Measures and moves the <c>.uf-cards</c> content of a strip or deck, and sets
    /// <c>uf-horizontal</c> / <c>uf-vertical</c> from <c>--uf-axis</c>.</summary>
    public abstract class CardFrameBase : IFrameBehaviour, IFrameNavigable
    {
        protected CardFrameBase(FrameContext context)
        {
            Element = context.Element;
            Environment = context.Environment;
        }

        public IFrameElement Element { get; }
        protected FrameEnvironment Environment { get; }
        /// <summary>The <c>.uf-cards</c> element, or null if the markup has none.</summary>
        public IFrameElement Content { get; private set; }
        public FrameStyle Style { get; private set; } = new FrameStyle();
        public FrameAxis Axis => Style.Get(FrameProperties.Axis, FrameAxis.Horizontal);
        public FrameAxis DragAxis => Axis;
        protected abstract float OffsetDp { get; }

        public float ToCommandDelta(float dx, float dy) => FrameAxisMath.ScrollDelta(Axis, dx, dy);

        public abstract bool IsMoving { get; }
        public abstract bool CanStep(int direction);

        public bool IsShown(IFrameElement descendant)
        {
            if (Content == null || !FrameGeometry.IsInside(descendant, Content) || descendant == Content) return true;
            return IsCardRangeShown(FrameGeometry.RectIn(descendant, Content), CardIndexOf(descendant));
        }

        /// <summary>Re-applying the same style (UI Toolkit re-resolves styles whenever a state class
        /// changes) does nothing, so animations are not disturbed.</summary>
        public void ApplyStyle(FrameStyle style)
        {
            style = style ?? new FrameStyle();
            if (style.SameAs(Style)) return;
            var axisChanged = style.Get(FrameProperties.Axis, FrameAxis.Horizontal) != Axis;
            Style = style.Clone();
            ApplyModelStyle(Style);
            if (axisChanged) _layout = null;
            OnLayoutChanged();
        }

        CardLayout _layout;

        /// <summary>Measures again; does nothing more if nothing moved (so a style or rescan pass
        /// does not interrupt an animation).</summary>
        public void OnLayoutChanged()
        {
            Element.SetClass(FrameTags.Horizontal, Axis == FrameAxis.Horizontal);
            Element.SetClass(FrameTags.Vertical, Axis == FrameAxis.Vertical);
            var content = Element.FindDescendant(FrameTags.Cards);
            if (content != Content)
            {
                Content?.SetTranslationDp(0f, 0f);
                Content = content;
                _layout = null;
            }
            var layout = Measure();
            if (FitVisibleCards(layout)) return; // sizes changed: the next layout pass measures again
            if (_layout != null && layout.SameAs(_layout)) return;
            _layout = layout;
            SetLayout(layout);
            Place();
            CheckWholeCards(layout);
        }

        /// <summary><c>--uf-visible-cards: N</c>: make each card (view − (N−1) gaps) / N long.
        /// Returns true if any card was resized.</summary>
        bool FitVisibleCards(CardLayout layout)
        {
            if (!Style.TryGet(FrameProperties.VisibleCards, out var n) || n < 1 || layout.Count == 0 || layout.ViewportDp <= 0f) return false;
            var gap = layout.Count > 1 ? Math.Max(0f, layout.Start(1) - layout.End(0)) : 0f;
            var size = (layout.ViewportDp - (n - 1) * gap) / n;
            if (size <= 1f) return false;
            var changed = false;
            var cards = Content.Children;
            for (var i = 0; i < cards.Count && i < layout.Count; i++)
            {
                if (Math.Abs(layout.Size(i) - size) <= 0.5f) continue;
                cards[i].SetSizeDp(Axis, size);
                changed = true;
            }
            return changed;
        }

        bool _warnedWholeCards;

        /// <summary>A deck whose view is not a whole number of (equal) cards rests with a gap or a
        /// cut-off card hidden: say so once, and how to fix it.</summary>
        void CheckWholeCards(CardLayout layout)
        {
            if (!(this is DeckFrame) || _warnedWholeCards || Style.Has(FrameProperties.VisibleCards) || layout.Count < 2) return;
            var size = layout.Size(0);
            var pitch = layout.Start(1) - layout.Start(0);
            for (var i = 1; i < layout.Count; i++)
                if (Math.Abs(layout.Size(i) - size) > 0.5f) return; // uneven cards: nothing simple to say
            if (pitch <= 0f) return;
            var fit = (layout.ViewportDp + (pitch - size)) / pitch; // cards that fit, counting the gaps
            if (Math.Abs(fit - Math.Round(fit)) < 0.02f) return;
            _warnedWholeCards = true;
            Environment.Warn?.Invoke($"UI Frames: deck '{Element.Name}' has room for {fit:0.##} cards, so it rests with part of its view empty. " +
                                     $"Set --uf-visible-cards (e.g. {Math.Max(1, (int)Math.Round(fit))}) or size it to a whole number of cards.");
        }

        public bool Execute(in FrameCommand command)
        {
            if (command.Id != FrameCommandId.Reveal) return ExecuteModel(command);
            if (!(command.Payload is IFrameElement target) || Content == null || !FrameGeometry.IsInside(target, Element)) return false;
            if (target != Content && FrameGeometry.IsInside(target, Content))
                Reveal(FrameGeometry.RectIn(target, Content), CardIndexOf(target));
            return true;
        }

        public abstract void Update();

        public virtual void Dispose()
        {
            Content?.SetTranslationDp(0f, 0f);
            Element.SetClass(FrameTags.Dragging, false);
            Element.SetClass(FrameTags.Horizontal, false);
            Element.SetClass(FrameTags.Vertical, false);
        }

        protected abstract bool ExecuteModel(in FrameCommand command);
        protected abstract void ApplyModelStyle(FrameStyle style);
        protected abstract void SetLayout(CardLayout layout);
        /// <summary>Bring a rect in content space (inside card <paramref name="card"/>) into view.</summary>
        protected abstract void Reveal(FrameRect rectInContent, int card);
        protected abstract bool IsCardRangeShown(FrameRect rectInContent, int card);

        /// <summary>Index of the card (child of the content) that contains <paramref name="descendant"/>, or −1.</summary>
        protected int CardIndexOf(IFrameElement descendant)
        {
            for (var e = descendant; e != null; e = e.Parent)
            {
                if (e.Parent != Content) continue;
                var cards = Content.Children;
                for (var i = 0; i < cards.Count; i++) if (cards[i] == e) return i;
                return -1;
            }
            return -1;
        }

        protected void Place()
        {
            if (Content == null) return;
            var o = OffsetDp;
            if (Axis == FrameAxis.Horizontal) Content.SetTranslationDp(-o, 0f);
            else Content.SetTranslationDp(0f, -o);
        }

        protected void OnDraggingChanged(bool dragging) => Element.SetClass(FrameTags.Dragging, dragging);

        CardLayout Measure()
        {
            if (Content == null) return CardLayout.Empty;
            var axis = Axis;
            var cards = Content.Children;
            var starts = new float[cards.Count];
            var sizes = new float[cards.Count];
            var contentLength = Content.LayoutRectDp.Size(axis);
            for (var i = 0; i < cards.Count; i++)
            {
                var r = cards[i].LayoutRectDp;
                starts[i] = r.Start(axis);
                sizes[i] = r.Size(axis);
                contentLength = Math.Max(contentLength, r.End(axis));
            }
            return new CardLayout(Element.LayoutRectDp.Size(axis), contentLength, starts, sizes);
        }
    }

    /// <summary>A card strip: drag, inertia, rubber-band ends, optional snap. Sets <c>uf-dragging</c>.</summary>
    public sealed class StripFrame : CardFrameBase
    {
        public StripFrame(FrameContext context) : base(context)
        {
            Model = new StripModel(context.Clock, new StripOptions
            {
                FlingThresholdDps = Environment.FlingThresholdDps,
                RubberBandLimitDp = Environment.RubberBandLimitDp,
            });
            Model.OffsetChanged += OnOffset;
            Model.DraggingChanged += OnDraggingChanged;
            OnLayoutChanged();
        }

        public StripModel Model { get; }
        protected override float OffsetDp => Model.OffsetDp;
        public override bool IsMoving => Model.IsMoving;
        public override bool CanStep(int direction) => Model.CanStep(direction);

        protected override bool ExecuteModel(in FrameCommand command) => Model.Execute(command);
        public override void Update() => Model.Update();

        protected override void Reveal(FrameRect r, int card) => Model.Reveal(r.Start(Axis), r.End(Axis));
        protected override bool IsCardRangeShown(FrameRect r, int card) => Model.IsRangeShown(r.Start(Axis), r.End(Axis));

        public override void Dispose()
        {
            Model.OffsetChanged -= OnOffset;
            Model.DraggingChanged -= OnDraggingChanged;
            base.Dispose();
        }

        protected override void ApplyModelStyle(FrameStyle style) => Model.ApplyStyle(style);
        protected override void SetLayout(CardLayout layout) => Model.SetLayout(layout);
        void OnOffset(float _) => Place();
    }

    /// <summary>A card deck: steps by card or page and rests on whole cards only; a card cut off
    /// at rest is hidden. Sets <c>uf-dragging</c>.</summary>
    public sealed class DeckFrame : CardFrameBase
    {
        public DeckFrame(FrameContext context) : base(context)
        {
            Model = new DeckModel(context.Clock, new DeckOptions
            {
                FlingThresholdDps = Environment.FlingThresholdDps,
                RubberBandLimitDp = Environment.RubberBandLimitDp,
            });
            Model.OffsetChanged += OnOffset;
            Model.DraggingChanged += OnDragging;
            Model.Settled += UpdateCardVisibility;
            OnLayoutChanged();
        }

        public DeckModel Model { get; }
        public int Index => Model.Index;
        protected override float OffsetDp => Model.OffsetDp;

        public override bool IsMoving => Model.IsMoving;
        public override bool CanStep(int direction) => Model.CanStep(direction);

        protected override bool ExecuteModel(in FrameCommand command)
        {
            var handled = Model.Execute(command);
            UpdateCardVisibility(); // a step starts moving, so cut-off cards show during it
            return handled;
        }

        protected override void Reveal(FrameRect r, int card)
        {
            if (card < 0) return;
            Model.Reveal(card);
            UpdateCardVisibility();
        }

        protected override bool IsCardRangeShown(FrameRect r, int card) =>
            card < 0 || (!Model.IsMoving && Model.Layout.IsFullyVisible(card, Model.OffsetDp));

        public override void Update() => Model.Update();

        public override void Dispose()
        {
            Model.OffsetChanged -= OnOffset;
            Model.DraggingChanged -= OnDragging;
            Model.Settled -= UpdateCardVisibility;
            if (Content != null) foreach (var c in Content.Children) c.SetVisible(true);
            base.Dispose();
        }

        protected override void ApplyModelStyle(FrameStyle style) => Model.ApplyStyle(style);

        protected override void SetLayout(CardLayout layout)
        {
            Model.SetLayout(layout);
            UpdateCardVisibility();
        }

        void OnOffset(float _) => Place();

        void OnDragging(bool dragging)
        {
            OnDraggingChanged(dragging);
            UpdateCardVisibility();
        }

        void UpdateCardVisibility()
        {
            if (Content == null) return;
            var cards = Content.Children;
            for (var i = 0; i < cards.Count && i < Model.Count; i++) cards[i].SetVisible(Model.IsCardShownAtRest(i));
        }
    }

    public static class FrameGeometry
    {
        /// <summary>Whether <paramref name="element"/> is <paramref name="ancestor"/> or inside it.</summary>
        public static bool IsInside(IFrameElement element, IFrameElement ancestor)
        {
            for (var e = element; e != null; e = e.Parent) if (e == ancestor) return true;
            return false;
        }

        /// <summary>The layout rect of <paramref name="element"/> relative to its ancestor
        /// <paramref name="ancestor"/> (sum of layout rects on the way up; translations ignored).</summary>
        public static FrameRect RectIn(IFrameElement element, IFrameElement ancestor)
        {
            var r = element.LayoutRectDp;
            float x = r.X, y = r.Y;
            for (var p = element.Parent; p != null && p != ancestor; p = p.Parent)
            {
                x += p.LayoutRectDp.X;
                y += p.LayoutRectDp.Y;
            }
            return new FrameRect(x, y, r.Width, r.Height);
        }
    }
}
