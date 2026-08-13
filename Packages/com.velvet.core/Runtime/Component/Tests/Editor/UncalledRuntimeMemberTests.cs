using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when an assembly-visible runtime method has no caller in the shipped assemblies, because the
    /// only thing left that can reach it is a test — which is the seam CLAUDE.md forbids under
    /// <c>Runtime/</c>, whatever the member is called and whether or not it admits to it.
    /// <para>
    /// This is the property <c>TestOnlyMemberConventionTests</c> approximates from two directions. Its name
    /// axis catches a member called <c>*ForTest</c> and its prose axis one whose comment says so, and a
    /// member that is neither walks past both. Nothing here reads a name or a comment.
    /// </para>
    /// <para>
    /// Every exemption below is derived from the code rather than listed. A hand-kept list of "reached some
    /// other way" would be the same mirror this fixture exists to replace, and would go stale in the
    /// direction that passes.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class UncalledRuntimeMemberTests
    {
        // The engine calls these itself, so no call site exists to find. Matched on the attribute type's
        // name so a Unity namespace move does not silently drop an exemption and fail an innocent member.
        private static readonly string[] EngineInvokedAttributes =
        {
            "RuntimeInitializeOnLoadMethodAttribute",
            "InitializeOnLoadMethodAttribute",
            "InitializeOnEnterPlayModeAttribute",
            "DidReloadScriptsAttribute",
            "MenuItemAttribute",
            "PreserveAttribute",
        };

        private static string ShippedAssembly(string name)
        {
            var directory = Path.GetDirectoryName(typeof(V).Assembly.Location)!;
            return Path.Combine(directory, name + ".dll");
        }

        /// <summary>Every method any instruction in the shipped assemblies names, by its own definition.</summary>
        /// <remarks>
        /// Resolved to the element method because a generic call site carries its instantiation in
        /// <c>FullName</c> and would never match the declaration — which is most of what a first pass
        /// reports. A reference into another assembly resolves to null and is simply not ours.
        /// </remarks>
        private static HashSet<string> CalledMethods(IEnumerable<ModuleDefinition> modules)
        {
            var called = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in modules)
            {
                foreach (var method in module.GetTypes().SelectMany(type => type.Methods))
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is not MethodReference reference)
                        {
                            continue;
                        }
                        var definition = SafeResolve(reference);
                        called.Add((definition ?? reference.GetElementMethod()).FullName);
                    }
                }
            }
            return called;
        }

        private static MethodDefinition? SafeResolve(MethodReference reference)
        {
            try
            {
                return reference.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }

        /// <summary>Every string literal in the shipped assemblies, which is how a reflective call names its target.</summary>
        private static HashSet<string> LiteralStrings(IEnumerable<ModuleDefinition> modules) =>
            new(modules
                    .SelectMany(module => module.GetTypes())
                    .SelectMany(type => type.Methods)
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions)
                    .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
                    .Select(instruction => (string)instruction.Operand),
                StringComparer.Ordinal);

        private static bool EngineInvoked(MethodDefinition method) =>
            method.CustomAttributes.Any(attribute =>
                EngineInvokedAttributes.Contains(attribute.AttributeType.Name, StringComparer.Ordinal));

        private static bool Callable(MethodDefinition method) =>
            // Public is library surface a consumer calls, and PublicApiSurfaceTests is what holds it honest.
            // Private cannot be reached from a test assembly except by reflection, which is the sanctioned
            // route. What is left is assembly-visible: reachable from a test because of InternalsVisibleTo,
            // and from nothing else outside the package.
            method.IsAssembly
            && !method.IsConstructor
            // A property accessor counts. An assembly-visible setter nothing calls is a member whose value
            // is only ever the one the field initializer left, and reading it back reports that value to a
            // caller who has no way to have changed it — which is what an unwired seam looks like from the
            // outside.
            && !method.IsAddOn && !method.IsRemoveOn
            // An override or an implementation is reached through the declaration it satisfies, and the
            // call site names that one.
            && !method.IsVirtual && !method.IsAbstract
            && !method.HasOverrides;

        [Test]
        public void Given_TheShippedAssemblies_When_TheirCallGraphIsRead_Then_NoAssemblyVisibleMethodIsUnreachable()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            using var editor = ModuleDefinition.ReadModule(ShippedAssembly("Velvet.Editor"));
            var modules = new[] { runtime, editor };
            var called = CalledMethods(modules);
            var literals = LiteralStrings(modules);

            // Act
            var unreachable = runtime.GetTypes()
                // A compiler-generated closure or state machine is named by its enclosing method's body and
                // never declared by hand, so an unreachable one is a fact about the compiler.
                .Where(type => !type.Name.Contains('<') && type.Namespace?.StartsWith("Velvet", StringComparison.Ordinal) == true)
                .SelectMany(type => type.Methods)
                .Where(Callable)
                .Where(method => !called.Contains(method.FullName))
                .Where(method => !EngineInvoked(method))
                // A name spelled as a literal is how GetMethod finds its target; the call graph cannot see
                // that edge, so the literal stands in for it.
                .Where(method => !literals.Contains(method.Name))
                .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert — the call count rides along because an empty graph leaves everything unreachable and
            // a graph that failed to load would report the opposite, and neither is this passing.
            Assert.That((called.Count > 1000, string.Join("\n", unreachable)), Is.EqualTo((true, string.Empty)),
                "nothing in the shipped assemblies calls these, so a test is the only thing that can; move "
                + "each to a TestUtilities reflection helper");
        }
    }
}
