using System;
using System.Collections.Generic;
using System.Reflection;
#if !ENABLE_IL2CPP
using System.Linq.Expressions;
#endif

namespace Velvet
{
    // Props-bail predicate: shallow per-member comparison of two props values, each member decided by the
    // per-type rule ComponentAttribute.Memoize states. Velvet props are record types, and a record's
    // synthesized Equals compares a nested record CLASS member by content, so it would call a fresh
    // instance of equal content unchanged where the reconciler counts a fresh instance as a change — a
    // different memoization axis, and the wrong key here.
    // The member set (public instance properties + fields) is reflected once per props type and
    // cached. Equality protocol: same reference is equal; null vs non-null is not equal; differing
    // runtime types are not equal; otherwise every member is compared under that rule.
    //
    // This predicate runs on every parent-driven re-render attempt of a Memoize=true component, so
    // per-call PropertyInfo/FieldInfo.GetValue reflection — and the boxing it does for each
    // value-type member on both sides of the comparison — is a hot-path cost that eats into the
    // savings Memoize exists to provide. Where the runtime can JIT, member reads and the per-member
    // ObjectIs.AreEqual<T> call are compiled once per props type into a cached delegate via
    // System.Linq.Expressions, so the generated IL passes typed values straight through instead of
    // boxing them to object.
    // Expression.Compile() emits and JITs IL at compile time; the IL2CPP AOT backend has no JIT to
    // target, so a compiled expression there either runs through a slow interpreter or fails
    // outright — neither is acceptable for a hot-path predicate. IL2CPP players therefore use the
    // reflection implementation unconditionally.
    // Not thread-safe — the Velvet Reconciler is main-thread only.
    internal static class ComponentPropsComparer
    {
        private static readonly Dictionary<Type, MemberInfo[]> s_memberCache = new();

#if !ENABLE_IL2CPP
        private static readonly Dictionary<Type, Func<object, object, bool>> s_compiledComparerCache = new();

        private static readonly MethodInfo s_areEqualOpenMethod =
            typeof(ObjectIs).GetMethod(nameof(ObjectIs.AreEqual), BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo s_areEqualObjectsMethod =
            typeof(ObjectIs).GetMethod(nameof(ObjectIs.AreEqualObjects), BindingFlags.Public | BindingFlags.Static)!;
#endif

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_memberCache.Clear();
#if !ENABLE_IL2CPP
            s_compiledComparerCache.Clear();
#endif
        }
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

            // Load-bearing, not a shortcut. Removing it makes ShallowEquals("ab", "cd") answer true —
            // measured on the compiled arm and on the reflection arm alike — so a memoized component
            // taking a bare string prop would bail on a real change. ComponentPropsComparerTests pins
            // the string and the primitive.
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
            {
                return ObjectIs.AreEqualObjects(prev, next);
            }

#if ENABLE_IL2CPP
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
#else
            return GetCompiledComparer(type)(prev, next);
#endif
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

#if ENABLE_IL2CPP
        private static object? ReadMember(MemberInfo member, object instance) => member switch
        {
            PropertyInfo p => p.GetValue(instance),
            FieldInfo f => f.GetValue(instance),
            _ => null,
        };
#else
        private static Func<object, object, bool> GetCompiledComparer(Type type)
        {
            if (s_compiledComparerCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var comparer = BuildComparer(type, GetMembers(type));
            s_compiledComparerCache[type] = comparer;
            return comparer;
        }

        // Builds a delegate equivalent to the AOT reflection path: read each cached member
        // from both sides and AND together per-member equality expressions (see BuildMemberEquality),
        // short-circuiting on the first mismatch. Closing a generic comparison over the member's
        // DECLARED type (via MakeGenericMethod) rather than always going through the boxed
        // AreEqualObjects overload lets the compiled IL pass typed member values straight to the
        // comparison instead of boxing each one into an object first — BuildMemberEquality is what
        // keeps that shortcut from diverging from the reflection path's runtime-type dispatch.
        private static Func<object, object, bool> BuildComparer(Type type, MemberInfo[] members)
        {
            var prevParam = Expression.Parameter(typeof(object), "prev");
            var nextParam = Expression.Parameter(typeof(object), "next");
            var prevTyped = Expression.Convert(prevParam, type);
            var nextTyped = Expression.Convert(nextParam, type);

            Expression body = Expression.Constant(true);
            for (var i = 0; i < members.Length; i++)
            {
                var member = members[i];
                var memberType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
                var prevAccess = Expression.MakeMemberAccess(prevTyped, member);
                var nextAccess = Expression.MakeMemberAccess(nextTyped, member);
                var equalityExpression = BuildMemberEquality(memberType, prevAccess, nextAccess);
                body = i == 0 ? equalityExpression : Expression.AndAlso(body, equalityExpression);
            }

            var lambda = Expression.Lambda<Func<object, object, bool>>(body, prevParam, nextParam);
            return lambda.Compile();
        }

        // Routes each member to the expression shape that keeps the compiled path's outcome identical
        // to the reflection path, which always calls AreEqualObjects on the boxed RUNTIME type:
        // - A member declared as object, an interface, System.ValueType, or System.Enum can hold a
        //   boxed value type or a string at runtime. Closing AreEqual<T> over that DECLARED type (T =
        //   object / the interface / ValueType / Enum) would hit AreEqual's non-value-type branch —
        //   ReferenceEquals — instead of unboxing, which is over-strict versus the reflection path
        //   (equal-content boxed floats/ints/strings would compare unequal). Routing to
        //   AreEqualObjects(object, object) matches the reflection path exactly: the member read is
        //   already a reference-typed value (an implicit upcast, no extra boxing), and AreEqualObjects
        //   re-derives the runtime type itself the same way the reflection path's GetType() does.
        // - A Nullable<U> member is lifted (HasValue compared first, AreEqual<U> on the unwrapped
        //   values only when both have one) rather than closing AreEqual<Nullable<U>> directly, because
        //   AreEqual<T> falls back to EqualityComparer<T>.Default for any T it does not special-case,
        //   and EqualityComparer<Nullable<U>>.Default treats +0f and -0f as equal for U = float/double —
        //   violating Object.is. The CLR unwraps a boxed nullable to a plain U before the reflection
        //   path's AreEqualObjects ever sees it, so closing AreEqual<U> over the underlying type here
        //   mirrors that unwrap and keeps the two paths agreeing.
        private static Expression BuildMemberEquality(Type memberType, Expression prevAccess, Expression nextAccess)
        {
            if (memberType == typeof(object) || memberType.IsInterface
                || memberType == typeof(System.ValueType) || memberType == typeof(System.Enum))
            {
                return Expression.Call(s_areEqualObjectsMethod, prevAccess, nextAccess);
            }

            if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var underlyingType = Nullable.GetUnderlyingType(memberType)!;
                var hasValueProperty = memberType.GetProperty(nameof(Nullable<int>.HasValue))!;
                var valueProperty = memberType.GetProperty(nameof(Nullable<int>.Value))!;
                var prevHasValue = Expression.Property(prevAccess, hasValueProperty);
                var nextHasValue = Expression.Property(nextAccess, hasValueProperty);
                var valuesEqual = Expression.Call(
                    s_areEqualOpenMethod.MakeGenericMethod(underlyingType),
                    Expression.Property(prevAccess, valueProperty),
                    Expression.Property(nextAccess, valueProperty));
                return Expression.AndAlso(
                    Expression.Equal(prevHasValue, nextHasValue),
                    Expression.OrElse(Expression.Not(prevHasValue), valuesEqual));
            }

            return Expression.Call(s_areEqualOpenMethod.MakeGenericMethod(memberType), prevAccess, nextAccess);
        }
#endif
    }
}
