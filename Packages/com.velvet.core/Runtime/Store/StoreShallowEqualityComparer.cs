using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Shallow equality comparers for selector return values that are sequences.
    /// Reference-equal elements at matching positions and matching
    /// lengths are considered equal; deep equality is intentionally not performed.
    /// </summary>
    /// <remarks>
    /// Use this for selectors that return an array/list slice, where a fresh list instance with the
    /// same elements should not be treated as a change. The canonical form is
    /// <c>UseStore(store, s =&gt; s.Items, StoreShallowEqualityComparer.Sequence&lt;Item&gt;())</c>.
    /// Tuple / record selectors do not need this helper — <see cref="EqualityComparer{T}.Default"/>
    /// already performs member-wise equality for <c>ValueTuple</c> and value-type records.
    /// </remarks>
    public static class StoreShallowEqualityComparer
    {
        /// <summary>
        /// Returns a comparer that treats two <see cref="IReadOnlyList{T}"/> as equal when their
        /// lengths match and each element pair is <c>Object.is</c>-equal: reference identity for reference
        /// types, bit-pattern equality for float/double, and boxed value equality for other value types.
        /// A fresh-but-value-equal reference-type element therefore counts as changed.
        /// </summary>
        public static IEqualityComparer<IReadOnlyList<T>> Sequence<T>() => SequenceComparer<T>.Instance;

        private sealed class SequenceComparer<T> : IEqualityComparer<IReadOnlyList<T>>
        {
            public static readonly SequenceComparer<T> Instance = new();

            public bool Equals(IReadOnlyList<T> x, IReadOnlyList<T> y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                if (x.Count != y.Count) return false;
                for (var i = 0; i < x.Count; i++)
                {
                    if (!ObjectIs.AreEqualObjects(x[i], y[i])) return false;
                }
                return true;
            }

            public int GetHashCode(IReadOnlyList<T> obj)
            {
                if (obj is null) return 0;
                // The selector hook stores the last value as a snapshot and only uses the comparer
                // via Equals, so the hash is never consulted in practice. Falling back to Count
                // keeps the contract well-defined without iterating the sequence on every check.
                return obj.Count;
            }
        }
    }
}
