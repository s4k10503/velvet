using System.Collections.Generic;

namespace Velvet
{
    // Object.is predicate behind Provider change detection, the UseState / Store bail and hook
    // dependency comparison. float and double compare by raw bit pattern, so NaN equals itself and
    // +0 does not equal -0; string by ordinal content, so a freshly built but content-equal one
    // bails; any other value type by its own equality, because boxing hands it a fresh identity at
    // every call boundary; any other reference type by instance.
    internal static class ObjectIs
    {
        public static bool AreEqual<T>(T a, T b)
        {
            if (typeof(T) == typeof(float))
            {
                var fa = (float)(object)a!;
                var fb = (float)(object)b!;
                return System.BitConverter.SingleToInt32Bits(fa)
                    == System.BitConverter.SingleToInt32Bits(fb);
            }

            if (typeof(T) == typeof(double))
            {
                var da = (double)(object)a!;
                var db = (double)(object)b!;
                return System.BitConverter.DoubleToInt64Bits(da)
                    == System.BitConverter.DoubleToInt64Bits(db);
            }

            if (typeof(T) == typeof(string))
            {
                return string.Equals((string?)(object?)a, (string?)(object?)b, System.StringComparison.Ordinal);
            }

            // Route by IsValueType, not by a null-test: Nullable<T> satisfies a lifted
            // `default(T) == null` (boxing an empty nullable yields a true null reference), which
            // would send it to the reference branch — where boxing each operand afresh makes equal
            // values never compare equal. Nullable<T> must fall through to the value comparison.
            if (!typeof(T).IsValueType)
            {
                return ReferenceEquals(a, b);
            }

            return EqualityComparer<T>.Default.Equals(a, b);
        }

        // Boxed-operand variant, for a comparison whose static type is erased to object by the time it
        // gets here — a props type's reflected members, a dependency-array element, a sequence's
        // elements. Same branches, selected from the operands' runtime type rather than from T, and
        // reached through the boxed object.Equals so no per-call comparer delegate is allocated.
        // Do not reroute this through AreEqual<T>: a value type erased to object lands on the
        // reference branch there and on the value branch here. ObjectIsTests pins that pair.
        public static bool AreEqualObjects(object? a, object? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null)
            {
                return false;
            }

            var type = a.GetType();
            if (type != b.GetType())
            {
                return false;
            }

            if (type == typeof(float))
            {
                return System.BitConverter.SingleToInt32Bits((float)a)
                    == System.BitConverter.SingleToInt32Bits((float)b);
            }

            if (type == typeof(double))
            {
                return System.BitConverter.DoubleToInt64Bits((double)a)
                    == System.BitConverter.DoubleToInt64Bits((double)b);
            }

            if (type == typeof(string))
            {
                return string.Equals((string)a, (string)b, System.StringComparison.Ordinal);
            }

            if (type.IsValueType)
            {
                return a.Equals(b);
            }

            // No recursion into the operands' contents: two distinct instances are unequal however
            // their members compare.
            return false;
        }

        public static bool AreEqualDeps(object?[]? a, object?[]? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null)
            {
                return false;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (!AreEqualObjects(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // IEqualityComparer<T> wrapper around ObjectIs.AreEqual<T> for the two APIs that take an
    // explicit comparer, Hooks.UseStore and Store.Select. The reused static instance avoids
    // per-call allocation.
    internal sealed class ObjectIsEqualityComparer<T> : IEqualityComparer<T>
    {
        public static readonly ObjectIsEqualityComparer<T> Instance = new();
        public bool Equals(T x, T y) => ObjectIs.AreEqual(x, y);
        public int GetHashCode(T obj) => obj is null ? 0 : obj.GetHashCode();
    }
}
