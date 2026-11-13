#if THREADS_UF_TOUCHCONTROLLER
using System;
using System.Collections.Generic;
using Threads.TouchController.Intentions;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

namespace Threads.UiFrames.TouchInput
{
    /// <summary>Which TouchSticks stick a joystick writes (<c>--uf-joystick-stick</c>).</summary>
    public enum JoystickStick { Left, Right, None }

    /// <summary>The joystick custom properties.</summary>
    public static class JoystickProperties
    {
        public static readonly FrameProperty<JoystickMode> Mode = FrameStyleParser.Keyword("--uf-joystick-mode",
            ("fixed", JoystickMode.Fixed), ("floating", JoystickMode.Floating));

        /// <summary>How far the knob travels from the centre (dp).</summary>
        public static readonly FrameProperty<float> Radius = Positive("--uf-joystick-radius", "a radius in dp, above 0.");

        /// <summary>Fraction of the radius that reads as zero (0 to below 1).</summary>
        public static readonly FrameProperty<float> DeadZone = new FrameProperty<float>("--uf-joystick-dead-zone",
            (string v, out float r, out string e) =>
            {
                e = "must be a fraction of the radius, 0 to below 1.";
                return FrameStyleParser.TryParseNumber(v, out r) && r >= 0f && r < 1f;
            });

        public static readonly FrameProperty<JoystickStick> Stick = FrameStyleParser.Keyword("--uf-joystick-stick",
            ("left", JoystickStick.Left), ("right", JoystickStick.Right), ("none", JoystickStick.None));

        /// <summary>Floating: the base follows a finger that goes past the radius.</summary>
        public static readonly FrameProperty<bool> Follow = Flag("--uf-joystick-follow");

        /// <summary>Hide the base and knob while nobody holds the joystick.</summary>
        public static readonly FrameProperty<bool> Hide = Flag("--uf-joystick-hide");

        static FrameProperty<float> Positive(string name, string message) =>
            new FrameProperty<float>(name, (string v, out float r, out string e) =>
            {
                e = "must be " + message;
                return FrameStyleParser.TryParseNumber(v, out r) && r > 0f;
            });

        static FrameProperty<bool> Flag(string name) =>
            FrameStyleParser.Keyword(name, ("true", true), ("false", false));
    }

    /// <summary>
    /// <c>.uf-joystick</c>: an on-screen joystick driven by TouchController's
    /// <see cref="TouchJoystick"/>, writing a TouchSticks stick for Input System actions. Children:
    /// <c>.uf-joystick__knob</c> (follows the thumb) and optionally <c>.uf-joystick__base</c> (moves to
    /// where a floating joystick is pressed). Fixed: the joystick's own centre is the centre.
    /// Floating: the whole element is where a press may start. Created by <see cref="UiFramesTouch"/>.
    /// </summary>
    public sealed class JoystickKind : IFrameKind
    {
        public const string Marker = "uf-joystick";
        public const string KnobClass = "uf-joystick__knob";
        public const string BaseClass = "uf-joystick__base";

        static readonly FrameProperty[] s_properties =
        {
            JoystickProperties.Mode, JoystickProperties.Radius, JoystickProperties.DeadZone, JoystickProperties.Stick,
            JoystickProperties.Follow, JoystickProperties.Hide, FrameProperties.Input,
        };

        readonly Func<IntentionSystem> _intentions;
        readonly Func<TouchSticksDevice> _sticks;

        public JoystickKind(Func<IntentionSystem> intentions, Func<TouchSticksDevice> sticks = null)
        {
            _intentions = intentions ?? throw new ArgumentNullException(nameof(intentions));
            _sticks = sticks;
        }

        public string MarkerClass => Marker;
        public IReadOnlyList<FrameProperty> Properties => s_properties;

        public IFrameBehaviour Create(FrameContext context)
        {
            var intentions = _intentions();
            return intentions == null ? null : new JoystickFrame(context, intentions, _sticks?.Invoke());
        }
    }

    /// <summary>The behaviour of a <c>.uf-joystick</c> element (see <see cref="JoystickKind"/>).</summary>
    public sealed class JoystickFrame : IFrameBehaviour
    {
        readonly IntentionSystem _intentions;
        readonly TouchSticksDevice _sticks;
        JoystickInputOutput _output;

        public JoystickFrame(FrameContext context, IntentionSystem intentions, TouchSticksDevice sticks)
        {
            Element = context.Element;
            _intentions = intentions;
            _sticks = sticks;
            Style = Defaults(new FrameStyle());
            Rebuild();
        }

        public IFrameElement Element { get; }
        public FrameStyle Style { get; private set; }
        public TouchJoystick Joystick { get; private set; }
        public FrameAxis DragAxis => FrameAxis.Horizontal;
        public float ToCommandDelta(float dx, float dy) => 0f;
        public bool Execute(in FrameCommand command) => false;
        public void Update() { }

        IFrameElement Knob => Element.FindDescendant(JoystickKind.KnobClass);
        IFrameElement Base => Element.FindDescendant(JoystickKind.BaseClass);

        public void ApplyStyle(FrameStyle style)
        {
            style = Defaults(style?.Clone() ?? new FrameStyle());
            if (style.SameAs(Style)) return;
            Style = style;
            Rebuild();
        }

        public void OnLayoutChanged() => Show();

        /// <summary>Joysticks are for fingers: unless <c>--uf-input</c> says otherwise, keyboard and
        /// mouse leave them alone (TouchController's editor simulation still turns a mouse into a finger).</summary>
        static FrameStyle Defaults(FrameStyle style)
        {
            if (!style.Has(FrameProperties.Input)) style.Set(FrameProperties.Input, FrameInputKinds.Touch);
            return style;
        }

        float PixelsPerDp => _intentions.Touch.Environment.PixelsPerDp;
        Vector2 ScreenSizePx => _intentions.Touch.Frame.ScreenSizeDp * PixelsPerDp;
        Rect ElementRectDp => FrameScreen.RectDp(Element, PixelsPerDp, ScreenSizePx);

        void Rebuild()
        {
            Teardown();
            var mode = Style.Get(JoystickProperties.Mode, JoystickMode.Fixed);
            var options = new TouchJoystickOptions
            {
                Name = Element.Name,
                Mode = mode,
                RadiusDp = Style.Get(JoystickProperties.Radius, 60f),
                DeadZone = Style.Get(JoystickProperties.DeadZone, 0.1f),
                Follow = Style.Get(JoystickProperties.Follow, false),
            };
            if (mode == JoystickMode.Fixed) options.CentreDp = () => ElementRectDp.center;
            else
            {
                options.ActivationArea = new FuncArea(p => FrameScreen.Contains(Element, p.PositionPx, p.ScreenSizePx));
                options.OriginBoundsDp = () => ElementRectDp;
            }
            Joystick = new TouchJoystick(_intentions, options);
            Joystick.Pressed += OnJoystick;
            Joystick.Changed += OnChanged;
            Joystick.Released += OnJoystick;
            Joystick.Enabled = Style.Accepts(FrameInputKinds.Touch);

            var stick = Style.Get(JoystickProperties.Stick, JoystickStick.Left);
            if (_sticks != null && stick != JoystickStick.None)
            {
                Vector2Control control = stick == JoystickStick.Left ? _sticks.LeftStick : _sticks.RightStick;
                if (control != null) _output = new JoystickInputOutput(Joystick, _sticks, control);
            }
            Show();
        }

        void Teardown()
        {
            _output?.Dispose();
            _output = null;
            if (Joystick == null) return;
            Joystick.Pressed -= OnJoystick;
            Joystick.Changed -= OnChanged;
            Joystick.Released -= OnJoystick;
            Joystick.Dispose();
            Joystick = null;
        }

        void OnJoystick(TouchJoystick _) => Show();
        void OnChanged(Vector2 _) => Show();

        /// <summary>
        /// Places the base at the origin and the knob at the thumb. Positions are worked out in the
        /// joystick element's own layout space from its (never moved) centre on screen, so they do
        /// not depend on where the moving parts were last drawn.
        /// </summary>
        void Show()
        {
            if (Joystick == null) return;
            var knob = Knob;
            var bas = Base;
            var held = Joystick.IsHeld;
            var hide = Style.Get(JoystickProperties.Hide, false);
            knob?.SetVisible(held || !hide);
            bas?.SetVisible(held || !hide);

            var size = Element.LayoutRectDp;
            var centreLocal = new Vector2(size.Width * 0.5f, size.Height * 0.5f);
            var centreDp = ElementRectDp.center;
            Vector2 Local(Vector2 screenDp)
            {
                var (x, y) = Element.ToLocalDeltaDp(centreDp.x, centreDp.y, screenDp.x, screenDp.y);
                return centreLocal + new Vector2(x, y);
            }

            var baseT = Vector2.zero;
            if (bas != null)
            {
                if (held && Joystick.Mode == JoystickMode.Floating)
                {
                    var rest = FrameGeometry.RectIn(bas, Element);
                    baseT = Local(Joystick.OriginDp) - new Vector2(rest.CentreX, rest.CentreY);
                }
                bas.SetTranslationDp(baseT.x, baseT.y);
            }
            if (knob != null)
            {
                var knobT = Vector2.zero;
                if (held)
                {
                    var rest = FrameGeometry.RectIn(knob, Element);
                    knobT = Local(Joystick.ThumbDp) - new Vector2(rest.CentreX, rest.CentreY);
                    if (bas != null && FrameGeometry.IsInside(knob, bas)) knobT -= baseT; // it already moves with the base
                }
                knob.SetTranslationDp(knobT.x, knobT.y);
            }
        }

        public void Dispose()
        {
            Teardown();
            Knob?.SetTranslationDp(0f, 0f);
            Base?.SetTranslationDp(0f, 0f);
            Knob?.SetVisible(true);
            Base?.SetVisible(true);
        }
    }
}
#endif
