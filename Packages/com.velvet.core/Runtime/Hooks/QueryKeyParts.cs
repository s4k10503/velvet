#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Velvet
{
    // How one part of a QueryKey is compared. Only a type that keeps object's own Equals is taken apart; a type
    // that declares its own equality keeps it.
    internal enum QueryKeyPartKind
    {
        // Compared with object.Equals.
        Value,
        // An array, a list: element by element, in order.
        Sequence,
        // A dictionary: by key, whatever order the entries were added in.
        Dictionary,
        // A set: by element, whatever order the elements were added in.
        Set,
    }

    // TanStack's hashKey, which sorts a plain object's keys before hashing it, and partialMatchKey, which
    // matches a filter's array as a leading run and a filter's object on the properties it names.
    internal static class QueryKeyParts
    {
        private static readonly ConcurrentDictionary<Type, QueryKeyPartKind> s_kinds = new();
        private static readonly Func<Type, QueryKeyPartKind> s_classify = Classify;

        internal static QueryKeyPartKind KindOf(object? part)
            => part == null ? QueryKeyPartKind.Value : s_kinds.GetOrAdd(part.GetType(), s_classify);

        private static QueryKeyPartKind Classify(Type type)
        {
            if (!typeof(IEnumerable).IsAssignableFrom(type)
                || type.GetMethod(nameof(object.Equals), new[] { typeof(object) })!.DeclaringType != typeof(object))
            {
                return QueryKeyPartKind.Value;
            }

            if (typeof(IDictionary).IsAssignableFrom(type)) return QueryKeyPartKind.Dictionary;
            return IsSet(type) ? QueryKeyPartKind.Set : QueryKeyPartKind.Sequence;
        }

        private static bool IsSet(Type type)
        {
            foreach (var implemented in type.GetInterfaces())
            {
                if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(ISet<>)) return true;
            }

            return false;
        }

        internal static int HashOf(object? part)
        {
            switch (KindOf(part))
            {
                case QueryKeyPartKind.Sequence:
                    return SequenceHash((IEnumerable)part!);
                case QueryKeyPartKind.Dictionary:
                    return DictionaryHash((IDictionary)part!);
                case QueryKeyPartKind.Set:
                    return SetHash((IEnumerable)part!);
                default:
                    return part?.GetHashCode() ?? 0;
            }
        }

        private static int SequenceHash(IEnumerable sequence)
        {
            var hash = 19;
            foreach (var item in sequence)
            {
                // MUTANT_SURVIVES(equivalent, arithmetic): equal sequences still fold to one value, and Equals compares their elements.
                hash = unchecked(hash * 31 + HashOf(item));
            }
            return hash;
        }

        // Folded by addition, which does not depend on the order the entries come in.
        private static int DictionaryHash(IDictionary dictionary)
        {
            var sum = 0;
            var entries = dictionary.GetEnumerator();
            while (entries.MoveNext())
            {
                sum = unchecked(sum + HashOf(entries.Key) * 31 + HashOf(entries.Value));
            }
            return sum;
        }

        private static int SetHash(IEnumerable set)
        {
            var sum = 0;
            foreach (var item in set)
            {
                // MUTANT_SURVIVES(equivalent, arithmetic): equal sets still fold to one value, and Equals compares their elements.
                sum = unchecked(sum + HashOf(item));
            }
            return sum;
        }

        // partial is partialMatchKey: the filter may be shorter than the part where it is a sequence, and may
        // name fewer entries where it is a dictionary. A set is compared whole either way.
        internal static bool Matches(object? part, object? filter, bool partial)
        {
            var kind = KindOf(part);
            if (kind == QueryKeyPartKind.Value || kind != KindOf(filter)) return object.Equals(part, filter);
            switch (kind)
            {
                case QueryKeyPartKind.Sequence:
                    return SequenceMatches((IEnumerable)part!, (IEnumerable)filter!, partial);
                case QueryKeyPartKind.Dictionary:
                    return DictionaryMatches((IDictionary)part!, (IDictionary)filter!, partial);
                default:
                    return SetEquals((IEnumerable)part!, (IEnumerable)filter!);
            }
        }

        private static bool SequenceMatches(IEnumerable part, IEnumerable filter, bool partial)
        {
            var partItems = part.GetEnumerator();
            var filterItems = filter.GetEnumerator();
            try
            {
                while (filterItems.MoveNext())
                {
                    if (!partItems.MoveNext() || !Matches(partItems.Current, filterItems.Current, partial)) return false;
                }
                return partial || !partItems.MoveNext();
            }
            finally
            {
                (partItems as IDisposable)?.Dispose();
                (filterItems as IDisposable)?.Dispose();
            }
        }

        private static bool DictionaryMatches(IDictionary part, IDictionary filter, bool partial)
        {
            if (!partial && part.Count != filter.Count) return false;
            var entries = filter.GetEnumerator();
            while (entries.MoveNext())
            {
                if (!part.Contains(entries.Key) || !Matches(part[entries.Key], entries.Value, partial)) return false;
            }
            return true;
        }

        // Each element of one set is paired with a distinct equal element of the other, so sets of elements
        // that compare by content need no hash code of their own.
        private static bool SetEquals(IEnumerable left, IEnumerable right)
        {
            var unmatched = new List<object?>();
            foreach (var item in right)
            {
                unmatched.Add(item);
            }

            foreach (var item in left)
            {
                var index = unmatched.FindIndex(candidate => Matches(item, candidate, partial: false));
                if (index < 0) return false;
                unmatched.RemoveAt(index);
            }

            return unmatched.Count == 0;
        }

        // A part compared by its elements prints them rather than its type name, so keys differing in such a
        // part's elements print differently wherever those elements do.
        internal static void Append(StringBuilder builder, object? part)
        {
            switch (KindOf(part))
            {
                case QueryKeyPartKind.Sequence:
                    AppendItems(builder, '[', (IEnumerable)part!, ']');
                    break;
                case QueryKeyPartKind.Set:
                    AppendItems(builder, '{', (IEnumerable)part!, '}');
                    break;
                case QueryKeyPartKind.Dictionary:
                    AppendEntries(builder, (IDictionary)part!);
                    break;
                default:
                    builder.Append(part?.ToString() ?? "null");
                    break;
            }
        }

        private static void AppendItems(StringBuilder builder, char open, IEnumerable items, char close)
        {
            builder.Append(open);
            var first = true;
            foreach (var item in items)
            {
                if (!first) builder.Append(", ");
                first = false;
                Append(builder, item);
            }
            builder.Append(close);
        }

        private static void AppendEntries(StringBuilder builder, IDictionary dictionary)
        {
            builder.Append('{');
            var first = true;
            var entries = dictionary.GetEnumerator();
            while (entries.MoveNext())
            {
                if (!first) builder.Append(", ");
                first = false;
                Append(builder, entries.Key);
                builder.Append(": ");
                Append(builder, entries.Value);
            }
            builder.Append('}');
        }
    }
}
