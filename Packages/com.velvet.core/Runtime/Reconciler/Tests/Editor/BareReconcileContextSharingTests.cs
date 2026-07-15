#nullable enable
using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a ComponentNode nested inside a tree reconciled via a bare
    /// Reconciler.Reconcile() call (not V.Mount) shares the caller's own ReconcilerContext instead
    /// of its fiber bootstrapping an orphaned, unrelated one — verified via an error boundary's
    /// SetAborted() call reaching the SAME context the caller reads IsAborted from. A bare
    /// Reconcile() call leaves FiberStack empty before the nested ComponentNode is registered, so
    /// its fiber.Parent stays null; ComponentRegistry must hand it the context it is itself running
    /// inside of rather than deriving one from that null parent.
    /// </summary>
    [TestFixture]
    internal sealed class BareReconcileContextSharingTests : ReconcilerTestFixture
    {
        private static bool s_fallbackShown;

        public override void SetUp()
        {
            base.SetUp();
            s_fallbackShown = false;
        }

        [Test]
        public void Given_ABareReconcileWithNestedErrorBoundary_When_ItsChildThrows_Then_TheAbortIsObservedOnTheCallersOwnContext()
        {
            // Arrange — Reconciler.Reconcile is called directly (not via V.Mount), so nothing is
            // pushed onto FiberStack before the nested ComponentNode is expanded during Reconcile.
            var newTree = new VNode[] { V.Component(BoundaryWrappingThrowerRender) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), newTree);

            // Assert — LastTopLevelWasAborted snapshots the caller's OWN _ctx.IsAborted right before
            // Reconciler.Reconcile's top-level pass resets it for the next call (Reconciler.cs), so
            // this observes whether the boundary's SetAborted() reached the SAME context the caller's
            // Reconcile() is running under, instead of an orphaned one silently absorbing it.
            Assert.That((s_fallbackShown, Reconciler.LastTopLevelWasAborted), Is.EqualTo((true, true)));
        }

        #region BoundaryWrappingThrower component (boundary + Hooks.UseFallback wrapping a throwing child)

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWrappingThrowerRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(text: "caught");
            });
            return V.Component(ThrowingChildRender, key: "throwing-child");
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new Exception("boom-child");

        #endregion
    }
}
