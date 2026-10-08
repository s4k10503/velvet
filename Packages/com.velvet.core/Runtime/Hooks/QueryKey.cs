#nullable enable
using System;
using System.Text;

namespace Velvet
{
    /// <summary>
    /// Identifies an entry in a <see cref="QueryClient"/>'s cache: TanStack Query's <c>queryKey</c> array.
    /// Two keys are equal when they hold the same number of parts and each pair of parts is equal under
    /// <see cref="object.Equals(object, object)"/>, so a key rebuilt every render with equal parts names the
    /// same entry, and a string, a boxed number or a record or tuple of such values compares by content. A
    /// part that is itself a <see cref="QueryKey"/> compares the same way. A part that is a collection whose
    /// type keeps <see cref="object"/>'s own <c>Equals</c> compares by what it holds: an array or a list
    /// element by element, in order, as v5's structural hash compares an array; a dictionary by key and
    /// value whatever order the entries were added in, as v5 sorts an object's keys before hashing it; a set
    /// by element, in any order. A collection type that defines its own equality keeps it.
    /// </summary>
    /// <remarks>
    /// The parts are copied when the key is built, so editing the array passed in afterwards does not move
    /// the key. A part whose equality or hash code changes after the key is built — a mutable collection,
    /// for instance — is outside that guarantee. A record or a tuple compares by its members' own
    /// <c>Equals</c>, so a collection held inside one compares by reference and names a new entry whenever it
    /// is rebuilt: give the collection a key part of its own. A collection part is enumerated when the key is
    /// built and again on every comparison, so pass a materialised collection rather than a lazy query.
    /// <para/>
    /// <see cref="QueryClient.InvalidateQueries"/> reads its key as a filter, v5's <c>partialMatchKey</c>: the
    /// filter's parts lead the key's, and a filter part that is an array or a list matches an array or a list
    /// that starts with it, one that is a dictionary matches a dictionary holding at least its entries, each
    /// value matched the same way.
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
                hash = unchecked(hash * 31 + QueryKeyParts.HashOf(part));
            }
            _hashCode = hash;
        }

        // TanStack's partialMatchKey: a filter key matches every key it leads, itself included.
        internal bool Matches(QueryKey filter) => PartsMatch(filter, partial: true);

        private bool PartsMatch(QueryKey filter, bool partial)
        {
            if (filter._parts.Length > _parts.Length) return false;
            for (var i = 0; i < filter._parts.Length; i++)
            {
                if (!QueryKeyParts.Matches(_parts[i], filter._parts[i], partial)) return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public bool Equals(QueryKey? other)
        {
            if (other is null) return false;
            return _hashCode == other._hashCode
                && _parts.Length == other._parts.Length
                && PartsMatch(other, partial: false);
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
                QueryKeyParts.Append(builder, _parts[i]);
            }
            return builder.Append(']').ToString();
        }
    }
}
