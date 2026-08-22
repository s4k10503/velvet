using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
#if !ENABLE_IL2CPP
using System.Linq.Expressions;
#endif

namespace Velvet
{
    // Props-bail predicate, React's shallowEqual transposed: the member set decides the props bag and
    // nothing else. A props value — one the bag's walk finds, or one handed in as the props itself — is
    // decided by Object.is.
    // Velvet props are record types, and a record's synthesized Equals compares a nested record CLASS
    // member by content, so it would call a fresh instance of equal content unchanged where the reconciler
    // counts a fresh instance as a change — a different memoization axis, and the wrong key here.
    // The member set (public instance properties + fields) is reflected once per props type and
    // cached. Equality protocol: same reference is equal; null vs non-null is not equal; differing
    // runtime types are not equal; otherwise the bag's members, or the value itself, are compared under
    // that rule.
    //
    // This predicate runs on every parent-driven re-render attempt of a Memoize=true component, so
    // per-call PropertyInfo/FieldInfo.GetValue reflection — and the boxing it does for each
    // value-type member on both sides of the comparison — is a hot-path cost that eats into the
    // savings Memoize exists to provide. Where the runtime can JIT, member reads and the per-member
    // comparison are compiled once per props type into a cached delegate via System.Linq.Expressions,
    // so the generated IL passes typed values straight through instead of boxing them to object.
    // Expression.Compile() emits and JITs IL at compile time; the IL2CPP AOT backend has no JIT to
    // target, so a compiled expression there either runs through a slow interpreter or fails
    // outright — neither is acceptable for a hot-path predicate. IL2CPP players therefore use the
    // reflection implementation unconditionally.
    // Not thread-safe — the Velvet Reconciler is main-thread only.
    internal static class ComponentPropsComparer
    {
        private static readonly Dictionary<Type, MemberInfo[]> s_memberCache = new();
        private static readonly Dictionary<Type, (FieldInfo Field, Type Read)[]> s_leafFieldCache = new();

#if !ENABLE_IL2CPP
        private static readonly Dictionary<Type, Func<object, object, bool>> s_compiledComparerCache = new();

        private static readonly MethodInfo s_areEqualOpenMethod =
            typeof(ObjectIs).GetMethod(nameof(ObjectIs.AreEqual), BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo s_valueEqualsMethod =
            typeof(ComponentPropsComparer).GetMethod(nameof(ValueEquals), BindingFlags.NonPublic | BindingFlags.Static)!;
#endif

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            s_memberCache.Clear();
            s_leafFieldCache.Clear();
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

            if (!IsPropsBag(type))
            {
                return ValueEquals(prev, next);
            }

#if ENABLE_IL2CPP
            return CompareMembersByReflection(type, prev, next);
#else
            return GetCompiledComparer(type)(prev, next);
#endif
        }

        // A props bag built by a render has an identity nothing matches, so its member set is the key.
        // Everything else is a value: a value type carries its own equality, and a collection is decided
        // by its instance because its member set answered equal for two lists of differing content. A
        // bare string takes the value route through this same test, and ComponentPropsComparerTests is
        // what fails if it stops.
        private static bool IsPropsBag(Type type)
            => !type.IsValueType && !typeof(IEnumerable).IsAssignableFrom(type);

        // Object.is on a props value. The member walk decides what it finds with this, and this reads no
        // member set, so a props bag is walked once whatever its members hold.
        // Strengthening the type's equality rather than replacing it with a field walk is what leaves a
        // struct holding a record class decided by that record's content.
        private static bool ValueEquals(object? prev, object? next)
        {
            if (!ObjectIs.AreEqualObjects(prev, next))
            {
                return false;
            }

            if (prev is null)
            {
                return true;
            }

            var type = prev.GetType();
            if (!type.IsValueType)
            {
                return true;
            }

            return FloatLeavesAgree(type, prev, next!);
        }

        private static bool FloatLeavesAgree(Type type, object prev, object next)
        {
            var leaves = LeafFields(type);
            for (var i = 0; i < leaves.Length; i++)
            {
                var prevLeaf = leaves[i].Field.GetValue(prev);
                var nextLeaf = leaves[i].Field.GetValue(next);
                if (prevLeaf is null || nextLeaf is null)
                {
                    continue;
                }

                var agree = leaves[i].Read.IsPrimitive
                    ? ObjectIs.AreEqualObjects(prevLeaf, nextLeaf)
                    : FloatLeavesAgree(leaves[i].Read, prevLeaf, nextLeaf);
                if (!agree)
                {
                    return false;
                }
            }

            return true;
        }

        // The fields under a type that carry floating point, each with the type to read it as. Reflected
        // once per type and cached.
        // A primitive contributes none: its own backing field carries its own type, so a descent into one
        // would not end, and its value is whole in any case. ComponentPropsComparerTests pins the backing
        // field that makes the first half of that true.
        private static (FieldInfo Field, Type Read)[] LeafFields(Type type)
        {
            if (s_leafFieldCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var result = type.IsPrimitive
                ? Array.Empty<(FieldInfo, Type)>()
                : CollectLeafFields(type);
            s_leafFieldCache[type] = result;
            return result;
        }

        private static (FieldInfo Field, Type Read)[] CollectLeafFields(Type type)
        {
            var found = new List<(FieldInfo, Type)>();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                // A Nullable<U> field is read as U, for the reason BuildMemberEquality's Nullable branch gives.
                var read = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                if (read == typeof(float) || read == typeof(double))
                {
                    found.Add((field, read));
                    continue;
                }

                // A reference is decided by its own equality, so the descent stops at one rather than
                // reading a float out of two instances that equality has already called equal.
                if (!read.IsValueType)
                {
                    continue;
                }

                if (LeafFields(read).Length != 0)
                {
                    found.Add((field, read));
                }
            }

            return found.ToArray();
        }

        // The AOT arm's walk, compiled on both arms rather than under the IL2CPP condition, so a case can
        // hold its answer beside the JIT arm's for the same pair.
        private static bool CompareMembersByReflection(Type type, object prev, object next)
        {
            var members = GetMembers(type);
            for (var i = 0; i < members.Length; i++)
            {
                var prevValue = ReadMember(members[i], prev);
                var nextValue = ReadMember(members[i], next);
                if (!ValueEquals(prevValue, nextValue))
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

#if !ENABLE_IL2CPP
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
        // ValueEquals overload lets the compiled IL pass typed member values straight to the
        // comparison instead of boxing them — BuildMemberEquality is what keeps that shortcut from
        // diverging from the reflection path's runtime-type dispatch.
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
        // to the reflection path, which always calls ValueEquals on the boxed RUNTIME type:
        // - A member declared as object, an interface, System.ValueType, or System.Enum can hold a
        //   boxed value type or a string at runtime. Closing AreEqual<T> over that DECLARED type (T =
        //   object / the interface / ValueType / Enum) would hit AreEqual's non-value-type branch —
        //   ReferenceEquals — instead of unboxing, which is over-strict versus the reflection path
        //   (equal-content boxed floats/ints/strings would compare unequal). Routing to
        //   ValueEquals(object, object) matches the reflection path exactly: the member read is
        //   already a reference-typed value (an implicit upcast, no extra boxing), and ValueEquals
        //   re-derives the runtime type itself the same way the reflection path's GetType() does.
        // - A Nullable<U> member is lifted (HasValue compared first, the unwrapped values compared only
        //   when both have one) rather than closing a comparison over Nullable<U> directly, because
        //   AreEqual<T> falls back to EqualityComparer<T>.Default for any T it does not special-case,
        //   and EqualityComparer<Nullable<U>>.Default treats +0f and -0f as equal for U = float/double —
        //   violating Object.is. The CLR unwraps a boxed nullable to a plain U before the reflection
        //   path's ValueEquals ever sees it, so recursing over the underlying type here mirrors that
        //   unwrap and keeps the two paths agreeing.
        // - A value type carries its own equality plus the bitwise leaf comparison ValueEquals adds on
        //   the other arm, which is empty for one holding no floating point.
        private static Expression BuildMemberEquality(Type memberType, Expression prevAccess, Expression nextAccess)
        {
            if (memberType == typeof(object) || memberType.IsInterface
                || memberType == typeof(System.ValueType) || memberType == typeof(System.Enum))
            {
                return Expression.Call(s_valueEqualsMethod, prevAccess, nextAccess);
            }

            if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var underlyingType = Nullable.GetUnderlyingType(memberType)!;
                var hasValueProperty = memberType.GetProperty(nameof(Nullable<int>.HasValue))!;
                var valueProperty = memberType.GetProperty(nameof(Nullable<int>.Value))!;
                var prevHasValue = Expression.Property(prevAccess, hasValueProperty);
                var nextHasValue = Expression.Property(nextAccess, hasValueProperty);
                var valuesEqual = BuildMemberEquality(
                    underlyingType,
                    Expression.Property(prevAccess, valueProperty),
                    Expression.Property(nextAccess, valueProperty));
                return Expression.AndAlso(
                    Expression.Equal(prevHasValue, nextHasValue),
                    Expression.OrElse(Expression.Not(prevHasValue), valuesEqual));
            }

            var ownEquality = Expression.Call(
                s_areEqualOpenMethod.MakeGenericMethod(memberType), prevAccess, nextAccess);
            if (!memberType.IsValueType)
            {
                return ownEquality;
            }

            return Expression.AndAlso(ownEquality, BuildFloatLeaves(memberType, prevAccess, nextAccess));
        }

        // FloatLeavesAgree compiled: the same leaves, reached by typed field access so the JIT arm reads
        // them without boxing, and passed over on the same terms — a Nullable leaf is read only where
        // both sides carry a value, which is what keeps Value off an empty one.
        private static Expression BuildFloatLeaves(Type type, Expression prevAccess, Expression nextAccess)
        {
            Expression body = Expression.Constant(true);
            var leaves = LeafFields(type);
            for (var i = 0; i < leaves.Length; i++)
            {
                var prevField = Expression.Field(prevAccess, leaves[i].Field);
                var nextField = Expression.Field(nextAccess, leaves[i].Field);
                Expression leaf;
                if (leaves[i].Field.FieldType == leaves[i].Read)
                {
                    leaf = BuildLeafEquality(leaves[i].Read, prevField, nextField);
                }
                else
                {
                    var nullableType = leaves[i].Field.FieldType;
                    var hasValueProperty = nullableType.GetProperty(nameof(Nullable<int>.HasValue))!;
                    var valueProperty = nullableType.GetProperty(nameof(Nullable<int>.Value))!;
                    var bothCarryValue = Expression.AndAlso(
                        Expression.Property(prevField, hasValueProperty),
                        Expression.Property(nextField, hasValueProperty));
                    leaf = Expression.OrElse(
                        Expression.Not(bothCarryValue),
                        BuildLeafEquality(
                            leaves[i].Read,
                            Expression.Property(prevField, valueProperty),
                            Expression.Property(nextField, valueProperty)));
                }

                body = Expression.AndAlso(body, leaf);
            }

            return body;
        }

        private static Expression BuildLeafEquality(Type readAs, Expression prevLeaf, Expression nextLeaf)
            => readAs.IsPrimitive
                ? Expression.Call(s_areEqualOpenMethod.MakeGenericMethod(readAs), prevLeaf, nextLeaf)
                : BuildFloatLeaves(readAs, prevLeaf, nextLeaf);
#endif
    }
}
