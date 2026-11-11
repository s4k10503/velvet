using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when the walk writes a child list itself rather than through <see cref="HostCommitQueue"/>: a call, in
    /// <see cref="ChildReconciler"/>, <see cref="GeneralPathReconciler"/> or <see cref="ChildElementPlacement"/> or
    /// in a type the compiler generated inside one of them, to a child-list writer of <c>VisualElement</c> or of its
    /// <c>Hierarchy</c> (<c>ChildListWriters</c>), or to <see cref="FiberElementCleaner"/>'s removal, which the queue
    /// is what calls. Read from the compiled assembly, so a call is found by what it calls and not by its spelling.
    /// </summary>
    [TestFixture]
    internal sealed class HostCommitRoutingTests
    {
        private static readonly string[] ChildListTypes =
        {
            "UnityEngine.UIElements.VisualElement",
            "UnityEngine.UIElements.VisualElement/Hierarchy",
        };

        private static readonly HashSet<string> ChildListWriters = new()
        {
            "Add", "Insert", "Remove", "RemoveAt", "RemoveFromHierarchy", "Clear", "Sort",
            "PlaceBehind", "PlaceInFront", "BringToFront", "SendToBack",
        };

        private static readonly string[] Walkers =
        {
            typeof(ChildReconciler).FullName!,
            typeof(GeneralPathReconciler).FullName!,
            typeof(ChildElementPlacement).FullName!,
        };

        private static bool WritesAChildList(MethodReference callee)
            => (ChildListTypes.Contains(callee.DeclaringType.FullName) && ChildListWriters.Contains(callee.Name))
                || (callee.DeclaringType.FullName == typeof(FiberElementCleaner).FullName
                    && callee.Name == nameof(FiberElementCleaner.RemoveElement));

        // The walker a type is, or is declared inside: a lambda's closure and a nested struct are types of their own.
        private static string? WalkerOf(TypeDefinition type)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (Walkers.Contains(t.FullName)) return t.FullName;
            }
            return null;
        }

        [Test]
        public void Given_TheWalkersOfAReconcile_When_TheirCallsAreRead_Then_NoneWritesAChildListItself()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var types = runtime.GetTypes().Where(type => WalkerOf(type) != null).ToList();

            // Act
            var missing = Walkers.Where(walker => types.All(type => WalkerOf(type) != walker))
                .Select(walker => "missing: " + walker);
            var offenders = types
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions
                    // Any operand, so a method group made a delegate is read as well as a call.
                    .Select(instruction => instruction.Operand as MethodReference)
                    .Where(callee => callee != null && WritesAChildList(callee))
                    .Select(callee => $"{method.DeclaringType.FullName}.{method.Name} calls "
                        + $"{callee!.DeclaringType.Name}.{callee.Name}"))
                .Distinct()
                .OrderBy(line => line);

            // Assert — a walker that moved or was renamed would scan as clean, so its absence is reported in the
            // same text.
            Assert.That(string.Join("\n", missing.Concat(offenders)), Is.Empty,
                "write the child list through HostCommitQueue");
        }
    }
}
