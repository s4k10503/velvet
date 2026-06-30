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
        public FontAsset upright;

        [Tooltip("Italic Font Asset for this weight. Takes precedence over Italic Address.")]
        public FontAsset italic;

        [Tooltip("Addressables key for the upright Font Asset (used when Upright is unset).")]
        public string uprightAddress;

        [Tooltip("Addressables key for the italic Font Asset (used when Italic is unset).")]
        public string italicAddress;
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
        public string name;

        [Tooltip("Per-weight Font Assets. A family needs at least one entry.")]
        public List<VelvetFontWeightEntry> weights = new();

        public VelvetFontFamily() { }

        public VelvetFontFamily(string name, params VelvetFontWeightEntry[] weights)
        {
            this.name = name;
            this.weights = weights != null ? new List<VelvetFontWeightEntry>(weights) : new List<VelvetFontWeightEntry>();
        }

        /// <summary>
        /// Picks the entry whose <see cref="VelvetFontWeightEntry.weight"/> is closest to
        /// <paramref name="requested"/>. Ties resolve to the heavier entry (matching CSS, which rounds a
        /// midpoint up). Returns null only when the family has no entries.
        /// </summary>
        public VelvetFontWeightEntry FindClosestWeight(VelvetFontWeight requested)
        {
            if (weights == null || weights.Count == 0)
            {
                return null;
            }

            VelvetFontWeightEntry best = null;
            var bestDistance = int.MaxValue;
            foreach (var entry in weights)
            {
                if (entry == null)
                {
                    continue;
                }

                var distance = Math.Abs((int)entry.weight - (int)requested);
                // A strictly-closer entry always wins; on an equal-distance tie a strictly-heavier later
                // entry takes over, so the heavier side of a tie is chosen (e.g. requested 500 between
                // 400 and 600 → 600), matching CSS's round-the-midpoint-up behaviour.
                if (distance < bestDistance || (distance == bestDistance && best != null && entry.weight > best.weight))
                {
                    best = entry;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
