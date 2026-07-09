using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the CompilerWeaver ILPP classifies an open virtual / interface dispatch whose declaring
    /// type is outside the BCL/Unity/UniTask carve-out (<c>CannotReachVelvetHook</c>). The classifier must not
    /// use "does the declaring assembly reference Velvet" as a proxy for "could an override reach a hook": an
    /// override can be declared in a third assembly that references Velvet even when the statically declared
    /// base/interface's own assembly never does, so the only sound classification for such a dispatch is
    /// unverifiable / non-SAFE regardless of the declaring assembly's own references.
    /// <para>
    /// The E2E weaving tests (<see cref="CompilerILPostProcessorE2ETests"/>) compile a single test assembly, so
    /// a callee declared in a genuinely separate, non-Velvet-referencing assembly cannot be produced that way.
    /// This fixture instead invokes the two private classifier methods
    /// (<c>ReachesNonSafeHook</c>/<c>CallsHookTransitively</c>) directly through reflection, against a callee
    /// synthesized with Cecil in a module whose reference list never includes Velvet at all.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class OpenDispatchHookSafetyTests
    {
        private const string CodeGenAssemblyName = "Unity.Velvet.CodeGen";
        private const string CompilerWeaverTypeFullName = "Velvet.CodeGen.CompilerWeaver";

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
