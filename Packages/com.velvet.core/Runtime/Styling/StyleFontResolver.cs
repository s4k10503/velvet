using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

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
        private const string AddressPrefix = "addr:";

        /// <summary>
        /// Extracts the font intent from <paramref name="classNames"/> and applies it, or clears the
        /// inline font style when no font class is present. Called by the reconciler whenever an
        /// element's class list changes (and on creation).
        /// </summary>
        public static void Apply(VisualElement element, string[] classNames)
        {
            if (element == null)
            {
                return;
            }

            if (!StyleFontClass.TryExtract(classNames, out var intent))
            {
                Clear(element);
                return;
            }

            ApplyIntent(element, intent);
        }

        /// <summary>
        /// Create/mount-time entry point: resolves the font intent only when <paramref name="classNames"/>
        /// carries a font class. A freshly created (or pool-reset) element has no inline font to clear, so
        /// for the ~99% of elements with no font class this gate skips the resolve entirely (mirroring the
        /// gap manipulator's <c>HasGapClass</c> early-out). Single source of truth for the create-side gate.
        /// </summary>
        public static void ApplyIfPresent(VisualElement element, string[] classNames)
        {
            if (StyleFontClass.HasFontClass(classNames))
            {
                Apply(element, classNames);
            }
        }

        /// <summary>
        /// Patch-time entry point: re-resolves the font intent when the class list changed and either the
        /// new or the old list carried a font class. The old-side check is what clears the inline font
        /// style when the last font class is removed. No-op when neither list has a font class. Single
        /// source of truth for the patch-side gate.
        /// </summary>
        public static void ApplyOnClassChange(VisualElement element, string[] oldClassNames, string[] newClassNames)
        {
            if (StyleFontClass.HasFontClass(newClassNames) || StyleFontClass.HasFontClass(oldClassNames))
            {
                Apply(element, newClassNames);
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

        internal static void ApplyIntent(VisualElement element, in FontIntent intent)
        {
            var weight = intent.HasWeight ? intent.Weight : VelvetFontWeight.Normal;

            FontAsset? asset;
            bool residualBold;
            bool residualItalic;

            if (intent.HasFamily && TryResolveAddressFamily(intent.Family, out var addrAsset))
            {
                // font-[addr:<key>] points straight at a single asset: weight/italic have no
                // per-variant asset here, so any requested bold/italic is emulated.
                asset = addrAsset;
                residualBold = (int)weight >= 600;
                residualItalic = intent.Italic;
            }
            else
            {
                var resolved = VelvetFonts.Resolve(intent.HasFamily ? intent.Family : null, weight, intent.Italic);
                asset = resolved.Asset;
                residualBold = resolved.ResidualBold;
                residualItalic = resolved.ResidualItalic;
            }

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
