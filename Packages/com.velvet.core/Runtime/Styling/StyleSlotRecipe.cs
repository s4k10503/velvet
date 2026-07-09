using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Defines class names for UI patterns with multiple slots (parts) in one place.
    /// Each slot can have a base class and variant axes.
    /// </summary>
    public sealed class StyleSlotRecipe
    {
        private readonly Dictionary<string, string> _baseSlots;
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>>? _variants;
        private readonly Dictionary<string, string>? _defaultVariants;
        private readonly SlotCompoundVariant[]? _compoundVariants;

        /// <summary>Recipe without variants.</summary>
        public StyleSlotRecipe(Dictionary<string, string> slots)
        {
            _baseSlots = slots;
        }

        /// <summary>Recipe with variant axes.</summary>
        public StyleSlotRecipe(
            Dictionary<string, string> slots,
            Dictionary<string, Dictionary<string, Dictionary<string, string>>> variants,
            Dictionary<string, string>? defaultVariants = null,
            SlotCompoundVariant[]? compoundVariants = null)
        {
            _baseSlots = slots;
            _variants = variants;
            _defaultVariants = defaultVariants;
            _compoundVariants = compoundVariants;
        }

        /// <summary>Returns class names for all slots without selecting any variants.</summary>
        public StyleSlotClasses Apply() => ApplyInternal(System.Array.Empty<(string, string)>());

        /// <summary>Returns class names for all slots with the given variant selections.</summary>
        public StyleSlotClasses Apply(params (string axis, string value)[] selections) => ApplyInternal(selections);

        private StyleSlotClasses ApplyInternal((string axis, string value)[] selections)
        {
            // Dedup selections to last-write-wins per axis, keeping each axis at its FIRST-occurrence
            // position (matching what the previous `selected[axis] = value` Dictionary-assignment loop
            // produced), then fill in defaultVariants for any axis still unselected. A linear scan over a
            // small array avoids the Dictionary allocation, mirroring StyleRecipe's deduped-array approach.
            var defaultCount = _defaultVariants?.Count ?? 0;
            var selected = new (string axis, string value)[selections.Length + defaultCount];
            var selectedCount = 0;

            foreach (var (axis, value) in selections)
            {
                var existing = IndexOfAxis(selected, selectedCount, axis);
                if (existing >= 0)
                {
                    selected[existing] = (axis, value);
                }
                else
                {
                    selected[selectedCount++] = (axis, value);
                }
            }

            if (_defaultVariants != null)
            {
                foreach (var (axis, defaultValue) in _defaultVariants)
                {
                    if (IndexOfAxis(selected, selectedCount, axis) < 0)
                    {
                        selected[selectedCount++] = (axis, defaultValue);
                    }
                }
            }

            // Build class names per slot (precompute loop-invariant values). parts is sized once to the
            // shared upper bound and reused across slots instead of allocating a fresh array per slot; a
            // slot that fills fewer entries than a previous one has its unused tail cleared before
            // StyleClassNames.Class (which reads the whole array) runs, so no stale entry from an earlier
            // slot can leak into a later one.
            var variantCount = _variants != null ? selectedCount : 0;
            var compoundCount = _compoundVariants?.Length ?? 0;
            var capacity = 1 + variantCount + compoundCount;
            var parts = new string?[capacity];
            var result = new Dictionary<string, string>(_baseSlots.Count);
            foreach (var (slot, baseClass) in _baseSlots)
            {
                var idx = 0;
                parts[idx++] = baseClass;

                if (_variants != null)
                {
                    for (var i = 0; i < selectedCount; i++)
                    {
                        var (axis, value) = selected[i];
                        if (_variants.TryGetValue(axis, out var axisSlots) &&
                            axisSlots.TryGetValue(value, out var slotOverrides) &&
                            slotOverrides.TryGetValue(slot, out var classes))
                        {
                            parts[idx++] = classes;
                        }
                    }
                }

                if (_compoundVariants != null)
                {
                    foreach (var compound in _compoundVariants)
                    {
                        if (compound.Matches(selected, selectedCount) &&
                            compound.SlotClasses.TryGetValue(slot, out var compoundClass))
                        {
                            parts[idx++] = compoundClass;
                        }
                    }
                }

                if (idx < capacity)
                {
                    System.Array.Clear(parts, idx, capacity - idx);
                }

                result[slot] = StyleClassNames.Class(parts) ?? "";
            }

            return new StyleSlotClasses(result);
        }

        // Linear scan for the first `count` entries of selected — small enough (bounded by the number of
        // axes a single Apply() call selects) that this stays cheaper than a Dictionary for the dedup pass.
        private static int IndexOfAxis((string axis, string value)[] selected, int count, string axis)
        {
            for (var i = 0; i < count; i++)
            {
                if (selected[i].axis == axis)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Adds classes to slots when a compound condition matches.</summary>
        public sealed class SlotCompoundVariant
        {
            /// <summary>Axis→value conditions that must all match for <see cref="SlotClasses"/> to apply.</summary>
            public Dictionary<string, string> Conditions { get; }

            /// <summary>Per-slot classes added when every entry in <see cref="Conditions"/> matches.</summary>
            public Dictionary<string, string> SlotClasses { get; }

            /// <summary>Creates a compound variant: add <paramref name="slotClasses"/> when all <paramref name="conditions"/> match.</summary>
            public SlotCompoundVariant(
                Dictionary<string, string> conditions,
                Dictionary<string, string> slotClasses)
            {
                Conditions = conditions;
                SlotClasses = slotClasses;
            }

            /// <summary>Linear-scan matching against a deduped selections array (no per-call allocation).</summary>
            public bool Matches((string axis, string value)[] selected, int count)
                => StyleCompoundVariantMatcher.Matches(Conditions, selected, count);
        }
    }

    /// <summary>Read-only wrapper from slot name → class name.</summary>
    public readonly struct StyleSlotClasses
    {
        private readonly Dictionary<string, string>? _slots;

        /// <summary>Wraps a slot-name → class-string map.</summary>
        public StyleSlotClasses(Dictionary<string, string> slots)
        {
            _slots = slots;
        }

        /// <summary>Returns the class string for <paramref name="slot"/>, or "" when the slot is unknown.</summary>
        public string this[string slot] =>
            _slots != null && _slots.TryGetValue(slot, out var value) ? value : "";
    }
}
