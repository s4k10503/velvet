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
            var selected = new Dictionary<string, string>(selections.Length);
            foreach (var (axis, value) in selections)
            {
                selected[axis] = value;
            }

            if (_defaultVariants != null)
            {
                foreach (var (axis, defaultValue) in _defaultVariants)
                {
                    selected.TryAdd(axis, defaultValue);
                }
            }

            // Build class names per slot (precompute loop-invariant values).
            var variantCount = _variants != null ? selected.Count : 0;
            var compoundCount = _compoundVariants?.Length ?? 0;
            var result = new Dictionary<string, string>(_baseSlots.Count);
            foreach (var (slot, baseClass) in _baseSlots)
            {
                var parts = new string?[1 + variantCount + compoundCount];
                var idx = 0;
                parts[idx++] = baseClass;

                if (_variants != null)
                {
                    foreach (var (axis, value) in selected)
                    {
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
                        if (compound.Matches(selected) &&
                            compound.SlotClasses.TryGetValue(slot, out var compoundClass))
                        {
                            parts[idx++] = compoundClass;
                        }
                    }
                }

                result[slot] = StyleClassNames.Class(parts);
            }

            return new StyleSlotClasses(result);
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

            public bool Matches(Dictionary<string, string> selected)
                => StyleCompoundVariantMatcher.Matches(Conditions, selected);
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
