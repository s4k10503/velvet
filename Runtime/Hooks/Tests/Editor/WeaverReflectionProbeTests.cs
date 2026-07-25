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
    /// Specifies two independent contracts of the CompilerWeaver / MetadataRegistrationWeaver ILPPs, both probed
    /// through reflection against synthetic Cecil modules (the weavers live in the editor-only
    /// <c>Unity.Velvet.CodeGen</c> assembly and are internal, so this test asmdef cannot reference them
    /// directly):
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
    }
}
