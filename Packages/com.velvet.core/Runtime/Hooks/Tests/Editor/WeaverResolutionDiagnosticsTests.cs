using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the ILPP weavers surface a diagnostic warning instead of failing silently when the
    /// Velvet runtime types they inject calls to cannot be resolved from the processed module.
    /// <list type="bullet">
    /// <item>CompilerWeaver: when <c>Velvet.Hooks</c> / <c>Velvet.VNode</c> cannot be resolved, every
    /// <c>[Component]</c> in the assembly is left unwoven — the warning names the assembly so that outcome is
    /// distinguishable from "nothing needed weaving".</item>
    /// <item>MetadataRegistrationWeaver: same contract when the module carries <c>[Component]</c> metadata to
    /// register but <c>Velvet.ComponentMethodRegistry</c> cannot be resolved.</item>
    /// </list>
    /// The weavers live in the <c>Unity.Velvet.CodeGen</c> editor assembly and are internal, so they are
    /// invoked through reflection; the probe modules are synthesized with Cecil and stripped of every
    /// assembly reference so resolution deterministically fails without consulting an assembly resolver.
    /// </summary>
    [TestFixture]
    internal sealed class WeaverResolutionDiagnosticsTests
    {
        private const string CodeGenAssemblyName = "Unity.Velvet.CodeGen";

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
            attribute.Properties.Add(new CustomAttributeNamedArgument(
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
    }
}
