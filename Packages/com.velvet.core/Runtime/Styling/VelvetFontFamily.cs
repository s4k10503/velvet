using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Velvet
{
    /// <summary>
    /// One weight slot of a <see cref="VelvetFontFamily"/>: the upright and italic Font Assets for a
    /// single <see cref="VelvetFontWeight"/>. Each asset can be supplied either as a direct reference
    /// (<see cref="upright"/> / <see cref="italic"/>) or as an Addressables key
    /// (<see cref="uprightAddress"/> / <see cref="italicAddress"/>) that <see cref="VelvetFonts"/>
    /// loads and caches on first use. A direct reference always wins over an address.
    /// </summary>
    [Serializable]
    public sealed class VelvetFontWeightEntry
    {
        public VelvetFontWeight weight = VelvetFontWeight.Normal;

        [Tooltip("Upright Font Asset for this weight. Takes precedence over Upright Address.")]
        public FontAsset? upright;

        [Tooltip("Italic Font Asset for this weight. Takes precedence over Italic Address.")]
        public FontAsset? italic;

        [Tooltip("Addressables key for the upright Font Asset (used when Upright is unset).")]
        public string? uprightAddress;

        [Tooltip("Addressables key for the italic Font Asset (used when Italic is unset).")]
        public string? italicAddress;
    }

    /// <summary>
    /// A named font family — the Velvet counterpart of a <c>font-family</c> entry. A family
    /// owns one or more <see cref="VelvetFontWeightEntry"/> slots keyed by <see cref="VelvetFontWeight"/>,
    /// and is selected through the <c>font-&lt;name&gt;</c> utility class (e.g. <c>font-sans</c> →
    /// the family named <c>"sans"</c>).
    /// <para/>
    /// Multilingual (CJK / fallback) coverage is a property of the Font Assets themselves: configure
    /// the local fallback table on the Font Asset, or the global fallback list on the panel's UITK
    /// Text Settings. Velvet only selects <em>which</em> family/weight asset to assign — TextCore
    /// performs the per-glyph fallback at render time.
    /// </summary>
    [Serializable]
    public sealed class VelvetFontFamily
    {
        [Tooltip("Family name targeted by the font-<name> utility class (e.g. \"sans\", \"serif\", \"mono\").")]
        public string name = null!;

        [Tooltip("Per-weight Font Assets. A family needs at least one entry.")]
        public List<VelvetFontWeightEntry> weights = new();

        public VelvetFontFamily() { }

        public VelvetFontFamily(string name, params VelvetFontWeightEntry[] weights)
        {
            this.name = name;
            this.weights = weights != null ? new List<VelvetFontWeightEntry>(weights) : new List<VelvetFontWeightEntry>();
        }

        /// <summary>
        /// Picks the entry CSS's font matching algorithm picks for <paramref name="requested"/>: an exact
        /// weight if one is registered; otherwise, for a request from 400 to 500, the lightest entry from
        /// the request up to 500, then the heaviest below the request, then the lightest above 500; for a
        /// request under 400, the heaviest at or below it, then the lightest above; for a request over 500,
        /// the lightest at or above it, then the heaviest below. Returns null when the family has no entries.
        /// </summary>
        public VelvetFontWeightEntry? FindClosestWeight(VelvetFontWeight requested) => Match(requested, italicFace: null);

        // FindClosestWeight over only the entries that carry the requested style's own face: CSS narrows
        // the faces by font-style before it compares weights, so a face of the requested style at another
        // weight beats the right weight in the wrong style.
        internal VelvetFontWeightEntry? FindClosestWeightWithFace(VelvetFontWeight requested, bool italicFace) =>
            Match(requested, italicFace);

        private VelvetFontWeightEntry? Match(VelvetFontWeight requested, bool? italicFace)
        {
            if (weights == null)
            {
                return null;
            }

            VelvetFontWeightEntry? best = null;
            var bestTier = int.MaxValue;
            var bestKey = int.MaxValue;
            foreach (var entry in weights)
            {
                if (entry == null || (italicFace != null && !HasFace(entry, italicFace.Value)))
                {
                    continue;
                }

                var (tier, key) = Rank((int)requested, (int)entry.weight);
                if (tier < bestTier || (tier == bestTier && key < bestKey))
                {
                    best = entry;
                    bestTier = tier;
                    bestKey = key;
                }
            }

            return best;
        }

        private static bool HasFace(VelvetFontWeightEntry entry, bool italicFace) =>
            italicFace
                ? entry.italic != null || !string.IsNullOrEmpty(entry.italicAddress)
                : entry.upright != null || !string.IsNullOrEmpty(entry.uprightAddress);

        // The search order FindClosestWeight documents: the tier is the pass that reaches the candidate, and
        // within a pass the lower key comes first, so an ascending pass keys on the weight and a descending
        // one on its negation.
        private static (int tier, int key) Rank(int desired, int candidate)
        {
            if (desired > 500)
            {
                return candidate >= desired ? (0, candidate) : (1, -candidate);
            }
            if (desired < 400)
            {
                return candidate <= desired ? (0, -candidate) : (1, candidate);
            }
            if (candidate >= desired && candidate <= 500)
            {
                return (0, candidate);
            }
            return candidate < desired ? (1, -candidate) : (2, candidate);
        }
    }
}
