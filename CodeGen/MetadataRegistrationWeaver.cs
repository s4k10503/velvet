using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Velvet.CodeGen
{
    // Build-time transform that injects Velvet.ComponentMethodRegistry registration calls into the
    // module initializer (<Module>.cctor) for every [Component] method that opts into
    // Error Boundary, the props-bail, or a DisplayName override. This replaces the Roslyn source
    // generator's [ModuleInitializer] hook: the epic decision is that everything expressible in IL moves
    // to the ILPostProcessor, leaving the Source Generator to API generation and analyzers only.
    // The registry is a process-global, IL2CPP / metadata-stripping-resilient string map keyed on
    // (DeclaringType.FullName, MethodName). The declaring type name is emitted in the runtime
    // Type.FullName form — '+' between an outer type and a nested one, and the Cecil `{arity}
    // suffix for generic types — so the key matches the MethodInfo.DeclaringType.FullName the runtime
    // lookup uses. A <Module> static constructor is created when absent; otherwise the calls are
    // appended before its existing ret. The module initializer is standard CLI, so IL2CPP runs it exactly
    // as it ran the source-generator-emitted one.
    internal static class MetadataRegistrationWeaver
    {
        private const string ComponentAttrFullName = "Velvet.ComponentAttribute";
        private const string RegistryTypeFullName = "Velvet.ComponentMethodRegistry";
        private const string ModuleTypeName = "<Module>";
        private const string IsErrorBoundaryProperty = "IsErrorBoundary";
        private const string MemoizeProperty = "Memoize";
        private const string DisplayNameProperty = "DisplayName";

        public static bool Weave(ModuleDefinition module, List<DiagnosticMessage> diagnostics)
        {
            _ = diagnostics;

            var entries = CollectEntries(module);
            if (entries.Count == 0)
            {
                return false;
            }

            var context = RegistryContext.TryResolve(module);
            if (context == null)
            {
                return false;
            }

            // Order by (type, method) so the injected IL is stable regardless of the GetTypes() enumeration order,
            // matching the deterministic output the source generator produced.
            entries.Sort(static (a, b) =>
            {
                var byType = string.CompareOrdinal(a.TypeFullName, b.TypeFullName);
                return byType != 0 ? byType : string.CompareOrdinal(a.MethodName, b.MethodName);
            });

            var cctor = GetOrCreateModuleInitializer(module);
            if (cctor == null)
            {
                return false;
            }
            var il = cctor.Body.GetILProcessor();
            var ret = FindLastReturn(cctor.Body);
            if (ret == null)
            {
                // A module initializer always ends in a reachable ret; a body without one is malformed IL the
                // weaver will not try to repair. Leave the assembly untouched rather than emit invalid IL.
                return false;
            }

            foreach (var entry in entries)
            {
                if (entry.IsErrorBoundary)
                {
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, entry.TypeFullName));
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, entry.MethodName));
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Call, context.RegisterErrorBoundary));
                }
                if (entry.Memoize)
                {
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, entry.TypeFullName));
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, entry.MethodName));
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Call, context.RegisterMemoize));
                }
                if (entry.DisplayName != null)
                {
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, entry.TypeFullName));
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, entry.MethodName));
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Ldstr, entry.DisplayName));
                    il.InsertBefore(ret, Instruction.Create(OpCodes.Call, context.RegisterDisplayName));
                }
            }

            return true;
        }

        private static List<MetadataEntry> CollectEntries(ModuleDefinition module)
        {
            var entries = new List<MetadataEntry>();
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!TryReadComponentAttribute(method, out var isErrorBoundary, out var memoize, out var displayName))
                    {
                        continue;
                    }
                    if (!isErrorBoundary && !memoize && displayName == null)
                    {
                        continue;
                    }
                    entries.Add(new MetadataEntry(
                        RuntimeFullName(method.DeclaringType),
                        method.Name,
                        isErrorBoundary,
                        memoize,
                        displayName));
                }
            }
            return entries;
        }

        // Reads the [Component] named arguments into the three metadata flags. Returns false when the
        // method carries no [Component] attribute. IsErrorBoundary / Memoize count only when
        // explicitly true; DisplayName counts only when a non-empty string.
        private static bool TryReadComponentAttribute(MethodDefinition method, out bool isErrorBoundary, out bool memoize, out string? displayName)
        {
            isErrorBoundary = false;
            memoize = false;
            displayName = null;

            CustomAttribute? componentAttr = null;
            foreach (var attr in method.CustomAttributes)
            {
                if (attr.AttributeType.FullName == ComponentAttrFullName)
                {
                    componentAttr = attr;
                    break;
                }
            }
            if (componentAttr == null)
            {
                return false;
            }

            foreach (var named in componentAttr.Properties)
            {
                switch (named.Name)
                {
                    case IsErrorBoundaryProperty when named.Argument.Value is bool b && b:
                        isErrorBoundary = true;
                        break;
                    case MemoizeProperty when named.Argument.Value is bool m && m:
                        memoize = true;
                        break;
                    case DisplayNameProperty when named.Argument.Value is string s && !string.IsNullOrEmpty(s):
                        displayName = s;
                        break;
                }
            }
            return true;
        }

        // Formats a Cecil declaring type into the runtime Type.FullName form. Cecil separates a nested
        // type from its outer type with '/', while reflection uses '+'; the generic `{arity}
        // suffix is already identical. Converting the separator makes the registry key match the
        // MethodInfo.DeclaringType.FullName the runtime lookup compares against.
        private static string RuntimeFullName(TypeReference type)
            => type.FullName.Replace('/', '+');

        // Returns the last ret in body, or null when none exists. Registration calls
        // are inserted before it so a pre-existing module-initializer body keeps its own ending.
        private static Instruction? FindLastReturn(MethodBody body)
        {
            for (var i = body.Instructions.Count - 1; i >= 0; i--)
            {
                if (body.Instructions[i].OpCode == OpCodes.Ret)
                {
                    return body.Instructions[i];
                }
            }
            return null;
        }

        // Returns the module's <Module> static constructor, creating an empty one (a single
        // ret) when the assembly has none. Registration calls are inserted before that ret, so any
        // pre-existing module-initializer body is preserved. Returns null only when the <Module>
        // type is absent, which a valid assembly never is.
        private static MethodDefinition? GetOrCreateModuleInitializer(ModuleDefinition module)
        {
            var moduleType = module.GetType(ModuleTypeName);
            if (moduleType == null)
            {
                return null;
            }
            var cctor = moduleType.GetStaticConstructor();
            if (cctor != null)
            {
                return cctor;
            }

            cctor = new MethodDefinition(
                ".cctor",
                MethodAttributes.Private
                    | MethodAttributes.HideBySig
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            cctor.Body = new MethodBody(cctor);
            cctor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            moduleType.Methods.Add(cctor);
            return cctor;
        }

        private readonly struct MetadataEntry
        {
            public MetadataEntry(string typeFullName, string methodName, bool isErrorBoundary, bool memoize, string? displayName)
            {
                TypeFullName = typeFullName;
                MethodName = methodName;
                IsErrorBoundary = isErrorBoundary;
                Memoize = memoize;
                DisplayName = displayName;
            }

            public string TypeFullName { get; }
            public string MethodName { get; }
            public bool IsErrorBoundary { get; }
            public bool Memoize { get; }
            public string? DisplayName { get; }
        }
    }

    internal sealed class RegistryContext
    {
        public required MethodReference RegisterErrorBoundary { get; init; }
        public required MethodReference RegisterMemoize { get; init; }
        public required MethodReference RegisterDisplayName { get; init; }

        public static RegistryContext? TryResolve(ModuleDefinition module)
        {
            var registryType = module.GetType(RegistryTypeFullName)
                ?? ResolveExternal(module, RegistryTypeFullName);
            if (registryType == null) return null;

            var registerErrorBoundary = ResolveRegisterMethod(registryType, "RegisterErrorBoundary", 2);
            var registerMemoize = ResolveRegisterMethod(registryType, "RegisterMemoize", 2);
            var registerDisplayName = ResolveRegisterMethod(registryType, "RegisterComponentDisplayName", 3);
            if (registerErrorBoundary == null || registerMemoize == null || registerDisplayName == null)
            {
                return null;
            }

            return new RegistryContext
            {
                RegisterErrorBoundary = module.ImportReference(registerErrorBoundary),
                RegisterMemoize = module.ImportReference(registerMemoize),
                RegisterDisplayName = module.ImportReference(registerDisplayName),
            };
        }

        private const string RegistryTypeFullName = "Velvet.ComponentMethodRegistry";

        private static TypeDefinition? ResolveExternal(ModuleDefinition module, string fullName)
        {
            foreach (var asmRef in module.AssemblyReferences)
            {
                var asm = module.AssemblyResolver.Resolve(asmRef);
                if (asm == null) continue;
                var t = asm.MainModule.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static MethodDefinition? ResolveRegisterMethod(TypeDefinition registry, string name, int parameterCount)
        {
            foreach (var m in registry.Methods)
            {
                if (m.Name == name && m.IsStatic && m.Parameters.Count == parameterCount) return m;
            }
            return null;
        }
    }
}
