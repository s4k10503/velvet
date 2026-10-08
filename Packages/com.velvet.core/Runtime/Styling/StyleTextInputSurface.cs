using System;

namespace Velvet
{
    // Sends the surface utilities written on a TextField-shaped control to its text-input box, the way a
    // utility written on an <input> paints the field itself, instead of leaving them on the outer control.
    //
    // The routing is a rewrite of the class list the factory hands the node: a surface token becomes the
    // text-input scope of the child-combinator variant (StyleChildVariantClass), so StyleChildVariantManipulator
    // applies it, with its state, theme and responsive layers, to the box and releases it on teardown. Layout,
    // size, margin and every other utility stay on the outer control.
    //
    // Left on the outer control, because nothing here applies them to the box: the gradient backgrounds, the
    // solid / dashed / dotted border line style, shadow, ring and outline. A token wrapping a variant the
    // child-combinator refuses (first:, has-[…]:, data-[…]:, supports-[…]:) stays as written for the same
    // reason. TextInputSurfaceTests pins each routed and each declined spelling.
    internal static class StyleTextInputSurface
    {
        private static readonly string[] s_declinedBackgrounds = { "bg-linear", "bg-gradient", "bg-radial", "bg-conic" };
        private static readonly string[] s_paddings =
            { "p-", "px-", "py-", "pt-", "pr-", "pb-", "pl-", "ps-", "pe-" };

        // classNames itself when no token is a surface utility, so a field without one allocates nothing and
        // keeps the shared parsed array.
        public static string[] Route(string[] classNames)
        {
            string[]? routed = null;
            for (var i = 0; i < classNames.Length; i++)
            {
                var token = classNames[i];
                if (!IsSurface(token))
                {
                    continue;
                }
                var scoped = StyleChildVariantClass.TextInputPrefix + token;
                if (!StyleChildVariantClass.TryParse(scoped, out _))
                {
                    continue;
                }
                routed ??= (string[])classNames.Clone();
                routed[i] = scoped;
            }
            return routed ?? classNames;
        }

        private static bool IsSurface(string token)
        {
            if (string.IsNullOrEmpty(token) || StyleChildVariantClass.IsChildVariant(token))
            {
                return false;
            }
            var leaf = token;
            while (StyleVariantClass.TryParse(leaf, out _, out var next) && !string.IsNullOrEmpty(next))
            {
                leaf = next!;
            }
            var core = StyleArbitraryValueResolver.StripImportant(leaf, out _);
            return IsBackground(core) || IsBorder(core) || IsRadius(core) || IsPadding(core);
        }

        private static bool IsBackground(string core)
            => core.StartsWith("bg-", StringComparison.Ordinal) && !StartsWithAny(core, s_declinedBackgrounds);

        private static bool IsBorder(string core)
            => (core == "border" || core.StartsWith("border-", StringComparison.Ordinal))
               && core != "border-solid" && core != "border-dashed" && core != "border-dotted";

        private static bool IsRadius(string core)
            => core == "rounded" || core.StartsWith("rounded-", StringComparison.Ordinal);

        private static bool IsPadding(string core) => StartsWithAny(core, s_paddings);

        private static bool StartsWithAny(string text, string[] prefixes)
            => Array.Exists(prefixes, prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }
}
