#nullable enable
using System;
using System.Reflection;
using UnityEngine.UIElements;
using WrittenProperties = System.Collections.Generic.List<(System.WeakReference<object> Rule, object Property)>;

namespace Velvet
{
    // Gives every rule of a stylesheet that declares a border radius the custom property CornerRadiusFit asks for. UI Toolkit
    // sends an element CustomStyleResolvedEvent when custom properties exist on it before or after its style is resolved, so
    // an element gets the event exactly when a rule declaring a radius starts or stops matching it, whatever changed the
    // match: a class from any writer, an ancestor's class, a pseudo-class. An element no such rule matches pays nothing.
    //
    // The stylesheets read are those above a mount (StyleThemeTokens.Refresh, which reads them again when one is attached,
    // removed or edited), with their imports, except the bundled utilities, whose radius rules each have a restating rule
    // of custom properties already (RadiusWatchBundledSheetsTests pins the pair), and the engine's default sheets. The
    // property is written into the sheet in memory with the engine's own edit calls, which rebuild the sheet and restyle
    // the panels using it. A sheet reimported or edited later has a new content hash and is read again. In the editor the
    // property is taken back before an assembly reload and when play mode ends, so a sheet opened in UI Builder afterwards
    // does not carry it.
    internal static class RadiusWatchSheets
    {
        internal const string Property = "--velvet-radius-watch";

        private static readonly PropertyInfo? s_sheetRules = EngineMember.StyleSheetRules.ResolveProperty();
        private static readonly PropertyInfo? s_ruleProperties = EngineMember.StyleRuleProperties.ResolveProperty();
        private static readonly PropertyInfo? s_propertyName = EngineMember.StylePropertyName.ResolveProperty();
        private static readonly PropertyInfo? s_values = EngineMember.StylePropertyValues.ResolveProperty();
        private static readonly MethodInfo? s_add = EngineMember.StyleRuleAddProperty.ResolveMethod();
        private static readonly MethodInfo? s_remove = EngineMember.StyleRuleRemoveProperty.ResolveMethod();
        private static readonly FieldInfo? s_keyword = EngineMember.StyleValueTypeKeyword.ResolveField();
        private static readonly ConstructorInfo? s_handle = EngineMember.StyleValueHandleConstruct.ResolveConstructor();
        private static readonly PropertyInfo? s_defaultSheet = EngineMember.StyleSheetIsDefault.ResolveProperty();

        private static readonly bool s_ready = s_sheetRules != null && s_ruleProperties != null && s_propertyName != null && s_values != null
            && s_add != null && s_remove != null && s_handle != null && s_keyword != null;

        // The content hash each sheet had once its rules were read, which the property moves when it is written.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StyleSheet, Read> s_read = new();

        private sealed class Read
        {
            public int Hash;
        }

        // What was written, kept for the take-back.
        private static readonly WrittenProperties s_written = new();

        // Reads the stylesheets of root and of every element above it, and what they import. Returns whether a sheet
        // changed, which moves its content hash.
        internal static bool EnsureAbove(VisualElement root)
        {
            var changed = false;
            for (var element = root; element != null; element = element.hierarchy.parent)
            {
                for (var i = 0; i < element.styleSheets.count; i++)
                {
                    changed |= Ensure(element.styleSheets[i]);
                }
            }
            return changed;
        }

        // The engine's default sheets and Velvet's own are not read.
        private static bool IsLeftAlone(StyleSheet sheet)
            => VelvetStyleUtilities.IsUtilities(sheet) || SnappedBorderSheets.IsSnapped(sheet) || IsDefault(sheet);

        private static bool IsDefault(StyleSheet sheet) => s_defaultSheet != null && (bool)s_defaultSheet.GetValue(sheet);

        private static bool Ensure(StyleSheet? sheet)
        {
            if (!s_ready || sheet == null || IsLeftAlone(sheet))
            {
                return false;
            }
            var changed = false;
            foreach (var imported in VelvetStyleUtilities.ImportsOf(sheet))
            {
                changed |= Ensure(imported);
            }
            if (s_read.TryGetValue(sheet, out var read) && read.Hash == sheet.contentHash)
            {
                return changed;
            }
            var wrote = Write(sheet);
            s_read.Remove(sheet);
            s_read.Add(sheet, new Read { Hash = sheet.contentHash });
            return changed || wrote;
        }

        private static bool Write(StyleSheet sheet)
        {
            var wrote = false;
            try
            {
                foreach (var rule in (Array)s_sheetRules!.GetValue(sheet))
                {
                    if (DeclaresRadius(rule, out var held) && !held)
                    {
                        var property = s_add!.Invoke(rule, new object[] { Property })!;
                        s_values!.SetValue(property, KeywordValue());
                        s_written.Add((new WeakReference<object>(rule), property));
                        wrote = true;
                    }
                }
            }
            catch (Exception)
            {
                // A sheet the engine's edit calls cannot take is left as it was.
            }
            return wrote;
        }

        private static bool DeclaresRadius(object rule, out bool held)
        {
            var declares = false;
            held = false;
            foreach (var property in (Array)s_ruleProperties!.GetValue(rule))
            {
                var name = (string)s_propertyName!.GetValue(property);
                held |= name == Property;
                declares |= IsRadius(name);
            }
            return declares;
        }

        private static bool IsRadius(string name)
            => name == "border-radius"
                || (name.StartsWith("border-", StringComparison.Ordinal) && name.EndsWith("-radius", StringComparison.Ordinal)
                    && name.Length > "border--radius".Length);

        // A value no sheet array holds, so one handle does for every rule of every sheet.
        private static Array KeywordValue()
        {
            var handle = s_handle!.Invoke(new[] { 0, s_keyword!.GetValue(null) });
            var values = Array.CreateInstance(s_handle.DeclaringType!, 1);
            values.SetValue(handle, 0);
            return values;
        }

        // Takes the property back out of every rule it was written into.
        internal static void TakeBack()
        {
            if (!s_ready)
            {
                return;
            }
            foreach (var (rule, property) in s_written)
            {
                if (rule.TryGetTarget(out var target))
                {
                    s_remove!.Invoke(target, new[] { property });
                }
            }
            s_written.Clear();
        }

#if UNITY_EDITOR
        static RadiusWatchSheets()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += TakeBack;
            UnityEditor.EditorApplication.playModeStateChanged += change =>
            {
                if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                {
                    TakeBack();
                }
            };
        }
#endif
    }
}
