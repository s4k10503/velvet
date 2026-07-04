using System;
using NUnit.Framework;
using Velvet;
using Velvet.TestUtilities;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that keyed child reconciliation skips patching a child whose new VNode is the SAME
    /// instance as the old one. An immutable VNode reused across renders (as auto-memoization hands
    /// back the cached instance) produces identical output, so re-patching it is wasted work — the
    /// indexed path already short-circuits on reference identity, and the keyed Pass 1 (linear prefix)
    /// and Pass 2 (map lookup) paths must do the same.
    /// <list type="bullet">
    /// <item>A keyed prefix re-reconciled with the same node instances patches none of them: the
    /// reference-identity check bypasses PatchNode, so a per-element ref callback does not re-fire.</item>
    /// <item>A keyed reorder that reuses the same node instances likewise skips the per-node patch
    /// while still re-placing the elements into the new order.</item>
    /// </list>
    /// The ref callback is the observable probe: PatchNode re-invokes it on every patch, so an
    /// unchanged invocation count proves the patch was skipped.
    /// </summary>
    [TestFixture]
    internal sealed class ChildReferenceIdentitySkipTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_KeyedPrefix_When_ReReconciledWithSameInstances_Then_PatchNodeIsSkipped()
        {
            // Arrange
            var refInvocations = 0;
            Func<VisualElement, Action> probe = _ => { refInvocations++; return null; };
            var a = V.Div(key: "a", refCallback: probe);
            var b = V.Div(key: "b", refCallback: probe);
            var tree1 = new VNode[] { a, b };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var afterMount = refInvocations;
            Assume.That(afterMount, Is.EqualTo(2), "Precondition: each keyed child's ref callback fires once on mount");

            // Act — re-reconcile the same keyed prefix with the SAME VNode instances
            var tree2 = new VNode[] { a, b };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(refInvocations, Is.EqualTo(afterMount),
                "Reference-identical keyed children skip PatchNode, so the ref callback does not re-fire");
        }

        [Test]
        public void Given_KeyedReorder_When_ReusingSameInstances_Then_PatchNodeIsSkipped()
        {
            // Arrange
            var refInvocations = 0;
            Func<VisualElement, Action> probe = _ => { refInvocations++; return null; };
            var a = V.Div(key: "a", refCallback: probe);
            var b = V.Div(key: "b", refCallback: probe);
            var c = V.Div(key: "c", refCallback: probe);
            var tree1 = new VNode[] { a, b, c };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var afterMount = refInvocations;
            Assume.That(afterMount, Is.EqualTo(3), "Precondition: each keyed child's ref callback fires once on mount");

            // Act — reorder so the head mismatch forces the Pass 2 map lookup, reusing the SAME instances
            var tree2 = new VNode[] { c, a, b };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(refInvocations, Is.EqualTo(afterMount),
                "A keyed reorder that reuses the same VNode instances skips PatchNode on each retained child");
        }
    }
}
