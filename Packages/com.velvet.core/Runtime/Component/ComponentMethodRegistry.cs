using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace Velvet
{
    /// <summary>
    /// Process-global registry of methods annotated with <c>[Component(IsErrorBoundary = true)]</c>
    /// and <c>[Component(DisplayName = "...")]</c>. Populated at startup by registration calls
    /// <c>MetadataRegistrationWeaver</c> injects into the assembly's module initializer at build time;
    /// consumed by <c>V.Component</c> on every render and by hook-rule violation paths.
    /// </summary>
    /// <remarks>
    /// The registry keys on <c>(declaringTypeFullName, methodName)</c>. Component's contract is
    /// "1 method = 1 attribute, no overloads," so a type/method-name pair is a unique identity without
    /// parameter signatures. Reflection on <see cref="ComponentAttribute"/> is intentionally avoided
    /// for IL2CPP / metadata-stripping resilience.
    /// </remarks>
    public static class ComponentMethodRegistry
    {
        private static readonly ConcurrentDictionary<(string TypeName, string MethodName), bool> s_errorBoundaries = new();
        private static readonly ConcurrentDictionary<(string TypeName, string MethodName), bool> s_memoized = new();
        private static readonly ConcurrentDictionary<(string TypeName, string MethodName), string> s_displayNames = new();
        private static readonly ConcurrentDictionary<MethodInfo, bool> s_methodCache = new();
        private static readonly ConcurrentDictionary<MethodInfo, bool> s_memoizeCache = new();

        /// <summary>
        /// Registers a component method as an Error Boundary. Invoked from the module-initializer calls
        /// <c>MetadataRegistrationWeaver</c> injects at build time.
        /// Idempotent: re-registering the same method is a no-op.
        /// </summary>
        /// <param name="declaringTypeFullName">Fully qualified type name of the class containing the component method.</param>
        /// <param name="methodName">Name of the static <c>[Component(IsErrorBoundary = true)]</c> method.</param>
        // Hidden from IntelliSense: this entry point is for weaver-injected code, not user code.
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void RegisterErrorBoundary(string declaringTypeFullName, string methodName)
        {
            s_errorBoundaries[(declaringTypeFullName, methodName)] = true;
        }

        /// <summary>
        /// Registers the <c>DisplayName</c> override for a component method. Invoked from the module-initializer
        /// calls <c>MetadataRegistrationWeaver</c> injects, only when the <c>[Component]</c> attribute supplies a
        /// non-empty <c>DisplayName</c>. Idempotent: re-registering the same method overwrites the previous value.
        /// </summary>
        /// <param name="declaringTypeFullName">Fully qualified type name of the class containing the component method.</param>
        /// <param name="methodName">Name of the static <c>[Component(DisplayName = "...")]</c> method.</param>
        /// <param name="displayName">The override name. Must be non-null and non-empty.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void RegisterComponentDisplayName(string declaringTypeFullName, string methodName, string displayName)
        {
            s_displayNames[(declaringTypeFullName, methodName)] = displayName;
        }

        /// <summary>
        /// Registers a component method as props-bail-opted-in (<c>[Component(Memoize = true)]</c>). Invoked
        /// from the module-initializer calls <c>MetadataRegistrationWeaver</c> injects. Idempotent: re-registering
        /// is a no-op. This is the IL2CPP / metadata-stripping-resilient path for <see cref="IsMemoized"/>; when
        /// present it is preferred over the reflection fallback.
        /// </summary>
        /// <param name="declaringTypeFullName">Fully qualified type name of the class containing the component method.</param>
        /// <param name="methodName">Name of the static <c>[Component(Memoize = true)]</c> method.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void RegisterMemoize(string declaringTypeFullName, string methodName)
        {
            s_memoized[(declaringTypeFullName, methodName)] = true;
        }

        /// <summary>
        /// Returns the <c>[Component(DisplayName = ...)]</c> override registered for <paramref name="method"/>,
        /// or <c>null</c> when no override was registered.
        /// </summary>
        internal static string? TryGetDisplayName(MethodInfo method)
        {
            if (method is null) return null;
            return TryLookupByMethod(method, s_displayNames, out var name) ? name : null;
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="method"/> was registered as an Error Boundary via the woven module initializer.
        /// </summary>
        // MethodInfo-keyed cache layered on top of the string-keyed registry: the string form is required
        // because the weaver only has IL-level type metadata to work with, not a runtime MethodInfo, but
        // per-render lookups against MethodInfo identity are O(1) on a RuntimeMethodHandle compare and avoid
        // two ordinal string-equals on every hit.
        internal static bool IsErrorBoundary(MethodInfo? method)
        {
            if (method is null) return false;
            return s_methodCache.GetOrAdd(method, static m =>
                TryLookupByMethod(m, s_errorBoundaries, out var flag) && flag);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="method"/> carries <c>[Component(Memoize = true)]</c>
        /// (props-bail opt-in). Prefers the weaver-populated string registry (<see cref="RegisterMemoize"/>) — the
        /// IL2CPP / metadata-stripping-resilient path, matching how <see cref="IsErrorBoundary"/> resolves —
        /// and falls back to reflecting <see cref="ComponentAttribute"/> off the method when the registry has
        /// no entry (e.g. before the assembly has been rewoven to include Memoize registrations). The result is
        /// cached per <see cref="MethodInfo"/> so the lookup cost is paid once per component method, not per render.
        /// </summary>
        internal static bool IsMemoized(MethodInfo? method)
        {
            if (method is null) return false;
            return s_memoizeCache.GetOrAdd(method, static m =>
            {
                if (TryLookupByMethod(m, s_memoized, out var registered) && registered)
                {
                    return true;
                }
                var attr = m.GetCustomAttribute<ComponentAttribute>(inherit: false);
                return attr is { Memoize: true };
            });
        }

        // Shared key resolution: tries the live `Type.FullName` first, then falls back to the weaver-emitted
        // open form for closed generics where `FullName` either gains a type-arg suffix or returns null.
        private static bool TryLookupByMethod<TValue>(
            MethodInfo method,
            ConcurrentDictionary<(string TypeName, string MethodName), TValue> registry,
            out TValue? value)
        {
            var declaringType = method.DeclaringType;
            if (declaringType is null)
            {
                value = default;
                return false;
            }

            var typeName = declaringType.FullName;
            if (typeName is not null && registry.TryGetValue((typeName, method.Name), out value))
            {
                return true;
            }

            // Closed generic fallback: weaver-emitted keys always use the open form (`Foo`1`, `Outer`1+Inner`).
            // At runtime, `Type.FullName` returns either:
            //   - closed instantiation: `Foo`1[[System.Int32, ...]]` (suffix mismatch)
            //   - any closed generic in declaring chain: `null` (BCL quirk for nested-in-generic)
            // Walk the chain and rebuild the open form so both cases resolve to the registered key.
            if (HasClosedGenericInChain(declaringType))
            {
                var openName = BuildOpenFormFullName(declaringType);
                if (openName is not null && registry.TryGetValue((openName, method.Name), out value))
                {
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool HasClosedGenericInChain(Type t)
        {
            for (var cur = t; cur is not null; cur = cur.DeclaringType)
            {
                if (cur.IsGenericType && !cur.IsGenericTypeDefinition) return true;
            }
            return false;
        }

        // Reconstructs the weaver's open-form FullName by walking DeclaringType: each generic segment uses its
        // open-definition MetadataName (e.g. `Foo`1`), nested types are joined with `+`, namespace from outermost.
        private static string BuildOpenFormFullName(Type t)
        {
            var sb = new StringBuilder();
            AppendOpenForm(t, sb);
            return sb.ToString();
        }

        private static void AppendOpenForm(Type t, StringBuilder sb)
        {
            if (t.DeclaringType is { } outer)
            {
                AppendOpenForm(outer, sb);
                sb.Append('+');
            }
            else if (!string.IsNullOrEmpty(t.Namespace))
            {
                sb.Append(t.Namespace).Append('.');
            }

            // Type.Name on a generic type already includes the `{arity} suffix (e.g. "Foo`1"), so do not append
            // it again. GetGenericTypeDefinition() peels off any closed-instantiation type-arg suffix while
            // preserving the arity portion of the name.
            var open = t.IsGenericType ? t.GetGenericTypeDefinition() : t;
            sb.Append(open.Name);
        }
    }
}
