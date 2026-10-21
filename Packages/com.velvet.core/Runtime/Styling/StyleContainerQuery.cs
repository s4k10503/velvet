#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Velvet
{
    // The condition a container variant prefix (`@md`, `@max-lg/main`, `@min-[475px]`, `@[40rem]`) states: a
    // width threshold, whether it is a floor or a ceiling, and the container it asks, by name or, unnamed, the
    // nearest one. Parsed from the whole prefix spelling, which is also the key a stacked variant is told apart
    // by (see StyleVariantClass.TryParse), so two spellings of one threshold are two queries that happen to agree.
    internal readonly struct StyleContainerQuery
    {
        // Tailwind v4's `--container-*` theme scale, in rem. A rem is 16px here, as everywhere in the arbitrary
        // value grammar (StyleArbitraryValueResolver.TryParseValue).
        private const float PxPerRem = 16f;

        private static readonly Dictionary<string, float> s_scaleRem = new(StringComparer.Ordinal)
        {
            ["3xs"] = 16f,
            ["2xs"] = 18f,
            ["xs"] = 20f,
            ["sm"] = 24f,
            ["md"] = 28f,
            ["lg"] = 32f,
            ["xl"] = 36f,
            ["2xl"] = 42f,
            ["3xl"] = 48f,
            ["4xl"] = 56f,
            ["5xl"] = 64f,
            ["6xl"] = 72f,
            ["7xl"] = 80f,
        };

        // Caps the threshold so its scaled value fits the 16-digit ordering key below.
        private const double OrderCapPx = 1e12;
        private const long OrderCeiling = 100_000_000_000_000L;

        private readonly string _unit;

        private StyleContainerQuery(float px, bool isMax, string name, string unit)
        {
            Px = px;
            IsMax = isMax;
            Name = name;
            _unit = unit;
        }

        /// <summary>The threshold in px.</summary>
        public float Px { get; }

        /// <summary>True for `@max-*` (width below the threshold), false for a floor (width at or above it).</summary>
        public bool IsMax { get; }

        /// <summary>The container this asks for; empty when unnamed.</summary>
        public string Name { get; }

        public bool Matches(float width) => IsMax ? width < Px : width >= Px;

        // A ceiling ranks below a floor, so where both are on the floor's declaration wins.
        public long Rank => IsMax ? StyleLayerPriority.ContainerMax : StyleLayerPriority.ContainerMin;

        // Where a query sits among the others of its rank, as a string that compares ordinally: by unit name, then a
        // floor ascending and a ceiling descending, then a named container ahead of the unnamed one and names
        // ordinally. Later wins. ContainerQueryOrderTests pins each step against Tailwind's emitted order.
        public string OrderKey
        {
            get
            {
                var scaled = (long)Math.Round(Math.Min(Px, OrderCapPx) * 100d);
                var ordered = IsMax ? OrderCeiling - scaled : scaled;
                var nameKey = Name.Length == 0 ? "~" : "!" + Name;
                return _unit + "|" + ordered.ToString("D16", CultureInfo.InvariantCulture) + "|" + nameKey;
            }
        }

        public static long RankOf(string? spelling)
            => TryParse(spelling, out var query) ? query.Rank : StyleLayerPriority.ContainerMin;

        public static string OrderKeyOf(string? spelling)
            => TryParse(spelling, out var query) ? query.OrderKey : string.Empty;

        /// <summary>
        /// Reads a whole variant prefix, leading `@` included and trailing `:` excluded. Declines a threshold
        /// that is negative, a bare number, relative (`%`, `em`) or not on the `--container-*` scale, an empty
        /// name, and a name containing a second `/`.
        /// </summary>
        public static bool TryParse(string? spelling, out StyleContainerQuery query)
        {
            query = default;
            if (string.IsNullOrEmpty(spelling) || spelling[0] != '@')
            {
                return false;
            }

            var slash = NameSlash(spelling);
            var named = slash < spelling.Length;
            var name = named ? spelling.Substring(slash + 1) : string.Empty;
            if (named && (name.Length == 0 || name.IndexOf('/') >= 0))
            {
                return false;
            }

            return TryParseThreshold(spelling.Substring(1, slash - 1), name, out query);
        }

        // The first '/' outside brackets, which opens the container name; an arbitrary value may hold its own.
        // The spelling's length when there is none.
        private static int NameSlash(string spelling)
        {
            var depth = 0;
            for (var i = 0; i < spelling.Length; i++)
            {
                switch (spelling[i])
                {
                    case '[':
                        depth++;
                        break;
                    case ']':
                        depth--;
                        break;
                    case '/' when depth == 0:
                        return i;
                }
            }
            return spelling.Length;
        }

        private static bool TryParseThreshold(string body, string name, out StyleContainerQuery query)
        {
            query = default;
            var isMax = body.StartsWith("max-", StringComparison.Ordinal);
            var rest = isMax || body.StartsWith("min-", StringComparison.Ordinal) ? body.Substring(4) : body;

            if (s_scaleRem.TryGetValue(rest, out var rem))
            {
                query = new StyleContainerQuery(rem * PxPerRem, isMax, name, "rem");
                return true;
            }

            if (!StyleArbitraryValueResolver.TryParseArbitraryPixels(rest.AsSpan(), out var px) || IsUnitless(rest))
            {
                return false;
            }
            query = new StyleContainerQuery(px, isMax, name, UnitOf(rest));
            return true;
        }

        // The shared length grammar reads a bare 0 as a length; a container threshold is declined without a unit, 0
        // included.
        private static bool IsUnitless(string bracketed)
        {
            var last = bracketed[bracketed.Length - 2];
            return char.IsDigit(last) || last == '.';
        }

        private static readonly char[] s_letters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

        // The unit text of a bracketed length (`[475px]` is "px"), which orders one unit ahead of another.
        private static string UnitOf(string bracketed)
        {
            var inner = bracketed.Substring(1, bracketed.Length - 2);
            return inner.Substring(inner.TrimEnd(s_letters).Length);
        }
    }
}
