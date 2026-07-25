using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="FiberStack"/>, the cursor that tracks the currently rendering
    /// <see cref="ComponentFiber"/> during a reconcile pass.
    /// <list type="bullet">
    /// <item>A fresh stack has no current fiber.</item>
    /// <item><c>Push</c> makes the pushed fiber the current one; with several pushed, the most recently pushed
    /// fiber is current (last-in, first-out).</item>
    /// <item><c>Pop</c> restores the previously pushed fiber as current, and emptying the stack returns the
    /// current fiber to none.</item>
    /// <item>Pushing a null fiber is rejected with <see cref="ArgumentNullException"/>.</item>
    /// <item>Popping an empty stack is rejected with <see cref="InvalidOperationException"/>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class FiberStackTests
    {
        [Test]
        public void Given_FreshStack_When_Inspected_Then_HasNoCurrentFiber()
        {
            // Act
            var stack = new FiberStack();

            // Assert
            Assert.That(stack.Current, Is.Null);
        }

        [Test]
        public void Given_EmptyStack_When_OneFiberPushed_Then_ThatFiberIsCurrent()
        {
            // Arrange
            var stack = new FiberStack();
            var fiber = new ComponentFiber();

            // Act
            stack.Push(fiber);

            // Assert
            Assert.That(stack.Current, Is.SameAs(fiber));
        }

        [Test]
        public void Given_TwoFibersPushed_When_Inspected_Then_LastPushedIsCurrent()
        {
            // Arrange
            var stack = new FiberStack();
            var outer = new ComponentFiber();
            var inner = new ComponentFiber();

            // Act
            stack.Push(outer);
            stack.Push(inner);

            // Assert
            Assert.That(stack.Current, Is.SameAs(inner));
        }

        [Test]
        public void Given_TwoFibersPushed_When_Popped_Then_PreviousFiberBecomesCurrent()
        {
            // Arrange
            var stack = new FiberStack();
            var outer = new ComponentFiber();
            var inner = new ComponentFiber();
            stack.Push(outer);
            stack.Push(inner);

            // Act
            stack.Pop();

            // Assert
            Assert.That(stack.Current, Is.SameAs(outer));
        }

        [Test]
        public void Given_SingleFiberPushed_When_PoppedToEmpty_Then_HasNoCurrentFiber()
        {
            // Arrange
            var stack = new FiberStack();
            stack.Push(new ComponentFiber());

            // Act
            stack.Pop();

            // Assert
            Assert.That(stack.Current, Is.Null);
        }

        [Test]
        public void Given_AnyStack_When_NullFiberPushed_Then_ThrowsArgumentNullException()
        {
            // Arrange
            var stack = new FiberStack();

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => stack.Push(null));
        }

        [Test]
        public void Given_EmptyStack_When_Popped_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var stack = new FiberStack();

            // Act + Assert
            Assert.Throws<InvalidOperationException>(() => stack.Pop());
        }
    }

#nullable enable
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
#nullable restore
}
