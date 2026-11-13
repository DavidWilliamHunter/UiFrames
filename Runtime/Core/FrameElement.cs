using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary>A rectangle in dp, in layout space: origin top-left, y down (as UI Toolkit layout).</summary>
    public readonly struct FrameRect
    {
        public readonly float X, Y, Width, Height;

        public FrameRect(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public float CentreX => X + Width * 0.5f;
        public float CentreY => Y + Height * 0.5f;
        public float Start(FrameAxis axis) => axis == FrameAxis.Horizontal ? X : Y;
        public float Size(FrameAxis axis) => axis == FrameAxis.Horizontal ? Width : Height;
        public float End(FrameAxis axis) => Start(axis) + Size(axis);

        public override string ToString() => $"({X}, {Y}, {Width}×{Height})";
    }

    /// <summary>
    /// One UI element as a frame sees it, the same for a UI Toolkit VisualElement and a uGUI
    /// RectTransform. Each UI adapter implements this; frame behaviours (built-in or from other
    /// packages) only use this, so they work in both UI systems unchanged.
    /// All geometry is in dp, layout space (origin top-left, y down).
    /// </summary>
    public interface IFrameElement
    {
        string Name { get; }
        /// <summary>The VisualElement or GameObject behind this, for behaviours that need more.</summary>
        object Native { get; }
        IFrameElement Parent { get; }
        IReadOnlyList<IFrameElement> Children { get; }
        /// <summary>The laid-out rectangle relative to the parent, ignoring any translation set here.</summary>
        FrameRect LayoutRectDp { get; }

        bool HasClass(string className);
        /// <summary>Adds or removes a class (state classes such as <c>uf-open</c>).</summary>
        void SetClass(string className, bool enabled);
        /// <summary>Moves the element from its laid-out place, in dp, y down.</summary>
        void SetTranslationDp(float x, float y);
        void SetVisible(bool visible);
    }

    public static class FrameElementExtensions
    {
        /// <summary>The first descendant (depth first, not the element itself) with the class, or null.</summary>
        public static IFrameElement FindDescendant(this IFrameElement element, string className)
        {
            if (element == null) return null;
            foreach (var child in element.Children)
            {
                if (child.HasClass(className)) return child;
                var found = child.FindDescendant(className);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>What the host provides to every frame.</summary>
    public sealed class FrameEnvironment
    {
        public FrameEnvironment(IFrameClock clock)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public IFrameClock Clock { get; }
        /// <summary>Release speed that counts as a fling (dp/s). The touch controller sets this from
        /// the platform's touch settings.</summary>
        public float FlingThresholdDps { get; set; } = 400f;
        /// <summary>The largest distance a rubber band shows past an end (dp).</summary>
        public float RubberBandLimitDp { get; set; } = 60f;
    }

    /// <summary>Everything a frame kind gets when it creates a behaviour for an element.</summary>
    public sealed class FrameContext
    {
        public FrameContext(IFrameElement element, FrameEnvironment environment, FrameRegistry registry)
        {
            Element = element ?? throw new ArgumentNullException(nameof(element));
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IFrameElement Element { get; }
        public FrameEnvironment Environment { get; }
        public FrameRegistry Registry { get; }
        public IFrameClock Clock => Environment.Clock;
    }

    /// <summary>
    /// The live behaviour attached to one tagged element. The binder calls <see cref="ApplyStyle"/>
    /// when its custom properties resolve, <see cref="OnLayoutChanged"/> when its geometry changes,
    /// <see cref="Update"/> every frame, and <see cref="IDisposable.Dispose"/> when the element goes.
    /// Controllers send it commands.
    /// </summary>
    public interface IFrameBehaviour : IFrameCommandTarget, IDisposable
    {
        IFrameElement Element { get; }
        /// <summary>The style last applied; controllers read <c>--uf-input</c> from it.</summary>
        FrameStyle Style { get; }
        /// <summary>The axis drags move along, so controllers can project motion and lock axes.</summary>
        FrameAxis DragAxis { get; }
        /// <summary>Turns screen motion (dp, y up) into this frame's command space.</summary>
        float ToCommandDelta(float dx, float dy);

        void ApplyStyle(FrameStyle style);
        void OnLayoutChanged();
        void Update();
    }

    /// <summary>
    /// A kind of frame: the marker class that tags it, the custom properties it reads, and how to
    /// make its behaviour. Register kinds in a <see cref="FrameRegistry"/>; the most recently
    /// registered kind wins when an element carries several marker classes.
    /// </summary>
    public interface IFrameKind
    {
        string MarkerClass { get; }
        IReadOnlyList<FrameProperty> Properties { get; }
        IFrameBehaviour Create(FrameContext context);
    }
}
