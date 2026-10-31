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
    // element with a border width therefore holds a currentcolor layer per side in its layer map at
    // StyleLayerPriority.Preflight, which every other layer outranks and whose colour ColorReferenceWatch follows.
    // The width comes from a border-width utility in its classes, from a divide on its parent (SetDivided), or from a
    // rule of a non-default stylesheet that sets one, which is looked for on an element carrying a class or a name of
    // its own when it attaches.
    //
    // A layer is written inline, which outranks every stylesheet rule, so a side is held back while a rule of a
    // stylesheet that is not a default one (control themes are, and CSS preflight overrides user-agent styles) matches
    // the element and sets that side's border colour, whatever the rule's selector, and while an inline colour that
    // is not Velvet's stands on it, as an inline style outranks preflight in CSS. The rules that match are asked of
    // the engine's own matcher (MatchedRulesExtractor) when the element's classes are applied and each time the
    // element is restyled with a different set of matched rules, which is what a class, a pseudo-state or a
    // stylesheet changing does. The restyle event reaches the element through the marker class ColorReferenceWatch
    // documents.
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

        [Flags]
        private enum Reasons
        {
            None = 0,
            Classes = 1,
            Divided = 2,
            Rule = 4,
        }

        private sealed class State
        {
            public Reasons Reasons;
            public Sides Held;
            public Sides Claimed;
            public bool Resyncing;
            // The key the engine cached the element's matched rules under when they were last asked.
            public long? Matched;
            // The colour each side's inline value last had while it was Velvet's, to tell a write that is not.
            public readonly Color[] Seen = new Color[4];
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

        private static readonly EventCallback<AttachToPanelEvent, VisualElement> s_onAttached = (_, element) =>
        {
            if (s_states.TryGetValue(element, out var state) && (state.Reasons & Reasons.Classes) != 0)
            {
                SnappedBorderSheets.FollowRootOf(element);
            }
            Resync(element, ask: true);
        };

        private static readonly EventCallback<AttachToPanelEvent, VisualElement> s_onCandidateAttached =
            (_, element) => LookForRule(element);

        private static readonly EventCallback<CustomStyleResolvedEvent, VisualElement> s_onResolved = (_, element) =>
        {
            // A restyle that moves no rule (a colour inherited from above, say) asks nothing of the matcher.
            var hash = StyleCascade.RulesHash(element);
            Resync(element, ask: !(s_states.TryGetValue(element, out var state) && hash is { } known && known == state.Matched));
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
            if (HasBorderWidth(classNames))
            {
                if (!Add(element, Reasons.Classes) && classesChanged)
                {
                    Resync(element, ask: true);
                }
                return;
            }
            Remove(element, Reasons.Classes);
            if (!classesChanged || !IsRuleCandidate(element, classNames))
            {
                return;
            }
            if (element.panel == null)
            {
                element.RegisterCallbackOnce(s_onCandidateAttached, element);
            }
            else
            {
                LookForRule(element);
            }
        }

        // The child of a divide, which draws the divider as its border on one edge and so has a width to draw a colour on.
        internal static void SetDivided(VisualElement child, bool divided)
        {
            if (s_extractor == null)
            {
                return;
            }
            if (divided)
            {
                Add(child, Reasons.Divided);
            }
            else
            {
                Remove(child, Reasons.Divided);
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
            element.UnregisterCallback(s_onCandidateAttached);
            ColorReferenceWatch.Unmark(element);
        }

        // Whether the element was asked for its rules in doing so.
        private static bool Add(VisualElement element, Reasons reason)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                state = new State();
                s_states.Add(element, state);
                ColorReferenceWatch.Mark(element);
                element.RegisterCallback(s_onAttached, element);
                element.RegisterCallback(s_onResolved, element);
                state.Reasons = reason;
                Resync(element, ask: true);
                return true;
            }
            if ((state.Reasons & reason) == 0)
            {
                state.Reasons |= reason;
                Resync(element, ask: true);
                return true;
            }
            return false;
        }

        private static void Remove(VisualElement element, Reasons reason)
        {
            if (!s_states.TryGetValue(element, out var state) || (state.Reasons & reason) == 0)
            {
                return;
            }
            state.Reasons &= ~reason;
            if (state.Reasons == Reasons.None)
            {
                Release(element);
            }
        }

        // An element that may be given a width by a rule of a stylesheet: one with a class that is not a Velvet
        // utility, or a name an id selector can match. Elements with neither are not asked.
        private static bool IsRuleCandidate(VisualElement element, string[] classNames)
        {
            if (!string.IsNullOrEmpty(element.name))
            {
                return true;
            }
            for (var i = 0; i < classNames.Length; i++)
            {
                if (FiberElementFactory.IsPlainClassToken(classNames[i]) && !StyleUtilityProperties.TryGet(classNames[i], out _))
                {
                    return true;
                }
            }
            return false;
        }

        private static void LookForRule(VisualElement element)
        {
            if (element.panel == null)
            {
                return;
            }
            var matched = Matches(element);
            if (matched.SetsWidth)
            {
                Add(element, Reasons.Rule);
            }
        }

        private static void Resync(VisualElement element, bool ask)
        {
            if (element.panel == null || !s_states.TryGetValue(element, out var state) || state.Resyncing)
            {
                return;
            }
            state.Resyncing = true;
            try
            {
                if (ask)
                {
                    state.Matched = StyleCascade.RulesHash(element);
                    var matched = Matches(element);
                    state.Claimed = matched.Claimed;
                    if ((state.Reasons & Reasons.Classes) != 0)
                    {
                        SnappedBorderSheets.FollowRootOf(element);
                    }
                    state.Reasons = matched.SetsWidth ? state.Reasons | Reasons.Rule : state.Reasons & ~Reasons.Rule;
                    if (state.Reasons == Reasons.None)
                    {
                        Release(element);
                        return;
                    }
                }
                var resolved = element.resolvedStyle.color;
                for (var i = 0; i < s_sideProperties.Length; i++)
                {
                    SyncSide(element, state, i, resolved);
                }
            }
            finally
            {
                state.Resyncing = false;
            }
        }

        private static void SyncSide(VisualElement element, State state, int index, Color resolved)
        {
            var side = (Sides)(1 << index);
            var inline = InlineColor(element, index);
            var set = inline.keyword == StyleKeyword.Undefined;
            if ((state.Held & side) != 0)
            {
                if (set && inline.value != resolved && inline.value != state.Seen[index])
                {
                    // Something else wrote the side since: its value stands, and the layer lets go without writing.
                    StyleArbitraryValueResolver.Withdraw(element, s_sideProperties[index], StyleLayerPriority.Preflight);
                    state.Held &= ~side;
                    return;
                }
                if ((state.Claimed & side) == 0)
                {
                    state.Seen[index] = set ? inline.value : state.Seen[index];
                    return;
                }
                StyleArbitraryValueResolver.Clear(element, s_sideProperties[index], StyleLayerPriority.Preflight);
                state.Held &= ~side;
                return;
            }
            if ((state.Claimed & side) != 0 || set)
            {
                return;
            }
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(s_sideProperties[index], Color.white, ColorReference.Current), StyleLayerPriority.Preflight);
            state.Held |= side;
            state.Seen[index] = InlineColor(element, index).value;
        }

        private static StyleColor InlineColor(VisualElement element, int index) => index switch
        {
            0 => element.style.borderTopColor,
            1 => element.style.borderRightColor,
            2 => element.style.borderBottomColor,
            _ => element.style.borderLeftColor,
        };

        private readonly struct Match
        {
            public readonly Sides Claimed;
            public readonly bool SetsWidth;

            public Match(Sides claimed, bool setsWidth) => (Claimed, SetsWidth) = (claimed, setsWidth);
        }

        // The sides a rule of a non-default stylesheet matching element sets the border colour of, and whether one sets
        // a border width.
        private static Match Matches(VisualElement element)
        {
            s_clear!.Invoke(s_extractor, null);
            s_find!.Invoke(s_extractor, new object[] { element });
            var claimed = Sides.None;
            var width = false;
            foreach (var record in (IList)s_records!.GetValue(s_extractor))
            {
                if ((bool)s_isDefault!.GetValue(s_recordSheet!.GetValue(record)))
                {
                    continue;
                }
                var rule = s_rule!.GetValue(s_recordSelector!.GetValue(record));
                foreach (var property in (Array)s_properties!.GetValue(rule))
                {
                    var name = (string)s_propertyName!.GetValue(property);
                    claimed |= SidesOf(name);
                    width |= IsWidth(name);
                }
            }
            s_clear.Invoke(s_extractor, null);
            return new Match(claimed, width);
        }

        private static bool IsWidth(string property)
            => property is "border-width" or "border-top-width" or "border-right-width" or "border-bottom-width"
                or "border-left-width";

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
