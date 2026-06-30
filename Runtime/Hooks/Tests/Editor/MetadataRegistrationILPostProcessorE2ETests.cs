using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the metadata-registration weaver (the <c>Unity.Velvet.CodeGen</c> ILPostProcessor), which
    /// injects <c>ComponentMethodRegistry.Register*</c> calls into the module initializer
    /// (<c>&lt;Module&gt;.cctor</c>).
    /// <list type="bullet">
    /// <item>A <c>[Component(IsErrorBoundary = true)]</c> emits a <c>RegisterErrorBoundary</c> call; a
    /// <c>[Component(Memoize = true)]</c> emits a <c>RegisterMemoize</c> call; a
    /// <c>[Component(DisplayName = ...)]</c> emits a <c>RegisterComponentDisplayName</c> call carrying the name.</item>
    /// <item>The registered type key is the declaring type's runtime <c>Type.FullName</c>: a nested type uses
    /// its <c>'+'</c>-separated form and a generic type uses its <c>`</c>arity-suffixed form, so the key the
    /// weaver emits matches the key the runtime lookup compares against.</item>
    /// <item>A flagless <c>[Component]</c> produces no registration of any kind.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class MetadataRegistrationILPostProcessorE2ETests
    {
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
                    typeof(MetadataRegistrationILPostProcessorE2ETests).FullName, nameof(ErrorBoundaryComponent)),
                Is.True, "[Component(IsErrorBoundary = true)] registers an Error Boundary in <Module>.cctor");
        }

        [Test]
        public void Given_MemoizeComponent_When_Woven_Then_RegistersMemoize()
        {
            // Arrange
            var cctor = LoadModuleInitializer();

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterMemoize",
                    typeof(MetadataRegistrationILPostProcessorE2ETests).FullName, nameof(MemoizeComponent)),
                Is.True, "[Component(Memoize = true)] registers the props-bail gate in <Module>.cctor");
        }

        [Test]
        public void Given_DisplayNameComponent_When_Woven_Then_RegistersDisplayName()
        {
            // Arrange
            var cctor = LoadModuleInitializer();

            // Act + Assert
            Assert.That(RegistersThreeArg(cctor, "RegisterComponentDisplayName",
                    typeof(MetadataRegistrationILPostProcessorE2ETests).FullName, nameof(DisplayNameComponent),
                    "CustomDisplayName"),
                Is.True, "[Component(DisplayName = ...)] registers the display name in <Module>.cctor");
        }

        [Test]
        public void Given_FlaglessComponent_When_Woven_Then_IsNotRegisteredAsErrorBoundary()
        {
            // Arrange
            var cctor = LoadModuleInitializer();
            var typeName = typeof(MetadataRegistrationILPostProcessorE2ETests).FullName;

            // Act + Assert
            Assert.That(RegistersTwoArg(cctor, "RegisterErrorBoundary", typeName, nameof(PlainComponent)), Is.False,
                "A flagless [Component] is not registered as an Error Boundary");
        }

        [Test]
        public void Given_FlaglessComponent_When_Woven_Then_IsNotRegisteredAsMemoize()
        {
            // Arrange
            var cctor = LoadModuleInitializer();
            var typeName = typeof(MetadataRegistrationILPostProcessorE2ETests).FullName;

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
            var assemblyPath = typeof(MetadataRegistrationILPostProcessorE2ETests).Assembly.Location;
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
    }
}
