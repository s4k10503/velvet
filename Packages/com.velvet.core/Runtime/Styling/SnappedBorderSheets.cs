#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A browser snaps a border width to whole device pixels and UI Toolkit draws the width it is given, so at a
    // panel scale whose device pixels do not divide the bundled widths (_borders.uss) the two differ. For such a scale
    // a root carrying the bundled utilities carries one more stylesheet, placed directly after them, that declares
    // the same single-class rules in the same order with each width snapped (StyleRelativeLengths.SnapToDevicePixels).
    // A rule there has the selector and the specificity of the bundled rule it snaps, in the bundled order and later in
    // the cascade, so the bundled width classes rank among themselves as they do unsnapped, and a class a variant
    // toggles is snapped the same; a rule of higher specificity, or of equal specificity in a stylesheet after it, outranks both. The sheet is followed through the
    // panel's scale by PanelScaleWatch. SnappedBorderSheetTests pins the rules against _borders.uss.
    internal static class SnappedBorderSheets
    {
        internal const string SheetName = "velvet-snapped-border-widths";

        // The specificity of a single class selector in a stylesheet the importer builds.
        private const int ClassSpecificity = 11;

        private const float WholeScaleTolerance = 1e-4f;

        // The width families of _borders.uss, in its order; the family's bare class is one pixel wide.
        private static readonly (string Family, string[] Properties)[] s_families =
        {
            ("border", new[] { "border-top-width", "border-right-width", "border-bottom-width", "border-left-width" }),
            ("border-x", new[] { "border-left-width", "border-right-width" }),
            ("border-y", new[] { "border-top-width", "border-bottom-width" }),
            ("border-t", new[] { "border-top-width" }),
            ("border-r", new[] { "border-right-width" }),
            ("border-b", new[] { "border-bottom-width" }),
            ("border-l", new[] { "border-left-width" }),
        };

        private static readonly int[] s_scale = { 0, 2, 4, 8 };

        private static readonly object s_owner = new();

        private static readonly Dictionary<float, StyleSheet> s_byScale = new();

        private static readonly ConditionalWeakTable<VisualElement, StyleSheet[]> s_held = new();

        private static readonly Type? s_builderType = EngineMember.StyleSheetBuilderConstruct.ResolveType();
        private static readonly MethodInfo? s_beginRule = EngineMember.StyleSheetBuilderBeginRule.ResolveMethod();
        private static readonly MethodInfo? s_beginSelector = EngineMember.StyleSheetBuilderBeginComplexSelector.ResolveMethod();
        private static readonly MethodInfo? s_addSelector = EngineMember.StyleSheetBuilderAddSimpleSelector.ResolveMethod();
        private static readonly MethodInfo? s_endSelector = EngineMember.StyleSheetBuilderEndComplexSelector.ResolveMethod();
        private static readonly MethodInfo? s_beginProperty = EngineMember.StyleSheetBuilderBeginProperty.ResolveMethod();
        private static readonly MethodInfo? s_addDimension = EngineMember.StyleSheetBuilderAddDimension.ResolveMethod();
        private static readonly MethodInfo? s_endProperty = EngineMember.StyleSheetBuilderEndProperty.ResolveMethod();
        private static readonly MethodInfo? s_endRule = EngineMember.StyleSheetBuilderEndRule.ResolveMethod();
        private static readonly MethodInfo? s_buildTo = EngineMember.StyleSheetBuilderBuildTo.ResolveMethod();
        private static readonly MethodInfo? s_createClass = EngineMember.StyleSelectorPartCreateClass.ResolveMethod();
        private static readonly FieldInfo? s_pixel = EngineMember.DimensionUnitPixel.ResolveField();

        private static readonly bool s_buildable = s_builderType != null && s_beginRule != null && s_beginSelector != null
            && s_addSelector != null && s_endSelector != null && s_beginProperty != null && s_addDimension != null
            && s_endProperty != null && s_endRule != null && s_buildTo != null && s_createClass != null && s_pixel != null;

        // Keeps root's snapped sheet in step with its panel's scale, from now and across a move to another panel.
        internal static void Follow(VisualElement root)
        {
            PanelScaleWatch.Track(root, s_owner, Place);
            Place(root);
        }

        // Follows the scale of the nearest ancestor of element, itself included, whose stylesheets bring in the bundled
        // utilities, for a project that attached them without AttachTo.
        internal static void FollowRootOf(VisualElement element)
        {
            for (var root = element; root != null; root = root.hierarchy.parent)
            {
                if (VelvetStyleUtilities.IndexOfUtilities(root) >= 0)
                {
                    Follow(root);
                    return;
                }
            }
        }

        internal static void Stop(VisualElement root) => PanelScaleWatch.Untrack(root, s_owner);

        internal static bool IsSnapped(StyleSheet sheet) => sheet != null && sheet.name == SheetName;

        // The sheet for a panel scale, or none where every bundled width is already a whole number of device pixels.
        internal static StyleSheet? For(float scale)
        {
            if (!s_buildable || scale <= 0f || Math.Abs(scale - Math.Round(scale)) <= WholeScaleTolerance)
            {
                return null;
            }
            if (!s_byScale.TryGetValue(scale, out var sheet) || sheet == null)
            {
                sheet = Build(scale);
                s_byScale[scale] = sheet;
            }
            return sheet;
        }

        // Puts root's snapped sheet, or none, directly after the stylesheet that brings in the bundled utilities.
        private static void Place(VisualElement root)
        {
            var wanted = root.panel != null && VelvetStyleUtilities.IndexOfUtilities(root) >= 0
                ? For(root.panel.scaledPixelsPerPoint)
                : null;
            var held = s_held.TryGetValue(root, out var holder) ? holder[0] : null;
            var at = VelvetStyleUtilities.IndexOfUtilities(root);
            if (wanted != null && ReferenceEquals(held, wanted) && at >= 0 && at + 1 < root.styleSheets.count
                && ReferenceEquals(root.styleSheets[at + 1], wanted))
            {
                return;
            }
            if (held != null)
            {
                root.styleSheets.Remove(held);
            }
            if (wanted == null)
            {
                s_held.Remove(root);
                return;
            }
            root.styleSheets.Insert(VelvetStyleUtilities.IndexOfUtilities(root) + 1, wanted);
            s_held.Remove(root);
            s_held.Add(root, new[] { wanted });
        }

        // Every class the sheet snaps, in the order the bundled sheet declares them, with its width in pixels and the
        // longhands it writes.
        internal static IEnumerable<(string Class, int Pixels, string[] Properties)> Rules()
        {
            foreach (var (family, properties) in s_families)
            {
                yield return (family, 1, properties);
                foreach (var width in s_scale)
                {
                    yield return (family + "-" + width, width, properties);
                }
            }
        }

        private static StyleSheet Build(float scale)
        {
            var builder = Activator.CreateInstance(s_builderType!)!;
            var partType = s_createClass!.ReturnType;
            var relationship = Enum.ToObject(s_addSelector!.GetParameters()[1].ParameterType, 0);
            var dimensionType = s_addDimension!.GetParameters()[0].ParameterType;
            var pixel = s_pixel!.GetValue(null);
            foreach (var (cls, px, properties) in Rules())
            {
                s_beginRule!.Invoke(builder, new object[] { 0 });
                s_beginSelector!.Invoke(builder, new object[] { ClassSpecificity });
                var parts = Array.CreateInstance(partType, 1);
                parts.SetValue(s_createClass.Invoke(null, new object[] { cls }), 0);
                s_addSelector.Invoke(builder, new[] { parts, relationship });
                s_endSelector!.Invoke(builder, null);
                var snapped = StyleRelativeLengths.SnapToDevicePixels(px, scale);
                foreach (var property in properties)
                {
                    s_beginProperty!.Invoke(builder, new object[] { property, -1 });
                    s_addDimension.Invoke(builder, new[] { Activator.CreateInstance(dimensionType, snapped, pixel) });
                    s_endProperty!.Invoke(builder, null);
                }
                s_endRule!.Invoke(builder, null);
            }
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.name = SheetName;
            sheet.hideFlags = HideFlags.HideAndDontSave;
            s_buildTo!.Invoke(builder, new object[] { sheet });
            return sheet;
        }
    }
}
