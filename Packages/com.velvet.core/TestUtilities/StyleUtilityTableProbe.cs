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
    }
}
