using System;

namespace Velvet
{
    // Resolves the class array actually applied to a MotionNode's element. Both the SELF case
    // (the node's own MotionNode.Animate) and the INHERITED case (a node with
    // MotionNode.Variants but no explicit Animate, taking the nearest ancestor Motion's
    // label) resolve through one path — the variant-inheritance model: effectiveLabel = Animate ?? inherited, looked
    // up in this node's own variants. Resolution happens at reconcile time (not at construction) so the
    // inherited label is in scope, the same way context values are read during render.
    internal static class MotionVariantResolver
    {
        // Returns base ClassNames augmented with the variant classes for the effective label
        // (Animate ?? ambientLabel) when this node has variants and the label is one
        // of its keys. Otherwise returns the base ClassNames unchanged. variantApplied is
        // true only when a variant actually merged — callers use it to skip the per-element applied-class
        // bookkeeping for the variant-less majority.
        public static string[] ResolveApplied(MotionNode node, string ambientLabel, out bool variantApplied)
        {
            variantApplied = false;
            var baseClasses = node.ClassNames ?? Array.Empty<string>();

            var label = node.Animate ?? ambientLabel;
            if (label == null || node.Variants == null)
            {
                return baseClasses;
            }

            if (!node.Variants.TryGetValue(label, out var variantClassString)
                || string.IsNullOrEmpty(variantClassString))
            {
                return baseClasses;
            }

            var variantClasses = V.ParseClassNames(variantClassString);
            if (variantClasses.Length == 0)
            {
                return baseClasses;
            }

            variantApplied = true;
            if (baseClasses.Length == 0)
            {
                return variantClasses;
            }

            var merged = new string[baseClasses.Length + variantClasses.Length];
            Array.Copy(baseClasses, merged, baseClasses.Length);
            Array.Copy(variantClasses, 0, merged, baseClasses.Length, variantClasses.Length);
            return merged;
        }

        // The label a Motion exposes to its descendants: its own Animate when set, else the
        // inherited ambientLabel (so the nearest-ancestor label keeps flowing down).
        public static string LabelForChildren(MotionNode node, string ambientLabel) => node.Animate ?? ambientLabel;
    }
}
