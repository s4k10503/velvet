using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when a type other than <see cref="ChildListWrites"/> calls a child-list writer of <c>VisualElement</c>,
    /// of one of its subclasses or of its <c>Hierarchy</c> (<c>ChildListWriters</c>), because a write made there leaves
    /// the <see cref="ContainerRows"/> of that container behind. Read from the compiled assembly, so a call is found by
    /// what it calls and not by its spelling, and only for the runtime assembly: <c>Velvet.Editor</c> and user code
    /// write child lists too. <c>PrivateSubtrees</c> names the types that write only into an element they built and
    /// own, which no walk addresses by slot. The control case reads the same scan over a type of this assembly that
    /// writes six ways, so a scan that stopped finding calls fails it. A second scan fails when a method of
    /// <c>MovedReaders</c> searches the live child list (<c>IndexOf</c>, or <see cref="LogicalChildSlots.IsInvisible"/>
    /// over each child) rather than reading the row model; its control reads a type that does both.
    /// </summary>
    [TestFixture]
    internal sealed class RowModelRoutingTests
    {
        private static readonly HashSet<string> ChildListWriters = new()
        {
            "Add", "Insert", "Remove", "RemoveAt", "RemoveFromHierarchy", "Clear", "Sort",
            "PlaceBehind", "PlaceInFront", "BringToFront", "SendToBack",
        };

        // Types that build a subtree of their own and write only into elements of it.
        private static readonly HashSet<string> PrivateSubtrees = new()
        {
        };

        // Methods whose reads of a container's child list come from its row model, so a search over the live list or a
        // walk testing each child for a slot must not come back into them. Grows as readers move.
        private static readonly HashSet<string> MovedReaders = new()
        {
            "Velvet.RingOverlay.PlaceBeside",
            "Velvet.FaceBorderOverlay.Place",
            "Velvet.BackdropFilterLayer.Place",
            "Velvet.BackdropFilterLayer.ImmediateRepaint",
            "Velvet.OutlineHosting.PlaceInTreeOrder",
            "Velvet.OutlineHosting.CompareTreeOrder",
            "Velvet.ParticlesDriver.SyncLayers",
            "Velvet.SilhouetteBoundsSpacer.IsAfterAllRenderedChildren",
            "Velvet.FiberZLayerCoordinator.TryGetLogicalPosition",
            "Velvet.FiberZLayerCoordinator.LastDeclaredChild",
            "Velvet.FiberVirtualListController.RebuildVisibleContainer",
        };

        private static bool IsListSearch(MethodReference callee)
            => (callee.Name == "IndexOf" && IsVisualElementType(callee.DeclaringType))
                || (callee.Name == "IsInvisible" && callee.DeclaringType.Name == nameof(LogicalChildSlots));

        private static List<string> SearchesIn(ModuleDefinition module, Func<MethodDefinition, bool> included)
            => module.GetTypes()
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody && included(method))
                .SelectMany(method => method.Body.Instructions
                    .Select(instruction => instruction.Operand as MethodReference)
                    .Where(callee => callee != null && IsListSearch(callee))
                    .Select(callee => $"{method.DeclaringType.FullName}.{method.Name} calls "
                        + $"{callee!.DeclaringType.Name}.{callee.Name}"))
                .Distinct()
                .OrderBy(line => line)
                .ToList();

        // Resolved through the loaded assemblies, which Cecil's own resolver cannot reach for engine types.
        private static bool IsVisualElementType(TypeReference? type)
        {
            if (type == null) return false;
            var assembly = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(each => each.GetName().Name == type.Scope.Name);
            var resolved = assembly?.GetType(type.FullName.Replace('/', '+'));
            return resolved != null
                && (typeof(UnityEngine.UIElements.VisualElement).IsAssignableFrom(resolved)
                    || resolved == typeof(UnityEngine.UIElements.VisualElement.Hierarchy));
        }

        private static string? OuterName(TypeDefinition type)
        {
            var outer = type;
            while (outer.DeclaringType != null) outer = outer.DeclaringType;
            return outer.FullName;
        }

        private static List<string> OffendersIn(ModuleDefinition module, Func<TypeDefinition, bool> included)
            => module.GetTypes()
                .Where(included)
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions
                    // Any operand, so a method group made a delegate is read as well as a call.
                    .Select(instruction => instruction.Operand as MethodReference)
                    .Where(callee => callee != null && ChildListWriters.Contains(callee.Name)
                        && IsVisualElementType(callee.DeclaringType))
                    .Select(callee => $"{method.DeclaringType.FullName}.{method.Name} calls "
                        + $"{callee!.DeclaringType.Name}.{callee.Name}"))
                .Distinct()
                .OrderBy(line => line)
                .ToList();

        // Six ways to write a child list, none through ChildListWrites.
        private static class Offender
        {
            internal static void Add(VisualElement parent, VisualElement child) => parent.Add(child);
            internal static void HierarchyAdd(VisualElement parent, VisualElement child) => parent.hierarchy.Add(child);
            internal static void Insert(VisualElement parent, VisualElement child) => parent.Insert(0, child);
            internal static void Detach(VisualElement child) => child.RemoveFromHierarchy();
            internal static void Reorder(VisualElement child) => child.BringToFront();
            internal static Action<VisualElement> MethodGroup(VisualElement parent) => parent.Add;
        }

        // Two list searches, one of each kind the scan reads.
        private static class SearchingReader
        {
            internal static int Index(VisualElement parent, VisualElement child) => parent.IndexOf(child);
            internal static bool Skips(VisualElement child) => LogicalChildSlots.IsInvisible(child);
        }

        [Test]
        public void Given_TheRuntimeAssembly_When_TheMovedReadersAreRead_Then_NoneSearchesTheLiveChildList()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);

            // Act
            var offenders = SearchesIn(runtime,
                method => MovedReaders.Contains($"{method.DeclaringType.FullName}.{method.Name}"));

            // Assert
            Assert.That(string.Join("\n", offenders), Is.Empty, "read the child list through ChildListWrites");
        }

        [Test]
        public void Given_ReadersThatSearchTheLiveListTwoWays_When_TheScanReadsThem_Then_ItFindsBoth()
        {
            // Arrange
            using var tests = ModuleDefinition.ReadModule(typeof(RowModelRoutingTests).Assembly.Location);

            // Act
            var offenders = SearchesIn(tests, method => method.DeclaringType.Name == nameof(SearchingReader));

            // Assert
            Assert.That(offenders, Has.Count.EqualTo(2));
        }

        [Test]
        public void Given_TheRuntimeAssembly_When_ItsCallsAreRead_Then_OnlyChildListWritesWritesAChildList()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var exempt = new HashSet<string>(PrivateSubtrees) { typeof(ChildListWrites).FullName! };

            // Act
            var offenders = OffendersIn(runtime, type => !exempt.Contains(OuterName(type)!));

            // Assert
            Assert.That(string.Join("\n", offenders), Is.Empty, "write the child list through ChildListWrites");
        }

        [Test]
        public void Given_ATypeThatWritesAChildListSixWays_When_TheScanReadsIt_Then_ItFindsEveryWay()
        {
            // Arrange
            using var tests = ModuleDefinition.ReadModule(typeof(RowModelRoutingTests).Assembly.Location);

            // Act
            var offenders = OffendersIn(tests, type => OuterName(type) == typeof(RowModelRoutingTests).FullName
                && type.Name.StartsWith("Offender", StringComparison.Ordinal));

            // Assert
            Assert.That(offenders, Has.Count.EqualTo(6));
        }
    }
}
