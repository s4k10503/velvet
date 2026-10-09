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
    /// is outside the BCL/Unity carve-out is not read as reaching a non-SAFE hook, whichever assembly declares
    /// it — where the call sits decides instead. A <c>Velvet.Hooks</c> member whose body reaches nothing but
    /// such a dispatch is still a hook call, its value captured ahead of the gate.</item>
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
        public void Given_OpenVirtualOutsideCarveOutInNonVelvetReferencingAssembly_When_ReachesNonSafeHookClassifies_Then_DoesNotReadItAsNonSafe()
        {
            // Arrange
            using var module = BuildNonVelvetReferencingModuleWithOpenVirtual(out var handler);
            Assume.That(module.AssemblyReferences.Any(r => r.Name == "Velvet"), Is.False,
                "Precondition: the synthetic module never references Velvet");

            // Act
            var isNonSafe = (bool)InvokeClassifier("ReachesNonSafeHook", handler);

            // Assert
            Assert.That(isNonSafe, Is.False,
                "The declared body of an open dispatch need not be the one that runs, so it proves nothing either way;"
                + " where the call sits decides instead");
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
            => InvokeWeave(weaverTypeFullName, module, out _);

        private static List<string> InvokeWeave(string weaverTypeFullName, ModuleDefinition module, out bool changed)
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
            changed = (bool)weaveMethod.Invoke(null, new[] { (object)module, diagnostics })!;

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
        // BCL/Unity namespace root. Returns the MethodDefinition for that method via handler.
        private static ModuleDefinition BuildNonVelvetReferencingModuleWithOpenVirtual(out MethodDefinition handler)
        {
            var module = ModuleDefinition.CreateModule("NonVelvetReferencingProbe", ModuleKind.Dll);
            handler = AddOpenVirtual(module);
            return module;
        }

        private static MethodDefinition AddOpenVirtual(ModuleDefinition module)
        {
            var baseType = new TypeDefinition("Probe", "Base",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var handler = new MethodDefinition("Handler",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual
                    | Mono.Cecil.MethodAttributes.NewSlot | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            handler.Body = new Mono.Cecil.Cil.MethodBody(handler);
            handler.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            baseType.Methods.Add(handler);
            module.Types.Add(baseType);
            return handler;
        }

        // Invokes CompilerWeaver's private static bool <name>(MethodReference, <walk state>) through reflection
        // (the CodeGen assembly is editor-only and not referenced by this test asmdef), handing it a fresh
        // instance of whatever state type it declares.
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
            var state = Activator.CreateInstance(method!.GetParameters()[1].ParameterType, nonPublic: true)!;
            return method.Invoke(null, new object[] { callee, state })!;
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
                "component", Mono.Cecil.ParameterAttributes.None, module.ImportReference(typeof(RuntimeMethodHandle))));
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
        public void Given_TwoElementDeconstructionWithTheSecondElementDiscarded_When_CompilerWeaverRuns_Then_MethodIsWoven()
        {
            // Arrange — `var (v, setV) = Hooks.UseTransition();` with `setV` never read lowers to
            // call -> dup -> ldfld Item1 -> stloc.0 -> pop.
            using var module = BuildHookShapeProbeModule("UnusedSecondElementProbe",
                out var method, out _, out var useTransition, out var tuple);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Boolean));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, useTransition));
            il.Append(Instruction.Create(OpCodes.Dup));
            il.Append(Instruction.Create(OpCodes.Ldfld, new FieldReference("Item1", module.TypeSystem.Boolean, tuple)));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.True,
                "A second element that is discarded cannot go stale in the cache, so the value element keys it");
        }

        // GREEN_ON_BASE(characterization): the base holds the same allow-list. What it pins is the allow-list
        // staying the set the pair shapes assume: adding `nameof(Velvet.Hooks.UseAnimationSequence)` to
        // `MemoSafeValueHookNames` reddens it.
        [Test]
        public void Given_TheValueHookAllowList_When_ItsPairReturningMembersAreListed_Then_ThoseAreTheFourWithAStableSecondElement()
        {
            // Arrange
            var codeGenAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == CodeGenAssemblyName);
            Assume.That(codeGenAssembly, Is.Not.Null,
                "Precondition: the Unity.Velvet.CodeGen assembly is loaded in the editor domain");
            var allowList = (System.Collections.Generic.HashSet<string>)codeGenAssembly!
                .GetType(CompilerWeaverTypeFullName, throwOnError: true)!
                .GetField("MemoSafeValueHookNames", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

            // Act
            var pairReturning = typeof(Hooks).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => allowList.Contains(m.Name) && m.ReturnType.IsGenericType
                    && m.ReturnType.GetGenericTypeDefinition() == typeof(System.ValueTuple<,>))
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal);

            // Assert
            Assert.That(string.Join(",", pairReturning), Is.EqualTo("UseOptimistic,UseReducer,UseState,UseTransition"),
                "A pair read with its second element is keyed on the first alone, which holds only while the second is reference-stable");
        }

        // The warnings the weaver raises for a body it failed to process, as opposed to one it declined.
        private static int FailureWarnings(IEnumerable<string> messages)
            => messages.Count(message => message.Contains("the IL post-processor threw"));

        private static MethodDefinition AddProbeComponent(ModuleDefinition module, string name,
            Action<ILProcessor> body)
        {
            var vnodeType = module.Types.Single(type => type.FullName == "Velvet.VNode");
            var method = new MethodDefinition(name,
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, vnodeType);
            method.Parameters.Add(new ParameterDefinition("p", Mono.Cecil.ParameterAttributes.None,
                module.TypeSystem.Object));
            var attributeType = new TypeReference("Velvet", "ComponentAttribute", module, module);
            method.CustomAttributes.Add(new CustomAttribute(
                new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true }));
            method.Body = new Mono.Cecil.Cil.MethodBody(method);
            body(method.Body.GetILProcessor());
            module.Types.Single(type => type.FullName == "Probe.Fixture").Methods.Add(method);
            AssignSequentialOffsets(method);
            return method;
        }

        // Components the weaver cannot process, beside one it can. `Broken` names no declaring type, so any
        // walk reaching it throws; the walks reach it through a call in the body, through a helper, and through a
        // Velvet.Hooks member, which only the reach fold descends. InjectFails throws after the weave has
        // started: a branch with no target follows its ret.
        private static ModuleDefinition BuildContainmentModule(out MethodDefinition injectFails,
            out MethodDefinition sibling)
        {
            var module = BuildHookShapeProbeModule("ContainmentProbe", out _, out _, out _, out _);
            var broken = new MethodReference("Broken", module.TypeSystem.Void);

            var readBroken = new MethodDefinition("ReadBroken",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Object);
            readBroken.Body = new Mono.Cecil.Cil.MethodBody(readBroken);
            var readIl = readBroken.Body.GetILProcessor();
            readIl.Append(Instruction.Create(OpCodes.Call, broken));
            readIl.Append(Instruction.Create(OpCodes.Ldnull));
            readIl.Append(Instruction.Create(OpCodes.Ret));
            module.Types.Single(type => type.FullName == "Velvet.Hooks").Methods.Add(readBroken);

            var helper = new MethodDefinition("Helper",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            helper.Body = new Mono.Cecil.Cil.MethodBody(helper);
            var helperIl = helper.Body.GetILProcessor();
            helperIl.Append(Instruction.Create(OpCodes.Call, broken));
            helperIl.Append(Instruction.Create(OpCodes.Ret));
            module.Types.Single(type => type.FullName == "Probe.Fixture").Methods.Add(helper);

            AddProbeComponent(module, "AnalysisFails", il =>
            {
                il.Append(Instruction.Create(OpCodes.Call, broken));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Ret));
            });
            foreach (var name in new[] { "ReachFails1", "ReachFails2" })
            {
                AddProbeComponent(module, name, il =>
                {
                    il.Append(Instruction.Create(OpCodes.Call, readBroken));
                    il.Append(Instruction.Create(OpCodes.Pop));
                    il.Append(Instruction.Create(OpCodes.Ldnull));
                    il.Append(Instruction.Create(OpCodes.Ret));
                });
            }
            foreach (var name in new[] { "NonSafeFails1", "NonSafeFails2" })
            {
                AddProbeComponent(module, name, il =>
                {
                    il.Append(Instruction.Create(OpCodes.Call, helper));
                    il.Append(Instruction.Create(OpCodes.Ldnull));
                    il.Append(Instruction.Create(OpCodes.Ret));
                });
            }
            injectFails = AddProbeComponent(module, "InjectFails", il =>
            {
                il.Append(Instruction.Create(OpCodes.Ldarg_0));
                il.Append(Instruction.Create(OpCodes.Pop));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Ret));
                var noTarget = Instruction.Create(OpCodes.Nop);
                noTarget.OpCode = OpCodes.Br;
                noTarget.Operand = null;
                il.Append(noTarget);
            });
            sibling = AddProbeComponent(module, "Sibling", il =>
            {
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Ret));
            });
            return module;
        }

        // The body as the next reader sees it: each instruction with its opcode, operand and neighbours, and the
        // variables.
        private static string Describe(MethodDefinition method)
            => string.Join("|", method.Body.Instructions.Select(instr =>
                $"{instr.Previous?.OpCode}<{instr.OpCode} {instr.Operand}>{instr.Next?.OpCode}"))
               + $"#{method.Body.Variables.Count}";

        [Test]
        public void Given_ABodyTheWeaverFailsOn_When_CompilerWeaverRuns_Then_ItIsWarnedAboutByNameAndExceptionType()
        {
            // Arrange
            using var module = BuildContainmentModule(out _, out _);

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(
                messages.Count(message => message.Contains("Probe.Fixture::AnalysisFails")
                    && message.Contains("NullReferenceException")),
                Is.EqualTo(1), "A weaver defect is visible rather than silent");
        }

        [Test]
        public void Given_ManyBodiesTheWeaverFailsOn_When_CompilerWeaverRuns_Then_EachFailureIsTheBodysOwn()
        {
            // Arrange — the walks that threw left their state mid-descent, so a later body reaching the same
            // callee would fail differently if the folds were kept
            using var module = BuildContainmentModule(out _, out _);

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(
                (FailureWarnings(messages), messages.Count(message => message.Contains("NullReferenceException"))),
                Is.EqualTo((6, 6)), "Every failure is the one the missing declaring type causes");
        }

        [Test]
        public void Given_AWeaveThatFailsAfterItBegan_When_CompilerWeaverRuns_Then_TheBodyIsAsItWas()
        {
            // Arrange
            using var module = BuildContainmentModule(out var injectFails, out _);
            var before = Describe(injectFails);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(Describe(injectFails), Is.EqualTo(before),
                "The instructions, their links and the variables are those the weave found");
        }

        [Test]
        public void Given_AModuleWhereABodyIsWoven_When_CompilerWeaverRuns_Then_ItReportsAChange()
        {
            // Arrange
            using var module = BuildContainmentModule(out _, out _);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module, out var changed);

            // Assert
            Assert.That(changed, Is.True, "The module was rewritten, so the post-processor has to write it");
        }

        // GREEN_ON_BASE(characterization): the base reports no change for a module it wove nothing in, as this does.
        // What it pins is the analysis result being honoured: `true` in place of `false` for the failed analysis
        // in `TryWeaveMethod` reports a change.
        [Test]
        public void Given_AModuleWhereNoBodyIsWoven_When_CompilerWeaverRuns_Then_ItReportsNoChange()
        {
            // Arrange
            using var module = BuildHookShapeProbeModule("NothingWovenProbe",
                out _, out _, out _, out _);
            AddProbeComponent(module, "Bails", il =>
            {
                il.Append(Instruction.Create(OpCodes.Call, UndeclaredMethodOf(module)));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Ret));
            });

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module, out var changed);

            // Assert
            Assert.That(changed, Is.False, "A module left as it was is not rewritten");
        }

        [Test]
        public void Given_BodiesTheWeaverFailsOn_When_CompilerWeaverRuns_Then_ABodyBesideThemIsWoven()
        {
            // Arrange
            using var module = BuildContainmentModule(out _, out var sibling);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(sibling), Is.True,
                "A failure costs its own component the memoization, and no other");
        }

        // GREEN_ON_BASE(characterization): the base raises no failure warning for this assembly either, since it
        // raises none at all. What it pins is the weave of these fixtures not throwing: deleting the
        // `type == null ||` test at the head of `TryCaptureStackValue` throws on the ref-returning custom hook
        // component, which the weaver reports and this reads.
        [Test]
        public void Given_TheAssemblyThisFixtureLivesIn_When_CompilerWeaverRunsOverIt_Then_NoBodyFailsToWeave()
        {
            // Arrange — every body this assembly's fixtures hold that the weaver left unwoven is processed again
            var location = typeof(WeaverReflectionProbeTests).Assembly.Location;
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(location));
            resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(typeof(Hooks).Assembly.Location));
            using var assembly = AssemblyDefinition.ReadAssembly(location,
                new ReaderParameters { AssemblyResolver = resolver });

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.CompilerWeaver", assembly.MainModule);

            // Assert
            Assert.That(messages.Where(message => message.Contains("threw")
                    || message.Contains("is disabled for assembly")),
                Is.Empty, "A body the weaver throws on, or an assembly it cannot resolve, is a failure to weave");
        }

        // `call -> dup -> ldfld Item1 -> stloc.0 -> <readOp> <readField> -> <afterRead>`, the Item2 read of a
        // two-element deconstruction with one part replaced.
        private static bool PairShapeIsWoven(string moduleName, OpCode readOp, string readField, OpCode afterRead)
        {
            using var module = BuildHookShapeProbeModule(moduleName,
                out var method, out _, out var useTransition, out var tuple);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Boolean));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Boolean));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, useTransition));
            il.Append(Instruction.Create(OpCodes.Dup));
            il.Append(Instruction.Create(OpCodes.Ldfld, new FieldReference("Item1", module.TypeSystem.Boolean, tuple)));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(readOp, new FieldReference(readField, module.TypeSystem.Boolean, tuple)));
            il.Append(Instruction.Create(afterRead));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            return BodyCallsTryGetMemoizedVNode(method);
        }

        // GREEN_ON_BASE(characterization): the base reads the second element of a pair by its name too.
        // What it pins is the name test on that read: deleting the `field.Name == fieldName` clause from
        // `IsFieldLoad` takes the second read of Item1 for the setter's.
        [Test]
        public void Given_APairWhoseSecondReadNamesItem1_When_CompilerWeaverRuns_Then_MethodIsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(PairShapeIsWoven("PairItem1Probe", OpCodes.Ldfld, "Item1", OpCodes.Stloc_1), Is.False,
                "The read after the value store has to be of Item2 for the pair to be the deconstruction");
        }

        // GREEN_ON_BASE(characterization): the base accepts only a field load there too.
        // What it pins is the opcode test on that read: deleting the `instr.OpCode == OpCodes.Ldfld` clause from
        // `IsFieldLoad` takes the address of Item2 for a read of it.
        [Test]
        public void Given_APairWhoseSecondReadTakesTheFieldAddress_When_CompilerWeaverRuns_Then_MethodIsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(PairShapeIsWoven("PairAddressProbe", OpCodes.Ldflda, "Item2", OpCodes.Stloc_1), Is.False,
                "An address of Item2 is not a read of its value");
        }

        // GREEN_ON_BASE(characterization): the base accepts only a store there too.
        // What it pins is the store test on the second element's consumer: deleting the
        // `!IsValueConsumingStore(setterStore)` clause from `TryMatchTwoElementDeconstruction` weaves it.
        [Test]
        public void Given_APairWhoseSecondElementIsPopped_When_CompilerWeaverRuns_Then_MethodIsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(PairShapeIsWoven("PairPopProbe", OpCodes.Ldfld, "Item2", OpCodes.Pop), Is.False,
                "A second element dropped after being read is not the deconstruction's setter store");
        }

        // GREEN_ON_BASE(characterization): the base refuses a read it cannot type too.
        // What it pins is the guard on the Item1 copy's type: deleting `if (type == null) return false;` from
        // `TryCaptureStackValue` records a copy with no type.
        [Test]
        public void Given_AnItem1ReadWhoseTypeCannotBeNamedInTheCallersTerms_When_CompilerWeaverRuns_Then_MethodIsLeftUnwoven()
        {
            // Arrange — Item1 declared on a type that is not a generic instance, with a type-level generic
            // parameter for its type, so there is no argument to substitute for it
            using var module = BuildHookShapeProbeModule("UnnamableItem1Probe",
                out var method, out _, out var useTransition, out _);
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, useTransition));
            il.Append(Instruction.Create(OpCodes.Ldfld, new FieldReference("Item1",
                new GenericParameter(0, GenericParameterType.Type, module), module.TypeSystem.Object)));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That((BodyCallsTryGetMemoizedVNode(method), FailureWarnings(messages)), Is.EqualTo((false, 0)),
                "A copy whose type cannot be named leaves nothing to key the cache on, and the weave says nothing of it");
        }

        // The index of the variable an ldloc form loads, or -1 for any other instruction.
        private static int LoadedVariableIndex(Instruction instr)
        {
            if (instr.OpCode == OpCodes.Ldloc_0) return 0;
            if (instr.OpCode == OpCodes.Ldloc_1) return 1;
            if (instr.OpCode == OpCodes.Ldloc_2) return 2;
            if (instr.OpCode == OpCodes.Ldloc_3) return 3;
            return (instr.OpCode == OpCodes.Ldloc || instr.OpCode == OpCodes.Ldloc_S)
                && instr.Operand is VariableDefinition variable ? variable.Index : -1;
        }

        [Test]
        public void Given_AHookValueLeftOnTheStack_When_CompilerWeaverRuns_Then_ItIsCopiedWithADupBeforeTheNextStore()
        {
            // Arrange — `Hooks.UseId(null)` followed by an instruction that is neither a store nor a pop
            using var module = BuildHookShapeProbeModule("StackCopyProbe",
                out var method, out var useId, out _, out _);
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Call, useId));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            var call = IndexOfCallTo(method, "UseId");
            var instructions = method.Body.Instructions;
            Assert.That(
                (instructions[call + 1].OpCode.Code, instructions[call + 2].OpCode.Name.StartsWith("stloc")),
                Is.EqualTo((Code.Dup, true)),
                "A value with no local of its own is copied off the stack where the call produced it");
        }

        // GREEN_ON_BASE(characterization): the base copies a stored hook value out of its local the same way.
        // What it pins is the copy reading the variable the hook stored into: `!=` in place of `==` in the copy
        // loop of `InjectMemoization` emits a dup there instead.
        [Test]
        public void Given_AHookValueStoredToALocal_When_CompilerWeaverRuns_Then_ItIsCopiedFromThatLocal()
        {
            // Arrange — `var id = Hooks.UseId(null);` into the second of two locals
            using var module = BuildHookShapeProbeModule("LocalCopyProbe",
                out var method, out var useId, out _, out _);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.String));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.String));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Call, useId));
            il.Append(Instruction.Create(OpCodes.Stloc_1));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            var call = IndexOfCallTo(method, "UseId");
            Assert.That(LoadedVariableIndex(method.Body.Instructions[call + 2]), Is.EqualTo(1),
                "A value with a local of its own is copied from that local, and not from the stack");
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

        // GREEN_ON_BASE(characterization): the base reads a Velvet.Hooks member reaching a dispatch as a hook too.
        // What it pins is that reading surviving the Opaque class: deleting the Velvet.Hooks branch at the end
        // of `ReachOf` leaves the member Opaque ahead of the gate, and reddens it.
        [Test]
        public void Given_AHooksMemberReachingOnlyADispatchAheadOfAHook_When_CompilerWeaverRuns_Then_MethodIsWoven()
        {
            // Arrange — `var value = Hooks.ReadThrough(); var id = Hooks.UseId(null);`, where ReadThrough's
            // body makes one open virtual call.
            using var module = BuildHookShapeProbeModule("HooksMemberDispatchProbe",
                out var method, out var useId, out _, out _);
            var handler = AddOpenVirtual(module);
            var readThrough = new MethodDefinition("ReadThrough",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Object);
            readThrough.Body = new Mono.Cecil.Cil.MethodBody(readThrough);
            var readIl = readThrough.Body.GetILProcessor();
            readIl.Append(Instruction.Create(OpCodes.Ldnull));
            readIl.Append(Instruction.Create(OpCodes.Callvirt, handler));
            readIl.Append(Instruction.Create(OpCodes.Ldnull));
            readIl.Append(Instruction.Create(OpCodes.Ret));
            module.Types.Single(type => type.FullName == "Velvet.Hooks").Methods.Add(readThrough);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Object));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.String));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, readThrough));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Call, useId));
            il.Append(Instruction.Create(OpCodes.Stloc_1));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.True,
                "A Velvet.Hooks member is classified a hook whatever its body dispatches to, so its value is captured");
        }

        // --- CompilerWeaver gate placement on a body with no hook, which the weaver gates at method entry ---

        [Test]
        public void Given_AnOlderGateOverload_When_CompilerWeaverRuns_Then_ItCallsTheFourParameterGate()
        {
            // Arrange
            using var module = BuildHookShapeProbeModule("MemoGateOverloadProbe", out var method,
                out _, out _, out _);
            var hooksType = module.Types.Single(type => type.FullName == "Velvet.Hooks");
            var olderGate = new MethodDefinition("TryGetMemoizedVNode",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Boolean);
            olderGate.Parameters.Add(new ParameterDefinition("deps", Mono.Cecil.ParameterAttributes.None,
                new ArrayType(module.TypeSystem.Object)));
            olderGate.Parameters.Add(new ParameterDefinition("slotIndex", Mono.Cecil.ParameterAttributes.Out,
                new ByReferenceType(module.TypeSystem.Int32)));
            olderGate.Parameters.Add(new ParameterDefinition("cached", Mono.Cecil.ParameterAttributes.Out,
                new ByReferenceType(method.ReturnType)));
            hooksType.Methods.Insert(0, olderGate);
            method.Parameters.Add(new ParameterDefinition("value", Mono.Cecil.ParameterAttributes.None,
                module.TypeSystem.Object));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            var gate = IndexOfCallTo(method, "TryGetMemoizedVNode");
            var arity = gate < 0 ? -1 : ((MethodReference)method.Body.Instructions[gate].Operand).Parameters.Count;
            Assert.That(arity, Is.EqualTo(4));
        }

        [Test]
        public void Given_AHooklessBodyWhoseTryOpensAtItsFirstInstruction_When_CompilerWeaverRuns_Then_TheGatePrecedesTheTry()
        {
            // Arrange — `try { result = null; } catch { result = null; } return result;` behind one parameter
            using var module = BuildHookShapeProbeModule("TryAtEntryProbe", out var method, out _, out _, out _);
            method.Parameters.Add(new ParameterDefinition("p", Mono.Cecil.ParameterAttributes.None,
                module.TypeSystem.Object));
            method.Body.Variables.Add(new VariableDefinition(method.ReturnType));
            var il = method.Body.GetILProcessor();
            var tryStart = Instruction.Create(OpCodes.Ldnull);
            var handlerStart = Instruction.Create(OpCodes.Pop);
            var exit = Instruction.Create(OpCodes.Ldloc_0);
            il.Append(tryStart);
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Leave_S, exit));
            il.Append(handlerStart);
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Leave_S, exit));
            il.Append(exit);
            il.Append(Instruction.Create(OpCodes.Ret));
            method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = tryStart,
                TryEnd = handlerStart,
                HandlerStart = handlerStart,
                HandlerEnd = exit,
                CatchType = module.TypeSystem.Object,
            });
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            var gate = IndexOfCallTo(method, "TryGetMemoizedVNode");
            var regionStart = method.Body.Instructions.IndexOf(method.Body.ExceptionHandlers[0].TryStart);
            Assert.That((woven: gate >= 0, gateFirst: gate < regionStart), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base bails a callee whose type declares no such method, as here.
        // What it pins is the safety fold's null guard: deleting `if (definition == null) return true;` from
        // `NonSafeHookFold.TryLeaf` hands the null on to `IsDispatchOpen`, and the weave throws.
        [Test]
        public void Given_ACallToAMethodItsTypeDoesNotDeclare_When_CompilerWeaverRuns_Then_MethodIsLeftUnwoven()
        {
            // Arrange — one parameter, and a call naming a method Probe.Fixture does not declare
            using var module = BuildHookShapeProbeModule("UndeclaredMethodProbe",
                out var method, out _, out _, out _);
            method.Parameters.Add(new ParameterDefinition("p", Mono.Cecil.ParameterAttributes.None,
                module.TypeSystem.Object));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, UndeclaredMethodOf(module)));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That((BodyCallsTryGetMemoizedVNode(method), FailureWarnings(messages)), Is.EqualTo((false, 0)),
                "A callee Resolve() returns nothing for cannot be inspected, so the weaver bails without failing");
        }

        // GREEN_ON_BASE(characterization): the base reads such a callee inside a Velvet.Hooks member as no hook.
        // What it pins is the reach fold's null guard: deleting `if (definition == null) return true;` from
        // `HookReachFold.TryLeaf` hands the null on to `IsDispatchOpen`, and the weave throws.
        [Test]
        public void Given_AHooksMemberCallingAMethodItsTypeDoesNotDeclare_When_CompilerWeaverRuns_Then_MethodIsWoven()
        {
            // Arrange — `var value = Hooks.ReadMissing(); var id = Hooks.UseId(null);`, where ReadMissing's body
            // calls a method Probe.Fixture does not declare. The safety fold classifies Hooks.* by name, so only
            // the reach fold descends into ReadMissing.
            using var module = BuildHookShapeProbeModule("HooksMemberUndeclaredProbe",
                out var method, out var useId, out _, out _);
            var readMissing = new MethodDefinition("ReadMissing",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Object);
            readMissing.Body = new Mono.Cecil.Cil.MethodBody(readMissing);
            var readIl = readMissing.Body.GetILProcessor();
            readIl.Append(Instruction.Create(OpCodes.Call, UndeclaredMethodOf(module)));
            readIl.Append(Instruction.Create(OpCodes.Ldnull));
            readIl.Append(Instruction.Create(OpCodes.Ret));
            module.Types.Single(type => type.FullName == "Velvet.Hooks").Methods.Add(readMissing);
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Object));
            method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.String));
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, readMissing));
            il.Append(Instruction.Create(OpCodes.Stloc_0));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Call, useId));
            il.Append(Instruction.Create(OpCodes.Stloc_1));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            var messages = InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That((BodyCallsTryGetMemoizedVNode(method), FailureWarnings(messages)), Is.EqualTo((true, 0)),
                "ReadMissing reaches nothing the reach fold can see, and UseId's value keys the cache");
        }

        // GREEN_ON_BASE(characterization): the base skips a System callee unresolved, so it weaves this body too.
        // What it pins is the safety fold keeping that carve-out: deleting the `CannotReachVelvetHook` line from
        // `NonSafeHookFold.TryLeaf` tries to resolve the callee, fails, and bails the body.
        [Test]
        public void Given_AHooklessBodyCallingAnUnresolvableSystemMethod_When_CompilerWeaverRuns_Then_MethodIsWoven()
        {
            // Arrange — one parameter, and a call into a System namespace in an assembly no resolver can find
            using var module = BuildHookShapeProbeModule("UnresolvableSystemCalleeProbe",
                out var method, out _, out _, out _);
            method.Parameters.Add(new ParameterDefinition("p", Mono.Cecil.ParameterAttributes.None,
                module.TypeSystem.Object));
            var missingScope = new AssemblyNameReference("System.WeaverProbe.Missing", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(missingScope);
            var missingType = new TypeReference("System.WeaverProbe", "Service", module, missingScope);
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, new MethodReference("Run", module.TypeSystem.Void, missingType)));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.True,
                "A BCL / Unity callee is read as hook-free without resolving it, so its failure bails nothing");
        }

        // A reference to a method the module's own Probe.Leaf type does not declare. The type has no base type,
        // so the lookup ends at it without resolving a base from another assembly.
        private static MethodReference UndeclaredMethodOf(ModuleDefinition module)
        {
            var leafType = new TypeDefinition("Probe", "Leaf",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class
                | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed);
            module.Types.Add(leafType);
            return new MethodReference("Undeclared", module.TypeSystem.Void, leafType);
        }

        // GREEN_ON_BASE(characterization): the base weaves no body without a hook, so it leaves this one alone too.
        // What this pins is the safety gate running ahead of the entry gate. An unresolvable callee is read as
        // no hook by the hook scan and as unverifiable by the safety gate, so moving the hookless branch of
        // `TryAnalyze` above its `ReachesAnyNonSafeHook` call reddens this case.
        [Test]
        public void Given_AHooklessBodyCallingAnUnresolvableMethod_When_CompilerWeaverRuns_Then_MethodIsLeftUnwoven()
        {
            // Arrange — one parameter, and a call into an assembly no resolver can find
            using var module = BuildHookShapeProbeModule("UnresolvableCalleeProbe",
                out var method, out _, out _, out _);
            method.Parameters.Add(new ParameterDefinition("p", Mono.Cecil.ParameterAttributes.None,
                module.TypeSystem.Object));
            var missingScope = new AssemblyNameReference("Velvet.WeaverProbe.Missing", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(missingScope);
            var missingType = new TypeReference("Probe.Missing", "Service", module, missingScope);
            var missingMethod = new MethodReference("Run", module.TypeSystem.Void, missingType);
            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Call, missingMethod));
            il.Append(Instruction.Create(OpCodes.Ldnull));
            il.Append(Instruction.Create(OpCodes.Ret));
            AssignSequentialOffsets(method);

            // Act
            InvokeWeave("Velvet.CodeGen.CompilerWeaver", module);

            // Assert
            Assert.That(BodyCallsTryGetMemoizedVNode(method), Is.False,
                "A callee the weaver cannot inspect bails a body with no hook as it bails one with a hook");
        }

        private static int IndexOfCallTo(MethodDefinition method, string name)
        {
            var instructions = method.Body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                if ((instructions[i].OpCode == OpCodes.Call || instructions[i].OpCode == OpCodes.Callvirt)
                    && instructions[i].Operand is MethodReference mr && mr.Name == name)
                {
                    return i;
                }
            }
            return -1;
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

        [Test]
        public void Given_CanonicalSet_When_Inspected_Then_MatchesTheExpectedNames()
        {
            // Arrange
            string[] expected =
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
                "UseSyncExternalStore",
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

            // Act + Assert
            Assert.That(PositionalHookNames.All, Is.EquivalentTo(expected),
                "PositionalHookNames.All drifted from the canonical set. If the change is intentional, update the" +
                " expected list here; the ILPP weaver reads PositionalHookNames.All directly.");
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
