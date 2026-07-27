using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Recognizes the `text-balance` utility for StyleTextBalanceManipulator, plus the one question about
    // OTHER classes its behavior depends on (DeclaresOwnWidth). text-balance carries no scale or
    // arbitrary-value form, so its own classifier is an exact match rather than a prefix + TryExtract pair.
    internal static class StyleTextBalanceClass
    {
        private const string ClassName = "text-balance";

        // Bundled prefixes that write the `width` longhand — the slot the manipulator borrows. `basis-` is
        // excluded: flex-basis sizes the main axis, which is the HEIGHT in UI Toolkit's default column
        // direction, so it would be a false positive on most elements. Inset shorthands likewise size a box
        // only while it is absolutely positioned, which has no in-flow parent width to balance against.
        private static readonly string[] WidthDeclaringPrefixes = { "w-", "size-" };

        // `width: auto` IS the default, so this declares nothing to stand down for.
        private const string AutoWidthClass = "w-auto";

        // Single-token half of DeclaresOwnWidth, so the token set has ONE definition: the scan below and
        // the variant-payload gate (StyleVariantPayload) both resolve the family through here. A variant
        // payload lands bare, which is the form this matches.
        public static bool IsWidthDeclaringToken(string cls)
        {
            if (string.IsNullOrEmpty(cls) || cls == AutoWidthClass)
            {
                return false;
            }
            foreach (var prefix in WidthDeclaringPrefixes)
            {
                if (cls.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // Single-token half of HasTextBalanceClass. Its own predicate so the token name has ONE
        // definition — both the array scan below and the variant-payload gate (StyleVariantPayload)
        // resolve the family through here.
        public static bool IsTextBalanceToken(string cls) => cls == ClassName;

        // Cheap early-out gate: true when classNames carries the exact `text-balance` token. No
        // allocation — used to skip manipulator attach/lookup on the common element with no such class.
        public static bool HasTextBalanceClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsTextBalanceToken(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // The two halves of "the element's own cascade sizes its width", split by cost because the caller
        // runs one on every derive and the other only behind its signature guard. No style read can answer
        // the question at all — the manipulator owns the inline width slot — and both of these are
        // uncontaminated, since balance writes style.width directly and registers no layer.

        // The bracket forms (w-[200px], !w-[200px], dark:size-[40px]), which resolve to inline style and so
        // never enter the class list. Two dictionary lookups, no allocation.
        public static bool DeclaresWidthLayer(VisualElement element)
            => element != null
                && (StyleArbitraryValueResolver.HasLayer(element, ArbitraryProperty.Width)
                    || StyleArbitraryValueResolver.HasLayer(element, ArbitraryProperty.Size));

        // The USS forms, read from the LIVE class list, which is where a variant's payload and a
        // bang-stripped `!w-32` also land. Walking it boxes an enumerator, so the caller keeps this behind
        // its early-out; every path that can change the answer forces a full derive anyway — a patch
        // through Refresh, and a variant payload through the layout re-sync its width token triggers.
        // NOT covered: an app writing the class imperatively (element.AddToClassList("w-40")). Nothing
        // observes that, and it cannot move the manipulator's signature either, so a width added that way
        // while balance holds a value is ignored until something unrelated forces a derive.
        public static bool DeclaresWidthClass(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }
            foreach (var cls in element.GetClasses())
            {
                if (IsWidthDeclaringToken(cls))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
