using System.Collections.Generic;

namespace Velvet
{
    // Shared matching logic for StyleRecipe.CompoundVariant / StyleSlotRecipe.SlotCompoundVariant.
    internal static class StyleCompoundVariantMatcher
    {
        // Returns whether every entry in conditions matches the corresponding selection
        // (allocation-free: selections is a deduped array scanned linearly).
        public static bool Matches(Dictionary<string, string> conditions, (string axis, string value)[] selections, int count)
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
