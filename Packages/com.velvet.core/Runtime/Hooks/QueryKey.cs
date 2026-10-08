#nullable enable
using System;
using System.Collections;
using System.Text;

namespace Velvet
{
    /// <summary>
    /// Identifies an entry in a <see cref="QueryClient"/>'s cache: TanStack Query's <c>queryKey</c> array.
    /// Two keys are equal when they hold the same number of parts and each pair of parts is equal under
    /// <see cref="object.Equals(object, object)"/>, so a key rebuilt every render with equal parts names the
    /// same entry, and a record, a tuple, a string or a boxed number compares by content. A part that is
    /// itself a <see cref="QueryKey"/> compares the same way, and a part that is a sequence other than a
    /// string — an array or a list — compares element by element, in order, as v5's structural hash
    /// compares an array.
    /// </summary>
    /// <remarks>
    /// The parts are copied when the key is built, so editing the array passed in afterwards does not move
    /// the key. A part whose equality or hash code changes after the key is built — a mutable collection,
    /// for instance — is outside that guarantee. <b>Deviation:</b> a dictionary is a sequence too and compares
    /// in its enumeration order, where v5 sorts an object's keys before hashing it.
    /// </remarks>
    public sealed class QueryKey : IEquatable<QueryKey>
    {
        private readonly object?[] _parts;
        private readonly int _hashCode;

        /// <summary>Builds a key from its parts, in order: <c>new QueryKey("todos", id)</c>.</summary>
        /// <param name="parts">The parts of the key. Must not be null; a part may be.</param>
        public QueryKey(params object?[] parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            _parts = (object?[])parts.Clone();
            var hash = 17;
            foreach (var part in _parts)
            {
                hash = unchecked(hash * 31 + HashOf(part));
            }
            _hashCode = hash;
        }

        private static int HashOf(object? part)
        {
            if (part is IEnumerable sequence and not string)
            {
                var hash = 19;
                foreach (var item in sequence)
                {
                    // MUTANT_SURVIVES(equivalent, arithmetic): equal sequences still fold to one value, and Equals compares their elements.
                    hash = unchecked(hash * 31 + HashOf(item));
                }
                return hash;
            }
            return part?.GetHashCode() ?? 0;
        }

        private static bool PartEquals(object? left, object? right)
            => left is IEnumerable leftSequence and not string && right is IEnumerable rightSequence and not string
                ? SequenceEquals(leftSequence, rightSequence)
                : object.Equals(left, right);

        private static bool SequenceEquals(IEnumerable left, IEnumerable right)
        {
            var leftItems = left.GetEnumerator();
            var rightItems = right.GetEnumerator();
            while (true)
            {
                var hasLeft = leftItems.MoveNext();
                if (hasLeft != rightItems.MoveNext()) return false;
                if (!hasLeft) return true;
                if (!PartEquals(leftItems.Current, rightItems.Current)) return false;
            }
        }

        // TanStack's partialMatchKey: a filter key matches every key it is a leading run of, itself included.
        internal bool StartsWith(QueryKey prefix)
        {
            if (prefix._parts.Length > _parts.Length) return false;
            for (var i = 0; i < prefix._parts.Length; i++)
            {
                if (!PartEquals(_parts[i], prefix._parts[i])) return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public bool Equals(QueryKey? other)
        {
            if (other is null) return false;
            return _hashCode == other._hashCode
                && _parts.Length == other._parts.Length
                && StartsWith(other);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is QueryKey other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _hashCode;

        /// <inheritdoc/>
        public override string ToString()
        {
            var builder = new StringBuilder("[");
            for (var i = 0; i < _parts.Length; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(_parts[i]?.ToString() ?? "null");
            }
            return builder.Append(']').ToString();
        }
    }
}
