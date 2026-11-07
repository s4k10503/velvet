#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Velvet
{
    // How one part of a QueryKey is compared. A collection is taken apart only when its type keeps object's own
    // Equals, and a record or an anonymous type only when its equality is the compiler's own; a type that
    // declares its own equality keeps it.
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
        // A tuple, a record or an anonymous type: by its members, each compared as a part is.
        Members,
    }

    // TanStack's hashKey, which sorts a plain object's keys before hashing it, and partialMatchKey, which
    // matches a filter's array as a leading run and a filter's object on the properties it names. A record, a
    // tuple or an anonymous type is the object of this port, so what it holds is compared as deeply as a part
    // is.
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
                return IsTuple(type) || MemberFieldsOf(type) != null ? QueryKeyPartKind.Members : QueryKeyPartKind.Value;
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

        // Only the framework's own tuples: a type that implements System.Runtime.CompilerServices.ITuple and declares its own equality keeps it.
        private static bool IsTuple(Type type)
            => type.Namespace == nameof(System)
                && (type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal)
                    || type.Name.StartsWith("Tuple`", StringComparison.Ordinal));

        private static readonly ConcurrentDictionary<Type, System.Reflection.FieldInfo[]?> s_memberFields = new();
        private static readonly Func<Type, System.Reflection.FieldInfo[]?> s_findMemberFields = FindMemberFields;

        // The fields a record's or an anonymous type's compiler-written Equals compares, null for a type whose
        // equality is not that: the instance fields of the type and of each record it derives from, which is the
        // set the synthesized Equals walks. A tuple is read through System.Runtime.CompilerServices.ITuple instead.
        private static System.Reflection.FieldInfo[]? MemberFieldsOf(Type type) => s_memberFields.GetOrAdd(type, s_findMemberFields);

        private static System.Reflection.FieldInfo[]? FindMemberFields(Type type)
        {
            const System.Reflection.BindingFlags equalsFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly;
            var fields = new List<System.Reflection.FieldInfo>();
            var level = type;
            while (level != null && level != typeof(object) && level != typeof(ValueType))
            {
                // A record is told by the Equals taking its own type, which the compiler writes unless the record
                // declares it; an anonymous type, which has none of that shape, by being written by the compiler.
                var typed = level.GetMethod(nameof(object.Equals), equalsFlags, null, new[] { level }, null);
                // The binder also offers Equals(object) for the type's own, which says nothing about a record.
                if (typed != null && typed.GetParameters()[0].ParameterType != level) typed = null;
                var compilerWritten = typed != null
                    ? typed.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
                    : level.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
                        && level.GetMethod(nameof(object.Equals), equalsFlags, null, new[] { typeof(object) }, null) != null;
                if (!compilerWritten) return null;
                fields.AddRange(QueryStructuralSharing.FieldsDeclaredBy(level));
                level = level.BaseType;
            }
            return fields.ToArray();
        }

        // The members of a Members part, in the order they are declared: a tuple's items, or the fields above.
        private static int MemberCount(object part)
            => part is System.Runtime.CompilerServices.ITuple tuple ? tuple.Length : MemberFieldsOf(part.GetType())!.Length;

        private static object? MemberAt(object part, int index)
            => part is System.Runtime.CompilerServices.ITuple tuple ? tuple[index] : MemberFieldsOf(part.GetType())![index].GetValue(part);

        // A part that holds itself, or nests past this depth, is refused as v5's JSON.stringify refuses a cycle,
        // rather than recursing until the stack overflows.
        private const int MaxDepth = 128;
        [ThreadStatic]
        private static int s_depth;

        private readonly struct DepthGuard : IDisposable
        {
            internal DepthGuard(bool enter)
            {
                if (s_depth >= MaxDepth)
                {
                    throw new InvalidOperationException(
                        $"A query key part holds itself or nests deeper than {MaxDepth} levels.");
                }
                s_depth++;
            }

            public void Dispose() => s_depth--;
        }

        internal static int HashOf(object? part)
        {
            var kind = KindOf(part);
            if (kind == QueryKeyPartKind.Value) return part?.GetHashCode() ?? 0;
            using var guard = new DepthGuard(true);
            switch (kind)
            {
                case QueryKeyPartKind.Members:
                    return MembersHash(part!);
                case QueryKeyPartKind.Sequence:
                    return SequenceHash((IEnumerable)part!);
                case QueryKeyPartKind.Dictionary:
                    return DictionaryHash((IDictionary)part!);
                default:
                    return SetHash((IEnumerable)part!);
            }
        }

        // The type joins the hash because two records of different types holding the same members are different
        // parts.
        private static int MembersHash(object part)
        {
            var hash = part.GetType().GetHashCode();
            var count = MemberCount(part);
            for (var i = 0; i < count; i++)
            {
                hash = unchecked(hash * 31 + HashOf(MemberAt(part, i)));
            }
            return hash;
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
            using var guard = new DepthGuard(true);
            switch (kind)
            {
                case QueryKeyPartKind.Members:
                    return MembersMatch(part!, filter!, partial);
                case QueryKeyPartKind.Sequence:
                    return SequenceMatches((IEnumerable)part!, (IEnumerable)filter!, partial);
                case QueryKeyPartKind.Dictionary:
                    return DictionaryMatches((IDictionary)part!, (IDictionary)filter!, partial);
                default:
                    return SetEquals((IEnumerable)part!, (IEnumerable)filter!);
            }
        }

        // Two parts of one type, as a record's Equals requires its EqualityContract to agree; a tuple's members
        // are its items, so tuples of different arity are different types.
        private static bool MembersMatch(object part, object filter, bool partial)
        {
            if (part.GetType() != filter.GetType()) return false;
            var count = MemberCount(filter);
            for (var i = 0; i < count; i++)
            {
                if (!Matches(MemberAt(part, i), MemberAt(filter, i), partial)) return false;
            }
            return true;
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
                case QueryKeyPartKind.Members:
                    if (!IsTuple(part!.GetType())) builder.Append(part.GetType().Name);
                    AppendMembers(builder, part);
                    break;
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

        private static void AppendMembers(StringBuilder builder, object part)
        {
            builder.Append('(');
            var count = MemberCount(part);
            for (var i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(", ");
                Append(builder, MemberAt(part, i));
            }
            builder.Append(')');
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
