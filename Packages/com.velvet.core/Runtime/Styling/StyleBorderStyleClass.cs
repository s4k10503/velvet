#nullable enable

namespace Velvet
{
    /// <summary>
    /// A resolved border line-style utility: which of solid / dashed / dotted the cascade selected, plus the
    /// winning class token (the allocation-free steady-state probe compares tokens, mirroring
    /// <see cref="StyleSkewClass"/>'s Source). <c>border-solid</c> is a recognized reset that overrides an
    /// earlier <c>border-dashed</c> / <c>border-dotted</c> in the cascade.
    /// </summary>
    internal readonly struct BorderStyleSpec
    {
        public readonly BorderLineStyle Style;
        public readonly string? Source;

        public BorderStyleSpec(BorderLineStyle style, string? source)
        {
            Style = style;
            Source = source;
        }

        // Active only for a non-solid style — border-solid leaves the native (solid) border in place, so a
        // cascade ending in it is inactive (no painted outline).
        public bool Active => Style == BorderLineStyle.Dashed || Style == BorderLineStyle.Dotted;
    }

    // Parses Velvet's border line-style utilities: border-dashed / border-dotted / border-solid. UI Toolkit
    // has no CSS border-style property, so a dashed / dotted border is painted by BorderStyleSilhouette (the
    // element's own generateVisualContent, like the skew silhouette) while the native border color is
    // suppressed and its width kept (the box still reserves the same layout space). border-solid is a
    // recognized reset — it overrides an earlier border-dashed in the cascade, keeping the native solid
    // border. Same shape as StyleSkewClass: a cheap gate plus a cascade-correct extractor (last recognized
    // class wins).
    internal static class StyleBorderStyleClass
    {
        private const string Dashed = "border-dashed";
        private const string Dotted = "border-dotted";
        private const string Solid = "border-solid";

        // True when cls is one of the border line-style utilities this layer owns.
        public static bool IsBorderStyleClass(string? cls)
            => cls == Dashed || cls == Dotted || cls == Solid;

        // Cheap early-out gate: true when ANY class is a border line-style utility.
        public static bool HasBorderStyleClass(string[]? classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsBorderStyleClass(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Allocation-free cascade probe: the LAST recognized border-style token (mirroring the token
        // TryExtract stores in BorderStyleSpec.Source), or false when none is present. The patch path compares
        // this against the live binding's Source to skip the parse in the steady state.
        public static bool TryGetWinningBorderStyleClass(string[]? classNames, out string? winner)
        {
            winner = null;
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsBorderStyleClass(cls))
                {
                    winner = cls;
                }
            }
            return winner != null;
        }

        // Resolves the cascade (last recognized class wins) into a BorderStyleSpec. Returns true only when the
        // result is ACTIVE (dashed or dotted) — border-solid resets, so a list ending in border-solid extracts
        // to false.
        public static bool TryExtract(string[]? classNames, out BorderStyleSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var style = BorderLineStyle.Solid;
            string? source = null;
            foreach (var cls in classNames)
            {
                if (cls == Dashed)
                {
                    style = BorderLineStyle.Dashed;
                    source = cls;
                }
                else if (cls == Dotted)
                {
                    style = BorderLineStyle.Dotted;
                    source = cls;
                }
                else if (cls == Solid)
                {
                    style = BorderLineStyle.Solid;
                    source = cls;
                }
            }

            spec = new BorderStyleSpec(style, source);
            return spec.Active;
        }
    }
}
