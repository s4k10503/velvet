#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

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
            // A class or record declared by the application, shared member by member through its instance fields.
            Object,
            // Any other class: kept only as the very instance already held.
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
        private static readonly ConcurrentDictionary<Type, FieldInfo[]> s_fields = new();
        private static readonly Func<Type, FieldInfo[]> s_declaredFields = DeclaredFields;

        // The namespaces whose classes are not the application's: a collection, a task, an engine object. Their
        // fields are the framework's to keep consistent, so they are kept whole or replaced whole.
        private static readonly string[] s_foreignNamespaces = { "System", "Microsoft", "UnityEngine", "UnityEditor", "Unity" };

        internal static T Replace<T>(T? previous, T next) => (T)ReplaceEqualDeep(previous, next, 0, null)!;

        private static object? ReplaceEqualDeep(object? previous, object? next, int depth, List<object>? path)
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
                    return ReplaceArray((Array)previous, (Array)next, depth, path);
                case Shape.List:
                    return ReplaceList((IList)previous, (IList)next, depth, path);
                case Shape.Dictionary:
                    return ReplaceDictionary((IDictionary)previous, (IDictionary)next, depth, path);
                case Shape.Object:
                    return ReplaceObject(previous, next, depth, path);
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

            if (type.IsValueType || type == typeof(string)) return Shape.Leaf;
            return IsApplicationClass(type) ? Shape.Object : Shape.Reference;
        }

        private static bool IsApplicationClass(Type type)
        {
            if (typeof(Delegate).IsAssignableFrom(type) || typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return false;
            }

            var ns = type.Namespace;
            if (ns == null) return true;
            foreach (var foreign in s_foreignNamespaces)
            {
                if (ns == foreign || ns.StartsWith(foreign + ".", StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static FieldInfo[] DeclaredFields(Type type)
        {
            var fields = new List<FieldInfo>();
            for (var level = type; level != null && level != typeof(object); level = level.BaseType)
            {
                fields.AddRange(level.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            }
            return fields.ToArray();
        }

        // The held instance when every field is shared, otherwise a copy of the arrived one with the shared fields
        // written over it. A class cannot be rebuilt through a constructor, so the copy is a field-by-field copy made without running a constructor. An
        // instance already being compared further up the walk is a cycle and is not entered again.
        private static object ReplaceObject(object previous, object next, int depth, List<object>? path)
        {
            path ??= new List<object>();
            foreach (var active in path)
            {
                if (ReferenceEquals(active, next)) return next;
            }

            var fields = s_fields.GetOrAdd(next.GetType(), s_declaredFields);
            var items = new object?[fields.Length];
            var shared = new bool[fields.Length];
            var kept = 0;
            var replaced = 0;
            path.Add(next);
            try
            {
                for (var i = 0; i < fields.Length; i++)
                {
                    var held = fields[i].GetValue(previous);
                    var arrived = fields[i].GetValue(next);
                    var item = ReplaceEqualDeep(held, arrived, depth + 1, path);
                    items[i] = item;
                    if (ReferenceEquals(item, held)) kept++;
                    // A value or a string carries nothing to share, so only another reference taken from the held instance counts.
                    shared[i] = item != null && !ReferenceEquals(item, arrived) && item is not string && !item.GetType().IsValueType;
                    if (shared[i]) replaced++;
                }
            }
            finally
            {
                path.RemoveAt(path.Count - 1);
            }

            if (kept == fields.Length) return previous;
            if (replaced == 0) return next;
            var copy = RuntimeHelpers.GetUninitializedObject(next.GetType());
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].SetValue(copy, shared[i] ? items[i] : fields[i].GetValue(next));
            }
            return copy;
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

        private static object ReplaceArray(Array previous, Array next, int depth, List<object>? path)
        {
            if (TryCompareUnmanaged(previous, next, out var same)) return same ? previous : next;
            switch (ShareItems(previous, next, depth, path, out var items))
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

        private static object ReplaceList(IList previous, IList next, int depth, List<object>? path)
        {
            if (TryCompareUnmanaged(previous, next, out var same)) return same ? previous : next;
            switch (ShareItems(previous, next, depth, path, out var items))
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

        private static Outcome ShareItems(IList previous, IList next, int depth, List<object>? path, out object?[] items)
        {
            items = new object?[next.Count];
            var kept = 0;
            var replaced = 0;
            for (var i = 0; i < items.Length; i++)
            {
                var held = i < previous.Count ? previous[i] : null;
                var arrived = next[i];
                var item = ReplaceEqualDeep(held, arrived, depth + 1, path);
                items[i] = item;
                if (i < previous.Count && ReferenceEquals(item, held)) kept++;
                if (!ReferenceEquals(item, arrived)) replaced++;
            }

            if (kept == items.Length && previous.Count == items.Length) return Outcome.Previous;
            return replaced == 0 ? Outcome.Next : Outcome.Mixed;
        }

        private static object ReplaceDictionary(IDictionary previous, IDictionary next, int depth, List<object>? path)
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
                var item = ReplaceEqualDeep(held, arrived, depth + 1, path);
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
