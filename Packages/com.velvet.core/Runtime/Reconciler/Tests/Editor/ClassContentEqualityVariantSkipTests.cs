using System;
using System.Reflection;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the class-content fast-path in <c>SyncClassDrivenStyling</c>: a re-render that supplies a
    /// content-identical but FRESHLY ALLOCATED ClassNames array — the shape any component that rebuilds
    /// its VNode tree each render produces (and the Motion variant path produces unconditionally, since
    /// resolving an unchanged active label still merges a new array) — must NOT re-derive the variant
    /// manipulators. Re-derivation is observed through the <c>StyleVariantManipulator</c>'s private
    /// hover-payload array: a re-derivation always installs a freshly extracted array via
    /// <c>UpdatePayloads</c>, so the array instance surviving the patch proves the whole
    /// <c>ApplyVariantManipulators</c> cascade was skipped. The payload store is a private field, hence
    /// reflection inside the test. GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class ClassContentEqualityVariantSkipTests
    {
        // Builds the leaf with a FRESH ClassNames array per call. Deliberately bypasses V.Div: its
        // ParseClassNames cache returns a reference-stable array for a constant className string, which
        // would hit the ReferenceEquals fast-path and mask the content-equality case under test.
        private static VNode[] Tree() => new VNode[]
        {
            new ElementNode
            {
                Name = "leaf",
                ClassNames = new[] { "p-4", "hover:bg-red-500" },
            },
        };

        // Reads the manipulator's derived hover-payload array. A private field is the only observation
        // point: the manipulator instance itself is reused by design, so instance identity cannot
        // distinguish "derivation skipped" from "derivation re-ran and updated the same instance".
        private static string[] HoverPayloadsOf(StyleVariantManipulator manipulator)
        {
            var field = typeof(StyleVariantManipulator).GetField("_hover", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not find StyleVariantManipulator._hover. The payload field may have been renamed.");
            return (string[])field.GetValue(manipulator);
        }

        [Test]
        public void Given_AMountedHoverVariantLeaf_When_RepatchedWithAContentIdenticalFreshClassArray_Then_TheVariantManipulatorIsNotRederived()
        {
            // Arrange — mount the leaf, then capture the manipulator's derived hover-payload array.
            using var scope = new ReconcilerScope();
            var oldTree = Tree();
            var newTree = Tree();
            Assume.That(
                ReferenceEquals(((ElementNode)oldTree[0]).ClassNames, ((ElementNode)newTree[0]).ClassNames),
                Is.False,
                "Precondition: the two renders carry distinct (content-identical) class array instances");
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var leaf = scope.Root[0];
            Assume.That(scope.Reconciler.Context.VariantManipulators.ContainsKey(leaf), Is.True,
                "Precondition: the hover: leaf is tracked while mounted");
            var manipulator = scope.Reconciler.Context.VariantManipulators[leaf];
            var payloadsBefore = HoverPayloadsOf(manipulator);

            // Act — patch with the content-identical, freshly allocated class array.
            scope.Reconciler.Reconcile(scope.Root, oldTree, newTree);
            Assume.That(scope.Root[0], Is.SameAs(leaf),
                "Premise guard: the leaf was patched in place — a replacement would trivially keep the " +
                "captured manipulator's payloads and mask a broken fast-path");

            // Assert — the derivation was skipped: a re-derivation would have installed a freshly
            // extracted payload array, so the surviving instance proves the content fast-path held.
            Assert.That(ReferenceEquals(payloadsBefore, HoverPayloadsOf(manipulator)), Is.True,
                "A content-identical class array must not re-derive the variant manipulator payloads");
        }
    }
}
