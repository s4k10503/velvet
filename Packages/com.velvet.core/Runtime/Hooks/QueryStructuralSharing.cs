#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Velvet
{
    // TanStack's replaceEqualDeep, the default of a query's structuralSharing: a new result keeps the instance
    // already held where it is deeply equal to it, and keeps the equal parts of it where it is not, so a
    // component comparing what it is handed by reference sees no change in the part that did not change.
    internal static class QueryStructuralSharing
    {
        private enum Shape
        {
            // Equal when Equals says so: a string, a number, a struct.
            Leaf,
            // An array of one dimension.
            Array,
            // Exactly List<T>.
            List,
            // Exactly Dictionary<TKey, TValue>.
            Dictionary,
            // Any other class, a record included: kept only as the very instance already held, as v5 treats a class instance.
            Reference,
        }

        private enum Outcome
        {
            Previous,
            Next,
            Mixed,
        }

        // v5 stops descending at the same depth and returns the new value.
        private const int MaxDepth = 500;

        private static readonly ConcurrentDictionary<Type, Shape> s_shapes = new();
        private static readonly Func<Type, Shape> s_classify = Classify;

        internal static T Replace<T>(T? previous, T next) => (T)ReplaceEqualDeep(previous, next, 0)!;

        private static object? ReplaceEqualDeep(object? previous, object? next, int depth)
        {
            if (ReferenceEquals(previous, next)) return previous;
            if (previous == null || next == null || depth > MaxDepth || previous.GetType() != next.GetType())
            {
                return next;
            }

            switch (s_shapes.GetOrAdd(next.GetType(), s_classify))
            {
                case Shape.Leaf:
                    return previous.Equals(next) ? previous : next;
                case Shape.Array:
                    return ReplaceArray((Array)previous, (Array)next, depth);
                case Shape.List:
                    return ReplaceList((IList)previous, (IList)next, depth);
                case Shape.Dictionary:
                    return ReplaceDictionary((IDictionary)previous, (IDictionary)next, depth);
                default:
                    return next;
            }
        }

        private static Shape Classify(Type type)
        {
            if (type.IsArray) return type.GetArrayRank() == 1 ? Shape.Array : Shape.Reference;
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>)) return Shape.List;
                if (definition == typeof(Dictionary<,>)) return Shape.Dictionary;
            }

            return type.IsValueType || type == typeof(string) ? Shape.Leaf : Shape.Reference;
        }

        // Elements of these types hold nothing to share, so two collections of them are the held one or the new
        // one without boxing an element.
        private static bool TryCompareUnmanaged(object previous, object next, out bool equal)
        {
            return TryCompare<int>(previous, next, out equal) || TryCompare<float>(previous, next, out equal)
                || TryCompare<double>(previous, next, out equal) || TryCompare<long>(previous, next, out equal)
                || TryCompare<byte>(previous, next, out equal) || TryCompare<bool>(previous, next, out equal)
                || TryCompare<char>(previous, next, out equal);
        }

        private static bool TryCompare<TItem>(object previous, object next, out bool equal) where TItem : unmanaged
        {
            if (previous is IList<TItem> held && next is IList<TItem> arrived)
            {
                equal = SameItems(held, arrived);
                // MUTANT_SURVIVES(equivalent, literal): the general path reaches the same instance, boxing each element.
                return true;
            }

            // MUTANT_SURVIVES(equivalent, literal): the value is read only where the method returned true.
            equal = false;
            return false;
        }

        private static bool SameItems<TItem>(IList<TItem> held, IList<TItem> arrived)
        {
            if (held.Count != arrived.Count) return false;
            for (var i = 0; i < held.Count; i++)
            {
                if (!EqualityComparer<TItem>.Default.Equals(held[i], arrived[i])) return false;
            }
            return true;
        }

        private static object ReplaceArray(Array previous, Array next, int depth)
        {
            if (TryCompareUnmanaged(previous, next, out var same)) return same ? previous : next;
            switch (ShareItems(previous, next, depth, out var items))
            {
                case Outcome.Previous:
                    return previous;
                case Outcome.Next:
                    return next;
                default:
                    var copy = Array.CreateInstance(next.GetType().GetElementType()!, items.Length);
                    for (var i = 0; i < items.Length; i++)
                    {
                        copy.SetValue(items[i], i);
                    }
                    return copy;
            }
        }

        private static object ReplaceList(IList previous, IList next, int depth)
        {
            if (TryCompareUnmanaged(previous, next, out var same)) return same ? previous : next;
            switch (ShareItems(previous, next, depth, out var items))
            {
                case Outcome.Previous:
                    return previous;
                case Outcome.Next:
                    return next;
                default:
                    var copy = (IList)Activator.CreateInstance(next.GetType(), items.Length)!;
                    foreach (var item in items)
                    {
                        copy.Add(item);
                    }
                    return copy;
            }
        }

        private static Outcome ShareItems(IList previous, IList next, int depth, out object?[] items)
        {
            items = new object?[next.Count];
            var kept = 0;
            var replaced = 0;
            for (var i = 0; i < items.Length; i++)
            {
                var held = i < previous.Count ? previous[i] : null;
                var arrived = next[i];
                var item = ReplaceEqualDeep(held, arrived, depth + 1);
                items[i] = item;
                if (i < previous.Count && ReferenceEquals(item, held)) kept++;
                if (!ReferenceEquals(item, arrived)) replaced++;
            }

            if (kept == items.Length && previous.Count == items.Length) return Outcome.Previous;
            return replaced == 0 ? Outcome.Next : Outcome.Mixed;
        }

        private static object ReplaceDictionary(IDictionary previous, IDictionary next, int depth)
        {
            var keys = new object[next.Count];
            var items = new object?[next.Count];
            var kept = 0;
            var replaced = 0;
            var index = 0;
            var entries = next.GetEnumerator();
            while (entries.MoveNext())
            {
                var arrived = entries.Value;
                var has = previous.Contains(entries.Key);
                var held = has ? previous[entries.Key] : null;
                var item = ReplaceEqualDeep(held, arrived, depth + 1);
                keys[index] = entries.Key;
                items[index++] = item;
                if (has && ReferenceEquals(item, held)) kept++;
                if (!ReferenceEquals(item, arrived)) replaced++;
            }

            if (kept == keys.Length && previous.Count == keys.Length) return previous;
            return replaced == 0 ? next : CopyDictionary(next, keys, items);
        }

        // The copy is made the way the dictionary that arrived was, comparer included.
        private static object CopyDictionary(IDictionary next, object[] keys, object?[] items)
        {
            var comparer = next.GetType().GetProperty(nameof(Dictionary<object, object>.Comparer))!.GetValue(next);
            var copy = (IDictionary)Activator.CreateInstance(next.GetType(), keys.Length, comparer)!;
            for (var i = 0; i < keys.Length; i++)
            {
                copy.Add(keys[i], items[i]);
            }
            return copy;
        }
    }
}
