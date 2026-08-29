using System.Collections.Generic;

namespace Velvet
{
    /// <remarks>
    /// For a selector returning a list, where a fresh instance of the same elements should not read as a
    /// change: <c>UseStore(store, s =&gt; s.Items, StoreShallowEqualityComparer.Sequence&lt;Item&gt;())</c>.
    /// A selector returning a tuple or a value-type record needs no comparer —
    /// <see cref="Hooks.UseStore{TStore,TSel}"/> documents which selector shapes its default already
    /// answers for.
    /// </remarks>
    public static class StoreShallowEqualityComparer
    {
        /// <summary>
        /// Returns a comparer that treats two <see cref="IReadOnlyList{T}"/> as equal when their
        /// lengths match and each element pair is <c>Object.is</c>-equal: reference identity for reference
        /// types (strings compare by ordinal value instead), bit-pattern equality for float/double, and
        /// boxed value equality for other value types. A fresh-but-value-equal reference-type element
        /// (other than a string) therefore counts as changed.
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
                // Equal sequences always share a count; using it avoids traversing the list for hashing.
                return obj.Count;
            }
        }
    }
}
