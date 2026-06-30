using System.Collections.Generic;

namespace Velvet
{
    // Shared matching logic for StyleRecipe.CompoundVariant / StyleSlotRecipe.SlotCompoundVariant.
    internal static class StyleCompoundVariantMatcher
    {
        // Returns whether every entry in conditions matches the corresponding entry in selected.
        public static bool Matches(Dictionary<string, string> conditions, Dictionary<string, string> selected)
        {
            foreach (var (axis, value) in conditions)
            {
                if (!selected.TryGetValue(axis, out var selectedValue) || selectedValue != value)
                {
                    return false;
                }
            }

            return true;
        }

        // Array-based matching (allocation-free variant for reducing GC pressure).
        public static bool MatchesArray(Dictionary<string, string> conditions, (string axis, string value)[] selections, int count)
        {
            foreach (var (axis, value) in conditions)
            {
                var found = false;
                for (var i = 0; i < count; i++)
                {
                    if (selections[i].axis == axis && selections[i].value == value)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
