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
    // rule of a non-default stylesheet that sets one, which an element with a class or a name of its own listens for
    // and any other element is asked about on attach.
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
            // Listening only: a rule might give the element a width later, which the matcher is asked about.
            Candidate = 8,
            // A width a child-scoped variant puts on this element (*:border, a field's input box).
            Scoped = 16,
            // A width the element's code wrote inline.
            Inline = 32,
            Layered = Classes | Divided | Rule | Scoped | Inline,
        }

        private sealed class State
        {
            public Reasons Reasons;
            public Sides Held;
            public Sides Claimed;
            public bool Resyncing;
            // Whether a colour has landed on the element yet; only the first lands without a transition.
            public bool Landed;
            // The child-variant manipulators that give this element a width.
            public System.Collections.Generic.HashSet<object>? ScopedBy;
            // The key the engine cached the element's matched rules under when they were last asked.
            public long? Matched;
            // The colour each side's inline value last had while it was Velvet's, to tell a write that is not.
            public readonly Color[] Seen = new Color[4];
        }

        private static readonly ConditionalWeakTable<VisualElement, State> s_states = new();

        private static readonly ConstructorInfo? s_construct = EngineMember.MatchedRulesExtractorConstruct.ResolveConstructor();
        private static readonly PropertyInfo? s_values = EngineMember.StylePropertyValues.ResolveProperty();
        private static readonly PropertyInfo? s_valueKind = EngineMember.StyleValueHandleKind.ResolveProperty();
        private static readonly MethodInfo? s_readFloat = EngineMember.StyleSheetReadFloat.ResolveMethod();
        private static readonly MethodInfo? s_readDimension = EngineMember.StyleSheetReadDimension.ResolveMethod();
        private static readonly FieldInfo? s_dimensionValue = EngineMember.DimensionValue.ResolveField();
        private static readonly MethodInfo? s_readKeyword = EngineMember.StyleSheetReadKeyword.ResolveMethod();
        private static readonly PropertyInfo? s_typeName = EngineMember.ElementTypeName.ResolveProperty();
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
            if (s_construct == null || s_find == null || s_clear == null || s_records == null || s_recordSheet == null || s_recordSelector == null
                || s_rule == null || s_isDefault == null || s_properties == null || s_propertyName == null)
            {
                return null;
            }
            try
            {
                return s_construct.Invoke(new object[] { new Func<StyleSheet, string>(_ => string.Empty) });
            }
            catch (Exception exception) when (exception is TargetInvocationException or MemberAccessException)
            {
                return null;
            }
        }

        // Nothing is written here: the text colour a layer reads is the element's own only once it has had its first
        // style pass, and the restyle event that follows the attach is what asks.
        private static readonly EventCallback<AttachToPanelEvent, VisualElement> s_onAttached = (_, element) =>
        {
            if (s_states.TryGetValue(element, out var state) && (state.Reasons & Reasons.Classes) != 0)
            {
                SnappedBorderSheets.FollowRootOf(element);
            }
        };

        // An element with a class of its own or a name listens at once, whatever the stylesheets say now: a stylesheet
        // added later changes what it matches, and the restyle that causes is what answers.
        private static readonly EventCallback<AttachToPanelEvent, VisualElement> s_onNamedAttached =
            (_, element) => Add(element, Reasons.Candidate);

        // Any other element is looked at when it attaches, for a rule selecting its type or every element.
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
            if (HasOwnBorderWidth(element, classNames))
            {
                if (!Add(element, Reasons.Classes) && classesChanged)
                {
                    Resync(element, ask: true);
                }
                return;
            }
            Remove(element, Reasons.Classes);
            if (!classesChanged)
            {
                return;
            }
            var named = IsNamedOrClassed(element, classNames);
            if (named)
            {
                element.UnregisterCallback(s_onCandidateAttached);
                element.RegisterCallback(s_onNamedAttached, element);
                if (element.panel != null)
                {
                    Add(element, Reasons.Candidate);
                }
                return;
            }
            element.UnregisterCallback(s_onNamedAttached);
            Remove(element, Reasons.Candidate);
            // Asked again on every attach: a reparent can bring a stylesheet with it.
            element.RegisterCallback(s_onCandidateAttached, element);
            if (element.panel != null && s_typeRulesSeen)
            {
                LookForRule(element);
            }
        }

        // The children a child-scoped variant gives a border width, counted by the manipulators that give it.
        internal static void SetScoped(VisualElement child, object owner, bool widthGiven)
        {
            if (s_extractor == null)
            {
                return;
            }
            if (widthGiven)
            {
                Add(child, Reasons.Scoped);
                s_states.TryGetValue(child, out var state);
                (state!.ScopedBy ??= new System.Collections.Generic.HashSet<object>()).Add(owner);
                return;
            }
            if (!s_states.TryGetValue(child, out var held) || held.ScopedBy == null)
            {
                return;
            }
            held.ScopedBy.Remove(owner);
            if (held.ScopedBy.Count == 0)
            {
                Remove(child, Reasons.Scoped);
            }
        }

        // Records the inline colours of element's sides as Velvet's own, after the resolver wrote one.
        internal static void NoteWritten(VisualElement element)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                return;
            }
            for (var i = 0; i < s_sideProperties.Length; i++)
            {
                var inline = InlineColor(element, i);
                if (inline.keyword == StyleKeyword.Undefined)
                {
                    state.Seen[i] = inline.value;
                }
            }
        }

        // Whether the inline colour on edge of owner is the preflight layer's, which no other layer outranks.
        internal static bool Writes(VisualElement owner, DivideEdge edge)
        {
            var property = edge switch
            {
                DivideEdge.Top => ArbitraryProperty.BorderTopColor,
                DivideEdge.Right => ArbitraryProperty.BorderRightColor,
                DivideEdge.Bottom => ArbitraryProperty.BorderBottomColor,
                _ => ArbitraryProperty.BorderLeftColor,
            };
            var shared = edge is DivideEdge.Left or DivideEdge.Right ? ArbitraryProperty.BorderXColor : ArbitraryProperty.BorderYColor;
            return StyleArbitraryValueResolver.TryRestingLayer(owner, property, s_noSwap, out _, out var key)
                && key == StyleLayerPriority.Preflight
                && !StyleArbitraryValueResolver.HasLayer(owner, ArbitraryProperty.BorderColor)
                && !StyleArbitraryValueResolver.HasLayer(owner, shared);
        }

        private static readonly System.Collections.Generic.HashSet<ArbitraryProperty> s_noSwap = new();

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
            element.UnregisterCallback(s_onCandidateAttached);
            element.UnregisterCallback(s_onNamedAttached);
            if (!s_states.Remove(element))
            {
                return;
            }
            element.UnregisterCallback(s_onAttached);
            element.UnregisterCallback(s_onResolved);
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
            else if ((state.Reasons & Reasons.Layered) == 0)
            {
                DropLayers(element, state);
            }
        }

        // Whether a rule naming the element's class or its name could give it a width: it has a name, or a class no
        // bundled utility stylesheet declares. Such an element listens once attached; any other is asked, on every attach,
        // for a rule that selects its type or every element.
        private static bool IsNamedOrClassed(VisualElement element, string[] classNames)
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

        // Set once a scanned stylesheet has a width rule selecting a type or every element.
        private static bool s_typeRulesSeen;

        // Starts listening on an attached candidate when a stylesheet reaching it has a rule setting a border width
        // that names one of its classes or its name.
        private static void LookForRule(VisualElement element)
        {
            if (element.panel == null)
            {
                return;
            }
            if (DeclaresWidth(element))
            {
                Add(element, Reasons.Candidate);
            }
            else
            {
                Remove(element, Reasons.Candidate);
            }
        }

        private static bool DeclaresWidth(VisualElement element)
        {
            for (var root = element; root != null; root = root.hierarchy.parent)
            {
                for (var i = 0; i < root.styleSheets.count; i++)
                {
                    var names = WidthNames.Of(root.styleSheets[i]);
                    if (names.Wildcard || names.Ids.Contains(element.name) || (names.Types.Count > 0 && s_typeName != null && names.Types.Contains((string)s_typeName.GetValue(element))))
                    {
                        return true;
                    }
                    foreach (var cls in element.GetClasses())
                    {
                        if (names.Classes.Contains(cls))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // The classes and ids the selectors of a stylesheet's border-width rules name, not counting the bundled
        // utilities' own, which a class token already decides.
        private sealed class WidthNames
        {
            public readonly System.Collections.Generic.HashSet<string> Classes = new();
            public readonly System.Collections.Generic.HashSet<string> Ids = new();
            public readonly System.Collections.Generic.HashSet<string> Types = new();
            public bool Wildcard;
            private int _hash;

            private static readonly ConditionalWeakTable<StyleSheet, WidthNames> s_names = new();

            // Read again once the sheet has a different content hash, as a reimport gives it.
            public static WidthNames Of(StyleSheet sheet)
            {
                var hash = sheet.contentHash;
                if (s_names.TryGetValue(sheet, out var known) && known._hash == hash)
                {
                    return known;
                }
                var names = new WidthNames { _hash = hash };
                names.Add(sheet);
                s_typeRulesSeen |= names.Wildcard || names.Types.Count > 0;
                s_names.Remove(sheet);
                s_names.Add(sheet, names);
                return names;
            }

            private static readonly PropertyInfo? s_rules = EngineMember.StyleSheetRules.ResolveProperty();
            private static readonly PropertyInfo? s_ruleSelectors = EngineMember.StyleRuleComplexSelectors.ResolveProperty();
            private static readonly PropertyInfo? s_simple = EngineMember.StyleComplexSelectorSelectors.ResolveProperty();
            private static readonly PropertyInfo? s_parts = EngineMember.StyleSelectorParts.ResolveProperty();
            private static readonly PropertyInfo? s_partValue = EngineMember.StyleSelectorPartValue.ResolveProperty();
            private static readonly PropertyInfo? s_partType = EngineMember.StyleSelectorPartType.ResolveProperty();

            private void Add(StyleSheet sheet)
            {
                if (VelvetStyleUtilities.IsUtilities(sheet) || SnappedBorderSheets.IsSnapped(sheet)
                    || (s_isDefault != null && (bool)s_isDefault.GetValue(sheet)) || s_rules == null
                    || s_ruleSelectors == null || s_simple == null || s_parts == null || s_partValue == null
                    || s_partType == null)
                {
                    return;
                }
                foreach (var imported in VelvetStyleUtilities.ImportsOf(sheet))
                {
                    Add(imported);
                }
                foreach (var rule in (Array)s_rules.GetValue(sheet))
                {
                    if (SetsWidth(sheet, rule))
                    {
                        AddNames(rule);
                    }
                }
            }

            private static bool SetsWidth(StyleSheet sheet, object rule)
            {
                foreach (var property in (Array)s_properties!.GetValue(rule))
                {
                    if (IsWidth((string)s_propertyName!.GetValue(property)) && !IsZero(sheet, property))
                    {
                        return true;
                    }
                }
                return false;
            }

            private void AddNames(object rule)
            {
                foreach (var complex in (Array)s_ruleSelectors!.GetValue(rule))
                {
                    foreach (var simple in (Array)s_simple!.GetValue(complex))
                    {
                        AddParts((Array)s_parts!.GetValue(simple));
                    }
                }
            }

            private void AddParts(Array parts)
            {
                foreach (var part in parts)
                {
                    var kind = Convert.ToInt32(s_partType!.GetValue(part));
                    var value = (string)s_partValue!.GetValue(part);
                    // StyleSelectorType.Wildcard, Type, Class and ID.
                    if (kind == 1)
                    {
                        Wildcard = true;
                    }
                    else if (kind == 2)
                    {
                        Types.Add(value);
                    }
                    else if (kind == 3)
                    {
                        Classes.Add(value);
                    }
                    else if (kind == 6)
                    {
                        Ids.Add(value);
                    }
                }
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
                }
                state.Reasons = HasInlineWidth(element) ? state.Reasons | Reasons.Inline : state.Reasons & ~Reasons.Inline;
                if ((state.Reasons & Reasons.Layered) == 0)
                {
                    DropLayers(element, state);
                    if (state.Reasons == Reasons.None)
                    {
                        Forget(element);
                    }
                    return;
                }
                for (var i = 0; i < s_sideProperties.Length; i++)
                {
                    SyncSide(element, state, i);
                }
            }
            finally
            {
                state.Resyncing = false;
            }
        }

        private static bool HasInlineWidth(VisualElement element)
        {
            var style = element.style;
            return IsSet(style.borderTopWidth) || IsSet(style.borderRightWidth) || IsSet(style.borderBottomWidth)
                || IsSet(style.borderLeftWidth);

            static bool IsSet(StyleFloat width) => width.keyword == StyleKeyword.Undefined && width.value > 0f;
        }

        private static void DropLayers(VisualElement element, State state)
        {
            for (var i = 0; i < s_sideProperties.Length; i++)
            {
                if ((state.Held & (Sides)(1 << i)) != 0)
                {
                    StyleArbitraryValueResolver.Clear(element, s_sideProperties[i], StyleLayerPriority.Preflight);
                }
            }
            state.Held = Sides.None;
        }

        private static void SyncSide(VisualElement element, State state, int index)
        {
            var side = (Sides)(1 << index);
            var inline = InlineColor(element, index);
            // A silhouette face's or a dashed divider's sentinel holds no colour of its own.
            var foreign = inline.keyword == StyleKeyword.Undefined && !SilhouetteFace.IsSentinel(inline.value);
            if ((state.Held & side) != 0)
            {
                // Every write Velvet makes to the slot is recorded (NoteWritten), so a different value is another writer's.
                if (foreign && inline.value != state.Seen[index])
                {
                    StyleArbitraryValueResolver.Withdraw(element, s_sideProperties[index], StyleLayerPriority.Preflight);
                    state.Held &= ~side;
                    return;
                }
                if ((state.Claimed & side) != 0)
                {
                    StyleArbitraryValueResolver.Clear(element, s_sideProperties[index], StyleLayerPriority.Preflight);
                    state.Held &= ~side;
                }
                return;
            }
            if ((state.Claimed & side) != 0 || foreign)
            {
                return;
            }
            // CSS runs no transition from a colour the element never showed; one it has shown transitions back to it.
            var hold = state.Landed ? default(StyleRelativeLengths.TransitionHold?) : new StyleRelativeLengths.TransitionHold(element);
            try
            {
                StyleArbitraryValueResolver.Apply(element,
                    new ArbitraryStyle(s_sideProperties[index], Color.white, ColorReference.Current), StyleLayerPriority.Preflight);
            }
            finally
            {
                hold?.Release();
            }
            state.Landed = true;
            state.Held |= side;
            // A face that took the slot's colour before the layer wrote it takes the layer's.
            SilhouetteFaceStash.Retake(element);
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
                    width |= IsWidth(name) && !IsZero(s_recordSheet.GetValue(record), property);
                }
            }
            s_clear.Invoke(s_extractor, null);
            return new Match(claimed, width);
        }

        // Whether every value of property reads as zero or initial, which draws no border.
        private static bool IsZero(object? sheet, object property)
        {
            if (s_values == null || s_valueKind == null || s_readFloat == null || s_readDimension == null
                || s_dimensionValue == null || s_readKeyword == null || sheet == null)
            {
                return false;
            }
            var values = (Array)s_values.GetValue(property);
            if (values.Length == 0)
            {
                return false;
            }
            foreach (var handle in values)
            {
                if (!IsZeroValue(sheet, handle))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsZeroValue(object sheet, object handle)
        {
            var args = new[] { handle };
            switch (s_valueKind!.GetValue(handle).ToString())
            {
                case "Float":
                    return (float)s_readFloat!.Invoke(sheet, args) == 0f;
                case "Dimension":
                    return (float)s_dimensionValue!.GetValue(s_readDimension!.Invoke(sheet, args)) == 0f;
                case "Keyword":
                    return s_readKeyword!.Invoke(sheet, args).ToString() == "Initial";
                default:
                    return false;
            }
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

        // The element's own classes with a border width, not counting those a child-scoped variant gives to a child nor
        // those a field's surface takes for its input box.
        private static bool HasOwnBorderWidth(VisualElement element, string[] classNames)
        {
            var control = StyleInputBoxSurface.IsControl(element);
            for (var i = 0; i < classNames.Length; i++)
            {
                var token = classNames[i];
                if (IsBorderWidthToken(token) && !StyleChildVariantClass.IsChildVariant(token)
                    && !(control && StyleInputBoxSurface.IsSurfaceToken(token)))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool HasBorderWidth(string[] classNames)
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

        // A bundled or bare-number width (border, border-2, border-x-4, border-s-2) or a bracketed one (border-[3px],
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
            var side = SideLength(rest);
            if (side > 0)
            {
                rest = rest.Length == side ? ReadOnlySpan<char>.Empty : rest.Slice(side + 1);
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

        // The length of the side suffix rest begins with (x y t r b l, or the logical s e bs be), or 0.
        private static int SideLength(ReadOnlySpan<char> rest)
        {
            foreach (var side in s_sideSuffixes)
            {
                if (rest.StartsWith(side.AsSpan(), StringComparison.Ordinal)
                    && (rest.Length == side.Length || rest[side.Length] == '-'))
                {
                    return side.Length;
                }
            }
            return 0;
        }

        private static readonly string[] s_sideSuffixes = { "bs", "be", "x", "y", "t", "r", "b", "l", "s", "e" };

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
