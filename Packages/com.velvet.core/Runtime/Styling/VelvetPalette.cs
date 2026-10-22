using System;
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // The default utility color palette as a C#-resolvable name -> Color map, for a C#-driven utility that writes an
    // INLINE color — one no USS class can reach, e.g. divide-{color} setting a child element's border color. Names are
    // "<hue>-<shade>" (e.g. "gray-200") plus the three specials white / black / transparent ("current"/"inherit" have
    // no fixed value and are intentionally absent). The hue-shade colours are VelvetPaletteColors', which
    // Velvet.StyleTable derives from the source it writes the --color-* tokens in Styles/_palette.uss from.
    internal static class VelvetPalette
    {
        // Parsed colors, built once on first access. transparent is handled before the map
        // (ColorUtility does not parse the "transparent" keyword).
        private static readonly Dictionary<string, Color> Parsed = BuildParsed();

        private static Dictionary<string, Color> BuildParsed()
        {
            var map = new Dictionary<string, Color>(VelvetPaletteColors.Hex.Count + 2)
            {
                ["white"] = Color.white,
                ["black"] = Color.black,
            };
            foreach (var kv in VelvetPaletteColors.Hex)
            {
                if (ColorUtility.TryParseHtmlString(kv.Value, out var color))
                {
                    map[kv.Key] = color;
                }
            }
            return map;
        }

        // Resolves a palette color name ("gray-200", "white", "black", "transparent") to a
        // Color. Returns false for an unknown name so the caller can fall through to other resolvers.
        public static bool TryGet(string name, out Color color)
        {
            if (string.IsNullOrEmpty(name))
            {
                color = default;
                return false;
            }
            if (name == "transparent")
            {
                color = new Color(0f, 0f, 0f, 0f);
                return true;
            }
            return Parsed.TryGetValue(name, out color);
        }

        // Resolves a color TOKEN — the suffix after a utility prefix — that is either a named palette
        // color ("gray-200", "white") or an arbitrary "[<color>]" value. Shared by the
        // color-bearing utilities (divide-{c}, from-/via-/to-{c}) so their grammar cannot drift.
        public static bool TryResolveColorToken(string suffix, out Color color)
            => TryResolveColorToken(suffix, false, out color);

        // longhand is StyleColorValueParser.TryParseColor's: true when the utility is a colour longhand.
        internal static bool TryResolveColorToken(string suffix, bool longhand, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(suffix))
            {
                return false;
            }
            if (StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                // A type hint is dropped here: the caller has already chosen the colour branch.
                return StyleArbitraryValueResolver.TrySplitTypeHint(inner, out _, out inner)
                    && StyleColorValueParser.TryParseColor(inner, longhand, out color);
            }
            return TryGet(suffix, out color);
        }
    }
}
