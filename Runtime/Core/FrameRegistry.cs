using System;
using System.Collections.Generic;

namespace Threads.UiFrames
{
    /// <summary>
    /// The frame kinds and custom properties in use. Build one in your bootstrap and pass it to the
    /// UI binders and controllers; there is no global registry.
    /// <para>Precedence is in your hands through registration order: <b>the most recent registration
    /// wins</b>. An element carrying several marker classes gets the behaviour of the most recently
    /// registered matching kind, and registering a kind for a marker class that is already taken
    /// replaces the earlier kind (that is how you override a built-in).</para>
    /// </summary>
    public sealed class FrameRegistry
    {
        readonly List<IFrameKind> _kinds = new List<IFrameKind>();
        readonly Dictionary<string, FrameProperty> _properties = new Dictionary<string, FrameProperty>(StringComparer.Ordinal);
        readonly List<FrameProperty> _propertyList = new List<FrameProperty>();

        public FrameRegistry()
        {
            foreach (var p in FrameProperties.Common) AddProperty(p);
        }

        /// <summary>A new registry with the drawer, card strip and card deck.</summary>
        public static FrameRegistry WithBuiltIns() =>
            new FrameRegistry().Add(new DrawerKind()).Add(new StripKind()).Add(new DeckKind());

        /// <summary>Kinds from oldest to newest registration (newest has precedence).</summary>
        public IReadOnlyList<IFrameKind> Kinds => _kinds;

        /// <summary>Every registered property; binders read all of these from each frame.</summary>
        public IReadOnlyList<FrameProperty> Properties => _propertyList;

        /// <summary>Raised after kinds or properties change, so binders can rescan.</summary>
        public event Action Changed;

        /// <summary>Registers a kind (and its properties) with the highest precedence. A kind already
        /// registered for the same marker class is replaced.</summary>
        public FrameRegistry Add(IFrameKind kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (string.IsNullOrWhiteSpace(kind.MarkerClass)) throw new ArgumentException("A frame kind needs a marker class.", nameof(kind));
            // Check every property first so a failed registration changes nothing.
            if (kind.Properties != null)
                foreach (var p in kind.Properties) CheckProperty(p);
            _kinds.RemoveAll(k => k.MarkerClass == kind.MarkerClass);
            _kinds.Add(kind);
            if (kind.Properties != null)
                foreach (var p in kind.Properties) AddPropertyUnchecked(p);
            Changed?.Invoke();
            return this;
        }

        /// <summary>Registers a property on its own, e.g. one read by a controller rather than a kind.
        /// A same-named property of the same type is replaced (most recent wins).</summary>
        public FrameRegistry AddProperty(FrameProperty property)
        {
            CheckProperty(property);
            if (AddPropertyUnchecked(property)) Changed?.Invoke();
            return this;
        }

        /// <summary>Removes the kind for a marker class. Its properties stay registered.</summary>
        public bool Remove(string markerClass)
        {
            var removed = _kinds.RemoveAll(k => k.MarkerClass == markerClass) > 0;
            if (removed) Changed?.Invoke();
            return removed;
        }

        public IFrameKind FindKind(string markerClass)
        {
            foreach (var k in _kinds) if (k.MarkerClass == markerClass) return k;
            return null;
        }

        public FrameProperty FindProperty(string name) =>
            name != null && _properties.TryGetValue(name.Trim(), out var p) ? p : null;

        /// <summary>The kind for an element: the most recently registered one whose marker class it carries.</summary>
        public IFrameKind Resolve(IFrameElement element)
        {
            if (element == null) return null;
            for (var i = _kinds.Count - 1; i >= 0; i--)
                if (element.HasClass(_kinds[i].MarkerClass)) return _kinds[i];
            return null;
        }

        /// <summary>Parses a raw value for a registered property by name into <paramref name="style"/>.</summary>
        public bool TryApply(FrameStyle style, string propertyName, string raw, out string error)
        {
            if (style == null) throw new ArgumentNullException(nameof(style));
            var p = FindProperty(propertyName);
            if (p == null) { error = $"Unknown frame property '{propertyName}'."; return false; }
            return style.TryApply(p, raw, out error);
        }

        void CheckProperty(FrameProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (_properties.TryGetValue(property.Name, out var existing) && existing.ValueType != property.ValueType)
                throw new InvalidOperationException(
                    $"Property '{property.Name}' is already registered as {existing.ValueType.Name}, not {property.ValueType.Name}.");
        }

        /// <summary>Adds the property, or replaces a same-named one (same type, most recent wins).</summary>
        bool AddPropertyUnchecked(FrameProperty property)
        {
            if (_properties.TryGetValue(property.Name, out var existing))
            {
                if (ReferenceEquals(existing, property)) return false;
                _propertyList[_propertyList.IndexOf(existing)] = property;
                _properties[property.Name] = property;
                return true;
            }
            _properties.Add(property.Name, property);
            _propertyList.Add(property);
            return true;
        }
    }
}
