#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;

namespace Velvet
{
    /// <summary>
    /// Identifies an entry in a <see cref="QueryClient"/>'s cache: TanStack Query's <c>queryKey</c> array.
    /// Two keys are equal when they hold the same number of parts and each pair of parts is equal under
    /// <see cref="object.Equals(object, object)"/>, so a key rebuilt every render with equal parts names the
    /// same entry, and a string, a boxed number or a record or tuple of such values compares by content. A
    /// part that is itself a <see cref="QueryKey"/> compares the same way. A part that is a sequence whose type
    /// keeps <see cref="object"/>'s own <c>Equals</c> — an array, a list, a set, a dictionary — compares
    /// element by element, in order, as v5's structural hash compares an array; a sequence type that defines
    /// its own equality keeps it.
    /// </summary>
    /// <remarks>
    /// The parts are copied when the key is built, so editing the array passed in afterwards does not move
    /// the key. A part whose equality or hash code changes after the key is built — a mutable collection,
    /// for instance — is outside that guarantee. A record or a tuple compares by its members' own
    /// <c>Equals</c>, so a collection held inside one compares by reference and names a new entry whenever it
    /// is rebuilt: give the collection a key part of its own. <b>Deviation:</b> a dictionary or a set compares
    /// in its enumeration order, where v5 sorts an object's keys before hashing it. A sequence part is
    /// enumerated when the key is built and again on every comparison, so pass a materialised collection
    /// rather than a lazy query.
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

        // Per type: whether it keeps object.Equals. Only such a sequence is compared by its elements; one whose
        // type declares its own equality keeps it.
        private static readonly ConcurrentDictionary<Type, bool> s_comparedByElement = new();

        private static bool IsComparedByElement(object? part)
            => part is IEnumerable && s_comparedByElement.GetOrAdd(part.GetType(),
                type => type.GetMethod(nameof(Equals), new[] { typeof(object) })!.DeclaringType == typeof(object));

        private static int HashOf(object? part)
        {
            if (IsComparedByElement(part))
            {
                var sequence = (IEnumerable)part!;
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
            => IsComparedByElement(left) && IsComparedByElement(right)
                ? SequenceEquals((IEnumerable)left!, (IEnumerable)right!)
                : object.Equals(left, right);

        private static bool SequenceEquals(IEnumerable left, IEnumerable right)
        {
            var leftItems = left.GetEnumerator();
            var rightItems = right.GetEnumerator();
            try
            {
                while (true)
                {
                    var hasLeft = leftItems.MoveNext();
                    if (hasLeft != rightItems.MoveNext()) return false;
                    if (!hasLeft) return true;
                    if (!PartEquals(leftItems.Current, rightItems.Current)) return false;
                }
            }
            finally
            {
                (leftItems as IDisposable)?.Dispose();
                (rightItems as IDisposable)?.Dispose();
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
                AppendPart(builder, _parts[i]);
            }
            return builder.Append(']').ToString();
        }

        // A sequence compared by its elements prints them rather than its type name, so keys differing in
        // such a part's elements print differently wherever those elements do.
        private static void AppendPart(StringBuilder builder, object? part)
        {
            if (!IsComparedByElement(part))
            {
                builder.Append(part?.ToString() ?? "null");
                return;
            }
            builder.Append('[');
            var first = true;
            foreach (var item in (IEnumerable)part!)
            {
                if (!first) builder.Append(", ");
                first = false;
                AppendPart(builder, item);
            }
            builder.Append(']');
        }
    }
}
