using System;
using System.Collections.Generic;
using System.Reflection;

namespace Velvet
{
    // Props-bail predicate: shallow per-property comparison of two props values using
    // identity equality (ObjectIs.AreEqualObjects) on each public member.
    // Velvet props are record types whose synthesized object.Equals is
    // deep structural equality. This comparer instead compares props shallowly — each top-level
    // member by identity, never recursing — so a record's value equality is the wrong key:
    // it would over-bail when a nested reference changes content in place and is generally a
    // different memoization axis.
    // The member set (public instance properties + fields) is reflected once per props type and
    // cached. Equality protocol: same reference is equal; null vs non-null is not equal; differing
    // runtime types are not equal; otherwise every member is compared by identity equality.
    // Not thread-safe — the Velvet Reconciler is main-thread only.
    internal static class ComponentPropsComparer
    {
        private static readonly Dictionary<Type, MemberInfo[]> s_memberCache = new();

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => s_memberCache.Clear();
#endif

        public static bool ShallowEquals(object? prev, object? next)
        {
            if (ReferenceEquals(prev, next))
            {
                return true;
            }

            if (prev is null || next is null)
            {
                return false;
            }

            var type = prev.GetType();
            if (type != next.GetType())
            {
                return false;
            }

            // Primitive / string / enum props passed directly (no record wrapper) compare by identity equality.
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
            {
                return ObjectIs.AreEqualObjects(prev, next);
            }

            var members = GetMembers(type);
            for (var i = 0; i < members.Length; i++)
            {
                var prevValue = ReadMember(members[i], prev);
                var nextValue = ReadMember(members[i], next);
                if (!ObjectIs.AreEqualObjects(prevValue, nextValue))
                {
                    return false;
                }
            }

            return true;
        }

        private static MemberInfo[] GetMembers(Type type)
        {
            if (s_memberCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var list = new List<MemberInfo>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var property in type.GetProperties(flags))
            {
                // Indexers and write-only members are not shallow-comparable keys.
                if (property.GetIndexParameters().Length != 0) continue;
                if (!property.CanRead) continue;
                // record types synthesize an EqualityContract property; it is identical for instances
                // of the same type and adds noise, so it is excluded.
                if (property.Name == "EqualityContract") continue;
                list.Add(property);
            }

            foreach (var field in type.GetFields(flags))
            {
                list.Add(field);
            }

            var result = list.ToArray();
            s_memberCache[type] = result;
            return result;
        }

        private static object? ReadMember(MemberInfo member, object instance) => member switch
        {
            PropertyInfo p => p.GetValue(instance),
            FieldInfo f => f.GetValue(instance),
            _ => null,
        };
    }
}
