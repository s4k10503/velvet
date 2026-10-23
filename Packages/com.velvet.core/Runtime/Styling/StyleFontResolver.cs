using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using TextElement = UnityEngine.UIElements.TextElement;

namespace Velvet
{
    /// <summary>
    /// Applies an element's font utility classes as inline style, resolving family + weight + italic
    /// together against the <see cref="VelvetFonts"/> registry. Setting <c>unityFontDefinition</c> and
    /// <c>unityFontStyleAndWeight</c> in one pass is what lets Velvet:
    /// <list type="bullet">
    /// <item>compose <c>font-bold italic</c> into <c>bold-and-italic</c> (impossible from two separate
    /// USS classes that share the single <c>-unity-font-style</c> property);</item>
    /// <item>render the full <c>font-thin</c>…<c>font-black</c> scale when weight-specific Font Assets
    /// are registered, and gracefully fold to the binary bold/normal threshold when they are not.</item>
    /// </list>
    /// Inline styles win over the USS fallback classes in <c>_typography.uss</c>, so this resolver is the
    /// authoritative font layer for every Velvet element.
    /// </summary>
    public static class StyleFontResolver
    {
        private const string AddressPrefix = VelvetFontStack.AddressPrefix;

        // The elements a font was resolved for, which a grown fallback table has to reach (MarkShowing): the
        // table answers for them in the text they already generated. Weak, so a released element goes with its
        // entry; Seen holds the membership so an element is listed once.
        private static readonly List<WeakReference<VisualElement>> s_resolved = new();
        private static readonly ConditionalWeakTable<VisualElement, object> s_seen = new();
        private static int s_pruneAt = 64;

        static StyleFontResolver()
        {
            VelvetFontStack.Grew += MarkShowing;
        }

        private static void Remember(VisualElement element)
        {
            if (s_seen.TryGetValue(element, out _))
            {
                return;
            }
            s_seen.Add(element, element);
            if (s_resolved.Count >= s_pruneAt)
            {
                s_resolved.RemoveAll(entry => !entry.TryGetTarget(out _));
                s_pruneAt = Math.Max(64, s_resolved.Count * 2);
            }
            s_resolved.Add(new WeakReference<VisualElement>(element));
        }

        // Builds the text again of every label under a resolved element that shows asset, so the entries just
        // added to its fallback table reach glyphs it has already laid out.
        private static void MarkShowing(FontAsset asset)
        {
            foreach (var entry in s_resolved.ToArray())
            {
                var alive = entry.TryGetTarget(out var element);
                if (alive && element.panel != null)
                {
                    MarkSubtree(element, asset);
                }
            }
        }

        private static void MarkSubtree(VisualElement element, FontAsset asset)
        {
            if (element is TextElement text && text.resolvedStyle.unityFontDefinition.fontAsset == asset)
            {
                text.MarkDirtyText();
            }
            var count = element.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                MarkSubtree(element.hierarchy[i], asset);
            }
        }

        /// <summary>
        /// Extracts the font intent from <paramref name="classNames"/> and applies it, or clears the
        /// inline font style when no font class is present. Called by the reconciler whenever an
        /// element's class list changes (and on creation).
        /// </summary>
        public static void Apply(VisualElement element, string[] classNames)
            => Apply(element, classNames, null, created: false);

        // A scope makes a weight or italic class without a family class resolve against the family an
        // ancestor names (FiberFontScope); without one it resolves against DefaultFamily.
        internal static void Apply(VisualElement element, string[] classNames, FiberFontScope? scope, bool created)
        {
            if (element == null)
            {
                return;
            }

            if (!StyleFontClass.TryExtract(classNames, out var intent))
            {
                scope?.Untrack(element);
                Clear(element);
                return;
            }

            ApplyIntent(element, intent, scope, created);
        }

        /// <summary>
        /// Create/mount-time entry point: resolves the font intent only when <paramref name="classNames"/>
        /// carries a font class. A freshly created (or pool-reset) element has no inline font to clear, so
        /// for the ~99% of elements with no font class this gate skips the resolve entirely (mirroring the
        /// gap manipulator's <c>HasGapClass</c> early-out). Single source of truth for the create-side gate.
        /// </summary>
        public static void ApplyIfPresent(VisualElement element, string[] classNames)
            => ApplyIfPresent(element, classNames, null);

        internal static void ApplyIfPresent(VisualElement element, string[] classNames, FiberFontScope? scope)
        {
            if (StyleFontClass.HasFontClass(classNames))
            {
                Apply(element, classNames, scope, created: true);
            }
        }

        /// <summary>
        /// Patch-time entry point: re-resolves the font intent when the class list changed and either the
        /// new or the old list carried a font class. The old-side check is what clears the inline font
        /// style when the last font class is removed. No-op when neither list has a font class. Single
        /// source of truth for the patch-side gate.
        /// </summary>
        public static void ApplyOnClassChange(VisualElement element, string[] oldClassNames, string[] newClassNames)
            => ApplyOnClassChange(element, oldClassNames, newClassNames, null);

        internal static void ApplyOnClassChange(VisualElement element, string[] oldClassNames,
            string[] newClassNames, FiberFontScope? scope)
        {
            if (StyleFontClass.HasFontClass(newClassNames) || StyleFontClass.HasFontClass(oldClassNames))
            {
                Apply(element, newClassNames, scope, created: false);
            }
        }

        /// <summary>Reverts both inline font properties to their USS / inherited defaults.</summary>
        public static void Clear(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            element.style.unityFontDefinition = StyleKeyword.Null;
            element.style.unityFontStyleAndWeight = StyleKeyword.Null;
        }

        internal static void ApplyIntent(VisualElement element, in FontIntent intent, FiberFontScope? scope,
            bool created)
        {
            var inherited = intent.HasFamily ? null : scope?.InheritedFamily(element);
            ApplyResolved(element, intent, inherited);
            scope?.Track(element, intent, inherited, created);
        }

        // inherited is the family an ancestor names, read only when the request names none of its own.
        internal static void ApplyResolved(VisualElement element, in FontIntent intent, string? inherited)
        {
            var weight = intent.HasWeight ? intent.Weight : VelvetFontWeight.Normal;
            var family = intent.HasFamily ? intent.Family : inherited;

            FontAsset? asset;
            bool residualBold;
            bool residualItalic;

            if (TryResolveAddressFamily(family, out var addrAsset))
            {
                // font-[addr:<key>] points straight at a single asset: weight/italic have no
                // per-variant asset here, so any requested bold/italic is emulated.
                asset = addrAsset;
                residualBold = (int)weight >= 600;
                residualItalic = intent.Italic;
            }
            else
            {
                var resolved = VelvetFonts.Resolve(family, weight, intent.Italic, intent.HasStretch ? intent.Stretch : 100f);
                asset = resolved.Asset;
                residualBold = resolved.ResidualBold;
                residualItalic = resolved.ResidualItalic;
            }

            Remember(element);

            bool needStyle;
            bool bold;
            bool ital;

            if (asset != null)
            {
                element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
                bold = residualBold;
                ital = residualItalic;
                needStyle = bold || ital;
            }
            else
            {
                element.style.unityFontDefinition = StyleKeyword.Null;
                bold = intent.HasWeight && (int)weight >= 600;
                ital = intent.Italic;
                needStyle = intent.HasWeight || intent.HasItalic;
            }

            if (needStyle)
            {
                element.style.unityFontStyleAndWeight = ComputeFontStyle(bold, ital);
            }
            else
            {
                element.style.unityFontStyleAndWeight = StyleKeyword.Null;
            }
        }

        /// <summary>
        /// Folds a (bold, italic) pair into the single <see cref="FontStyle"/> value that
        /// <c>-unity-font-style</c> understands. This is the composition step that <c>-unity-font-style</c>
        /// cannot do across two separate classes.
        /// </summary>
        public static FontStyle ComputeFontStyle(bool bold, bool italic)
        {
            if (bold && italic)
            {
                return FontStyle.BoldAndItalic;
            }

            if (bold)
            {
                return FontStyle.Bold;
            }

            if (italic)
            {
                return FontStyle.Italic;
            }

            return FontStyle.Normal;
        }

        private static bool TryResolveAddressFamily(string? family, out FontAsset? asset)
        {
            asset = null;
            if (string.IsNullOrEmpty(family) || !family.StartsWith(AddressPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return VelvetFonts.TryLoadAddressableFont(family.Substring(AddressPrefix.Length), out asset);
        }
    }
}
