using System;
using System.Collections.Generic;
using System.Globalization;

namespace Threads.UiFrames
{
    /// <summary>Which screen edge a drawer is attached to.</summary>
    public enum FrameEdge { Auto, Left, Right, Top, Bottom }

    /// <summary>The axis a strip or deck scrolls along.</summary>
    public enum FrameAxis { Horizontal, Vertical }

    /// <summary>Where a card strip comes to rest.</summary>
    public enum FrameSnap { None, Card }

    /// <summary>How far a swipe moves a card deck.</summary>
    public enum FrameDeckStep { Card, Page }

    /// <summary>The kinds of controller a frame accepts (<c>--uf-input</c>). Markup never names a
    /// device; it can only restrict which classes of controller apply.</summary>
    [Flags]
    public enum FrameInputKinds
    {
        None = 0,
        /// <summary>Direct manipulation by finger.</summary>
        Touch = 1,
        /// <summary>Mouse and pen: drag and wheel.</summary>
        Pointer = 2,
        /// <summary>Keyboard and gamepad: focus and commands.</summary>
        Navigation = 4,
        All = Touch | Pointer | Navigation,
    }

    /// <summary>How keyboard/gamepad navigation treats a frame (<c>--uf-navigation</c>). Levels: the
    /// frame is one item; Enter goes in (to its cards, then a card's controls), Esc comes out.
    /// Flat: the frame is transparent and its controls are items of the level around it.</summary>
    public enum FrameNavigationMode { Levels, Flat }

    /// <summary>Easing curves available through <c>--uf-easing</c>.</summary>
    public enum FrameEasing { Linear, EaseIn, EaseOut, EaseInOut }

    /// <summary>
    /// A custom style property: its <c>--name</c> and how to parse its value. Declare one per
    /// property (usually as a static field) and add it to a <see cref="FrameRegistry"/>, directly
    /// or through an <see cref="IFrameKind"/>. UI Toolkit can only read custom properties whose
    /// names are declared up front, so every property a frame reads must be registered.
    /// </summary>
    public abstract class FrameProperty
    {
        protected FrameProperty(string name)
        {
            if (name == null || !name.StartsWith("--", StringComparison.Ordinal) || name.Length < 3)
                throw new ArgumentException($"A custom property name starts with '--': '{name}'.", nameof(name));
            Name = name;
        }

        public string Name { get; }
        public abstract Type ValueType { get; }

        /// <summary>Parses a raw value (as written in USS or the uGUI stylesheet).</summary>
        public abstract bool TryParseBoxed(string raw, out object value, out string error);

        public override string ToString() => Name;
    }

    /// <summary>Parses a property value that has already been trimmed and unquoted (case is kept).</summary>
    public delegate bool FramePropertyParser<T>(string value, out T result, out string error);

    /// <summary>A typed custom style property.</summary>
    public sealed class FrameProperty<T> : FrameProperty
    {
        readonly FramePropertyParser<T> _parse;

        public FrameProperty(string name, FramePropertyParser<T> parse) : base(name)
        {
            _parse = parse ?? throw new ArgumentNullException(nameof(parse));
        }

        public override Type ValueType => typeof(T);

        public bool TryParse(string raw, out T value, out string error)
        {
            if (_parse(FrameStyleParser.Unquote(raw), out value, out error)) return true;
            error = $"{Name}: {error ?? $"'{raw}' is not a valid value."}";
            return false;
        }

        public override bool TryParseBoxed(string raw, out object value, out string error)
        {
            var ok = TryParse(raw, out var v, out error);
            value = ok ? (object)v : null;
            return ok;
        }
    }

    /// <summary>
    /// The resolved custom properties of one frame, from USS (UI Toolkit) or a
    /// <c>UiFrameStyleSheet</c> (uGUI). Unset properties are absent, so each frame applies its own default.
    /// </summary>
    public sealed class FrameStyle
    {
        readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

        public int Count => _values.Count;

        public bool TryGet<T>(FrameProperty<T> property, out T value)
        {
            if (_values.TryGetValue(property.Name, out var boxed) && boxed is T v) { value = v; return true; }
            value = default;
            return false;
        }

        public T Get<T>(FrameProperty<T> property, T fallback) => TryGet(property, out var v) ? v : fallback;

        public bool Has(FrameProperty property) => _values.ContainsKey(property.Name);

        public void Set<T>(FrameProperty<T> property, T value) => _values[property.Name] = value;

        internal void SetBoxed(FrameProperty property, object value) => _values[property.Name] = value;

        public void Unset(FrameProperty property) => _values.Remove(property.Name);

        public void Clear() => _values.Clear();

        /// <summary>Parses and sets one property. On failure the style is unchanged.</summary>
        public bool TryApply(FrameProperty property, string raw, out string error)
        {
            if (!property.TryParseBoxed(raw, out var value, out error)) return false;
            _values[property.Name] = value;
            return true;
        }

        /// <summary>Same properties with equal values.</summary>
        public bool SameAs(FrameStyle other)
        {
            if (other == null || other._values.Count != _values.Count) return false;
            foreach (var kv in _values)
                if (!other._values.TryGetValue(kv.Key, out var v) || !Equals(v, kv.Value)) return false;
            return true;
        }

        public FrameStyle Clone()
        {
            var c = new FrameStyle();
            foreach (var kv in _values) c._values[kv.Key] = kv.Value;
            return c;
        }

        /// <summary>Copies every value set in <paramref name="other"/> over this one (later rules win, as in CSS).</summary>
        public void Overlay(FrameStyle other)
        {
            if (other == null) return;
            foreach (var kv in other._values) _values[kv.Key] = kv.Value;
        }

        /// <summary>Whether this frame accepts a kind of controller (<c>--uf-input</c>; all by default).</summary>
        public bool Accepts(FrameInputKinds kind) => (Get(FrameProperties.Input, FrameInputKinds.All) & kind) != 0;
    }

    /// <summary>The built-in custom properties.</summary>
    public static class FrameProperties
    {
        public static readonly FrameProperty<FrameEdge> Edge = FrameStyleParser.Keyword("--uf-edge",
            ("auto", FrameEdge.Auto), ("left", FrameEdge.Left), ("right", FrameEdge.Right),
            ("top", FrameEdge.Top), ("bottom", FrameEdge.Bottom));

        public static readonly FrameProperty<FrameAxis> Axis = FrameStyleParser.Keyword("--uf-axis",
            ("horizontal", FrameAxis.Horizontal), ("vertical", FrameAxis.Vertical));

        public static readonly FrameProperty<FrameSnap> Snap = FrameStyleParser.Keyword("--uf-snap",
            ("none", FrameSnap.None), ("card", FrameSnap.Card));

        public static readonly FrameProperty<FrameDeckStep> DeckStep = FrameStyleParser.Keyword("--uf-deck-step",
            ("card", FrameDeckStep.Card), ("page", FrameDeckStep.Page));

        public static readonly FrameProperty<FrameEasing> Easing = FrameStyleParser.Keyword("--uf-easing",
            ("linear", FrameEasing.Linear), ("ease-in", FrameEasing.EaseIn),
            ("ease-out", FrameEasing.EaseOut), ("ease-in-out", FrameEasing.EaseInOut));

        /// <summary>Animation length, e.g. <c>160ms</c> or <c>0.2s</c>.</summary>
        public static readonly FrameProperty<float> Duration = FrameStyleParser.Duration("--uf-duration");

        /// <summary>Fraction of inertial speed left after one second (as uGUI's ScrollRect), 0 to below 1.</summary>
        public static readonly FrameProperty<float> Deceleration = new FrameProperty<float>("--uf-deceleration",
            (string v, out float r, out string e) =>
            {
                e = null;
                if (FrameStyleParser.TryParseNumber(v, out r) && r >= 0f && r < 1f) return true;
                e = $"'{v}' must be a number from 0 (stop at once) to below 1.";
                return false;
            });

        /// <summary>The controller kinds a frame accepts: <c>all</c>, <c>none</c>, or a list of
        /// <c>touch</c>, <c>pointer</c>, <c>navigation</c>.</summary>
        public static readonly FrameProperty<FrameInputKinds> Input = new FrameProperty<FrameInputKinds>("--uf-input", FrameStyleParser.TryParseInput);

        /// <summary>Strips and decks: size the cards along the axis so exactly this many fit in the
        /// view (gaps kept). A deck then always rests showing whole cards, whatever the screen size.</summary>
        public static readonly FrameProperty<int> VisibleCards = new FrameProperty<int>("--uf-visible-cards",
            (string v, out int n, out string e) =>
            {
                e = "must be a whole number of cards, 1 or more.";
                return int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n) && n >= 1;
            });

        /// <summary>How far one arrow key moves a slider being adjusted, as a fraction of its range
        /// (UI Toolkit sliders; default 0.1 as uGUI). Set it on the frame around the slider.</summary>
        public static readonly FrameProperty<float> ArrowStep = new FrameProperty<float>("--uf-arrow-step",
            (string v, out float r, out string e) =>
            {
                e = "must be a fraction of the range, above 0 and at most 1.";
                return FrameStyleParser.TryParseNumber(v, out r) && r > 0f && r <= 1f;
            });

        public static readonly FrameProperty<FrameNavigationMode> Navigation = FrameStyleParser.Keyword("--uf-navigation",
            ("levels", FrameNavigationMode.Levels), ("flat", FrameNavigationMode.Flat));

        /// <summary>Properties that every frame can use.</summary>
        public static readonly FrameProperty[] Common = { Duration, Easing, Input, Navigation, ArrowStep };
    }

    /// <summary>The marker and state classes of the built-in frames.</summary>
    public static class FrameTags
    {
        public const string Drawer = "uf-drawer";
        public const string DrawerHandle = "uf-drawer-handle";
        public const string CardStrip = "uf-card-strip";
        public const string Cards = "uf-cards";
        public const string CardDeck = "uf-card-deck";
        public const string NoDrag = "uf-no-drag";
        /// <summary>Used by mouse and touch only: keyboard/gamepad navigation does not stop on this
        /// element or anything inside it (e.g. a menu row's slider: the row is the stop, and
        /// Left/Right adjust it).</summary>
        public const string PointerOnly = "uf-pointer-only";
        public const string Joystick = "uf-joystick";
        public const string JoystickKnob = "uf-joystick__knob";

        // State classes set by the frames.
        public const string Open = "uf-open";
        public const string Dragging = "uf-dragging";
        public const string Focused = "uf-focused";
        /// <summary>The keyboard/gamepad selection: a frame, card or control.</summary>
        public const string Selected = "uf-selected";
        /// <summary>A frame or card the keyboard has entered (Enter); Esc leaves it.</summary>
        public const string Entered = "uf-entered";
        /// <summary>A control taking the arrow keys itself (a slider being adjusted, a field being edited).</summary>
        public const string Editing = "uf-editing";
        /// <summary>Set on strips and decks from <c>--uf-axis</c>, so USS can lay out their cards
        /// (USS cannot select on a custom property).</summary>
        public const string Horizontal = "uf-horizontal";
        public const string Vertical = "uf-vertical";
    }

    /// <summary>Helpers for writing property parsers.</summary>
    public static class FrameStyleParser
    {
        /// <summary>A property whose value is one of a set of keywords.</summary>
        public static FrameProperty<T> Keyword<T>(string name, params (string word, T value)[] words)
        {
            var list = string.Join(", ", Array.ConvertAll(words, w => w.word));
            return new FrameProperty<T>(name, (string raw, out T result, out string error) =>
            {
                var v = raw.ToLowerInvariant(); // CSS keywords ignore case
                foreach (var (word, value) in words)
                {
                    if (v == word) { result = value; error = null; return true; }
                }
                result = default;
                error = $"'{v}' is not one of {list}.";
                return false;
            });
        }

        /// <summary>A duration in seconds, written with a unit (<c>ms</c> or <c>s</c>) as in CSS.</summary>
        public static FrameProperty<float> Duration(string name) =>
            new FrameProperty<float>(name, (string v, out float s, out string e) =>
            {
                e = null;
                if (TryParseDuration(v, out s)) return true;
                e = $"'{v}' is not a duration (use e.g. 160ms or 0.2s).";
                return false;
            });

        public static bool TryParseDuration(string value, out float seconds)
        {
            seconds = 0f;
            var v = Unquote(value).ToLowerInvariant();
            float scale;
            if (v.EndsWith("ms", StringComparison.Ordinal)) { v = v.Substring(0, v.Length - 2); scale = 0.001f; }
            else if (v.EndsWith("s", StringComparison.Ordinal)) { v = v.Substring(0, v.Length - 1); scale = 1f; }
            else return false;
            if (!TryParseNumber(v, out var n) || n < 0f) return false;
            seconds = n * scale;
            return true;
        }

        public static bool TryParseNumber(string v, out float n) =>
            float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !float.IsNaN(n) && !float.IsInfinity(n);

        public static bool TryParseInput(string value, out FrameInputKinds kinds, out string error)
        {
            var v = Unquote(value).ToLowerInvariant();
            kinds = FrameInputKinds.None;
            error = "must be 'all', 'none' or a list of touch, pointer, navigation.";
            if (v == "all") { kinds = FrameInputKinds.All; return true; }
            if (v == "none") return true;
            var parts = v.Split(new[] { ' ', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            foreach (var p in parts)
            {
                switch (p)
                {
                    case "touch": kinds |= FrameInputKinds.Touch; break;
                    case "pointer": kinds |= FrameInputKinds.Pointer; break;
                    case "navigation": kinds |= FrameInputKinds.Navigation; break;
                    default: kinds = FrameInputKinds.None; return false;
                }
            }
            return true;
        }

        /// <summary>Trims and removes the quotes USS keeps on string values.</summary>
        public static string Unquote(string value)
        {
            if (value == null) return string.Empty;
            var v = value.Trim();
            if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0]) v = v.Substring(1, v.Length - 2).Trim();
            return v;
        }
    }
}
