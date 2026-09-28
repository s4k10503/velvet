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
        // poseTransition is what a swap INTO the resolved pose plays on: that pose's own
        // MotionVariant.Transition when it declares one, else the node's own — resolved here, off THIS
        // lookup, rather than by a caller re-deriving the effective label, which could pair a pose with
        // another label's timing. The label-not-found return below hands back the node's own, which is what
        // a variant→no-variant swap plays on; the class-string returns after it keep what the pose declared,
        // one that applies nothing still being the pose swapped into.
        public static string[] ResolveApplied(MotionNode node, string ambientLabel, out string[] variantClasses,
            out StyleTransitionConfig? poseTransition)
        {
            var baseClasses = node.ClassNames ?? Array.Empty<string>();
            poseTransition = node.Transition;

            var label = node.Animate ?? ambientLabel;
            if (label == null || node.Variants == null)
            {
                variantClasses = Array.Empty<string>();
                return baseClasses;
            }

            if (!node.Variants.TryGetValue(label, out var variant))
            {
                variantClasses = Array.Empty<string>();
                return baseClasses;
            }

            // Resolved before the class gates below, because what a pose APPLIES cannot decide whose timing
            // a swap into it reads. Measured on a pose that carries a transition and applies no class: from
            // under those gates it left the swap on the node's config while TryResolveVariantInitial gave
            // the mount enter the pose's, so one declaration animated at two speeds.
            if (variant.Transition != null)
            {
                poseTransition = variant.Transition;
            }

            if (string.IsNullOrEmpty(variant.ClassName))
            {
                variantClasses = Array.Empty<string>();
                return baseClasses;
            }

            var parsed = V.ParseClassNames(variant.ClassName);
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

        // Framer Motion's variant child: a Motion with variants whose animate label is inherited. Each one under
        // an orchestrating Motion takes a stagger slot, whether or not the label changes its own pose.
        public static bool IsVariantChild(MotionNode node) => node.Animate == null && node.Variants != null;

        // The label a Motion's mount enter starts from, which is also the one its descendants inherit: its own
        // Initial, else the inherited one while its animate label is inherited too.
        public static string? InitialLabel(MotionNode node, string? ambientInitial)
            => node.Initial ?? (node.Animate == null ? ambientInitial : null);
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
        // Inline-resolved classes of an earlier pose a spring or bezier kept moving under a pose that did not name
        // them, and left the element resting at (FiberNodePatcher.RemoveStaleInlineTokens). The next label change
        // diffs them away as the old pose's.
        public readonly string[] Carried;

        public MotionAppliedClassSet(string[] merged, string[] variantClasses, string[]? carried = null)
        {
            Merged = merged;
            VariantClasses = variantClasses;
            Carried = carried ?? Array.Empty<string>();
        }
    }

    // A tween swap — a runtime label change or a variant enter — whose pose's inline-resolved tokens are kept
    // off the element until the swap writes them (see FiberNodePatcher.ResolveInlineHold). Applied is the class set
    // the class-driven sync last wrote to the element, Target the one the swap brings it to, and HeldTokens
    // the inline-resolved tokens Applied carries in place of Target's. Release is handed to the swap as its
    // onSwap, and its identity is what ties the pending play to this hold.
    internal sealed class MotionHeldInline
    {
        public readonly string[] HeldTokens;
        public string[] Applied;
        public string[] Target;
        public Action Release;

        public MotionHeldInline(string[] heldTokens, string[] applied, string[] target)
        {
            HeldTokens = heldTokens;
            Applied = applied;
            Target = target;
        }
    }
}
