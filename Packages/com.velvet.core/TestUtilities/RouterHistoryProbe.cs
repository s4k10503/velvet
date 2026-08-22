using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Reads the <see cref="Router"/>'s history stack. Reached by reflection because production types carry
    /// no test-only members, and the stack has no public accessor: <c>CurrentLocation</c> plus
    /// <c>CanGoBack</c>/<c>CanGoForward</c> describe where the user is, not what entries stand behind them.
    /// </summary>
    public static class RouterHistoryProbe
    {
        private const string HistoryFieldName = "_history";

        /// <summary>
        /// The path of every history entry, oldest first, joined with commas.
        /// </summary>
        /// <exception cref="MissingFieldException">
        /// The history field was renamed or removed. Throwing is the point: a probe that quietly reached
        /// nothing would report an empty stack, which several callers assert is not what they left behind.
        /// </exception>
        public static string PathsOf(Router router) => string.Join(",", EntryPaths(router));

        /// <summary>
        /// The number of entries on the history stack.
        /// </summary>
        public static int CountOf(Router router) => ((ICollection)HistoryOf(router)).Count;

        private const string PathFieldName = "Path";

        private static IEnumerable<string> EntryPaths(Router router)
        {
            foreach (var entry in HistoryOf(router))
            {
                var field = entry.GetType().GetField(PathFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null)
                {
                    throw new MissingFieldException(entry.GetType().FullName, PathFieldName);
                }
                yield return (string)field.GetValue(entry)!;
            }
        }

        private static IEnumerable HistoryOf(Router router)
        {
            var field = typeof(Router).GetField(HistoryFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(Router).FullName, HistoryFieldName);
            }
            return (IEnumerable)field.GetValue(router)!;
        }
    }
}
