#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Velvet
{
    // Tailwind writes translate-*, scale-x-* / scale-y-* and the uniform scale-*, skew-*, and the filter
    // utilities as custom properties that one declaration per family composes (translate: var(--tw-translate-x)
    // var(--tw-translate-y), filter: var(--tw-blur,) var(--tw-brightness,) …). The declaration is invalid at
    // computed-value time where a custom property it reads holds a value the browser rejects, and each custom
    // property holds the value of the rule that wins it, so a rejected value takes the family only where its rule
    // wins its property, as measured in Chromium against Tailwind's own CSS; RejectedCompositeTests pins it. Where
    // the family is discarded, its tokens are replaced by the family's identity values. A list normalized on its
    // own — a V.Motion pose — reads the families its element's own list rejected without a variant through
    // WithoutFamiliesRejectedIn. The gesture, presence and drag channels add stylesheet classes only, which the
    // inline identity values hold against; RejectedCompositeLayerTests pins both layers.
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

        // The tokens that write each family's identity: what the family's declaration computes to when no
        // utility of it applies.
        private static readonly string[][] s_identity =
        {
            Array.Empty<string>(),
            new[] { "translate-x-[0]", "translate-y-[0]" },
            new[] { "scale-x-[1]", "scale-y-[1]" },
            new[] { "skew-x-[0]", "skew-y-[0]" },
            new[]
            {
                "blur-[0]", "brightness-[1]", "contrast-[1]", "grayscale-[0]", "hue-rotate-[0]", "invert-[0]",
                "saturate-[1]", "sepia-[0]",
            },
        };

        // Each normalized list's families rejected without a variant, as a mask of 1 << Family, for the lists
        // layered over it. Keyed by the array the parse cache hands out for the list, which it keeps stable.
        private static readonly ConditionalWeakTable<string[], StrongBox<int>> s_rejectedEverywhere = new();

        // One token of a composed family. Writes and Reads are masks over the family's custom properties: those
        // the token sets, and those its copy of the family's declaration reads.
        private readonly struct Entry
        {
            public readonly string Variants;
            public readonly Family Family;
            public readonly bool Important;
            public readonly bool Rejected;
            public readonly int Writes;
            public readonly int Reads;
            public readonly string Utility;

            public Entry(string variants, Family family, bool important, bool rejected, (int Writes, int Reads) axes,
                string utility)
            {
                Variants = variants;
                Family = family;
                Important = important;
                Rejected = rejected;
                Writes = axes.Writes;
                Reads = axes.Reads;
                Utility = utility;
            }
        }

        // The mask of families tokens discards without a variant.
        internal static int RejectedEverywhere(string[] tokens)
        {
            var entries = ReadAll(tokens);
            var mask = 0;
            foreach (var read in entries)
            {
                if (read is { Rejected: true } entry && RejectedAt(entries, entry.Family, string.Empty))
                {
                    mask |= 1 << (int)entry.Family;
                }
            }
            return mask;
        }

        // Records mask against normalized, the list the normalizer handed back for tokens it read it from.
        internal static void Remember(string[] normalized, int mask)
        {
            if (mask != 0)
            {
                s_rejectedEverywhere.AddOrUpdate(normalized, new StrongBox<int>(mask));
            }
        }

        // layer without its tokens of a family owner, the list it is layered over, discards without a variant, so
        // the family holds at its identity under every layer.
        internal static string[] WithoutFamiliesRejectedIn(string[] owner, string[] layer)
        {
            if (!s_rejectedEverywhere.TryGetValue(owner, out var box))
            {
                return layer;
            }
            var mask = box.Value;
            return Array.FindAll(layer, token => !(TryRead(token, out var entry) && (mask & (1 << (int)entry.Family)) != 0));
        }

        // tokens with each family whose declaration the browser discards written as that family's identity at the
        // variants where it is discarded; tokens itself when no token holds a rejected value.
        internal static string[] WithoutRejectedComposites(string[] tokens)
        {
            var entries = ReadAll(tokens);
            if (!Array.Exists(entries, read => read is { Rejected: true }))
            {
                return tokens;
            }
            var verdicts = new Dictionary<(string Variants, Family Family), bool>();
            var kept = new List<string>(tokens.Length);
            var written = new HashSet<(string, Family)>();
            for (var i = 0; i < tokens.Length; i++)
            {
                if (entries[i] is not { } entry || !HoldsRejection(entries, entry.Family))
                {
                    kept.Add(tokens[i]);
                    continue;
                }
                var atBase = Verdict(verdicts, entries, entry.Family, string.Empty);
                var here = entry.Variants.Length == 0 ? atBase : Verdict(verdicts, entries, entry.Family, entry.Variants);
                if (!here && !atBase)
                {
                    kept.Add(tokens[i]);
                }
                // Where the family is discarded at the base as well, the base's identity holds and nothing is written.
                else if (written.Add((entry.Variants, entry.Family)) && !(here && atBase && entry.Variants.Length != 0))
                {
                    WriteScope(kept, tokens, entries, entry, here);
                }
            }
            return kept.ToArray();
        }

        private static void WriteScope(List<string> kept, string[] tokens, Entry?[] entries, Entry entry, bool discarded)
        {
            if (discarded)
            {
                // Important where an important utility of the family applies, which it has to outrank.
                WriteIdentity(kept, tokens, entry, HasImportant(entries, entry.Family, entry.Variants));
            }
            else
            {
                // The family holds here over a base where it is discarded, so it is written here whole, the values
                // it inherits from the base included, and important where the base's identity is.
                WriteWinners(kept, entries, entry, HasImportant(entries, entry.Family, string.Empty));
            }
        }

        private static bool HoldsRejection(Entry?[] entries, Family family)
            => Array.Exists(entries, read => read is { Rejected: true } other && other.Family == family);

        private static bool HasImportant(Entry?[] entries, Family family, string variants)
            => Array.Exists(entries, read => read is { Important: true } other && other.Family == family
                && (other.Variants.Length == 0 || other.Variants == variants));

        private static bool Verdict(Dictionary<(string, Family), bool> verdicts, Entry?[] entries, Family family,
            string variants)
        {
            if (!verdicts.TryGetValue((variants, family), out var rejected))
            {
                rejected = RejectedAt(entries, family, variants);
                verdicts[(variants, family)] = rejected;
            }
            return rejected;
        }

        // Whether family's declaration is discarded where variants applies: each custom property holds the value
        // of the rule that wins it (Outranks), and the declaration that wins the same way is invalid when a
        // property it reads holds a rejected value.
        private static bool RejectedAt(Entry?[] entries, Family family, string variants)
        {
            if (WinnerOf(entries, family, variants, property: 0) is not { } declaration)
            {
                return false;
            }
            var reads = declaration.Reads;
            for (var bit = 1; bit <= reads; bit <<= 1)
            {
                if ((reads & bit) != 0 && WinnerOf(entries, family, variants, bit) is { Rejected: true })
                {
                    return true;
                }
            }
            return false;
        }

        // The entry of family that wins property (its bit; 0 for the family's declaration, which every utility of
        // the family writes) where variants applies: one without a variant, or behind variants themselves.
        private static Entry? WinnerOf(Entry?[] entries, Family family, string variants, int property)
        {
            Entry? winner = null;
            foreach (var read in entries)
            {
                if (read is not { } entry || entry.Family != family
                    || entry.Variants.Length != 0 && entry.Variants != variants
                    || property != 0 && (entry.Writes & property) == 0)
                {
                    continue;
                }
                if (winner is not { } current || Outranks(entry, current))
                {
                    winner = entry;
                }
            }
            return winner;
        }

        // An important rule over a plain one, then a rule behind variants over one without, which Tailwind emits
        // after it, then the later in Tailwind's order.
        private static bool Outranks(Entry a, Entry z)
        {
            if (a.Important != z.Important)
            {
                return a.Important;
            }
            if (a.Variants.Length != z.Variants.Length)
            {
                return a.Variants.Length != 0;
            }
            return CompareRules(a, z) >= 0;
        }

        // Tailwind's order between two rules of one family behind the same variants (StyleRuleOrder names the
        // steps): by the first custom property they differ on in property-order.ts's order, which is the bit
        // order, a rule that runs out of them sorting last; then by candidate. The family's declaration, which
        // both write, never decides.
        private static int CompareRules(Entry a, Entry z)
        {
            var aWrites = a.Writes;
            var zWrites = z.Writes;
            while (aWrites != zWrites)
            {
                var aLowest = aWrites & -aWrites;
                var zLowest = zWrites & -zWrites;
                if (aLowest != zLowest)
                {
                    return aLowest == 0 ? 1 : zLowest == 0 ? -1 : aLowest.CompareTo(zLowest);
                }
                aWrites &= ~aLowest;
                zWrites &= ~zLowest;
            }
            return StyleCandidateOrder.Compare(a.Utility, z.Utility);
        }

        private static void WriteIdentity(List<string> kept, string[] tokens, Entry entry, bool important)
        {
            var prefix = (important ? "!" : string.Empty) + entry.Variants;
            foreach (var identity in s_identity[(int)entry.Family])
            {
                kept.Add(prefix + identity);
            }
            // A drop shadow is part of the filter, so one elsewhere in the list goes with it.
            if (entry.Family == Family.Filter && Array.Exists(tokens, IsDropShadow))
            {
                kept.Add(prefix + "drop-shadow-none");
            }
        }

        // The utility winning each property of entry's family at entry's variants, written behind them.
        private static void WriteWinners(List<string> kept, Entry?[] entries, Entry entry, bool important)
        {
            var all = 0;
            foreach (var read in entries)
            {
                if (read is { } other && other.Family == entry.Family)
                {
                    all |= other.Writes;
                }
            }
            var written = new HashSet<string>();
            for (var bit = 1; bit <= all; bit <<= 1)
            {
                if ((all & bit) != 0 && WinnerOf(entries, entry.Family, entry.Variants, bit) is { } winner)
                {
                    WriteBehind(kept, written, winner, entry.Variants, important);
                }
            }
            // A drop shadow's size and colour are set by separate utilities that one bit stands for, and none of
            // them holds a value this rejects, so each applying one is written.
            foreach (var read in entries)
            {
                if (read is { } other && other.Family == entry.Family && IsDropShadow(other.Utility)
                    && (other.Variants.Length == 0 || other.Variants == entry.Variants))
                {
                    WriteBehind(kept, written, other, entry.Variants, important);
                }
            }
        }

        private static void WriteBehind(List<string> kept, HashSet<string> written, Entry entry, string variants,
            bool important)
        {
            if (written.Add(entry.Utility))
            {
                kept.Add((important || entry.Important ? "!" : string.Empty) + variants + entry.Utility);
            }
        }

        private static bool IsDropShadow(string token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(token, out _);
            var payload = core.Substring(StyleSpacingPrecedence.VariantsLength(core));
            return payload == "drop-shadow" || payload.StartsWith("drop-shadow-", StringComparison.Ordinal);
        }

        private static Entry?[] ReadAll(string[] tokens)
        {
            var entries = new Entry?[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                entries[i] = TryRead(tokens[i], out var entry) ? entry : null;
            }
            return entries;
        }

        private static bool TryRead(string token, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            var length = StyleSpacingPrecedence.VariantsLength(core);
            var utility = core.Substring(length);
            if (!StyleArbitraryValueResolver.TryDropTypeHint(utility, out var payload))
            {
                return false;
            }
            var body = payload.Length > 0 && payload[0] == '-' ? payload.Substring(1) : payload;
            var family = FamilyOf(body, out var function, out var axes);
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
            entry = new Entry(core.Substring(0, length), family, important, rejected, axes, utility);
            return true;
        }

        // The family a utility's body belongs to, the CSS function or longhand its bracket is written into, and the
        // family's custom properties it writes and reads.
        private static Family FamilyOf(string body, out string function, out (int Writes, int Reads) axes)
        {
            function = string.Empty;
            axes = default;
            if (body.StartsWith(TranslatePrefix, StringComparison.Ordinal))
            {
                function = "translate";
                axes = AxesOf(body.Substring(TranslatePrefix.Length), uniform: X | Y);
                return Family.Translate;
            }
            // A bracketed scale-[…] is written as scale itself, beside no other utility.
            if (body.StartsWith(ScalePrefix, StringComparison.Ordinal)
                && (body[body.Length - 1] != ']' || IsAxis(body.Substring(ScalePrefix.Length))))
            {
                function = "scale";
                axes = AxesOf(body.Substring(ScalePrefix.Length), uniform: X | Y | Z);
                return Family.Scale;
            }
            var transform = TransformPropertyOf(body);
            if (transform != 0)
            {
                function = "angle";
                axes = (transform, (1 << s_transformPrefixes.Length) - 1);
                return Family.Transform;
            }
            for (var i = 0; i < s_filters.Length; i++)
            {
                if (body == s_filters[i] || body.StartsWith(s_filters[i] + "-", StringComparison.Ordinal))
                {
                    function = s_filters[i];
                    axes = (1 << i, (1 << s_filters.Length) - 1);
                    return Family.Filter;
                }
            }
            return Family.None;
        }

        private const string TranslatePrefix = "translate-";
        private const string ScalePrefix = "scale-";
        private const int X = 1;
        private const int Y = 2;
        private const int Z = 4;

        private static bool IsAxis(string rest) => AxisOf(rest) != 0;

        private static int AxisOf(string rest)
            => rest.StartsWith("x-", StringComparison.Ordinal) ? X
                : rest.StartsWith("y-", StringComparison.Ordinal) ? Y
                : rest.StartsWith("z-", StringComparison.Ordinal) ? Z
                : 0;

        // What a translate or scale utility writes of x, y and z, and what its declaration reads: an axis its own,
        // the declaration reading z only beside a z utility's own; none no property; 3d the declaration over all
        // three; any other value the family's uniform properties, read as x and y.
        private static (int Writes, int Reads) AxesOf(string rest, int uniform)
        {
            var axis = AxisOf(rest);
            if (axis != 0)
            {
                return (axis, axis == Z ? X | Y | Z : X | Y);
            }
            return rest switch
            {
                "none" => (0, 0),
                "3d" => (0, X | Y | Z),
                _ => (uniform, X | Y),
            };
        }

        // The transform properties in property-order.ts's order, each written by the utilities its prefix opens.
        private static readonly string[] s_transformPrefixes = { "rotate-x-", "rotate-y-", "rotate-z-", "skew-x-", "skew-y-" };

        private const int SkewBoth = (1 << 3) | (1 << 4);

        // The transform properties a skew or 3D rotate utility writes; 0 for any other utility.
        private static int TransformPropertyOf(string body)
        {
            for (var i = 0; i < s_transformPrefixes.Length; i++)
            {
                if (body.StartsWith(s_transformPrefixes[i], StringComparison.Ordinal))
                {
                    return 1 << i;
                }
            }
            return body.StartsWith("skew-", StringComparison.Ordinal) ? SkewBoth : 0;
        }

        // In property-order.ts's order.
        private static readonly string[] s_filters =
        {
            "blur", "brightness", "contrast", DropShadow, "grayscale", "hue-rotate", "invert", "saturate", "sepia",
        };

        private const string DropShadow = "drop-shadow";

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
                "translate" => IsZero(value) || IsDimension(value, TailwindDataTypes.s_lengthUnits) || IsPercentage(value),
                "scale" => IsNumber(value) || IsPercentage(value),
                "angle" or "hue-rotate" => IsZero(value) || IsDimension(value, s_angleUnits),
                "blur" => !value.StartsWith("-", StringComparison.Ordinal)
                    && (IsZero(value) || IsDimension(value, TailwindDataTypes.s_lengthUnits)),
                // A drop shadow is read by its own grammar, which this does not repeat.
                "drop-shadow" => true,
                _ => !value.StartsWith("-", StringComparison.Ordinal) && (IsNumber(value) || IsPercentage(value)),
            };
        }

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
