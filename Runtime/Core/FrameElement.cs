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
        /// <summary>Moves the element from its laid-out place, in dp along its own axes (y down),
        /// so a rotated or scaled element moves along its own rotated axes.</summary>
        void SetTranslationDp(float x, float y);
        /// <summary>Hides an element without changing layout. Hidden elements cannot be pressed or focused.</summary>
        void SetVisible(bool visible);

        /// <summary>Sets the element's laid-out size along <paramref name="axis"/> (dp), e.g. a card's
        /// width for <c>--uf-visible-cards</c>. The layout updates later, as for any size change.</summary>
        void SetSizeDp(FrameAxis axis, float size);

        /// <summary>
        /// A pointer movement from (fromX, fromY) to (toX, toY), given in screen dp with y up, as a
        /// movement in this element's local layout space (dp, y down). It accounts for every scale,
        /// rotation and camera between the screen and the element, so content moved by the result
        /// follows the pointer 1:1. It is linear, so it also converts velocities (pass 0, 0 as from).
        /// </summary>
        (float x, float y) ToLocalDeltaDp(float fromX, float fromY, float toX, float toY);

        /// <summary>Where the element is drawn now (translations, scale and rotation included), as an
        /// axis-aligned rect in screen dp, y down. Only relative positions are used (spatial
        /// keyboard navigation); the origin is up to the adapter.</summary>
        FrameRect ScreenRectDp { get; }
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
        /// <summary>Screen pixels per dp. The Unity host sets it from the screen's density.</summary>
        public float PixelsPerDp { get; set; } = 1f;
        /// <summary>How far a pointer moves before a press becomes a frame drag (dp).</summary>
        public float DragSlopDp { get; set; } = 8f;
        /// <summary>How far one mouse-wheel notch scrolls a strip (dp). Decks step one card per notch.</summary>
        public float WheelStepDp { get; set; } = 48f;
        /// <summary>How often binders look for UI added, removed or re-tagged at run time (seconds;
        /// 0 = only when asked with Rescan). Cards added to an existing strip are seen at once.</summary>
        public float AutoRescanSeconds { get; set; } = 0.5f;
        /// <summary>Warnings about frames that cannot work as set up (logged once each).</summary>
        public Action<string> Warn { get; set; }
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
        /// <summary>Turns motion in this frame's local layout space (dp, y down; see
        /// <see cref="IFrameElement.ToLocalDeltaDp"/>) into this frame's command space.</summary>
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

    /// <summary>
    /// Optional for a frame behaviour: lets keyboard/gamepad navigation and UI focus work with it.
    /// The built-in frames implement it; implement it in your own kinds to get the same support.
    /// </summary>
    public interface IFrameNavigable
    {
        /// <summary>Animating, coasting or being dragged.</summary>
        bool IsMoving { get; }
        /// <summary>Whether a step (+1 next, −1 previous) would move the frame. When it would not,
        /// navigation leaves the frame and the UI moves focus on.</summary>
        bool CanStep(int direction);
        /// <summary>Whether <paramref name="descendant"/> is in view in this frame at its current position.</summary>
        bool IsShown(IFrameElement descendant);
    }

    /// <summary>How a control uses the arrow keys once it has been entered with Enter.</summary>
    public enum FrameControlArrows
    {
        /// <summary>Not at all (buttons, toggles): Enter presses it.</summary>
        None,
        /// <summary>Arrows change its value (sliders, scrollbars): Enter starts and ends adjusting.</summary>
        Adjust,
        /// <summary>A text field: Enter starts editing; Enter or Esc ends it.</summary>
        Text,
    }

    /// <summary>
    /// A UI system's focus, as frames see it: UI Toolkit's focus controller, uGUI's EventSystem
    /// selection. Implement <see cref="IFrameNavigationHost"/> to support keyboard navigation too.
    /// </summary>
    public interface IFrameFocusHost
    {
        /// <summary>The root of this host's tree (a UIDocument's root, a Canvas).</summary>
        IFrameElement Root { get; }
        /// <summary>The element with UI focus in this host's tree, or null.</summary>
        IFrameElement Focused { get; }
        /// <summary>Raised when UI focus moves to an element in this tree (null when it leaves).</summary>
        event Action<IFrameElement> FocusChanged;
        /// <summary>Whether the element is in this host's tree.</summary>
        bool Owns(IFrameElement element);
        bool CanFocus(IFrameElement element);
        bool Focus(IFrameElement element);
    }

    /// <summary>
    /// What a UI adapter adds for keyboard/gamepad navigation in levels (<see cref="FrameNavigator"/>):
    /// which elements are controls and how they use the arrows, and a way for frames and cards
    /// themselves to take focus.
    /// </summary>
    public interface IFrameNavigationHost : IFrameFocusHost
    {
        /// <summary>The navigator driven by this host's key events; set by <see cref="FrameNavigator.Startup"/>.</summary>
        FrameNavigator Navigator { get; set; }
        /// <summary>Whether <paramref name="element"/> is a control the user can focus (button, slider,
        /// field…), as designed: not a frame or card that the host made focusable for navigation.</summary>
        bool IsControl(IFrameElement element);
        FrameControlArrows ArrowUse(IFrameElement control);
        /// <summary>Lets a frame or card element take UI focus (for keyboard selection).</summary>
        void EnsureFocusable(IFrameElement element);
        /// <summary>A control starts or stops taking the arrow keys itself.</summary>
        void SetEditing(IFrameElement control, bool editing);
    }
}
