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
        // of its keys. Otherwise returns the base ClassNames unchanged. variantClasses is the variant-only
        // tail that was concatenated on — Array.Empty when nothing merged. Callers use its Length both to
        // skip the per-element applied-class bookkeeping for the variant-less majority AND, as an explicit
        // value rather than something re-derived from the merged array's tail by POSITION, as the from/to
        // input to a runtime variant swap (see FiberNodePatcher.PatchMotion) — deriving it positionally would
        // silently assume the base portion never changes length between renders.
        public static string[] ResolveApplied(MotionNode node, string ambientLabel, out string[] variantClasses)
        {
            var baseClasses = node.ClassNames ?? Array.Empty<string>();

            var label = node.Animate ?? ambientLabel;
            if (label == null || node.Variants == null)
            {
                variantClasses = Array.Empty<string>();
                return baseClasses;
            }

            if (!node.Variants.TryGetValue(label, out var variantClassString)
                || string.IsNullOrEmpty(variantClassString))
            {
                variantClasses = Array.Empty<string>();
                return baseClasses;
            }

            var parsed = V.ParseClassNames(variantClassString);
            if (parsed.Length == 0)
            {
                variantClasses = Array.Empty<string>();
                return baseClasses;
            }

            variantClasses = parsed;
            if (baseClasses.Length == 0)
            {
                return parsed;
            }

            var merged = new string[baseClasses.Length + parsed.Length];
            Array.Copy(baseClasses, merged, baseClasses.Length);
            Array.Copy(parsed, 0, merged, baseClasses.Length, parsed.Length);
            return merged;
        }

        // The label a Motion exposes to its descendants: its own Animate when set, else the
        // inherited ambientLabel (so the nearest-ancestor label keeps flowing down).
        public static string LabelForChildren(MotionNode node, string ambientLabel) => node.Animate ?? ambientLabel;
    }

    // Per-Motion-element applied-class bookkeeping pair: the full merged array (base + variant classes, used
    // for the ordinary class-driven styling diff via PatchBaseElement) alongside the variant-only classes that
    // were concatenated onto it (used as the explicit from/to input to a runtime variant swap — see
    // FiberNodePatcher.PatchMotion). Kept together so a caller never has to re-derive the variant tail from the
    // merged array by POSITION, which would silently assume the base portion's length never changes.
    internal readonly struct MotionAppliedClassSet
    {
        public readonly string[] Merged;
        public readonly string[] VariantClasses;

        public MotionAppliedClassSet(string[] merged, string[] variantClasses)
        {
            Merged = merged;
            VariantClasses = variantClasses;
        }
    }
}
