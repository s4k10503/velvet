using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when the walk writes a child list itself rather than through <see cref="HostCommitQueue"/>: a call, in
    /// <see cref="ChildReconciler"/>, <see cref="GeneralPathReconciler"/>, <see cref="ChildElementPlacement"/> or
    /// <see cref="NodePatchWalk"/> or in a type the compiler generated inside one of them, to a child-list writer of
    /// <c>VisualElement</c> or of its <c>Hierarchy</c> (<c>ChildListWriters</c>), or to
    /// <see cref="FiberElementCleaner"/>'s removal, which the queue is what calls. Read from the compiled assembly, so
    /// a call is found by what it calls and not by its spelling.
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
            typeof(NodePatchWalk).FullName!,
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

        // The calls FinishPass makes that write a child list or reconcile into one other than through the queue: the
        // portal drain, the retired z-layer reals and the z-layer arrangement, and the two placement drains.
        private static readonly string[] FinishPassPhase =
        {
            nameof(ChildReconciler) + "." + nameof(ChildReconciler.DrainPendingPortalMounts),
            nameof(FiberElementCleaner) + "." + nameof(FiberElementCleaner.ReleaseRetiredZLayerReals),
            nameof(FiberZLayerCoordinator) + "." + nameof(FiberZLayerCoordinator.ResortLayers),
            nameof(RingOverlay) + "." + nameof(RingOverlay.DrainPendingPlacements),
            nameof(BackdropFilterLayer) + "." + nameof(BackdropFilterLayer.DrainPendingPlacements),
        };

        [Test]
        public void Given_FinishPass_When_ItsCallsAreRead_Then_TheRecordedWritesAreAppliedBeforeEveryChildListWriterItReaches()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var finishPass = runtime.GetType(typeof(Reconciler).FullName).Methods.Single(method => method.Name == "FinishPass");
            var calls = finishPass.Body.Instructions
                .Where(instruction => instruction.Operand is MethodReference)
                .Select(instruction => (Offset: instruction.Offset,
                    Name: ((MethodReference)instruction.Operand).DeclaringType.Name + "." + ((MethodReference)instruction.Operand).Name))
                .ToList();
            var apply = calls.Where(call => call.Name == nameof(HostCommitQueue) + "." + nameof(HostCommitQueue.EndRecordedPass)).Select(call => call.Offset)
                .DefaultIfEmpty(int.MaxValue).Min();

            // Act
            var early = FinishPassPhase
                .Where(name => calls.Any(call => call.Name == name && call.Offset < apply))
                .ToList();
            var missing = FinishPassPhase.Where(name => calls.All(call => call.Name != name)).Select(name => "missing: " + name);

            // Assert
            Assert.That(string.Join("\n", early.Select(name => "before the apply: " + name).Concat(missing)), Is.Empty);
        }
    }
}
