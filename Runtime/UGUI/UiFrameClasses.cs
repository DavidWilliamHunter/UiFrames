using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Threads.UiFrames.UGUI
{
    /// <summary>One <c>--property: value</c> pair, as written in USS.</summary>
    [Serializable]
    public struct UiFrameDeclaration
    {
        [Tooltip("Custom property name, e.g. --uf-edge")]
        public string property;
        [Tooltip("Value as in USS, e.g. left, 160ms, card")]
        public string value;

        public UiFrameDeclaration(string property, string value)
        {
            this.property = property;
            this.value = value;
        }
    }

    /// <summary>
    /// The uGUI counterpart of a VisualElement's class list and inline style: tag a GameObject with
    /// classes such as <c>uf-drawer</c> or <c>uf-cards</c>, and optionally set <c>--*</c> properties
    /// inline (they win over <see cref="UiFrameStyleSheet"/> rules, as inline USS does).
    /// Frames add state classes (<c>uf-open</c>, <c>uf-dragging</c>, <c>uf-focused</c>) at run time;
    /// hook visuals to them with <see cref="classEvents"/> or <see cref="ClassChanged"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI Frames/UI Frame Classes")]
    public sealed class UiFrameClasses : MonoBehaviour
    {
        [Serializable]
        public sealed class ClassEvent
        {
            public string className;
            [Tooltip("Called with true when the class is added, false when removed.")]
            public UnityEvent<bool> changed = new UnityEvent<bool>();
        }

        [Tooltip("Classes, as in a UXML class attribute (one per entry, no dots).")]
        [SerializeField] List<string> classes = new List<string>();
        [Tooltip("Inline custom properties; these win over style sheet rules.")]
        [SerializeField] List<UiFrameDeclaration> inlineStyle = new List<UiFrameDeclaration>();
        [Tooltip("Run-time hooks for state classes such as uf-open.")]
        public List<ClassEvent> classEvents = new List<ClassEvent>();

        readonly HashSet<string> _runtime = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The designed classes (not the run-time state classes).</summary>
        public IReadOnlyList<string> Classes => classes;
        public IReadOnlyList<UiFrameDeclaration> InlineStyle => inlineStyle;

        /// <summary>A class was added (true) or removed (false).</summary>
        public event Action<string, bool> ClassChanged;
        /// <summary>The designed classes or inline style changed (e.g. edited in the Inspector).</summary>
        public event Action Changed;

        public bool HasClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            if (_runtime.Contains(className)) return true;
            foreach (var c in classes) if (c == className) return true;
            return false;
        }

        /// <summary>Adds or removes a class at run time (not saved).</summary>
        public void SetClass(string className, bool enabled)
        {
            if (string.IsNullOrEmpty(className)) return;
            var had = HasClass(className);
            if (enabled) _runtime.Add(className);
            else
            {
                _runtime.Remove(className);
                if (classes.Remove(className)) Changed?.Invoke();
            }
            if (had == HasClass(className)) return;
            ClassChanged?.Invoke(className, enabled);
            foreach (var e in classEvents)
                if (e != null && e.className == className) e.changed?.Invoke(enabled);
        }

        /// <summary>Sets the designed classes from code (e.g. when building UI at run time).</summary>
        public void SetClasses(params string[] names)
        {
            classes.Clear();
            foreach (var n in names) if (!string.IsNullOrWhiteSpace(n)) classes.Add(n.Trim().TrimStart('.'));
            Changed?.Invoke();
        }

        public void SetInline(string property, string value)
        {
            for (var i = 0; i < inlineStyle.Count; i++)
            {
                if (inlineStyle[i].property != property) continue;
                inlineStyle[i] = new UiFrameDeclaration(property, value);
                Changed?.Invoke();
                return;
            }
            inlineStyle.Add(new UiFrameDeclaration(property, value));
            Changed?.Invoke();
        }

        /// <summary>Removes an inline declaration (style sheet rules then decide).</summary>
        public bool RemoveInline(string property)
        {
            var removed = inlineStyle.RemoveAll(d => d.property == property) > 0;
            if (removed) Changed?.Invoke();
            return removed;
        }

        void OnValidate()
        {
            for (var i = 0; i < classes.Count; i++)
                if (classes[i] != null) classes[i] = classes[i].Trim().TrimStart('.');
            Changed?.Invoke();
        }
    }
}
