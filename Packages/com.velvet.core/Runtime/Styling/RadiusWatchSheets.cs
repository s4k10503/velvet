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
    // A source whose content hash changes, as an in-place edit does, has a new sheet derived for it, and a sheet whose
    // source is gone is destroyed. It is no sheet an editor or a tool can save: it is never the source, and the source is
    // never edited. Left out are the bundled utilities, whose radius rules
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

        // The sheet derived from each source, which a source of a new content hash derives a new one for: an element holding
        // the old one swaps it for the new at its next read, which also restyles it.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StyleSheet, Derived> s_derived = new();

        // Every sheet derived from each source, kept to be destroyed once the source is gone.
        private static readonly System.Collections.Generic.List<(WeakReference<StyleSheet> Source, StyleSheet Watch)> s_made = new();

        private static readonly System.Collections.Generic.HashSet<StyleSheet> s_wanted = new();

        private static bool s_reported;

        // Every sheet made here, by reference, so that one destroyed since is still known for what it was: the engine's null
        // check of a destroyed object answers true, and its name cannot be read. A sheet that outlived a domain reload is
        // known by its name, which is only asked while it is not destroyed.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StyleSheet, object> s_known = new();

        internal static bool IsWatch(StyleSheet sheet)
            => (object)sheet != null && (s_known.TryGetValue(sheet, out _) || (sheet != null && sheet.name == SheetName));

        // Attaches the derived sheets of the stylesheets of element and of every element above it.
        internal static void EnsureAbove(VisualElement element)
        {
            for (; element != null; element = element.hierarchy.parent)
            {
                EnsureOn(element);
            }
        }

        // Attaches, to element, the derived sheet of each stylesheet it holds and of what they import, and takes off it the
        // derived sheets of sources it no longer holds.
        internal static void EnsureOn(VisualElement element)
        {
            if (!s_ready)
            {
                return;
            }
            DestroyOrphans();
            s_wanted.Clear();
            var count = element.styleSheets.count;
            for (var i = 0; i < count; i++)
            {
                Attach(element, element.styleSheets[i]);
            }
            DropStale(element);
        }

        // Takes off element the watch sheets it no longer wants, and any sheet destroyed since it was added. The engine
        // refuses a destroyed sheet where one is removed, so the others are put back in their order instead.
        private static void DropStale(VisualElement element)
        {
            var stale = false;
            for (var i = element.styleSheets.count - 1; i >= 0 && !stale; i--)
            {
                var held = element.styleSheets[i];
                stale = held == null || (IsWatch(held) && !s_wanted.Contains(held));
            }
            if (!stale)
            {
                return;
            }
            var keep = new System.Collections.Generic.List<StyleSheet>();
            for (var i = 0; i < element.styleSheets.count; i++)
            {
                var held = element.styleSheets[i];
                if (held != null && !(IsWatch(held) && !s_wanted.Contains(held)))
                {
                    keep.Add(held);
                }
            }
            element.styleSheets.Clear();
            foreach (var held in keep)
            {
                element.styleSheets.Add(held);
            }
        }

        private static void Attach(VisualElement element, StyleSheet? source)
        {
            if (source == null || VelvetStyleUtilities.IsUtilities(source) || SnappedBorderSheets.IsSnapped(source) || IsWatch(source))
            {
                return;
            }
            var watch = DerivedOf(source);
            if (watch != null)
            {
                s_wanted.Add(watch);
                if (!element.styleSheets.Contains(watch))
                {
                    element.styleSheets.Add(watch);
                }
            }
            foreach (var imported in VelvetStyleUtilities.ImportsOf(source))
            {
                Attach(element, imported);
            }
        }

        internal static StyleSheet? DerivedOf(StyleSheet source)
        {
            if (s_derived.TryGetValue(source, out var known) && known.Hash == source.contentHash)
            {
                return known.Sheet;
            }
            var derived = known ?? new Derived();
            derived.Hash = source.contentHash;
            if (known == null)
            {
                s_derived.Add(source, derived);
            }
            StyleSheet? made = null;
            try
            {
                var rules = RadiusRules(source);
                derived.Sheet = null;
                if (rules.Count > 0)
                {
                    made = NewSheet();
                    Build(rules, made);
                    derived.Sheet = made;
                    s_made.Add((new WeakReference<StyleSheet>(source), made));
                }
            }
            catch (Exception exception)
            {
                derived.Sheet = null;
                if (made != null)
                {
                    Destroy(made);
                }
                if (!s_reported)
                {
                    s_reported = true;
                    Debug.LogWarning($"Velvet could not derive a radius watch sheet from '{source.name}', so a radius that arrives there without a size change waits for the next one: {exception.Message}");
                }
            }
            return derived.Sheet;
        }

        // Destroys the sheets derived from a source that no longer exists.
        private static void DestroyOrphans()
        {
            for (var i = s_made.Count - 1; i >= 0; i--)
            {
                if (s_made[i].Source.TryGetTarget(out var source) && source != null)
                {
                    continue;
                }
                var orphan = s_made[i].Watch;
                s_made.RemoveAt(i);
                Destroy(orphan);
            }
        }

        private static void Destroy(StyleSheet sheet)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(sheet);
                return;
            }
#endif
            UnityEngine.Object.Destroy(sheet);
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
            s_known.Add(sheet, s_known);
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
