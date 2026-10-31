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
            foreach (var prefix in new[]
            {
                "rounded-tl-", "rounded-tr-", "rounded-br-", "rounded-bl-", "rounded-ss-", "rounded-se-", "rounded-ee-",
                "rounded-es-", "rounded-t-", "rounded-r-", "rounded-b-", "rounded-l-", "rounded-s-", "rounded-e-", "rounded-",
            })
            {
                list.Add(new Family(prefix, "radius-", null));
            }
            foreach (var prefix in new[]
            {
                "px-", "py-", "pt-", "pr-", "pb-", "pl-", "p-", "mx-", "my-", "mt-", "mr-", "mb-", "ml-", "m-", "gap-x-",
                "gap-y-", "gap-", "min-w-", "min-h-", "w-", "h-", "size-", "top-", "right-", "bottom-", "left-", "inset-",
            })
            {
                list.Add(new Family(prefix, "spacing-", "length"));
            }
            return list.ToArray();
        }

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
            if (scope.Classes.Count >= MaxCached)
            {
                scope.Classes.Clear();
            }
            if (!scope.Classes.TryGetValue(core, out var normalized))
            {
                normalized = Resolve(core, scope);
                scope.Classes[core] = normalized;
            }
            return normalized;
        }

        private static string? Resolve(string core, StyleThemeTokens.Scope scope)
        {
            var negative = core[0] == '-';
            var unsigned = negative ? core.Substring(1) : core;
            foreach (var family in s_families)
            {
                if (!unsigned.StartsWith(family.Prefix, StringComparison.Ordinal) || (negative && family.Namespace != "spacing-"))
                {
                    continue;
                }
                var rest = unsigned.Substring(family.Prefix.Length);
                var slash = rest.IndexOf('/');
                var name = slash < 0 ? rest : rest.Substring(0, slash);
                // Only a colour takes an opacity modifier; text-lg/6 is a size with a line height.
                if (name.Length == 0 || !IsName(name) || (slash >= 0 && family.Namespace != "color-"))
                {
                    continue;
                }
                var hint = family.Hint == null ? string.Empty : family.Hint + ":";
                var property = "--" + family.Namespace + name;
                if (!negative && Generates(scope, unsigned, family, name, slash >= 0, property))
                {
                    return family.Prefix + "[" + hint + "var(" + property + ")]" + (slash < 0 ? string.Empty : rest.Substring(slash));
                }
                // Tailwind's spacing scale: a number is that many of the one --spacing unit, unless the theme names it.
                if (family.Namespace == "spacing-" && scope.Project.Contains("--spacing") && !scope.Project.Contains(property)
                    && IsMultiple(name))
                {
                    return (negative ? "-" : string.Empty) + family.Prefix + "[" + hint + "calc(var(--spacing)*"
                        + (negative ? "-" : string.Empty) + name + ")]";
                }
            }
            return null;
        }

        private static bool IsMultiple(string name)
            => double.TryParse(name, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out _);

        // Whether the class is a utility the bundled sheets give a name in a namespace the project cleared
        // (StyleThemeTokens.Scope.Cleared) and declares no token for, which the namespace's `initial` removes.
        internal static bool IsClearedBundled(string core)
        {
            if (StyleThemeTokens.Current is not { } scope || scope.Cleared.Count == 0 || core.Length < 3 || core.IndexOf('[') >= 0)
            {
                return false;
            }
            foreach (var family in s_families)
            {
                if (!core.StartsWith(family.Prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                var rest = core.Substring(family.Prefix.Length);
                var slash = rest.IndexOf('/');
                var name = slash < 0 ? rest : rest.Substring(0, slash);
                if (name.Length > 0 && IsName(name) && scope.ClearsBundled(family.Namespace, name))
                {
                    return true;
                }
            }
            return false;
        }

        // A variable the bundled sheets declare already has its utilities where the sheet or a palette name gives
        // them; the class is read here when it is a form those do not give (a modifier, a border side), or when a
        // project sheet declares the variable and so changes what the static reading would draw.
        private static bool Generates(StyleThemeTokens.Scope scope, string core, in Family family, string name, bool modified, string property)
        {
            if (scope.Project.Contains(property))
            {
                return modified || !StyleUtilityProperties.TryGet(core, out _);
            }
            if (family.Namespace != "color-" || !(scope.Project.Contains(property) || scope.Bundled.Contains(property)))
            {
                return false;
            }
            return !VelvetPalette.TryGet(name, out _) && (modified || !StyleUtilityProperties.TryGet(core, out _));
        }

        private static bool IsName(string name)
        {
            foreach (var c in name)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
