#nullable enable
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using WatchRules = System.Collections.Generic.List<object>;

namespace Velvet
{
    // Gives CornerRadiusFit the style events it needs without touching a stylesheet of the application's. UI Toolkit sends an
    // element CustomStyleResolvedEvent when custom properties exist on it before or after its style is resolved, and a
    // custom property exists on an element once a rule that declares it matches, wherever that rule sits in the cascade.
    // So for each stylesheet with a rule that declares a border radius a second, runtime-only sheet redeclares the selectors of
    // those rules with the one property Property and nothing else, and is attached to the elements that hold the first. A
    // rule that starts or stops matching an element then reaches the fit, whatever changed the match: a class from any
    // writer, an ancestor's class, a pseudo-class. An element none of those rules matches carries no property of its own.
    //
    // The sheet is rebuilt, in place, when the content hash of its source changes, and is no sheet an editor or a tool can
    // save: it is never the source, and the source is never edited. Left out are the bundled utilities, whose radius rules
    // each have a restating rule of custom properties already (RadiusWatchSheetsPanelTests pins the pair), and the sheets of
    // Velvet's own.
    internal static class RadiusWatchSheets
    {
        internal const string Property = "--velvet-radius-watch";

        private const string SheetName = "velvet-radius-watch";

        private static readonly PropertyInfo? s_sheetRules = EngineMember.StyleSheetRules.ResolveProperty();
        private static readonly PropertyInfo? s_ruleProperties = EngineMember.StyleRuleProperties.ResolveProperty();
        private static readonly PropertyInfo? s_propertyName = EngineMember.StylePropertyName.ResolveProperty();
        private static readonly PropertyInfo? s_ruleSelectors = EngineMember.StyleRuleComplexSelectors.ResolveProperty();
        private static readonly PropertyInfo? s_complex = EngineMember.StyleComplexSelectorSelectors.ResolveProperty();
        private static readonly PropertyInfo? s_specificity = EngineMember.StyleComplexSelectorSpecificity.ResolveProperty();
        private static readonly PropertyInfo? s_parts = EngineMember.StyleSelectorParts.ResolveProperty();
        private static readonly PropertyInfo? s_relationship = EngineMember.StyleSelectorPreviousRelationship.ResolveProperty();
        private static readonly FieldInfo? s_declaredVariables = EngineMember.StyleRuleCustomPropertiesCount.ResolveField();

        private static readonly Type? s_builderType = EngineMember.StyleSheetBuilderConstruct.ResolveType();
        private static readonly MethodInfo? s_beginProperty = EngineMember.StyleSheetBuilderBeginProperty.ResolveMethod();
        private static readonly MethodInfo? s_beginRule = EngineMember.StyleSheetBuilderBeginRule.ResolveMethod();
        private static readonly MethodInfo? s_beginSelector = EngineMember.StyleSheetBuilderBeginComplexSelector.ResolveMethod();
        private static readonly MethodInfo? s_addSelector = EngineMember.StyleSheetBuilderAddSimpleSelector.ResolveMethod();
        private static readonly MethodInfo? s_endSelector = EngineMember.StyleSheetBuilderEndComplexSelector.ResolveMethod();
        private static readonly MethodInfo? s_addFloat = EngineMember.StyleSheetBuilderAddFloat.ResolveMethod();
        private static readonly MethodInfo? s_endProperty = EngineMember.StyleSheetBuilderEndProperty.ResolveMethod();
        private static readonly MethodInfo? s_endRule = EngineMember.StyleSheetBuilderEndRule.ResolveMethod();
        private static readonly MethodInfo? s_buildTo = EngineMember.StyleSheetBuilderBuildTo.ResolveMethod();

        private static readonly bool s_ready = s_sheetRules != null && s_ruleProperties != null && s_propertyName != null
            && s_ruleSelectors != null && s_complex != null && s_specificity != null && s_parts != null
            && s_relationship != null && s_builderType != null && s_beginRule != null && s_beginSelector != null
            && s_addSelector != null && s_endSelector != null && s_beginProperty != null && s_addFloat != null
            && s_endProperty != null && s_endRule != null && s_buildTo != null && s_declaredVariables != null;

        private sealed class Derived
        {
            public int Hash;
            public StyleSheet? Sheet;
        }

        // The sheet derived from each source, which a source of a new content hash builds again into the same object.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StyleSheet, Derived> s_derived = new();

        internal static bool IsWatch(StyleSheet sheet) => sheet != null && sheet.name == SheetName;

        // Attaches the derived sheets of the stylesheets of element and of every element above it.
        internal static void EnsureAbove(VisualElement element)
        {
            for (; element != null; element = element.hierarchy.parent)
            {
                EnsureOn(element);
            }
        }

        // Attaches, to element, the derived sheet of each stylesheet it holds and of what they import.
        internal static void EnsureOn(VisualElement element)
        {
            if (!s_ready)
            {
                return;
            }
            var count = element.styleSheets.count;
            for (var i = 0; i < count; i++)
            {
                Attach(element, element.styleSheets[i], followImports: true);
            }
        }

        private static void Attach(VisualElement element, StyleSheet? source, bool followImports)
        {
            if (source == null || VelvetStyleUtilities.IsUtilities(source) || SnappedBorderSheets.IsSnapped(source) || IsWatch(source))
            {
                return;
            }
            var watch = DerivedOf(source);
            if (watch != null && !element.styleSheets.Contains(watch))
            {
                element.styleSheets.Add(watch);
            }
            if (followImports)
            {
                foreach (var imported in VelvetStyleUtilities.ImportsOf(source))
                {
                    Attach(element, imported, followImports: true);
                }
            }
        }

        internal static StyleSheet? DerivedOf(StyleSheet source)
        {
            if (s_derived.TryGetValue(source, out var known) && known.Hash == source.contentHash)
            {
                return known.Sheet;
            }
            var rules = RadiusRules(source);
            var derived = known ?? new Derived();
            if (rules.Count > 0 || derived.Sheet != null)
            {
                derived.Sheet ??= NewSheet();
                Build(rules, derived.Sheet);
            }
            derived.Hash = source.contentHash;
            if (known == null)
            {
                s_derived.Add(source, derived);
            }
            return derived.Sheet;
        }

        private static WatchRules RadiusRules(StyleSheet source)
        {
            var found = new WatchRules();
            foreach (var rule in (Array)s_sheetRules!.GetValue(source))
            {
                foreach (var property in (Array)s_ruleProperties!.GetValue(rule))
                {
                    if (IsRadius((string)s_propertyName!.GetValue(property)))
                    {
                        found.Add(rule);
                        break;
                    }
                }
            }
            return found;
        }

        private static bool IsRadius(string name)
            => name == "border-radius"
                || (name.StartsWith("border-", StringComparison.Ordinal) && name.EndsWith("-radius", StringComparison.Ordinal)
                    && name.Length > "border--radius".Length);

        private static StyleSheet NewSheet()
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.name = SheetName;
            sheet.hideFlags = HideFlags.HideAndDontSave;
            return sheet;
        }

        private static void Build(WatchRules rules, StyleSheet into)
        {
            var builder = Activator.CreateInstance(s_builderType!)!;
            foreach (var rule in rules)
            {
                s_beginRule!.Invoke(builder, new object[] { 0 });
                foreach (var complex in (Array)s_ruleSelectors!.GetValue(rule))
                {
                    s_beginSelector!.Invoke(builder, new[] { s_specificity!.GetValue(complex) });
                    foreach (var simple in (Array)s_complex!.GetValue(complex))
                    {
                        var parts = (Array)((Array)s_parts!.GetValue(simple)).Clone();
                        s_addSelector!.Invoke(builder, new[] { parts, s_relationship!.GetValue(simple) });
                    }
                    s_endSelector!.Invoke(builder, null);
                }
                s_beginProperty!.Invoke(builder, new object[] { Property, -1 });
                s_addFloat!.Invoke(builder, new object[] { 1f });
                s_endProperty!.Invoke(builder, null);
                s_endRule!.Invoke(builder, null);
            }
            s_buildTo!.Invoke(builder, new object[] { into });
            // The property is only asked for in an element's computed style, never read as a variable, so the rules count none
            // and the variable context the elements they match inherit stays as it was (RadiusWatchSheetsPanelTests pins the
            // count and CornerRadiusFitPanelTests the event that comes anyway). Rebuilding the sheet would count it again.
            foreach (var rule in (Array)s_sheetRules!.GetValue(into))
            {
                s_declaredVariables!.SetValue(rule, 0);
            }
        }
    }
}
