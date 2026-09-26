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
    /// Specifies that each path pushing a rendering fiber onto the stack takes it off again: disposing a
    /// mounted tree, the strict-mode diagnostic render, and the resume of a parked time-sliced render each
    /// leave the stack as they found it.
    /// </summary>
    [TestFixture]
    internal sealed class FiberStackBalanceTests
    {
        private static ComponentFiber? s_listFiber;
        private static int s_listTick;
        private static int s_leafRenders;

        [SetUp]
        public void SetUp()
        {
            s_listFiber = null;
            s_listTick = 0;
            s_leafRenders = 0;
            FiberStrictMode.Enabled = false;
        }

        [TearDown]
        public void TearDown() => FiberStrictMode.Enabled = false;

        // GREEN_ON_BASE(characterization): the merge base pops the fiber its unmount reconcile pushes.
        // Dropping the pop through the stack PushFiber hands back is what leaves it on the stack.
        [Test]
        public void Given_AMountedTree_When_ItIsDisposed_Then_NoFiberIsLeftOnTheStack()
        {
            // Arrange
            var root = new UnityEngine.UIElements.VisualElement();
            var mounted = V.Mount(root, V.Component(NestingHostRender, key: "host"));
            var stack = mounted.Root.Reconciler!.Context.FiberStack;

            // Act
            mounted.Dispose();

            // Assert — the row count says the unmount reconcile that pushes the fiber ran.
            Assert.That("rows " + root.childCount + ", stack depth " + stack.Depth, Is.EqualTo("rows 0, stack depth 0"));
        }

        // GREEN_ON_BASE(characterization): the merge base pops the fiber its strict-mode diagnostic render pushes.
        // Dropping the pop through the stack PushFiber hands back is what leaves it on the stack.
        [Test]
        public void Given_StrictMode_When_ANestedComponentRenders_Then_NoFiberIsLeftOnTheStack()
        {
            // Arrange
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(new UnityEngine.UIElements.VisualElement(), V.Component(NestingHostRender, key: "host"));

            // Assert — the render count says the diagnostic render ran beside the committed one.
            Assert.That("leaf rendered " + s_leafRenders + ", stack depth " + mounted.Root.Reconciler!.Context.FiberStack.Depth,
                Is.EqualTo("leaf rendered 2, stack depth 0"));
        }

        // GREEN_ON_BASE(characterization): the merge base pops the fiber a resumed time-sliced render pushes.
        // Dropping the pop through the stack PushFiber hands back is what leaves it on the stack.
        [Test]
        public void Given_AParkedTimeSlicedRender_When_ItIsResumed_Then_NoFiberIsLeftOnTheStack()
        {
            // Arrange
            using var mounted = V.Mount(new UnityEngine.UIElements.VisualElement(), V.Component(ListRender, key: "list"));
            s_listTick = 1;
            s_listFiber!.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_listFiber.HasPendingReconcileWorkForTest();

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That((parked ? "parked" : "not parked") + ", stack depth " + mounted.Root.Reconciler!.Context.FiberStack.Depth,
                Is.EqualTo("parked, stack depth 0"));
        }

        [Component(Compiler = false)]
        private static VNode LeafRender()
        {
            s_leafRenders++;
            return V.Label(text: "leaf");
        }

        [Component(Compiler = false)]
        private static VNode NestingHostRender()
            => V.Div(children: new VNode[] { V.Component(LeafRender, key: "leaf") });

        [Component(Compiler = false)]
        private static VNode ListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            var children = new VNode[4];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = V.Label(text: "item-" + i + ":" + s_listTick, key: "item" + i);
            }
            return V.Div(children: children);
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
