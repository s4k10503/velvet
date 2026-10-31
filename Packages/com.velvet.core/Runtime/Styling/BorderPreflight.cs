#nullable enable
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Tailwind's preflight gives every element `border-color: currentcolor`, below every rule an author writes. An
    // element carrying a border-width utility therefore holds a currentcolor layer in its layer map at
    // StyleLayerPriority.Preflight, which every other layer outranks and whose colour ColorReferenceWatch follows.
    //
    // A layer is written inline, which outranks every stylesheet rule, so a side is held back while a rule of a
    // stylesheet that is not a default one (control themes are, and CSS preflight overrides user-agent styles) matches
    // the element and sets that side's border colour, whatever the rule's selector. The rules that match are asked of
    // the engine's own matcher (MatchedRulesExtractor) when the element's classes are applied and each time the
    // element is restyled, which is what a class, a pseudo-state or a stylesheet changing does. The restyle event
    // reaches the element through the marker class ColorReferenceWatch documents.
    internal static class BorderPreflight
    {
        [Flags]
        private enum Sides
        {
            None = 0,
            Top = 1,
            Right = 2,
            Bottom = 4,
            Left = 8,
            All = Top | Right | Bottom | Left,
        }

        private sealed class State
        {
            public Sides Held;
            public bool Resyncing;
            // The key the engine cached the element's matched rules under when they were last asked.
            public long? Matched;
        }

        private static readonly ConditionalWeakTable<VisualElement, State> s_states = new();

        private static readonly MethodInfo? s_find = EngineMember.MatchedRulesExtractorFind.ResolveMethod();
        private static readonly MethodInfo? s_clear = EngineMember.MatchedRulesExtractorClear.ResolveMethod();
        private static readonly FieldInfo? s_records = EngineMember.MatchedRulesExtractorRecords.ResolveField();
        private static readonly FieldInfo? s_recordSheet = EngineMember.SelectorMatchRecordSheet.ResolveField();
        private static readonly FieldInfo? s_recordSelector = EngineMember.SelectorMatchRecordSelector.ResolveField();
        private static readonly PropertyInfo? s_rule = EngineMember.StyleComplexSelectorRule.ResolveProperty();
        private static readonly PropertyInfo? s_isDefault = EngineMember.StyleSheetIsDefault.ResolveProperty();
        private static readonly PropertyInfo? s_properties = EngineMember.StyleRuleProperties.ResolveProperty();
        private static readonly PropertyInfo? s_propertyName = EngineMember.StylePropertyName.ResolveProperty();

        // Without the matcher a rule that sets a border colour cannot be told from none, and a layer would then
        // outrank it; so nothing is written.
        private static readonly object? s_extractor = CreateExtractor();

        private static object? CreateExtractor()
        {
            if (s_find == null || s_clear == null || s_records == null || s_recordSheet == null || s_recordSelector == null
                || s_rule == null || s_isDefault == null || s_properties == null || s_propertyName == null)
            {
                return null;
            }
            return Activator.CreateInstance(s_find.DeclaringType!, new Func<StyleSheet, string>(_ => string.Empty));
        }

        private static readonly EventCallback<AttachToPanelEvent, VisualElement> s_onAttached = (_, element) => Resync(element);

        private static readonly EventCallback<CustomStyleResolvedEvent, VisualElement> s_onResolved = (_, element) =>
        {
            // A restyle that moves no rule (a colour inherited from above, say) asks nothing of the matcher.
            if (s_states.TryGetValue(element, out var state) && StyleCascade.RulesHash(element) is { } hash
                && hash == state.Matched)
            {
                return;
            }
            Resync(element);
        };

        private static readonly ArbitraryProperty[] s_sideProperties =
        {
            ArbitraryProperty.BorderTopColor,
            ArbitraryProperty.BorderRightColor,
            ArbitraryProperty.BorderBottomColor,
            ArbitraryProperty.BorderLeftColor,
        };

        // classNames is the class list the element's passes resolved; classesChanged is whether it differs from the
        // list before the pass.
        internal static void OnClassesResolved(VisualElement element, string[] classNames, bool classesChanged)
        {
            if (s_extractor == null)
            {
                return;
            }
            if (!HasBorderWidth(classNames))
            {
                Release(element);
                return;
            }
            if (!s_states.TryGetValue(element, out _))
            {
                s_states.Add(element, new State());
                ColorReferenceWatch.Mark(element);
                element.RegisterCallback(s_onAttached, element);
                element.RegisterCallback(s_onResolved, element);
                classesChanged = true;
            }
            if (classesChanged)
            {
                Resync(element);
            }
        }

        // Withdraws what the element holds and stops listening.
        internal static void Release(VisualElement element)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                return;
            }
            Forget(element);
            for (var i = 0; i < s_sideProperties.Length; i++)
            {
                if ((state.Held & (Sides)(1 << i)) != 0)
                {
                    StyleArbitraryValueResolver.Clear(element, s_sideProperties[i], StyleLayerPriority.Preflight);
                }
            }
        }

        // Stops listening, for a teardown that drops the layer map itself.
        internal static void Forget(VisualElement element)
        {
            if (!s_states.Remove(element))
            {
                return;
            }
            element.UnregisterCallback(s_onAttached);
            element.UnregisterCallback(s_onResolved);
            ColorReferenceWatch.Unmark(element);
        }

        private static void Resync(VisualElement element)
        {
            if (element.panel == null || !s_states.TryGetValue(element, out var state) || state.Resyncing)
            {
                return;
            }
            state.Resyncing = true;
            try
            {
                state.Matched = StyleCascade.RulesHash(element);
                var wanted = Sides.All & ~Claimed(element);
                for (var i = 0; i < s_sideProperties.Length; i++)
                {
                    var side = (Sides)(1 << i);
                    var holds = (state.Held & side) != 0;
                    if (holds == ((wanted & side) != 0))
                    {
                        continue;
                    }
                    if (holds)
                    {
                        StyleArbitraryValueResolver.Clear(element, s_sideProperties[i], StyleLayerPriority.Preflight);
                        state.Held &= ~side;
                    }
                    else
                    {
                        StyleArbitraryValueResolver.Apply(element,
                            new ArbitraryStyle(s_sideProperties[i], Color.white, ColorReference.Current),
                            StyleLayerPriority.Preflight);
                        state.Held |= side;
                    }
                }
            }
            finally
            {
                state.Resyncing = false;
            }
        }

        // The sides a rule of a non-default stylesheet matching element sets the border colour of.
        private static Sides Claimed(VisualElement element)
        {
            s_clear!.Invoke(s_extractor, null);
            s_find!.Invoke(s_extractor, new object[] { element });
            var claimed = Sides.None;
            foreach (var record in (IList)s_records!.GetValue(s_extractor))
            {
                if ((bool)s_isDefault!.GetValue(s_recordSheet!.GetValue(record)))
                {
                    continue;
                }
                var rule = s_rule!.GetValue(s_recordSelector!.GetValue(record));
                foreach (var property in (Array)s_properties!.GetValue(rule))
                {
                    claimed |= SidesOf((string)s_propertyName!.GetValue(property));
                }
            }
            s_clear.Invoke(s_extractor, null);
            return claimed;
        }

        private static Sides SidesOf(string property) => property switch
        {
            "border-color" => Sides.All,
            "border-top-color" => Sides.Top,
            "border-right-color" => Sides.Right,
            "border-bottom-color" => Sides.Bottom,
            "border-left-color" => Sides.Left,
            _ => Sides.None,
        };

        private static bool HasBorderWidth(string[] classNames)
        {
            for (var i = 0; i < classNames.Length; i++)
            {
                if (IsBorderWidthToken(classNames[i]))
                {
                    return true;
                }
            }
            return false;
        }

        // A bundled or bare-number width (border, border-2, border-x-4) or a bracketed one (border-[3px],
        // border-t-[0.5rem]), under any variants and the important marker. A zero width draws nothing.
        internal static bool IsBorderWidthToken(string token)
        {
            var core = token.AsSpan();
            var colon = core.LastIndexOf(':');
            if (colon >= 0)
            {
                core = core.Slice(colon + 1);
            }
            core = core.TrimStart('!').TrimEnd('!');
            if (!core.StartsWith("border".AsSpan(), StringComparison.Ordinal))
            {
                return false;
            }
            var rest = core.Slice("border".Length);
            if (rest.Length == 0)
            {
                return true;
            }
            if (rest[0] != '-')
            {
                return false;
            }
            rest = rest.Slice(1);
            if (rest.Length > 0 && "xytrbl".IndexOf(rest[0]) >= 0 && (rest.Length == 1 || rest[1] == '-'))
            {
                rest = rest.Length == 1 ? ReadOnlySpan<char>.Empty : rest.Slice(2);
                if (rest.Length == 0)
                {
                    return true;
                }
            }
            if (rest.Length > 0 && rest[0] == '[')
            {
                return StyleArbitraryValueResolver.TryParse(core.ToString(), out var style) && IsWidth(style.Property);
            }
            return IsNonZeroNumber(rest);
        }

        private static bool IsWidth(ArbitraryProperty property)
            => property is ArbitraryProperty.BorderWidth or ArbitraryProperty.BorderTopWidth
                or ArbitraryProperty.BorderRightWidth or ArbitraryProperty.BorderBottomWidth
                or ArbitraryProperty.BorderLeftWidth;

        private static bool IsNonZeroNumber(ReadOnlySpan<char> text)
        {
            var nonZero = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
                nonZero |= text[i] != '0';
            }
            return nonZero;
        }
    }
}
