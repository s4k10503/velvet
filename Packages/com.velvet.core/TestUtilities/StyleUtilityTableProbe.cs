using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Velvet.TestUtilities
{
    internal static class StyleUtilityTableProbe
    {
        internal static IEnumerable<string> ClassNames()
            => ((IDictionary)typeof(StyleUtilityProperties)
                .GetField("ByClassName", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!).Keys.Cast<string>();

        // The longhands transition-all's rule lists, in the bundled table's order.
        internal static string[] TransitionAllLonghands()
        {
            StyleTransitionUtilities.TryGet("transition-all", out _, out var listed);
            return Enumerable.Range(0, StyleUtilityProperties.LonghandCount).Select(i => (StyleLonghand)i)
                .Where(listed.Contains).Select(StyleUtilityProperties.UssName).ToArray();
        }
    }
}
