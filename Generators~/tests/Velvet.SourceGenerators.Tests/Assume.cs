using System;
using System.Collections.Generic;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Preconditions for guards that assert an emptiness ("nothing diverges"). Such a guard passes on an empty
    /// input for the same reason it passes on a correct one, so the inputs it filters must be proven non-empty
    /// separately or the whole fixture is decorative.
    /// </summary>
    internal static class Assume
    {
        public static void NotEmpty<T>(IReadOnlyCollection<T> values, string what)
        {
            if (values.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Precondition failed: {what}. There is nothing to compare against, which would make " +
                    "this guard pass vacuously.");
            }
        }
    }
}
