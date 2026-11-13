using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threads.UiFrames.UGUI
{
    /// <summary>
    /// A style sheet for uGUI frames: rules that match <see cref="UiFrameClasses"/> and set
    /// <c>--*</c> custom properties, in the same vocabulary as USS. A selector is one or more class
    /// names that must all be present (<c>.uf-card-strip.big</c>), with commas for alternatives.
    /// Rules apply in order and later rules win; inline declarations on the element win over all.
    /// </summary>
    [CreateAssetMenu(menuName = "UI Frames/Style Sheet", fileName = "UiFrameStyleSheet")]
    public sealed class UiFrameStyleSheet : ScriptableObject
    {
        [Serializable]
        public sealed class Rule
        {
            [Tooltip("e.g. .uf-card-strip.photos  or  .uf-drawer, .menu")]
            public string selector;
            public List<UiFrameDeclaration> declarations = new List<UiFrameDeclaration>();
        }

        public List<Rule> rules = new List<Rule>();

        /// <summary>Raised when the asset is edited.</summary>
        public event Action Changed;

        public Rule AddRule(string selector, params UiFrameDeclaration[] declarations)
        {
            var r = new Rule { selector = selector };
            r.declarations.AddRange(declarations);
            rules.Add(r);
            Changed?.Invoke();
            return r;
        }

        /// <summary>Whether an element with these classes matches <paramref name="selector"/>.</summary>
        public static bool Matches(string selector, Func<string, bool> hasClass)
        {
            if (string.IsNullOrWhiteSpace(selector)) return false;
            foreach (var alternative in selector.Split(','))
            {
                var parts = alternative.Split(new[] { '.', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                var all = true;
                foreach (var p in parts)
                {
                    if (hasClass(p)) continue;
                    all = false;
                    break;
                }
                if (all) return true;
            }
            return false;
        }

        /// <summary>Applies matching rules, in order, to <paramref name="into"/>. Bad values are reported, not applied.</summary>
        public void Apply(FrameRegistry registry, Func<string, bool> hasClass, FrameStyle into, Action<string> report)
        {
            foreach (var rule in rules)
            {
                if (rule == null || !Matches(rule.selector, hasClass)) continue;
                foreach (var d in rule.declarations)
                {
                    if (string.IsNullOrWhiteSpace(d.property)) continue;
                    if (!registry.TryApply(into, d.property, d.value, out var error))
                        report?.Invoke($"{name} '{rule.selector}': {error}");
                }
            }
        }

        void OnValidate() => Changed?.Invoke();
    }
}
