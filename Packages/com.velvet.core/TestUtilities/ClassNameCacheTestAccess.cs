using System;
using System.Collections;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Drains <c>V</c>'s process-wide class-name parse cache. Reached by reflection because production types
    /// carry no test-only members.
    /// </summary>
    public static class ClassNameCacheTestAccess
    {
        private const string CacheFieldName = "s_classNameCache";

        /// <summary>
        /// Empties the cache, so a string parsed before the call parses to a fresh array after it.
        /// </summary>
        /// <exception cref="MissingFieldException">
        /// The cache field was renamed or removed. Throwing is the point: callers drain to keep their own
        /// entries below the cache's size bound, or to push content-identical trees off the reference-identity
        /// fast path, and a clear that quietly reached nothing would leave both asserting on the wrong state.
        /// </exception>
        // Bypasses: nothing — it resets a static cache, which no production path does.
        public static void ClearForTest()
        {
            var field = typeof(V).GetField(CacheFieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(V).FullName, CacheFieldName);
            }
            // The non-generic view avoids pinning the cache's value type, which is not what this reaches for.
            ((IDictionary)field.GetValue(null)!).Clear();
        }
    }
}
