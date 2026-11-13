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
            FrameProperties.Axis, FrameProperties.Snap, FrameProperties.Deceleration,
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
            FrameProperties.Axis, FrameProperties.DeckStep,
            FrameProperties.Duration, FrameProperties.Easing, FrameProperties.Input,
        };

        public string MarkerClass => FrameTags.CardDeck;
        public IReadOnlyList<FrameProperty> Properties => s_properties;
        public IFrameBehaviour Create(FrameContext context) => new DeckFrame(context);
    }

    /// <summary>
    /// A drawer. Lay the element out in its <b>open</b> position against its edge; when closed it is
    /// moved off by its own width or height (a handle placed outside its bounds stays in view).
    /// Sets <c>uf-open</c> and <c>uf-dragging</c>.
    /// </summary>
    public sealed class DrawerFrame : IFrameBehaviour
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
            OnLayoutChanged();
        }

        public IFrameElement Element { get; }
        public DrawerModel Model { get; }
        public FrameStyle Style { get; private set; } = new FrameStyle();
        /// <summary>The edge in use (<c>auto</c> resolved).</summary>
        public FrameEdge Edge { get; private set; } = FrameEdge.Left;
        public bool IsOpen => Model.IsOpen;
        public FrameAxis DragAxis => FrameAxisMath.DrawerAxis(Edge);

        public float ToCommandDelta(float dx, float dy) => FrameAxisMath.DrawerDelta(Edge, dx, dy);

        public void ApplyStyle(FrameStyle style)
        {
            Style = style?.Clone() ?? new FrameStyle();
            Model.ApplyStyle(Style);
            OnLayoutChanged();
        }

        public void OnLayoutChanged()
        {
            Edge = ResolveEdge();
            Model.ExtentDp = Element.LayoutRectDp.Size(DragAxis);
            Place();
        }

        public bool Execute(in FrameCommand command) => Model.Execute(command);
        public void Update() => Model.Update();

        public void Dispose()
        {
            Model.ProgressChanged -= OnProgress;
            Model.OpenChanged -= OnOpenChanged;
            Model.DraggingChanged -= OnDraggingChanged;
            Element.SetTranslationDp(0f, 0f);
            Element.SetClass(FrameTags.Open, false);
            Element.SetClass(FrameTags.Dragging, false);
        }

        FrameEdge ResolveEdge()
        {
            var edge = Style.Get(FrameProperties.Edge, FrameEdge.Auto);
            if (edge != FrameEdge.Auto) return edge;
            var handle = Element.FindDescendant(FrameTags.DrawerHandle);
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

        void OnProgress(float _) => Place();
        void OnOpenChanged(bool open) => Element.SetClass(FrameTags.Open, open);
        void OnDraggingChanged(bool dragging) => Element.SetClass(FrameTags.Dragging, dragging);
    }

    /// <summary>Measures and moves the <c>.uf-cards</c> content of a strip or deck.</summary>
    public abstract class CardFrameBase : IFrameBehaviour
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

        public void ApplyStyle(FrameStyle style)
        {
            Style = style?.Clone() ?? new FrameStyle();
            ApplyModelStyle(Style);
            OnLayoutChanged();
        }

        public void OnLayoutChanged()
        {
            if (Content != null) Content.SetTranslationDp(0f, 0f);
            Content = Element.FindDescendant(FrameTags.Cards);
            SetLayout(Measure());
            Place();
        }

        public abstract bool Execute(in FrameCommand command);
        public abstract void Update();

        public virtual void Dispose()
        {
            Content?.SetTranslationDp(0f, 0f);
            Element.SetClass(FrameTags.Dragging, false);
        }

        protected abstract void ApplyModelStyle(FrameStyle style);
        protected abstract void SetLayout(CardLayout layout);

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

        public override bool Execute(in FrameCommand command) => Model.Execute(command);
        public override void Update() => Model.Update();

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

        public override bool Execute(in FrameCommand command)
        {
            var handled = Model.Execute(command);
            UpdateCardVisibility(); // a step starts moving, so cut-off cards show during it
            return handled;
        }

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
