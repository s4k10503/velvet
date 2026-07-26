using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies three independent, no-render contracts probed through reflection/Cecil against the compile-time
    /// weavers and their canonical inputs (the weavers live in the editor-only <c>Unity.Velvet.CodeGen</c>
    /// assembly and are internal, so this test asmdef cannot reference them directly):
    /// <list type="bullet">
    /// <item>Resolution-failure diagnostics: when a Velvet runtime type the weaver injects calls to cannot be
    /// resolved from the processed module, the weaver surfaces a diagnostic warning naming the assembly instead
    /// of failing silently — for CompilerWeaver when <c>Velvet.Hooks</c> / <c>Velvet.VNode</c> is unresolvable,
    /// and for MetadataRegistrationWeaver when <c>Velvet.ComponentMethodRegistry</c> is unresolvable.</item>
    /// <item>Open-dispatch hook-safety classification: an open virtual / interface dispatch whose declaring type
    /// is outside the BCL/Unity/UniTask carve-out must be classified as unverifiable / non-SAFE regardless of
    /// whether its own declaring assembly references Velvet, because an override reaching a hook can be declared
    /// in a third assembly that does. The two private classifier methods
    /// (<c>ReachesNonSafeHook</c>/<c>CallsHookTransitively</c>) must agree on this classification.</item>
    /// <item>Metadata-registration E2E: the woven <c>&lt;Module&gt;.cctor</c> of THIS test assembly carries the
    /// <c>ComponentMethodRegistry.Register*</c> calls the weaver actually injected for the
    /// <c>[Component(...)]</c>-flagged methods declared below, keyed by the declaring type's runtime
    /// <c>Type.FullName</c> (including the nested-type <c>'+'</c> form and the generic-type backtick-arity
    /// form), and a flagless <c>[Component]</c> emits no registration of any kind.</item>
    /// <item><see cref="PositionalHookNames.All"/> pin: the canonical set of hook names that allocate a
    /// positional slot is exactly the expected names (no more, no fewer, no duplicates), and structurally, every
    /// public <c>Use*</c> hook whose implementation allocates a positional slot is present in that list — the
    /// ILPP weaver reads the list directly, so an omission is silently invisible to it rather than caught here.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class WeaverReflectionProbeTests
    {
        private const string CodeGenAssemblyName = "Unity.Velvet.CodeGen";
        private const string CompilerWeaverTypeFullName = "Velvet.CodeGen.CompilerWeaver";

        [Test]
        public void Given_ModuleWithoutVelvetReference_When_CompilerWeaverRuns_Then_WarnsNamingTheAssembly()
        {
            // Arrange
            using var module = ModuleDefinition.CreateModule("VelvetWeaverProbe", ModuleKind.Dll);
            module.AssemblyReferences.Clear();

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(messages, Has.Some.Contains("VelvetWeaverProbe"),
                "A resolution failure must produce a diagnostic naming the assembly instead of silently"
                + " disabling auto-memoization for it");
        }

        [Test]
        public void Given_ModuleWithComponentMetadataButNoVelvetReference_When_MetadataWeaverRuns_Then_WarnsNamingTheAssembly()
        {
            // Arrange
            using var module = CreateModuleWithMemoizedComponent("VelvetRegistryProbe");

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.MetadataRegistrationWeaver", module);

            // Assert
            Assert.That(messages, Has.Some.Contains("VelvetRegistryProbe"),
                "A resolution failure must produce a diagnostic naming the assembly instead of silently"
                + " dropping the [Component] metadata registrations");
        }

        [Test]
        public void Given_OpenVirtualOutsideCarveOutInNonVelvetReferencingAssembly_When_ReachesNonSafeHookClassifies_Then_TreatsCalleeAsNonSafe()
        {
            // Arrange
            using var module = BuildNonVelvetReferencingModuleWithOpenVirtual(out var handler);
            Assume.That(module.AssemblyReferences.Any(r => r.Name == "Velvet"), Is.False,
                "Precondition: the synthetic module never references Velvet");

            // Act
            var isNonSafe = (bool)InvokeClassifier("ReachesNonSafeHook", handler);

            // Assert
            Assert.That(isNonSafe, Is.True,
                "An open dispatch outside the BCL/Unity/UniTask carve-out is unverifiable regardless of whether"
                + " its declaring assembly references Velvet, because an override composing a hook can live in"
                + " a third assembly that does");
        }

        [Test]
        public void Given_OpenVirtualOutsideCarveOutInNonVelvetReferencingAssembly_When_CallsHookTransitivelyClassifies_Then_TreatsCalleeAsMayReachHook()
        {
            // Arrange
            using var module = BuildNonVelvetReferencingModuleWithOpenVirtual(out var handler);

            // Act
            var mayReachHook = (bool)InvokeClassifier("CallsHookTransitively", handler);

            // Assert
            Assert.That(mayReachHook, Is.True,
                "CallsHookTransitively must classify the same open dispatch identically to ReachesNonSafeHook,"
                + " or the two walkers would disagree about whether the call is a hook call");
        }

        // Synthesizes a module carrying one method with [Component(Memoize = true)] so the metadata weaver
        // has an entry to register, then strips every assembly reference so RegistryContext resolution
        // deterministically fails. The attribute type is scoped to the module itself: the weaver matches the
        // attribute by full name only and never resolves it.
        private static ModuleDefinition CreateModuleWithMemoizedComponent(string name)
        {
            var module = ModuleDefinition.CreateModule(name, ModuleKind.Dll);
            var type = new TypeDefinition("Probe", "Fixture",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class
                | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            var method = new MethodDefinition("Component",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Object);
            var attributeType = new TypeReference("Velvet", "ComponentAttribute", module, module);
            var attributeCtor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType)
            {
                HasThis = true,
            };
            var attribute = new CustomAttribute(attributeCtor);
            attribute.Properties.Add(new Mono.Cecil.CustomAttributeNamedArgument(
                "Memoize", new CustomAttributeArgument(module.TypeSystem.Boolean, true)));
            method.CustomAttributes.Add(attribute);
            type.Methods.Add(method);
            module.Types.Add(type);
            // Strip the references the TypeSystem lazily added (corlib for Object / Boolean / Void) AFTER
            // building the shape, so the weaver's external resolution has nothing to consult and fails
            // without touching an assembly resolver.
            module.AssemblyReferences.Clear();
            return module;
        }

        // Invokes the internal static Weave(ModuleDefinition, List<DiagnosticMessage>) through reflection
        // (the CodeGen assembly is editor-only and not referenced by this test asmdef) and returns the
        // MessageData of every emitted diagnostic.
        private static List<string> InvokeWeave(string weaverTypeFullName, ModuleDefinition module)
        {
            var codeGenAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == CodeGenAssemblyName);
            Assume.That(codeGenAssembly, Is.Not.Null,
                "Precondition: the Unity.Velvet.CodeGen assembly is loaded in the editor domain");
            var weaverType = codeGenAssembly!.GetType(weaverTypeFullName, throwOnError: true);
            var weaveMethod = weaverType!.GetMethod("Weave",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assume.That(weaveMethod, Is.Not.Null,
                "Precondition: the weaver exposes a public static Weave method");

            var diagnostics = Activator.CreateInstance(weaveMethod!.GetParameters()[1].ParameterType)!;
            weaveMethod.Invoke(null, new[] { (object)module, diagnostics });

            var messages = new List<string>();
            foreach (var diagnostic in (IEnumerable)diagnostics)
            {
                var messageData = diagnostic.GetType().GetProperty("MessageData")?.GetValue(diagnostic);
                messages.Add(messageData as string ?? string.Empty);
            }
            return messages;
        }

        // Builds a module with no reference to Velvet, declaring a public, non-sealed class with an
        // overridable (virtual, non-final) method — an open dispatch whose declaring type is outside every
        // BCL/Unity/UniTask namespace root. Returns the MethodDefinition for that method via handler.
        private static ModuleDefinition BuildNonVelvetReferencingModuleWithOpenVirtual(out MethodDefinition handler)
        {
            var module = ModuleDefinition.CreateModule("NonVelvetReferencingProbe", ModuleKind.Dll);
            var baseType = new TypeDefinition("Probe", "Base",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            handler = new MethodDefinition("Handler",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual
                    | Mono.Cecil.MethodAttributes.NewSlot | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            handler.Body = new Mono.Cecil.Cil.MethodBody(handler);
            handler.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            baseType.Methods.Add(handler);
            module.Types.Add(baseType);
            return module;
        }

        // Invokes CompilerWeaver's private static bool <name>(MethodReference, Dictionary<string, bool>)
        // through reflection (the CodeGen assembly is editor-only and not referenced by this test asmdef).
        private static object InvokeClassifier(string methodName, MethodReference callee)
        {
            var codeGenAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == CodeGenAssemblyName);
            Assume.That(codeGenAssembly, Is.Not.Null,
                "Precondition: the Unity.Velvet.CodeGen assembly is loaded in the editor domain");
            var weaverType = codeGenAssembly!.GetType(CompilerWeaverTypeFullName, throwOnError: true);
            var method = weaverType!.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assume.That(method, Is.Not.Null,
                $"Precondition: {CompilerWeaverTypeFullName} exposes a private static {methodName} method");
            var cache = new Dictionary<string, bool>();
            return method!.Invoke(null, new object[] { callee, cache })!;
        }

        // --- CompilerWeaver hook-return consumption shape probes ---
        //
        // TryAnalyze recognizes exactly four ways a hook call's return value can be consumed: discarded via
        // a bare Pop (bail — an uncaptured reactive input cannot be tracked), a direct stloc capture, an
        // Item1-only tuple deconstruction (`var (v, _) = ...`), and a two-element tuple deconstruction
        // (`var (v, setV) = ...`, the Dup shape). These probes hand-assemble one Cecil method body per shape
        // and invoke the real CompilerWeaver.Weave through reflection, then inspect the resulting IL for the
        // injected TryGetMemoizedVNode call — the only observable signature of a successful weave.
        //
        // "Velvet.Hooks" / "Velvet.VNode" and the TryGetMemoizedVNode / StoreMemoizedVNode registration
        // surface are declared directly inside the probe module rather than imported from the real Velvet
        // assembly, so WeaverContext.TryResolve succeeds without any assembly-resolver setup. Hook-safety
        // classification in CompilerWeaver matches purely by name against the real, process-loaded
        // Velvet.PositionalHookNames.All / SAFE allow-lists, so a same-named synthetic Hooks method is
        // classified identically to the real one.

        private static ModuleDefinition BuildHookShapeProbeModule(
            string moduleName,
            out MethodDefinition targetMethod,
            out MethodReference useId,
            out MethodReference useTransition,
            out TypeReference transitionTuple)
        {
            var module = ModuleDefinition.CreateModule(moduleName, ModuleKind.Dll);

            var vnodeType = new TypeDefinition("Velvet", "VNode",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(vnodeType);

            var hooksType = new TypeDefinition("Velvet", "Hooks",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract
                | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(hooksType);

            // A non-generic 2-element ValueTuple return (matching Hooks.UseTransition's real shape) keeps the
            // deconstruction probes free of generic-method-instantiation bookkeeping.
            var valueTupleDef = module.ImportReference(typeof(System.ValueTuple<,>));
            var tuple = new GenericInstanceType(valueTupleDef);
            tuple.GenericArguments.Add(module.TypeSystem.Boolean);
            tuple.GenericArguments.Add(module.TypeSystem.Object);
            transitionTuple = tuple;

            var useIdMethod = new MethodDefinition("UseId",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.String);
            useIdMethod.Parameters.Add(new ParameterDefinition(
                "prefix", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String));
            hooksType.Methods.Add(useIdMethod);
            useId = useIdMethod;

            var useTransitionMethod = new MethodDefinition("UseTransition",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, tuple);
            hooksType.Methods.Add(useTransitionMethod);
            useTransition = useTransitionMethod;

            var objectArrayType = new ArrayType(module.TypeSystem.Object);
            var tryGetMethod = new MethodDefinition("TryGetMemoizedVNode",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Boolean);
            tryGetMethod.Parameters.Add(new ParameterDefinition(
                "deps", Mono.Cecil.ParameterAttributes.None, objectArrayType));
            tryGetMethod.Parameters.Add(new ParameterDefinition(
                "slotIndex", Mono.Cecil.ParameterAttributes.Out, new ByReferenceType(module.TypeSystem.Int32)));
            tryGetMethod.Parameters.Add(new ParameterDefinition(
                "cached", Mono.Cecil.ParameterAttributes.Out, new ByReferenceType(vnodeType)));
            hooksType.Methods.Add(tryGetMethod);

            var storeMethod = new MethodDefinition("StoreMemoizedVNode",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            storeMethod.Parameters.Add(new ParameterDefinition(
                "slotIndex", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
            storeMethod.Parameters.Add(new ParameterDefinition(
                "deps", Mono.Cecil.ParameterAttributes.None, objectArrayType));
            storeMethod.Parameters.Add(new ParameterDefinition(
                "result", Mono.Cecil.ParameterAttributes.None, vnodeType));
            hooksType.Methods.Add(storeMethod);

            var componentType = new TypeDefinition("Probe", "Fixture",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class
                | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
            module.Types.Add(componentType);

            targetMethod = new MethodDefinition("Component",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, vnodeType);
            var attributeType = new TypeReference("Velvet", "ComponentAttribute", module, module);
            var attributeCtor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
            targetMethod.CustomAttributes.Add(new CustomAttribute(attributeCtor));
            targetMethod.Body = new Mono.Cecil.Cil.MethodBody(targetMethod);
            componentType.Methods.Add(targetMethod);

            return module;
        }

        private static bool BodyCallsTryGetMemoizedVNode(MethodDefinition method)
        {
            foreach (var instr in method.Body.Instructions)
            {
                if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt)
                    && instr.Operand is MethodReference mr && mr.Name == "TryGetMemoizedVNode")
                {
                    return true;
                }
            }
            return false;
        }

        // A hand-built body's instructions carry no Offset (real offsets exist only for IL parsed from an
        // actual compiled stream); TryAnalyze's return-after-hook-boundary check compares Offset, so a probe
        // body must assign a strictly increasing Offset per instruction to reproduce real program order.
        private static void AssignSequentialOffsets(MethodDefinition method)
        {
            var offset = 0;
            foreach (var instr in method.Body.Instructions)
            {
                instr.Offset = offset++;
            }
        }

        [Test]
        public void Given_DirectStlocHookCapture_When_CompilerWeaverRuns_Then_MethodIsWoven()
        {
            // Arrange — `var id = Hooks.UseId(null);` lowers to call -> stloc.0.
            using var module = BuildHookShapeProbeModule("DirectCaptureProbe",
                out var method, out var useId, out _, out _);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.String));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Call, useId));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.True,
                "A directly stloc-captured hook result is a sound dep and must be woven");
        }

        [Test]
        public void Given_Item1OnlyDeconstructionHookCapture_When_CompilerWeaverRuns_Then_MethodIsWoven()
        {
            // Arrange — `var (v, _) = Hooks.UseTransition();` lowers to call -> ldfld Item1 -> stloc.0.
            using var module = BuildHookShapeProbeModule("Item1DeconstructionProbe",
                out var method, out _, out var useTransition, out var tuple);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Boolean));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, useTransition));
            il.Append(Instruction.Create(OpCodes.Ldfld, new FieldReference("Item1", module.TypeSystem.Boolean, tuple)));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.True,
                "An Item1-only tuple deconstruction captures a sound dep and must be woven");
        }

        [Test]
        public void Given_TwoElementDeconstructionHookCapture_When_CompilerWeaverRuns_Then_MethodIsWoven()
        {
            // Arrange — `var (v, setV) = Hooks.UseTransition();` lowers to
            // call -> dup -> ldfld Item1 -> stloc.0 -> ldfld Item2 -> stloc.1.
            using var module = BuildHookShapeProbeModule("TwoElementDeconstructionProbe",
                out var method, out _, out var useTransition, out var tuple);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Boolean));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Object));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, useTransition));
            il.Append(Instruction.Create(OpCodes.Dup));
            il.Append(Instruction.Create(OpCodes.Ldfld, new FieldReference("Item1", module.TypeSystem.Boolean, tuple)));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, new FieldReference("Item2", module.TypeSystem.Object, tuple)));
            il.Append(Instruction.Create(OpCodes.Stloc_1));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.True,
                "A two-element tuple deconstruction captures the value element as a sound dep and must be woven");
        }

        [Test]
        public void Given_DiscardedHookResult_When_CompilerWeaverRuns_Then_MethodIsLeftUnwoven()
        {
            // Arrange — `Hooks.UseId(null);` as a bare statement lowers to call -> pop.
            using var module = BuildHookShapeProbeModule("DiscardedResultProbe",
                out var method, out var useId, out _, out _);
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Call, useId));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.False,
                "A discarded hook result is not captured in the deps array and must be left unwoven");
        }

        // --- Metadata-registration weaver E2E: <Module>.cctor of THIS assembly, woven for real ---

        private const string RegistryFullName = "Velvet.ComponentMethodRegistry";

        [Component(IsErrorBoundary = true)]
        public static VNode ErrorBoundaryComponent() => V.Label(text: "eb");

        [Component(Memoize = true)]
        public static VNode MemoizeComponent() => V.Label(text: "memo");

        [Component(DisplayName = "CustomDisplayName")]
        public static VNode DisplayNameComponent() => V.Label(text: "named");

        // No flags: the metadata weaver must not register it. It also has no hook, so the memo weaver bails too.
        [Component]
        public static VNode PlainComponent() => V.Label(text: "plain");

        public static class NestedHost
        {
            [Component(IsErrorBoundary = true)]
            public static VNode Render() => V.Label(text: "nested");
        }

        public static class GenericHost<T>
        {
            [Component(Memoize = true)]
            public static VNode Render() => V.Label(text: "generic");
        }

        [Test]
        public void Given_ErrorBoundaryComponent_When_Woven_Then_RegistersErrorBoundary()
        {
            // Arrange
            var cctor = LoadModuleInitializer();

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterErrorBoundary",
                    typeof(WeaverReflectionProbeTests).FullName, nameof(ErrorBoundaryComponent)),
                Is.True, "[Component(IsErrorBoundary = true)] registers an Error Boundary in <Module>.cctor");
        }

        [Test]
        public void Given_MemoizeComponent_When_Woven_Then_RegistersMemoize()
        {
            // Arrange
            var cctor = LoadModuleInitializer();

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterMemoize",
                    typeof(WeaverReflectionProbeTests).FullName, nameof(MemoizeComponent)),
                Is.True, "[Component(Memoize = true)] registers the props-bail gate in <Module>.cctor");
        }

        [Test]
        public void Given_DisplayNameComponent_When_Woven_Then_RegistersDisplayName()
        {
            // Arrange
            var cctor = LoadModuleInitializer();

            // Act + Assert
            Assert.That(RegistersThreeArg(cctor, "RegisterComponentDisplayName",
                    typeof(WeaverReflectionProbeTests).FullName, nameof(DisplayNameComponent),
                    "CustomDisplayName"),
                Is.True, "[Component(DisplayName = ...)] registers the display name in <Module>.cctor");
        }

        [Test]
        public void Given_FlaglessComponent_When_Woven_Then_IsNotRegisteredAsErrorBoundary()
        {
            // Arrange
            var cctor = LoadModuleInitializer();
            var typeName = typeof(WeaverReflectionProbeTests).FullName;

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterErrorBoundary", typeName, nameof(PlainComponent)), Is.False,
                "A flagless [Component] is not registered as an Error Boundary");
        }

        [Test]
        public void Given_FlaglessComponent_When_Woven_Then_IsNotRegisteredAsMemoize()
        {
            // Arrange
            var cctor = LoadModuleInitializer();
            var typeName = typeof(WeaverReflectionProbeTests).FullName;

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterMemoize", typeName, nameof(PlainComponent)), Is.False,
                "A flagless [Component] is not registered as a props-bail gate");
        }

        [Test]
        public void Given_NestedDeclaringType_When_Woven_Then_RegistersUnderRuntimeFullName()
        {
            // Arrange — typeof(NestedHost).FullName is the reflection form ('+' between outer and nested type).
            var cctor = LoadModuleInitializer();

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterErrorBoundary", typeof(NestedHost).FullName, "Render"),
                Is.True, "A nested declaring type registers under its '+'-separated runtime FullName");
        }

        [Test]
        public void Given_GenericDeclaringType_When_Woven_Then_RegistersUnderRuntimeFullName()
        {
            // Arrange — typeof(GenericHost<>).FullName carries the `1 arity suffix.
            var cctor = LoadModuleInitializer();

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterMemoize", typeof(GenericHost<>).FullName, "Render"),
                Is.True, "A generic declaring type registers under its `arity-suffixed runtime FullName");
        }

        private static MethodDefinition LoadModuleInitializer()
        {
            var assemblyPath = typeof(WeaverReflectionProbeTests).Assembly.Location;
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var moduleType = assembly.MainModule.GetType("<Module>");
            Assume.That(moduleType, Is.Not.Null, "Precondition: the <Module> type exists in the assembly");
            var cctor = moduleType.Methods.SingleOrDefault(m => m.Name == ".cctor");
            Assume.That(cctor, Is.Not.Null, "Precondition: <Module>.cctor is injected when the assembly has metadata components");
            return cctor;
        }

        // A 2-arg Register call is injected as `ldstr type; ldstr method; call`, so the two ldstr operands
        // immediately precede the call. The weaver emits no other instructions between them.
        private static bool RegistersTwoArg(MethodDefinition cctor, string registerMethod, string typeFullName, string methodName)
        {
            var instrs = cctor.Body.Instructions;
            for (var i = 2; i < instrs.Count; i++)
            {
                if (IsRegistryCall(instrs[i], registerMethod)
                    && IsLdstr(instrs[i - 2], typeFullName)
                    && IsLdstr(instrs[i - 1], methodName))
                {
                    return true;
                }
            }
            return false;
        }

        // A 3-arg Register call is injected as `ldstr type; ldstr method; ldstr displayName; call`.
        private static bool RegistersThreeArg(MethodDefinition cctor, string registerMethod, string typeFullName, string methodName, string displayName)
        {
            var instrs = cctor.Body.Instructions;
            for (var i = 3; i < instrs.Count; i++)
            {
                if (IsRegistryCall(instrs[i], registerMethod)
                    && IsLdstr(instrs[i - 3], typeFullName)
                    && IsLdstr(instrs[i - 2], methodName)
                    && IsLdstr(instrs[i - 1], displayName))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsRegistryCall(Instruction instr, string methodName)
            => instr.OpCode == OpCodes.Call
                && instr.Operand is MethodReference mr
                && mr.Name == methodName
                && mr.DeclaringType.FullName == RegistryFullName;

        private static bool IsLdstr(Instruction instr, string value)
            => instr.OpCode == OpCodes.Ldstr && (string)instr.Operand == value;

        // --- PositionalHookNames.All canonical pin ---

        private static readonly string[] Expected =
        {
            "UseEffect",
            "UseLayoutEffect",
            "UseInsertionEffect",
            "UseCallback",
            "UseMemo",
            "UseBlocker",
            "UseState",
            "UseReducer",
            "UseOptimistic",
            "UseStore",
            "UseContext",
            "UseRef",
            "UseMutableRef",
            "UseImperativeHandle",
            "UseTransition",
            "UseId",
            "UseDeferredValue",
            "UseMutation",
            "UseService",
            "UseFallback",
            "Use",
            "UseFrame",
        };

        [Test]
        public void Given_CanonicalSet_When_Inspected_Then_MatchesTheExpectedNames()
        {
            // Act + Assert
            Assert.That(PositionalHookNames.All, Is.EquivalentTo(Expected),
                "PositionalHookNames.All drifted from the canonical set. If the change is intentional, update the" +
                " Expected list here; the ILPP weaver reads PositionalHookNames.All directly.");
        }

        [Test]
        public void Given_CanonicalSet_When_Inspected_Then_HasNoDuplicates()
        {
            // Act + Assert
            Assert.That(PositionalHookNames.All, Is.Unique);
        }

        [Test]
        public void Given_HooksAssembly_When_PositionalSlotConsumersAreEnumerated_Then_EveryOneIsInTheCanonicalList()
        {
            // Arrange
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(Hooks).Assembly.Location);
            var hooksType = assembly.MainModule.GetType(typeof(Hooks).FullName);
            Assume.That(hooksType, Is.Not.Null, "Precondition: Velvet.Hooks is present in the Velvet assembly");

            // Act
            var slotConsumers = EnumeratePositionalSlotConsumers(hooksType);

            // Assert
            Assert.That(slotConsumers, Is.SubsetOf(PositionalHookNames.All),
                "Every public hook that allocates a positional slot (a HookIndexTable cursor or an async" +
                " resource slot) must be listed in PositionalHookNames.All, or the ILPP weaver silently" +
                " treats its calls as non-hook plumbing and may skip or mis-anchor them.");
        }

        // Enumerates the public Use* hooks whose implementation allocates a positional slot: the method's
        // body — or the body of a non-hook Hooks helper it calls, transitively — touches a HookIndexTable
        // cursor field or advances the fiber's async resource slot cursor. Descent deliberately stops at
        // other public Use* hooks: those allocate their own slot and are enumerated independently, while a
        // hook that merely composes them (e.g. UseNavigation over UseState) is tracked transitively by the
        // weaver and does not need a list entry of its own.
        private static IReadOnlyCollection<string> EnumeratePositionalSlotConsumers(TypeDefinition hooksType)
        {
            var consumers = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var method in hooksType.Methods)
            {
                if (!IsPublicHookMethod(method)) continue;
                if (AllocatesPositionalSlot(method, hooksType, new HashSet<string>()))
                {
                    consumers.Add(method.Name);
                }
            }
            return consumers;
        }

        private static bool IsPublicHookMethod(MethodDefinition method)
            => method.IsPublic
                && method.IsStatic
                && method.Name.StartsWith("Use", System.StringComparison.Ordinal);

        private static bool AllocatesPositionalSlot(
            MethodDefinition method, TypeDefinition hooksType, HashSet<string> visited)
        {
            if (!method.HasBody || !visited.Add(method.FullName)) return false;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is FieldReference field
                    && field.DeclaringType.FullName == "Velvet.HookIndexTable")
                {
                    return true;
                }
                if (instruction.Operand is not MethodReference callee) continue;
                if (callee.Name == "NextAsyncSlotIndex")
                {
                    return true;
                }
                // Only same-type helpers are followed, so resolution never leaves the already-loaded
                // module (resolving foreign references would require an assembly resolver).
                if (callee.DeclaringType.FullName != hooksType.FullName) continue;
                var calleeDefinition = callee.Resolve();
                if (calleeDefinition == null) continue;
                if (IsPublicHookMethod(calleeDefinition)) continue;
                if (AllocatesPositionalSlot(calleeDefinition, hooksType, visited))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
