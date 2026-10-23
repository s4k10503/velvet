using System;
using System.Collections.Generic;
using UnityEngine.TextCore.Text;

namespace Velvet
{
    // A font-family list (font-[Inter,Noto_Sans_JP,sans-serif]) and what VelvetFonts makes of it: the first
    // family that resolves to a Font Asset is the one assigned, and the assets of the families after it become
    // entries of that asset's fallback table, in list order, so a glyph the first lacks is taken from the next as
    // the browser takes it from the next family. The table belongs to the asset, not to the label that named the
    // list: an entry stays on it until Release, and every label showing that asset gets it.
    internal static class VelvetFontStack
    {
        // The entries Velvet appended, per asset, so Release takes out only those and leaves a fallback table
        // someone configured on the asset as it was.
        private static readonly Dictionary<FontAsset, List<FontAsset>> s_appended = new();

        // Raised with the asset whose table Append just grew, for the labels already showing it
        // (StyleFontResolver.MarkShowing).
        public static event Action<FontAsset>? Grew;

        // The names of a family list, trimmed, unquoted and without the empty ones. A family starting with the
        // address prefix is one key whatever it holds.
        public static List<string> Split(string family)
        {
            var names = new List<string>();
            if (family.StartsWith(AddressPrefix, StringComparison.Ordinal))
            {
                names.Add(family);
                return names;
            }
            foreach (var part in family.Split(','))
            {
                var name = Unquote(part.Trim());
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
            return names;
        }

        public const string AddressPrefix = "addr:";

        // Whether family is a list of more than one name.
        public static bool IsList(string? family)
            => !string.IsNullOrEmpty(family)
               && !family!.StartsWith(AddressPrefix, StringComparison.Ordinal)
               && family.IndexOf(',') >= 0;

        private static string Unquote(string name)
        {
            if (name.Length >= 2 && (name[0] == '\'' || name[0] == '"') && name[name.Length - 1] == name[0])
            {
                return name.Substring(1, name.Length - 2).Trim();
            }
            return name;
        }

        // Appends each fallback to primary's table that is not already there and would not make the table reach
        // back to primary. Returns whether the table changed.
        public static bool Append(FontAsset primary, IReadOnlyList<FontAsset> fallbacks)
        {
            var changed = false;
            foreach (var fallback in fallbacks)
            {
                if (fallback == null || fallback == primary || Reaches(fallback, primary))
                {
                    continue;
                }
                var table = primary.fallbackFontAssetTable;
                if (table == null)
                {
                    table = new List<FontAsset>();
                    primary.fallbackFontAssetTable = table;
                }
                if (table.Contains(fallback))
                {
                    continue;
                }
                table.Add(fallback);
                if (!s_appended.TryGetValue(primary, out var added))
                {
                    s_appended[primary] = added = new List<FontAsset>();
                }
                added.Add(fallback);
                changed = true;
            }
            if (changed)
            {
                Grew?.Invoke(primary);
            }
            return changed;
        }

        // Takes out every entry Append added.
        public static void Release()
        {
            foreach (var (primary, added) in s_appended)
            {
                var table = primary != null ? primary.fallbackFontAssetTable : null;
                if (table == null)
                {
                    continue;
                }
                foreach (var fallback in added)
                {
                    table.Remove(fallback);
                }
            }
            s_appended.Clear();
        }

        // Whether target is in from's fallback tables, however deep.
        private static bool Reaches(FontAsset from, FontAsset target)
        {
            var seen = new HashSet<FontAsset>();
            var pending = new Stack<FontAsset>();
            pending.Push(from);
            while (pending.Count > 0)
            {
                var asset = pending.Pop();
                if (asset == null || !seen.Add(asset))
                {
                    continue;
                }
                if (asset == target)
                {
                    return true;
                }
                var table = asset.fallbackFontAssetTable;
                if (table == null)
                {
                    continue;
                }
                foreach (var next in table)
                {
                    pending.Push(next);
                }
            }
            return false;
        }
    }
}
