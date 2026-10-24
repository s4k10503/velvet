using System.Collections.Generic;
using Mono.Cecil;

namespace Velvet.CodeGen
{
    internal static partial class CompilerWeaver
    {
        // What a call means to the scope a body is cut into, read off the callee alone.
        internal enum CallKind
        {
            // Reads its arguments and changes none of them.
            Pure,
            // A function of its arguments by contract, which may change what it is handed.
            Plain,
            // A hook whose result is compared as a dependency: its value changes when the component has to render.
            ReactiveHook,
            // A hook returning a stable object whose contents change without a render.
            ContainerHook,
            // A hook whose result a dependency comparison does not represent.
            VolatileHook,
            // A hook that returns nothing.
            EffectHook,
            // Reaches an override the scan cannot see, or a callee it cannot read.
            Opaque,
            // Reads state that is not an argument: a call with nothing to read from, or a framework member
            // outside the pure allow-list.
            Ambient,
        }

        // Portal and WorldSpace mount their children into a host of their own. No scope is cut around such a node or
        // inside it.
        internal static bool MountsIntoHost(MethodReference callee)
            => callee.DeclaringType.FullName == typeof(Velvet.V).FullName
                && (callee.Name == nameof(Velvet.V.Portal) || callee.Name == nameof(Velvet.V.WorldSpace));

        internal static bool IsHookKind(CallKind kind)
            => kind == CallKind.ReactiveHook || kind == CallKind.ContainerHook
                || kind == CallKind.VolatileHook || kind == CallKind.EffectHook;

        private static readonly HashSet<string> ContainerHookNames = new()
        {
            nameof(Velvet.Hooks.UseRef),
            nameof(Velvet.Hooks.UseMutableRef),
            nameof(Velvet.Hooks.UseService),
        };

        // Members of these types are functions of their arguments that change none of them.
        private static readonly HashSet<string> PureFrameworkTypes = new()
        {
            "System.String", "System.Math", "System.MathF", "System.Convert", "System.Object", "System.Enum",
            "System.ValueType", "System.Char", "System.Boolean", "System.Byte", "System.SByte", "System.Int16",
            "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single",
            "System.Double", "System.Decimal", "System.Nullable`1", "System.Linq.Enumerable", "System.Array",
            "UnityEngine.Mathf", "UnityEngine.Color", "UnityEngine.Color32", "UnityEngine.Vector2",
            "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Rect",
        };

        private const string VNodePoolFullName = "Velvet.VNodePool";

        private const string CompilerServicesNamespace = "System.Runtime.CompilerServices.";
        private const string ValueTuplePrefix = "System.ValueTuple";

        // Classifies each distinct callee once for a weave.
        internal sealed class CallClassifier
        {
            private readonly HookReachFold _reach;
            private readonly NonSafeHookFold _nonSafe;
            private readonly Dictionary<string, CallKind> _kinds = new();

            public CallClassifier(HookReachFold reach, NonSafeHookFold nonSafe)
            {
                _reach = reach;
                _nonSafe = nonSafe;
            }

            public CallKind Of(MethodReference callee, bool construction)
            {
                var key = (construction ? "new " : string.Empty) + callee.FullName;
                if (_kinds.TryGetValue(key, out var known)) return known;
                return _kinds[key] = Compute(callee, construction);
            }

            private CallKind Compute(MethodReference callee, bool construction)
            {
                var hook = HookKindOf(callee);
                if (hook != null) return hook.Value;
                if ((_reach.ReachOf(callee) & HookReach.Opaque) != 0) return CallKind.Opaque;
                return construction ? OfConstruction(callee) : OfPlainCall(callee);
            }

            private CallKind? HookKindOf(MethodReference callee)
            {
                if (IsDirectHookCall(callee)) return DirectHookKind(callee);
                if ((_reach.ReachOf(callee) & HookReach.Hook) == 0) return null;
                var composesNonSafe = _nonSafe.Of(callee) != 0 || (_reach.ReachOf(callee) & HookReach.Opaque) != 0;
                return composesNonSafe ? CallKind.VolatileHook : CallKind.ReactiveHook;
            }

            private static CallKind DirectHookKind(MethodReference callee)
            {
                if (IsDirectVoidSafeHookCall(callee)) return CallKind.EffectHook;
                if (ContainerHookNames.Contains(callee.Name)) return CallKind.ContainerHook;
                return IsDirectNonSafeHookCall(callee) ? CallKind.VolatileHook : CallKind.ReactiveHook;
            }

            private static CallKind OfConstruction(MethodReference constructor)
            {
                var declaring = constructor.DeclaringType;
                if (declaring.IsValueType || IsFrameworkType(declaring.FullName) || IsDelegateType(declaring))
                {
                    return CallKind.Pure;
                }
                return CallKind.Plain;
            }

            private static CallKind OfPlainCall(MethodReference callee)
            {
                var declaring = callee.DeclaringType.FullName;
                if (declaring == typeof(Velvet.V).FullName) return MountsIntoHost(callee) ? CallKind.Ambient : CallKind.Pure;
                if (declaring == VNodePoolFullName) return CallKind.Ambient;
                if (callee.HasThis && callee.Name.StartsWith("get_", System.StringComparison.Ordinal)) return CallKind.Pure;
                if (callee.Name == "Invoke" && IsDelegateType(callee.DeclaringType)) return CallKind.Plain;
                if (IsFrameworkType(declaring) && IsPureFramework(declaring)) return IsVoidMutator(callee) ? CallKind.Plain : CallKind.Pure;
                if (IsFrameworkType(declaring)) return CallKind.Ambient;
                return !callee.HasThis && callee.Parameters.Count == 0 ? CallKind.Ambient : CallKind.Plain;
            }

            // A void member of an otherwise pure type has no effect but the one on what it was handed.
            private static bool IsVoidMutator(MethodReference callee) => callee.ReturnType.FullName == "System.Void";

            private static bool IsFrameworkType(string fullName)
                => fullName.StartsWith("System.", System.StringComparison.Ordinal)
                    || fullName.StartsWith("Unity", System.StringComparison.Ordinal)
                    || fullName.StartsWith("Mono.", System.StringComparison.Ordinal);

            private static bool IsPureFramework(string fullName)
                => PureFrameworkTypes.Contains(fullName)
                    || fullName.StartsWith(CompilerServicesNamespace, System.StringComparison.Ordinal)
                    || fullName.StartsWith(ValueTuplePrefix, System.StringComparison.Ordinal);

            private static bool IsDelegateType(TypeReference type)
            {
                var name = type.FullName;
                return name.StartsWith("System.Action", System.StringComparison.Ordinal)
                    || name.StartsWith("System.Func", System.StringComparison.Ordinal);
            }
        }
    }
}
