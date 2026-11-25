#nullable enable
using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // Runtime-built stylesheets of one rule, a class selector declaring one custom property, which is what makes UI
    // Toolkit send CustomStyleResolvedEvent to an element carrying the class on a panel without the bundled
    // stylesheet too: FiberFontScope's for an inheritor, StyleTransitionBehavior's for an element that can run a
    // transition. A reader of an element's sheets skips them through IsMarker.
    internal static class MarkerSheets
    {
        // The specificity of a single class selector in a stylesheet the importer builds, as SnappedBorderSheets gives it.
        private const int ClassSpecificity = 11;

        private static readonly Type? s_builderType = EngineMember.StyleSheetBuilderConstruct.ResolveType();
        private static readonly MethodInfo? s_beginRule = EngineMember.StyleSheetBuilderBeginRule.ResolveMethod();
        private static readonly MethodInfo? s_beginSelector = EngineMember.StyleSheetBuilderBeginComplexSelector.ResolveMethod();
        private static readonly MethodInfo? s_createClass = EngineMember.StyleSelectorPartCreateClass.ResolveMethod();
        private static readonly MethodInfo? s_addSelector = EngineMember.StyleSheetBuilderAddSimpleSelector.ResolveMethod();
        private static readonly MethodInfo? s_endSelector = EngineMember.StyleSheetBuilderEndComplexSelector.ResolveMethod();
        private static readonly MethodInfo? s_beginProperty = EngineMember.StyleSheetBuilderBeginProperty.ResolveMethod();
        private static readonly MethodInfo? s_addFloat = EngineMember.StyleSheetBuilderAddFloat.ResolveMethod();
        private static readonly MethodInfo? s_endProperty = EngineMember.StyleSheetBuilderEndProperty.ResolveMethod();
        private static readonly MethodInfo? s_endRule = EngineMember.StyleSheetBuilderEndRule.ResolveMethod();
        private static readonly MethodInfo? s_buildTo = EngineMember.StyleSheetBuilderBuildTo.ResolveMethod();
        private static readonly PropertyInfo? s_sheetRules = EngineMember.StyleSheetRules.ResolveProperty();
        private static readonly FieldInfo? s_declaredVariables = EngineMember.StyleRuleCustomPropertiesCount.ResolveField();
        private static readonly PropertyInfo? s_isDefault = EngineMember.StyleSheetIsDefault.ResolveProperty();

        // Each sheet is named for its class, which is how one built in an earlier domain is known.
        private static readonly string[] s_names = { FiberFontScope.MarkerClass, StyleTransitionBehavior.WatchClass };

        internal static bool Builds { get; } = s_builderType != null && s_beginRule != null && s_beginSelector != null
            && s_addSelector != null && s_endSelector != null && s_beginProperty != null && s_addFloat != null
            && s_endProperty != null && s_endRule != null && s_buildTo != null && s_createClass != null && s_isDefault != null
            && s_sheetRules != null && s_declaredVariables != null;

        internal static bool IsMarker(StyleSheet? sheet) => sheet != null && Array.IndexOf(s_names, sheet.name) >= 0;

        // Marked a default sheet, as a theme is, so that the matcher's readers do not take its rule for an author's
        // (BorderPreflight.HasAuthorSheet). Null where a member does not resolve.
        internal static StyleSheet? Build(string className, string property)
        {
            if (!Builds)
            {
                return null;
            }

            var builder = Activator.CreateInstance(s_builderType!)!;
            s_beginRule!.Invoke(builder, new object[] { 0 });
            s_beginSelector!.Invoke(builder, new object[] { ClassSpecificity });
            var parts = Array.CreateInstance(s_createClass!.ReturnType, 1);
            parts.SetValue(s_createClass.Invoke(null, new object[] { className }), 0);
            s_addSelector!.Invoke(builder, new[] { parts, Enum.ToObject(s_addSelector.GetParameters()[1].ParameterType, 0) });
            s_endSelector!.Invoke(builder, null);
            s_beginProperty!.Invoke(builder, new object[] { property, -1 });
            s_addFloat!.Invoke(builder, new object[] { 1f });
            s_endProperty!.Invoke(builder, null);
            s_endRule!.Invoke(builder, null);
            var sheet = UnityEngine.ScriptableObject.CreateInstance<StyleSheet>();
            sheet.name = className;
            sheet.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            s_buildTo!.Invoke(builder, new object[] { sheet });
            // Counted as declaring no variable, as RadiusWatchSheets.Build counts its rules.
            s_declaredVariables!.SetValue(((Array)s_sheetRules!.GetValue(sheet)).GetValue(0), 0);
            s_isDefault!.SetValue(sheet, true);
            return sheet;
        }
    }
}
