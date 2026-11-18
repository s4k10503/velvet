using System;
using System.Collections.Generic;

namespace Velvet
{
    // A side radius (`rounded-l-lg`) that a higher-priority class claims one corner of keeps painting its other
    // corner: the corners it still owns are written by the per-corner classes of the same size
    // (`rounded-bl-lg`), which the stylesheets declare for every size a side class has.
    internal static class StyleCornerRemainder
    {
        private const string Prefix = "rounded-";

        private static readonly (StyleLonghand Longhand, string Name)[] s_corners =
        {
            (StyleLonghand.BorderTopLeftRadius, "tl"),
            (StyleLonghand.BorderTopRightRadius, "tr"),
            (StyleLonghand.BorderBottomRightRadius, "br"),
            (StyleLonghand.BorderBottomLeftRadius, "bl"),
        };

        // The per-corner classes standing for the corners of cls that claimed leaves it; false where cls is no side
        // radius, or a per-corner class of its size is missing.
        public static bool TryOf(string cls, StyleLonghandSet properties, StyleLonghandSet claimed,
            out string[] remainder)
        {
            remainder = Array.Empty<string>();
            if (!cls.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }
            var rest = cls.Substring(Prefix.Length);
            var dash = rest.IndexOf('-');
            var side = dash < 0 ? rest : rest.Substring(0, dash);
            if (side != "t" && side != "r" && side != "b" && side != "l")
            {
                return false;
            }
            var suffix = dash < 0 ? string.Empty : rest.Substring(dash);
            var names = new List<string>(2);
            foreach (var (longhand, name) in s_corners)
            {
                if (!properties.Contains(longhand) || claimed.Contains(longhand))
                {
                    continue;
                }
                var corner = Prefix + name + suffix;
                if (!StyleUtilityProperties.TryGet(corner, out var rule) || rule.Properties != StyleLonghandSet.Of(longhand))
                {
                    return false;
                }
                names.Add(corner);
            }
            remainder = names.ToArray();
            return names.Count > 0;
        }
    }
}
