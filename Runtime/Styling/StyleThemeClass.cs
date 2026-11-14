#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Tailwind v4 generates a utility for each variable a theme declares in a namespace (`--color-brand` makes
    // `bg-brand`, `--radius-pill` makes `rounded-pill`): https://tailwindcss.com/docs/theme#theme-variable-namespaces.
    // The class is written as the bracketed utility reading that variable, which StyleCustomProperties resolves on
    // the element, so a theme class redeclaring the variable is followed. Variants, importance and the rule order
    // are those of the bracketed utility.
    internal static class StyleThemeClass
    {
        private readonly struct Family
        {
            public readonly string Prefix;
            public readonly string Namespace;
            public readonly string? Hint;

            public Family(string prefix, string ns, string? hint)
            {
                Prefix = prefix;
                Namespace = ns;
                Hint = hint;
            }
        }

        // The utilities Tailwind derives from --spacing, longest prefix first within a family.
        private static readonly string[] SpacingPrefixes =
        {
            "inset-x-", "inset-y-", "space-x-", "space-y-", "translate-x-", "translate-y-", "gap-x-", "gap-y-", "min-w-",
            "min-h-", "max-w-", "max-h-", "scroll-px-", "scroll-py-", "scroll-pt-", "scroll-pr-", "scroll-pb-", "scroll-pl-",
            "scroll-ps-", "scroll-pe-", "scroll-mx-", "scroll-my-", "scroll-mt-", "scroll-mr-", "scroll-mb-", "scroll-ml-",
            "scroll-ms-", "scroll-me-", "scroll-p-", "scroll-m-", "basis-", "size-", "ps-", "pe-", "ms-", "me-", "px-", "py-",
            "pt-", "pr-", "pb-", "pl-", "p-", "mx-", "my-", "mt-", "mr-", "mb-", "ml-", "m-", "gap-", "start-", "end-", "top-",
            "right-", "bottom-", "left-", "inset-", "w-", "h-",
        };

        // Longest prefix first, so border-b-primary reads the colour of the bottom side before border- could take
        // `b-primary` as a name. A prefix two namespaces share (text-, shadow-, font-) lists the colour or the
        // first-declared namespace first, and the first whose variable is declared wins.
        private static readonly Family[] s_families = BuildFamilies();

        private static Family[] BuildFamilies()
        {
            var list = new List<Family>();
            foreach (var prefix in new[]
            {
                "border-bs-", "border-be-", "border-t-", "border-r-", "border-b-", "border-l-", "border-x-", "border-y-",
                "border-s-", "border-e-", "ring-offset-", "inset-ring-", "inset-shadow-", "drop-shadow-", "border-", "outline-",
                "divide-", "caret-", "shadow-", "ring-", "from-", "via-", "to-", "text-", "bg-",
            })
            {
                list.Add(new Family(prefix, "color-", "color"));
            }
            list.Add(new Family("text-", "text-", "length"));
            list.Add(new Family("font-", "font-weight-", "weight"));
            list.Add(new Family("font-", "font-", "family-name"));
            list.Add(new Family("tracking-", "tracking-", null));
            list.Add(new Family("leading-", "leading-", null));
            list.Add(new Family("shadow-", "shadow-", null));
            list.Add(new Family("blur-", "blur-", null));
            list.Add(new Family("inset-shadow-", "inset-shadow-", null));
            list.Add(new Family("drop-shadow-", "drop-shadow-", null));
            list.Add(new Family("aspect-", "aspect-", null));
            list.Add(new Family("ease-", "ease-", null));
            foreach (var prefix in new[]
            {
                "rounded-tl-", "rounded-tr-", "rounded-br-", "rounded-bl-", "rounded-ss-", "rounded-se-", "rounded-ee-",
                "rounded-es-", "rounded-t-", "rounded-r-", "rounded-b-", "rounded-l-", "rounded-s-", "rounded-e-", "rounded-",
            })
            {
                list.Add(new Family(prefix, "radius-", null));
            }
            // Tailwind reads these from --spacing (the one multiplier) and the named --spacing-* keys; --container-* is
            // read after them where a utility takes both (w-, min-w-, max-w-).
            foreach (var prefix in SpacingPrefixes)
            {
                list.Add(new Family(prefix, "spacing-", "length"));
            }
            foreach (var prefix in new[] { "max-w-", "min-w-", "w-" })
            {
                list.Add(new Family(prefix, "container-", "length"));
            }
            return list.ToArray();
        }

        // The first segment of every family prefix, so a class like `flex` or `hidden` is turned away without a lookup.
        // An array rather than a set: IsHead compares a span of the class against each, where a set would need the
        // segment cut out as a string first.
        private static readonly string[] s_heads = BuildHeads();

        private static string[] BuildHeads()
        {
            var heads = new HashSet<string>(StringComparer.Ordinal);
            foreach (var family in s_families)
            {
                heads.Add(family.Prefix.Substring(0, family.Prefix.IndexOf('-')));
            }
            var distinct = new string[heads.Count];
            heads.CopyTo(distinct);
            return distinct;
        }

        private static bool IsHead(ReadOnlySpan<char> segment)
        {
            foreach (var head in s_heads)
            {
                if (segment.SequenceEqual(head.AsSpan()))
                {
                    return true;
                }
            }
            return false;
        }

        // Whether core, read from start, opens with the first segment of a family prefix and a dash after it. Classes
        // no theme variable makes pass it too (scroll-smooth): it is the cheap test ahead of the lookups, not their
        // answer.
        private static bool IsFamilyShaped(string core, int start)
        {
            var head = start < core.Length && core[start] == '-' ? start + 1 : start;
            var dash = core.IndexOf('-', head);
            return dash != -1 && IsHead(core.AsSpan(head, dash - head));
        }

        // IsFamilyShaped asked of a class as written, its utility read past the last ':' and a bang that opens it. It fails
        // only where it fails on the class StripImportant hands on too, so a caller asks it ahead of that copy.
        internal static bool MayBeFamilyShapedAsWritten(string className)
        {
            var start = className.LastIndexOf(':') + 1;
            if (className.IndexOf('!', start) == start)
            {
                start++;
            }
            return IsFamilyShaped(className, start);
        }

        // Whether a class with none of the special characters is one a theme variable makes or a namespace's initial
        // removes, which the custom-property pass owns rather than the class list.
        internal static bool OwnsPlainToken(string core) => Normalize(core) != null || IsClearedBundled(core);

        // Whether the class is one a theme variable makes, or one with the shape of such a class that nothing else
        // gives a meaning to (so a sheet declaring its variable later would make it one). The utility after the last
        // variant is cut out only once IsFamilyShaped passes it, so a class it turns away allocates nothing
        // (ClassReadAllocationTests).
        internal static bool MayBecomeThemeClass(string core)
        {
            var start = core.LastIndexOf(':') + 1;
            if (core.IndexOf('[') >= 0 || !IsFamilyShaped(core, start))
            {
                return false;
            }
            var utility = start == 0 ? core : core.Substring(start);
            return Normalize(utility) != null || !StyleUtilityProperties.TryGet(utility, out _);
        }

        // The scale names Tailwind and the bundled sheets give a namespace by default, which its `initial` removes.
        internal static bool IsBuiltInName(string ns, string name)
            => s_builtIns.TryGetValue(ns, out var names) && names.Contains(name);

        private static readonly Dictionary<string, HashSet<string>> s_builtIns = new(StringComparer.Ordinal)
        {
            ["shadow-"] = new(StringComparer.Ordinal) { "2xs", "xs", "sm", "md", "lg", "xl", "2xl", "none" },
            ["inset-shadow-"] = new(StringComparer.Ordinal) { "2xs", "xs", "sm", "none" },
            ["drop-shadow-"] = new(StringComparer.Ordinal) { "xs", "sm", "md", "lg", "xl", "2xl", "none" },
            ["blur-"] = new(StringComparer.Ordinal) { "xs", "sm", "md", "lg", "xl", "2xl", "3xl", "none" },
            ["font-weight-"] = new(StringComparer.Ordinal)
                { "thin", "extralight", "light", "normal", "medium", "semibold", "bold", "extrabold", "black" },
            ["font-"] = new(StringComparer.Ordinal) { "sans", "serif", "mono" },
            ["tracking-"] = new(StringComparer.Ordinal) { "tighter", "tight", "normal", "wide", "wider", "widest" },
            ["leading-"] = new(StringComparer.Ordinal) { "none", "tight", "snug", "normal", "relaxed", "loose" },
            ["aspect-"] = new(StringComparer.Ordinal) { "square", "video", "auto" },
            ["ease-"] = new(StringComparer.Ordinal) { "in", "out", "in-out" },
            ["container-"] = new(StringComparer.Ordinal)
                { "3xs", "2xs", "xs", "sm", "md", "lg", "xl", "2xl", "3xl", "4xl", "5xl", "6xl", "7xl" },
            ["animate-"] = new(StringComparer.Ordinal) { "spin", "ping", "pulse", "bounce", "none" },
            ["breakpoint-"] = new(StringComparer.Ordinal) { "sm", "md", "lg", "xl", "2xl" },
        };

        private const int MaxCached = 4096;

        // The bracketed utility core stands for, or null where core is no utility a declared theme variable makes.
        // core carries no important bang and no variant. Null as well outside a mount's pass (StyleThemeTokens.Use).
        internal static string? Normalize(string core)
        {
            if (core.Length < 3 || core.IndexOf('[') >= 0 || core.IndexOf('(') >= 0
                || StyleThemeTokens.Current is not { } scope)
            {
                return null;
            }
            if (!scope.Classes.TryGetValue(core, out var normalized))
            {
                normalized = Resolve(core, scope);
                if (scope.Classes.Count < MaxCached)
                {
                    scope.Classes[core] = normalized;
                }
            }
            return normalized;
        }

        private static string? Resolve(string core, StyleThemeTokens.Scope scope)
        {
            var negative = core[0] == '-';
            var unsigned = negative ? core.Substring(1) : core;
            foreach (var family in s_families)
            {
                if (unsigned.StartsWith(family.Prefix, StringComparison.Ordinal)
                    && (!negative || family.Namespace == "spacing-")
                    && TryRewrite(scope, family, unsigned.Substring(family.Prefix.Length), negative, out var rewritten))
                {
                    return rewritten;
                }
            }
            return null;
        }

        private static bool TryRewrite(StyleThemeTokens.Scope scope, in Family family, string rest, bool negative, out string rewritten)
        {
            rewritten = string.Empty;
            var slash = rest.IndexOf('/');
            var name = slash < 0 ? rest : rest.Substring(0, slash);
            // A colour takes an opacity modifier, and a font size a line height (text-lg/7).
            if (name.Length == 0 || !IsName(name) || (slash >= 0 && family.Namespace != "color-" && family.Namespace != "text-")
                || scope.ClearsBundled(family.Namespace, name))
            {
                return false;
            }
            var hint = family.Hint == null ? string.Empty : family.Hint + ":";
            var property = "--" + family.Namespace + name;
            var sign = negative ? "-" : string.Empty;
            var core = family.Prefix + rest;
            if (family.Namespace == "spacing-" && TryRewriteSpacing(scope, family, name, sign, Generates(scope, core, family, name, false, property), out rewritten))
            {
                return true;
            }
            if (Generates(scope, core, family, name, slash >= 0, property))
            {
                // An easing function a sheet declares once is written into the class, as read from the sheet.
                if (family.Namespace == "ease-" && scope.Counts.TryGetValue(property, out var declared) && declared == 1
                    && scope.Values.TryGetValue(property, out var easing))
                {
                    rewritten = family.Prefix + "[" + easing.Trim().Replace(' ', '_') + "]";
                    return true;
                }
                rewritten = sign + family.Prefix + "[" + hint + "var(" + property + ")]" + (slash < 0 ? string.Empty : rest.Substring(slash));
                return true;
            }
            return false;
        }

        // A variable the bundled sheets declare already has its utilities where the sheet or a palette name gives
        // them; the class is read here when it is a form those do not give (a modifier, a border side), or when a
        // project sheet declares the variable and so changes what the static reading would draw. A named spacing key
        // is read before the multiplier, so it overrides the bundled numeric utility of its name.
        private static bool Generates(StyleThemeTokens.Scope scope, string core, in Family family, string name, bool modified, string property)
        {
            if (scope.Project.Contains(property))
            {
                return modified || family.Namespace == "spacing-" || family.Namespace == "ease-"
                    || !StyleUtilityProperties.TryGet(core, out _);
            }
            if (family.Namespace != "color-" || !scope.Bundled.Contains(property))
            {
                return false;
            }
            return !VelvetPalette.TryGet(name, out _) && (modified || !StyleUtilityProperties.TryGet(core, out _));
        }

        // Tailwind's spacing scale: a named --spacing-<name> key is read first, and a number is that many of the one
        // --spacing unit. Written with the literal where one rule declares the variable, so the class is a fixed value
        // that nothing watches; otherwise as a var() the element reads.
        private static bool TryRewriteSpacing(StyleThemeTokens.Scope scope, in Family family, string name, string sign,
            bool keyed, out string rewritten)
        {
            rewritten = string.Empty;
            var hint = family.Hint + ":";
            var property = "--spacing-" + name;
            if (keyed && scope.TryGetSinglePx(property, out var named))
            {
                rewritten = sign + family.Prefix + "[" + hint + Px(named) + "]";
                return true;
            }
            if (keyed || !scope.Project.Contains("--spacing") || scope.Project.Contains(property) || !IsMultiple(name))
            {
                return false;
            }
            var literal = scope.TryGetSinglePx("--spacing", out var unit)
                ? Px(unit * (float)double.Parse(name, System.Globalization.CultureInfo.InvariantCulture))
                : "calc(var(--spacing)*" + name + ")";
            rewritten = sign + family.Prefix + "[" + hint + literal + "]";
            return true;
        }

        private static string Px(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "px";

        private static bool IsMultiple(string name)
            => double.TryParse(name, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out _);

        // Whether the class is a utility the bundled sheets or Velvet give a name in a namespace a project sheet set to
        // initial and declares no token for, which that namespace's `initial` removes.
        internal static bool IsClearedBundled(string core)
        {
            if (StyleThemeTokens.Current is not { } scope || scope.Cleared.Count == 0 || core.Length < 3 || core.IndexOf('[') >= 0)
            {
                return false;
            }
            var unsigned = core[0] == '-' ? core.Substring(1) : core;
            foreach (var family in s_families)
            {
                if (!unsigned.StartsWith(family.Prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                var rest = unsigned.Substring(family.Prefix.Length);
                var slash = rest.IndexOf('/');
                var name = slash < 0 ? rest : rest.Substring(0, slash);
                if (name.Length > 0 && IsName(name) && (scope.ClearsBundled(family.Namespace, name)
                    || (family.Namespace == "spacing-" && ClearsNumericSpacing(scope, name))))
                {
                    return true;
                }
            }
            return false;
        }

        // `--*: initial` removes the multiplier and the bundled numeric scale with it, unless the project declares
        // --spacing itself.
        private static bool ClearsNumericSpacing(StyleThemeTokens.Scope scope, string name)
            => scope.Cleared.Contains("all-") && !scope.Project.Contains("--spacing") && !scope.Project.Contains("--spacing-" + name)
                && (IsMultiple(name) || name == "px");

        private static bool IsName(string name)
        {
            foreach (var c in name)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-' && c != '.')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
