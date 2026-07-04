using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Class-name builder utility with named variant axes.
    /// Specify a value for each variant axis (visual, size, etc.) and expand to the corresponding classes.
    /// </summary>
    public sealed class StyleRecipe
    {
        private readonly string _base;
        private readonly Dictionary<string, Dictionary<string, string>> _variants;
        private readonly Dictionary<string, string>? _defaultVariants;
        private readonly CompoundVariant[]? _compoundVariants;

        /// <summary>
        /// Builds a recipe from a base class string, a per-axis variant→classes map, optional per-axis
        /// default values, and optional compound variants (classes applied when several axes match together).
        /// </summary>
        public StyleRecipe(
            string @base,
            Dictionary<string, Dictionary<string, string>> variants,
            Dictionary<string, string>? defaultVariants = null,
            CompoundVariant[]? compoundVariants = null)
        {
            _base = @base;
            _variants = variants;
            _defaultVariants = defaultVariants;
            _compoundVariants = compoundVariants;
        }

        /// <summary>Expands by named variant axes. Unspecified axes fall back to defaultVariants.</summary>
        public string? Apply(params (string axis, string value)[] selections) => ApplyInternal(selections, null);

        /// <summary>Named variant axes plus an extra class appended at the end.</summary>
        public string? Apply(string extra, params (string axis, string value)[] selections) => ApplyInternal(selections, extra);

        private string? ApplyInternal((string axis, string value)[] selections, string? extra)
        {
            // Build an effective array: selections deduped to last-wins per axis, then defaultVariants for
            // any axis still unspecified. A repeated axis keeps only the last value
            // (Apply(("visual","primary"),("visual","secondary")) selects "secondary").
            // The deduped array feeds both the per-axis emit and compound matching, so neither sees a stale
            // overridden value. A linear scan instead of a Dictionary keeps this allocation-light. The
            // surviving value per axis matches StyleSlotRecipe (whose selected dictionary also keeps the last write);
            // only the value is guaranteed equal, not the emitted class order.
            var defaultCount = _defaultVariants?.Count ?? 0;
            var effective = new (string axis, string value)[selections.Length + defaultCount];
            var effectiveCount = 0;

            for (var i = 0; i < selections.Length; i++)
            {
                var (axis, value) = selections[i];
                if (IsAxisOverriddenLater(selections, i, axis))
                {
                    continue;
                }
                effective[effectiveCount++] = (axis, value);
            }

            if (defaultCount > 0)
            {
                foreach (var (axis, defaultValue) in _defaultVariants!)
                {
                    if (!ContainsAxis(selections, axis))
                    {
                        effective[effectiveCount++] = (axis, defaultValue);
                    }
                }
            }

            // Exact upper bound: base (always 1) + per-axis classes + compound + extra (0 or 1). Per-axis
            // TryGetValue misses and unmatched compounds can leave idx below this, so the trailing trim stays.
            var capacity = 1 + effectiveCount + (_compoundVariants?.Length ?? 0) + (string.IsNullOrEmpty(extra) ? 0 : 1);
            var parts = new string?[capacity];
            var idx = 0;
            parts[idx++] = _base;

            for (var i = 0; i < effectiveCount; i++)
            {
                var (axis, value) = effective[i];
                if (_variants.TryGetValue(axis, out var axisValues) &&
                    axisValues.TryGetValue(value, out var classes))
                {
                    parts[idx++] = classes;
                }
            }

            if (_compoundVariants != null)
            {
                foreach (var compound in _compoundVariants)
                {
                    if (compound.Matches(effective, effectiveCount))
                    {
                        parts[idx++] = compound.ClassName;
                    }
                }
            }

            if (!string.IsNullOrEmpty(extra))
            {
                parts[idx++] = extra;
            }

            if (idx < parts.Length)
            {
                var trimmed = new string?[idx];
                System.Array.Copy(parts, trimmed, idx);
                return StyleClassNames.Class(trimmed);
            }

            return StyleClassNames.Class(parts);
        }

        private static bool ContainsAxis((string axis, string value)[] selections, string axis)
        {
            foreach (var (a, _) in selections)
            {
                if (a == axis)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsAxisOverriddenLater((string axis, string value)[] selections, int index, string axis)
        {
            for (var j = index + 1; j < selections.Length; j++)
            {
                if (selections[j].axis == axis)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Compound variant that applies an extra class when multiple axis conditions all match.</summary>
        public sealed class CompoundVariant
        {
            /// <summary>Axis→value conditions that must all match for <see cref="ClassName"/> to apply.</summary>
            public Dictionary<string, string> Conditions { get; }

            /// <summary>Class string applied when every entry in <see cref="Conditions"/> matches.</summary>
            public string ClassName { get; }

            /// <summary>Creates a compound variant: apply <paramref name="className"/> when all <paramref name="conditions"/> match.</summary>
            public CompoundVariant(Dictionary<string, string> conditions, string className)
            {
                Conditions = conditions;
                ClassName = className;
            }

            /// <summary>Linear-scan matching against a deduped selections array (no per-call allocation).</summary>
            public bool Matches((string axis, string value)[] selections, int count)
                => StyleCompoundVariantMatcher.MatchesArray(Conditions, selections, count);
        }
    }
}
