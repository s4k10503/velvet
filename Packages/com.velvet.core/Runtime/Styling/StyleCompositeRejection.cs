#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Tailwind writes translate-*, scale-x-* / scale-y-* and the uniform scale-*, skew-*, and the filter
    // utilities as custom properties that one declaration per family composes (translate: var(--tw-translate-x)
    // var(--tw-translate-y), filter: var(--tw-blur,) var(--tw-brightness,) …). A value the browser rejects in one
    // of them makes that declaration invalid at computed-value time, so the family's other utilities go with it, as
    // measured in Chromium against Tailwind's own CSS; RejectedCompositeTests pins the families.
    //
    // The custom properties are the element's: a variant or an important modifier decides only which rule's value a
    // property holds. So a family is judged once per variant set its tokens are written under, over the tokens of
    // that set and of every set it contains, and on the value holding each property alone. Where it is rejected
    // under a variant set, its tokens there give way to tokens under the same variants that write each property the
    // contained sets hold back to a value that does nothing (translate-x-0, blur-none), so the family is off while
    // those variants apply; where it is accepted, a holding value that a rejection below took out is written again
    // under its variants. Two variant sets neither of which contains the other are judged apart. A [&>*]: scope
    // styles the children rather than the element, and is judged on its own tokens.
    internal static class StyleCompositeRejection
    {
        private enum Family
        {
            None,
            Translate,
            Scale,
            Transform,
            Filter,
        }

        private static readonly Family[] s_families = { Family.Translate, Family.Scale, Family.Transform, Family.Filter };

        // One family token: the properties it writes, one bit each, and the variant set it is written under.
        private sealed class Reading
        {
            public int Index { get; init; }
            public Family Family { get; init; }
            public int Properties { get; init; }
            public bool Rejected { get; init; }
            public bool Important { get; init; }
            // As written, each followed by its ':'.
            public string Variants { get; init; } = string.Empty;
            public List<string> Parts { get; init; } = new();
            public string Scope { get; init; } = string.Empty;
            public bool Child { get; init; }
            // The utility without its variants and important modifier.
            public string Leaf { get; init; } = string.Empty;
        }

        // tokens with each family judged as above; tokens itself when no token has a value the browser rejects.
        internal static string[] WithoutRejectedComposites(string[] tokens)
        {
            List<Reading>? readings = null;
            var rejected = false;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryRead(tokens[i], i, out var reading))
                {
                    (readings ??= new List<Reading>()).Add(reading);
                    rejected |= reading.Rejected;
                }
            }
            if (!rejected)
            {
                return tokens;
            }
            var removed = new bool[tokens.Length];
            var written = new Dictionary<int, List<string>>();
            foreach (var family in s_families)
            {
                Judge(readings!.FindAll(reading => reading.Family == family), removed, written);
            }
            var kept = new List<string>(tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (written.TryGetValue(i, out var before))
                {
                    kept.AddRange(before);
                }
                if (!removed[i])
                {
                    kept.Add(tokens[i]);
                }
            }
            return kept.ToArray();
        }

        // Takes out the tokens of each variant set the family is rejected under, and records what each set writes in
        // their place, ahead of the set's first token.
        private static void Judge(List<Reading> members, bool[] removed, Dictionary<int, List<string>> written)
        {
            var scopes = new List<Reading>();
            foreach (var member in members)
            {
                if (!scopes.Exists(scope => scope.Scope == member.Scope))
                {
                    scopes.Add(member);
                }
            }
            var rejected = scopes.FindAll(scope => Holding(members, scope).Exists(holder => holder.Reading.Rejected));
            if (rejected.Count == 0)
            {
                return;
            }
            foreach (var member in members)
            {
                if (rejected.Exists(scope => scope.Scope == member.Scope))
                {
                    removed[member.Index] = true;
                }
            }
            foreach (var scope in scopes)
            {
                if (scope.Child || scope.Parts.Count == 0)
                {
                    continue;
                }
                var tokens = rejected.Contains(scope)
                    ? Neutralising(members, scope, scopes, rejected)
                    : Restoring(members, scope, removed);
                if (tokens.Count > 0)
                {
                    written[scope.Index] = tokens;
                }
            }
        }

        // The tokens under scope's variants that write each property a set it contains, and the family is accepted
        // under, holds back to a value that does nothing; important where what holds it there is.
        private static List<string> Neutralising(List<Reading> members, Reading scope, List<Reading> scopes,
            List<Reading> rejected)
        {
            var tokens = new List<string>();
            var neutrals = NeutralsOf(scope.Family);
            for (var bit = 0; bit < neutrals.Length; bit++)
            {
                var neutral = neutrals[bit];
                var important = false;
                var held = false;
                foreach (var below in scopes)
                {
                    if (below == scope || below.Child || rejected.Contains(below) || !Contains(scope.Parts, below.Parts))
                    {
                        continue;
                    }
                    foreach (var (property, holder) in Holding(members, below))
                    {
                        if (property == 1 << bit)
                        {
                            held = true;
                            important |= holder.Important;
                        }
                    }
                }
                if (held && neutral != null)
                {
                    tokens.Add(scope.Variants + neutral + (important ? "!" : string.Empty));
                }
            }
            return tokens;
        }

        // The holding tokens of an accepted scope that a rejection below it took out, written under its variants.
        private static List<string> Restoring(List<Reading> members, Reading scope, bool[] removed)
        {
            var tokens = new List<string>();
            foreach (var (_, holder) in Holding(members, scope))
            {
                var token = scope.Variants + holder.Leaf + (holder.Important ? "!" : string.Empty);
                if (removed[holder.Index] && !tokens.Contains(token))
                {
                    tokens.Add(token);
                }
            }
            return tokens;
        }

        // Each property the tokens applying under scope write, with the token holding it.
        private static List<(int Property, Reading Reading)> Holding(List<Reading> members, Reading scope)
        {
            var holding = new List<(int, Reading)>();
            for (var property = 1; property <= AllProperties(scope.Family); property <<= 1)
            {
                Reading? holder = null;
                foreach (var member in members)
                {
                    var applies = scope.Child ? member.Scope == scope.Scope : !member.Child && Contains(scope.Parts, member.Parts);
                    if (applies && (member.Properties & property) != 0 && (holder == null || Compare(holder, member) < 0))
                    {
                        holder = member;
                    }
                }
                if (holder != null)
                {
                    holding.Add((property, holder));
                }
            }
            return holding;
        }

        // Negative when z holds a property both write over a: an important one over any other, then the one under
        // more variants, which Tailwind emits later where one variant set contains the other, then the one writing
        // fewer of the family's properties, then the later by name.
        private static int Compare(Reading a, Reading z)
        {
            if (a.Important != z.Important)
            {
                return a.Important ? 1 : -1;
            }
            if (a.Parts.Count != z.Parts.Count)
            {
                return a.Parts.Count.CompareTo(z.Parts.Count);
            }
            var byCount = CountOf(z.Properties).CompareTo(CountOf(a.Properties));
            if (byCount != 0)
            {
                return byCount;
            }
            return StyleRuleOrder.CompareCandidates(a.Leaf, z.Leaf);
        }

        private static int CountOf(int properties)
        {
            var count = 0;
            for (; properties != 0; properties &= properties - 1)
            {
                count++;
            }
            return count;
        }

        // Whether every variant of inner is one of outer's.
        private static bool Contains(List<string> outer, List<string> inner)
            => inner.TrueForAll(outer.Contains);

        private static bool TryRead(string token, int index, out Reading reading)
        {
            reading = null!;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            var variantsLength = StyleSpacingPrecedence.VariantsLength(core);
            var leaf = StyleArbitraryValueResolver.StripImportant(core.Substring(variantsLength), out var leafImportant);
            if (!StyleArbitraryValueResolver.TryDropTypeHint(leaf, out var payload))
            {
                return false;
            }
            var body = payload.Length > 0 && payload[0] == '-' ? payload.Substring(1) : payload;
            var family = FamilyOf(body, out var function, out var properties);
            if (family == Family.None)
            {
                return false;
            }
            var rejected = false;
            var open = body.IndexOf('[');
            if (open > 0 && body[body.Length - 1] == ']')
            {
                var value = body.Substring(open + 1, body.Length - open - 2).Replace('_', ' ').Trim();
                rejected = !Accepts(function, value);
            }
            var variants = core.Substring(0, variantsLength);
            var child = StyleChildVariantClass.IsChildVariant(variants);
            reading = new Reading
            {
                Index = index,
                Family = family,
                Properties = properties,
                Rejected = rejected,
                Important = important || leafImportant,
                Variants = variants,
                Parts = StyleSpacingPrecedence.VariantPartsOf(variants),
                Scope = (child ? "&" : string.Empty) + StyleSpacingPrecedence.VariantSetOf(variants),
                Child = child,
                Leaf = leaf,
            };
            return true;
        }

        // The family a utility's body belongs to, the CSS function or longhand its bracket is written into, and the
        // family's custom properties it writes: translate-4 both axes, translate-none none of them.
        private static Family FamilyOf(string body, out string function, out int properties)
        {
            function = string.Empty;
            properties = 0;
            if (body.StartsWith("translate-", StringComparison.Ordinal))
            {
                function = "translate";
                properties = AxesOf(body, "translate-", Both);
                return Family.Translate;
            }
            // A bracketed scale-[…] is written as scale itself, beside no other utility.
            if (body.StartsWith("scale-", StringComparison.Ordinal)
                && (body[body.Length - 1] != ']' || body.StartsWith("scale-x-", StringComparison.Ordinal)
                    || body.StartsWith("scale-y-", StringComparison.Ordinal) || body.StartsWith("scale-z-", StringComparison.Ordinal)))
            {
                function = "scale";
                properties = AxesOf(body, "scale-", Both | Depth);
                return Family.Scale;
            }
            if (body.StartsWith("skew-", StringComparison.Ordinal))
            {
                function = "angle";
                properties = AxesOf(body, "skew-", Both);
                return Family.Transform;
            }
            if (body.StartsWith("rotate-x-", StringComparison.Ordinal) || body.StartsWith("rotate-y-", StringComparison.Ordinal)
                || body.StartsWith("rotate-z-", StringComparison.Ordinal))
            {
                function = "angle";
                properties = AxesOf(body, "rotate-", 0) << 2;
                return Family.Transform;
            }
            for (var i = 0; i < s_filters.Length; i++)
            {
                var filter = s_filters[i];
                if (body == filter || body.StartsWith(filter + "-", StringComparison.Ordinal))
                {
                    function = filter;
                    properties = 1 << i;
                    return Family.Filter;
                }
            }
            return Family.None;
        }

        private const int Both = 0b011;
        private const int Depth = 0b100;

        // The axis bits a prefix-x- / -y- / -z- utility writes, none for prefix-none and prefix-3d, which write no
        // custom property, and whole for the rest.
        private static int AxesOf(string body, string prefix, int whole)
        {
            var axis = body.Length > prefix.Length + 1 && body[prefix.Length + 1] == '-' ? body[prefix.Length] : '\0';
            return axis switch
            {
                'x' => 0b001,
                'y' => 0b010,
                'z' => 0b100,
                _ => body == prefix + "none" || body == prefix + "3d" ? 0 : whole,
            };
        }

        private static int AllProperties(Family family) => (1 << NeutralsOf(family).Length) - 1;

        // By property bit, a token writing it back to a value that does nothing; null where Velvet draws no such
        // property.
        private static string?[] NeutralsOf(Family family) => family switch
        {
            Family.Translate => new[] { "translate-x-0", "translate-y-0", null },
            Family.Scale => new[] { "scale-x-100", "scale-y-100", null },
            Family.Transform => new[] { "skew-x-0", "skew-y-0", null, null, null },
            _ => s_filterNeutrals,
        };

        private static readonly string[] s_filters =
        {
            "blur", "brightness", "contrast", "grayscale", "hue-rotate", "invert", "saturate", "sepia", "drop-shadow",
        };

        private static readonly string?[] s_filterNeutrals =
        {
            "blur-none", "brightness-100", "contrast-100", "grayscale-0", "hue-rotate-0", "invert-0", "saturate-100",
            "sepia-0", "drop-shadow-none",
        };

        // Whether the browser takes value where Tailwind writes it for function. A value only a math function or a
        // var() spells is taken, since nothing here reads one to the end.
        private static bool Accepts(string function, string value)
        {
            if (value.IndexOf('(') >= 0)
            {
                return true;
            }
            return function switch
            {
                "translate" => TakesLengthPercentage(value),
                "scale" => IsNumber(value) || IsPercentage(value),
                "angle" or "hue-rotate" => IsZero(value) || IsDimension(value, s_angleUnits),
                "blur" => !value.StartsWith("-", StringComparison.Ordinal) && (IsZero(value) || IsLength(value)),
                // A drop shadow is read by its own grammar, which this does not repeat.
                "drop-shadow" => true,
                _ => !value.StartsWith("-", StringComparison.Ordinal) && (IsNumber(value) || IsPercentage(value)),
            };
        }

        // Whether the browser takes value as a <length-percentage>, a function taken as Accepts takes one.
        internal static bool TakesLengthPercentage(string value)
            => value.IndexOf('(') >= 0 || IsZero(value) || IsLength(value) || IsPercentage(value);

        internal static bool IsLength(string value) => IsDimension(value, TailwindDataTypes.s_lengthUnits);

        private static readonly string[] s_angleUnits = { "deg", "grad", "rad", "turn" };

        private static bool IsNumber(string value)
            => TailwindDataTypes.NumberEnd(value, withExponent: true) == value.Length
                || TailwindDataTypes.NumberEnd(value, withExponent: false) == value.Length;

        private static bool IsZero(string value) => IsNumber(value) && float.TryParse(value,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && n == 0f;

        private static bool IsPercentage(string value)
            => value.Length > 1 && value[value.Length - 1] == '%' && IsNumber(value.Substring(0, value.Length - 1));

        // A number followed by one of units, in any case, as CSS reads a unit.
        private static bool IsDimension(string value, string[] units)
            => EndsInUnit(value, units, withExponent: true) || EndsInUnit(value, units, withExponent: false);

        private static bool EndsInUnit(string value, string[] units, bool withExponent)
        {
            var end = TailwindDataTypes.NumberEnd(value, withExponent);
            return end > 0
                && Array.Exists(units, unit => string.Equals(value.Substring(end), unit, StringComparison.OrdinalIgnoreCase));
        }
    }
}
