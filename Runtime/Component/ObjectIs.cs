using System.Collections.Generic;

namespace Velvet
{
    // Identity-equality predicate used for Provider value change detection and hook dependency
    // comparison. Reference types compare by reference; value types compare by value, with the
    // special-number handling described below.
    // Reference types are compared by object.ReferenceEquals, EXCEPT
    // string, which compares by value (ordinal) — strings are treated as primitives,
    // so a content-equal but freshly-built string bails. float and double are compared by raw
    // bit pattern so that NaN equals itself and +0 does not equal -0. Other value types
    // fall back to EqualityComparer<T>.Default because their boxed identity is unstable on
    // every call boundary.
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
                // Strings are treated as primitives, so compare by value — otherwise a dynamically-built but
                // content-equal string (interpolation / concat / Format) would never bail and a UseState /
                // UseStore / Provider holding it would re-render every time. This matches the boxed
                // AreEqualObjects path. Handles nulls (string.Equals(null, null) is true).
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

        // Boxed-operand variant of AreEqual<T> for callers that only hold
        // object references (e.g. per-property shallow comparison over a
        // record's reflected members, where the static element type is erased).
        // Reference types compare by object.ReferenceEquals.
        // float / double compare by raw bit pattern (NaN equals itself, +0 does
        // not equal -0). Other value types compare by their boxed object.Equals,
        // which for primitives and record members yields the same result as
        // EqualityComparer<T>.Default without per-call delegate allocation.
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
                // Strings are treated as primitive values: two content-equal strings are equal regardless of
                // instance identity. C# strings are reference types, so compare by value (otherwise a
                // dynamically-built but content-equal string prop would never bail). This matches the
                // generic AreEqual<string> path.
                return string.Equals((string)a, (string)b, System.StringComparison.Ordinal);
            }

            if (type.IsValueType)
            {
                return a.Equals(b);
            }

            // Other reference-type members follow reference-identity semantics: the comparison is
            // shallow per-prop and never recurses into nested object identity.
            return false;
        }

        // Element-wise identity comparison of two dependency arrays. Returns true when both
        // refer to the same instance, and false when either is null or the lengths differ. Each pair
        // of elements is compared by AreEqualObjects, so reference-type elements bail only on
        // reference identity and a fresh-but-equal record instance counts as changed — the same strictness
        // the reconciler and Provider apply when deciding to re-render. This is the comparer the inner
        // component memo keys on, so a cached VNode is reused only when every captured input is identity-equal
        // to the committed one.
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

    // IEqualityComparer<T> wrapper around ObjectIs.AreEqual<T> so the
    // identity-equality semantics can be passed to hook APIs that take an explicit comparer
    // (UseStore / UseDeferredValue). The reused static instance avoids per-call allocation.
    internal sealed class ObjectIsEqualityComparer<T> : IEqualityComparer<T>
    {
        public static readonly ObjectIsEqualityComparer<T> Instance = new();
        public bool Equals(T x, T y) => ObjectIs.AreEqual(x, y);
        public int GetHashCode(T obj) => obj is null ? 0 : obj.GetHashCode();
    }
}
