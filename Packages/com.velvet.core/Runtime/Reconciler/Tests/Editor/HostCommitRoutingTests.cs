using System;
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

        // GREEN_ON_BASE(characterization): the base's walkers write no child list themselves either; the case is on trial for the shared table beside it that changed.
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

        private static bool IsOrIsInside(TypeDefinition type, string fullName)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (t.FullName == fullName) return true;
            }
            return false;
        }

        // GREEN_ON_BASE(characterization): the base's cleaner reaches the removal only through the queue as well; the case is on trial for the shared table beside it that changed.
        [Test]
        public void Given_TheCleanerOfARemovedRow_When_ItsCallsAreRead_Then_ItWritesTheRowsRemovalOnlyThroughTheQueue()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var calls = runtime.GetTypes()
                .Where(type => IsOrIsInside(type, typeof(FiberElementCleaner).FullName!))
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Select(instruction => instruction.Operand as MethodReference)
                .Where(callee => callee != null)
                .Select(callee => callee!.DeclaringType.Name + "." + callee.Name)
                .ToList();

            // Act
            var direct = calls
                .Where(call => call == nameof(ChildListWrites) + "." + nameof(ChildListWrites.RemoveAt)
                    || call == nameof(ChildListWrites) + "." + nameof(ChildListWrites.RemoveRow))
                .Select(call => "writes the list itself: " + call);
            var missing = calls.Contains(nameof(HostCommitQueue) + "." + nameof(HostCommitQueue.RemoveRow))
                ? Enumerable.Empty<string>() : new[] { "missing: HostCommitQueue.RemoveRow" };

            // Assert — a cleaner that moved or was renamed would scan as clean, so what the queue's call is missing
            // is reported in the same text.
            Assert.That(string.Join("\n", direct.Distinct().Concat(missing)), Is.Empty);
        }

        // The methods that read only a child list, or write one, and so answer from the row model and record a write
        // behind the ones recorded already. Realign (the engine's layout list) and HierarchySort (the order of the live
        // list) act on the live list, and none is listed, nor is Exchange, which ends in HierarchySort.
        private static readonly (Type Type, string Method)[] ModelSites =
        {
            (typeof(ChildListWrites), "InsertCore"),
            (typeof(ChildListWrites), nameof(ChildListWrites.Remove)),
            (typeof(ChildListWrites), nameof(ChildListWrites.HierarchyRemove)),
            (typeof(ChildListWrites), nameof(ChildListWrites.RemoveAt)),
            (typeof(ChildListWrites), nameof(ChildListWrites.RemoveFromHierarchy)),
            (typeof(ChildListWrites), nameof(ChildListWrites.Clear)),
            (typeof(FiberClipPathApplier), "WrapClipPathInPlace"),
            (typeof(WrapperInfrastructure), nameof(WrapperInfrastructure.RemoveWrapperRestoreInner)),
        };

        private static readonly HashSet<string> Appliers = new() { "Apply", "HierarchySort" };

        // GREEN_ON_BASE(characterization): the base's model sites call neither name the set keeps.
        [Test]
        public void Given_TheSitesThatAnswerFromTheRowModel_When_TheirCallsAreRead_Then_NoneAppliesTheRecordedWrites()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var sites = ModelSites.Select(site => (Site: site, Method: runtime.GetType(site.Type.FullName)
                .Methods.SingleOrDefault(method => method.Name == site.Method && method.HasBody))).ToList();

            // Act
            var missing = sites.Where(site => site.Method == null).Select(site => "missing: " + site.Site.Type.Name + "." + site.Site.Method);
            var offenders = sites.Where(site => site.Method != null)
                .SelectMany(site => site.Method!.Body.Instructions
                    .Select(instruction => instruction.Operand as MethodReference)
                    .Where(callee => callee != null && callee.DeclaringType.FullName == typeof(ChildListWrites).FullName
                        && Appliers.Contains(callee.Name))
                    .Select(callee => $"{site.Site.Type.Name}.{site.Site.Method} calls {callee!.Name}"))
                .Distinct()
                .OrderBy(line => line);

            // Assert — a site that moved or was renamed would scan as clean, so its absence is reported in the same text.
            Assert.That(string.Join("\n", missing.Concat(offenders)), Is.Empty);
        }

        // The calls FinishPass makes that write a child list or reconcile into one other than through the queue: the
        // portal drain, the z-layer settle of what the pass patched, the pooled returns of the rows it removed (which
        // detach the inner of a wrapper), the retired z-layer reals, the z-layer teardown and arrangement, and the two
        // placement drains.
        private static readonly string[] FinishPassPhase =
        {
            nameof(ChildReconciler) + "." + nameof(ChildReconciler.DrainPendingPortalMounts),
            nameof(FiberZLayerCoordinator) + "." + nameof(FiberZLayerCoordinator.SettlePatched),
            nameof(FiberElementCleaner) + "." + nameof(FiberElementCleaner.ReleasePendingPoolReturns),
            nameof(FiberElementCleaner) + "." + nameof(FiberElementCleaner.ReleaseRetiredZLayerReals),
            nameof(FiberZLayerCoordinator) + "." + nameof(FiberZLayerCoordinator.DrainTeardowns),
            nameof(FiberZLayerCoordinator) + "." + nameof(FiberZLayerCoordinator.ResortLayers),
            nameof(RingOverlay) + "." + nameof(RingOverlay.DrainPendingPlacements),
            nameof(BackdropFilterLayer) + "." + nameof(BackdropFilterLayer.DrainPendingPlacements),
        };

        // GREEN_ON_BASE(characterization): the base applies the recorded writes ahead of the writers FinishPass names as well; the case is on trial for the shared table beside it that changed.
        [Test]
        public void Given_FinishPass_When_ItsCallsAreRead_Then_TheRecordedWritesAreAppliedBeforeTheWritersItNames()
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
