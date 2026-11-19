using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseImperativeHandle"/> in a function component.
    /// <list type="bullet">
    /// <item>On the first render the factory runs once and the produced handle is assigned to the parent-supplied
    /// <see cref="Ref{T}"/>.</item>
    /// <item>Unchanged deps across a re-render do not re-invoke the factory; changed deps re-invoke it and assign the
    /// new handle.</item>
    /// <item>A render-phase re-run whose committed deps are unchanged does not rebuild the handle and leaves the
    /// parent ref pointing at the committed handle (referential stability).</item>
    /// <item>When the parent swaps the supplied ref, the old ref is cleared and the handle is assigned into the new
    /// ref; swapping the ref to null detaches from the old ref.</item>
    /// <item>With no ref supplied the hook still evaluates the factory and performs no assignment.</item>
    /// <item>A null factory raises an <see cref="ArgumentNullException"/>, on the first render and when the factory
    /// becomes null on a later render.</item>
    /// <item>Unmount clears the parent ref via the registered detach.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// A component without a fallback emits render exceptions through the root path via <c>Debug.LogException</c>, so
    /// the null-factory cases are captured with <c>LogAssert</c>. The parent ref swap is expressed via a parent
    /// component that re-renders and changes the child's <c>componentRef</c>. Static captures are reset together in
    /// <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseImperativeHandleTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetHandle();
            ResetRefSwitcher();
            ResetFactoryToggle();
        }

        [Test]
        public void Given_FirstRender_When_Mounted_Then_FactoryRunsExactlyOnce()
        {
            // Arrange
            var handle = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_handleFactory = () => { s_handleFactoryCallCount++; return handle; };

            // Act
            using var mounted = V.Mount(_root, V.Component(HandleRender, componentRef: handleRef, key: "h"));

            // Assert
            Assert.That(s_handleFactoryCallCount, Is.EqualTo(1), "The factory runs once on the first render");
        }

        [Test]
        public void Given_FirstRender_When_Mounted_Then_HandleIsAssignedToRef()
        {
            // Arrange
            var handle = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_handleFactory = () => handle;

            // Act
            using var mounted = V.Mount(_root, V.Component(HandleRender, componentRef: handleRef, key: "h"));

            // Assert
            Assert.That(handleRef.Current, Is.SameAs(handle), "The produced handle is assigned to the parent ref");
        }

        [Test]
        public void Given_SameDeps_When_ReRendered_Then_FactoryDoesNotRunAgain()
        {
            // Arrange
            var handle = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_handleFactory = () => { s_handleFactoryCallCount++; return handle; };
            s_handleDeps = new object[] { 42 };
            using var mounted = V.Mount(_root, V.Component(HandleRender, componentRef: handleRef, key: "h"));

            // Act — deps stay equal across the re-render
            s_handleSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_handleFactoryCallCount, Is.EqualTo(1), "Unchanged deps do not re-invoke the factory");
        }

        [Test]
        public void Given_ChangedDeps_When_ReRendered_Then_FactoryRunsAgain()
        {
            // Arrange
            var first = new FocusableHandle();
            var second = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_handleFactory = () => { s_handleFactoryCallCount++; return first; };
            s_handleDeps = new object[] { 1 };
            using var mounted = V.Mount(_root, V.Component(HandleRender, componentRef: handleRef, key: "h"));

            // Act — change both the factory result and the deps
            s_handleFactory = () => { s_handleFactoryCallCount++; return second; };
            s_handleDeps = new object[] { 2 };
            s_handleSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_handleFactoryCallCount, Is.EqualTo(2), "Changed deps re-invoke the factory");
        }

        [Test]
        public void Given_ChangedDeps_When_ReRendered_Then_NewHandleIsAssignedToRef()
        {
            // Arrange
            var first = new FocusableHandle();
            var second = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_handleFactory = () => first;
            s_handleDeps = new object[] { 1 };
            using var mounted = V.Mount(_root, V.Component(HandleRender, componentRef: handleRef, key: "h"));
            Assume.That(handleRef.Current, Is.SameAs(first), "Precondition: the first handle was assigned");

            // Act
            s_handleFactory = () => second;
            s_handleDeps = new object[] { 2 };
            s_handleSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(handleRef.Current, Is.SameAs(second), "The rebuilt handle is assigned to the parent ref");
        }

        [Test]
        public void Given_RenderPhaseDepOscillation_When_SettledToCommittedDep_Then_FactoryDoesNotRunAgain()
        {
            // Arrange — mount builds the handle once for the committed dep ["settled"]; the discarded attempt's dep
            // "transient" never builds a handle during render (factory call and ref write are deferred to settle).
            var handleRef = new Ref<IFocusable>();
            using var mounted = V.Mount(
                _root, V.Component(RenderPhaseOscillationHandleRender, componentRef: handleRef, key: "osc"));
            Assume.That(s_oscHandleFactoryCount, Is.EqualTo(1), "Precondition: mount built the handle once");

            // Act
            s_oscHandleSetPhase.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_oscHandleRenderCount, Is.EqualTo(3), "Precondition: 1 mount + 2 render-phase attempts (phase 1 -> 2)");

            // Assert
            Assert.That(s_oscHandleFactoryCount, Is.EqualTo(1),
                "An unchanged committed dep across the re-run does not rebuild the handle");
        }

        [Test]
        public void Given_RenderPhaseDepOscillation_When_SettledToCommittedDep_Then_RefKeepsCommittedHandle()
        {
            // Arrange
            var handleRef = new Ref<IFocusable>();
            using var mounted = V.Mount(
                _root, V.Component(RenderPhaseOscillationHandleRender, componentRef: handleRef, key: "osc"));
            var committedHandle = handleRef.Current;
            Assume.That(committedHandle, Is.Not.Null, "Precondition: mount assigned the committed handle");

            // Act
            s_oscHandleSetPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(handleRef.Current, Is.SameAs(committedHandle),
                "The parent ref still points at the committed handle after the oscillation");
        }

        [Test]
        public void Given_RefSwapped_When_ReRendered_Then_OldRefIsCleared()
        {
            // Arrange
            var handle = new FocusableHandle();
            var firstRef = new Ref<IFocusable>();
            var secondRef = new Ref<IFocusable>();
            s_handleFactory = () => handle;
            s_refSwitcherChildRef = firstRef;
            using var mounted = V.Mount(_root, V.Component(RefSwitcherParentRender, key: "parent"));
            Assume.That(firstRef.Current, Is.SameAs(handle), "Precondition: the handle is assigned to the first ref");

            // Act — the parent swaps the child's componentRef, then the child re-renders so the hook observes it
            s_refSwitcherChildRef = secondRef;
            s_refSwitcherSetTick.Invoke(1);
            s_handleSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(firstRef.Current, Is.Null, "The old ref is cleared when the parent swaps the ref");
        }

        [Test]
        public void Given_RefSwapped_When_ReRendered_Then_HandleIsAssignedToNewRef()
        {
            // Arrange
            var handle = new FocusableHandle();
            var firstRef = new Ref<IFocusable>();
            var secondRef = new Ref<IFocusable>();
            s_handleFactory = () => handle;
            s_refSwitcherChildRef = firstRef;
            using var mounted = V.Mount(_root, V.Component(RefSwitcherParentRender, key: "parent"));

            // Act
            s_refSwitcherChildRef = secondRef;
            s_refSwitcherSetTick.Invoke(1);
            s_handleSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(secondRef.Current, Is.SameAs(handle), "The handle is assigned into the new ref");
        }

        [Test]
        public void Given_RefSwappedToNull_When_ReRendered_Then_OldRefIsCleared()
        {
            // Arrange
            var handle = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_handleFactory = () => handle;
            s_refSwitcherChildRef = handleRef;
            using var mounted = V.Mount(_root, V.Component(RefSwitcherParentRender, key: "parent"));
            Assume.That(handleRef.Current, Is.SameAs(handle), "Precondition: the handle is assigned to the ref");

            // Act — the parent nulls the child's componentRef, then the child re-renders
            s_refSwitcherChildRef = null;
            s_refSwitcherSetTick.Invoke(1);
            s_handleSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(handleRef.Current, Is.Null, "Resetting componentRef to null detaches the hook from the old ref");
        }

        [Test]
        public void Given_NoRef_When_Mounted_Then_FactoryStillRuns()
        {
            // Arrange
            var handle = new FocusableHandle();
            s_handleFactory = () => { s_handleFactoryCallCount++; return handle; };

            // Act — no componentRef supplied
            using var mounted = V.Mount(_root, V.Component(HandleRender, key: "h"));

            // Assert
            Assert.That(s_handleFactoryCallCount, Is.EqualTo(1), "Without a ref the hook still evaluates the factory");
        }

        [Test]
        public void Given_NullFactory_When_Mounted_Then_LogsArgumentNullException()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentNullException"));
            var handleRef = new Ref<IFocusable>();

            // Act
            using var mounted = V.Mount(_root, V.Component(NullFactoryRender, componentRef: handleRef, key: "null-factory"));

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_FactoryBecomesNull_When_ReRendered_Then_LogsArgumentNullException()
        {
            // Arrange
            var handle = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_factoryToggleFactory = () => handle;
            using var mounted = V.Mount(_root, V.Component(FactoryToggleRender, componentRef: handleRef, key: "toggle"));

            // Act
            s_factoryToggleFactory = null;
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentNullException"));
            s_factoryToggleSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_MountedComponent_When_Unmounted_Then_RefCurrentBecomesNull()
        {
            // Arrange
            var handle = new FocusableHandle();
            var handleRef = new Ref<IFocusable>();
            s_handleFactory = () => handle;
            var mounted = V.Mount(_root, V.Component(HandleRender, componentRef: handleRef, key: "h"));
            Assume.That(handleRef.Current, Is.SameAs(handle), "Precondition: the handle is assigned before unmount");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(handleRef.Current, Is.Null, "Unmount clears the registered ref");
        }

        private interface IFocusable
        {
            void Focus();
        }

        private sealed class FocusableHandle : IFocusable
        {
            public int FocusCallCount { get; private set; }
            public void Focus() => FocusCallCount++;
        }

        #region Handle component (UseImperativeHandle + UseState tick + factory/deps)

        private static Func<IFocusable> s_handleFactory;
        private static object[] s_handleDeps;
        private static int s_handleFactoryCallCount;
        private static Action<int> s_handleSetTick;
        private static int s_oscHandleFactoryCount;
        private static int s_oscHandleRenderCount;
        private static Action<int> s_oscHandleSetPhase;

        private static void ResetHandle()
        {
            s_handleFactory = null;
            s_handleDeps = Array.Empty<object>();
            s_handleFactoryCallCount = 0;
            s_handleSetTick = null;
            s_oscHandleFactoryCount = 0;
            s_oscHandleRenderCount = 0;
            s_oscHandleSetPhase = null;
        }

        // A render-phase setState normalizes an odd phase to the next even phase in one re-run, so the handle dep
        // swings to "transient" on the discarded attempt and back to the committed "settled" on the settled one.
        [Component]
        private static VNode RenderPhaseOscillationHandleRender()
        {
            s_oscHandleRenderCount++;
            var (phase, setPhase) = Hooks.UseState(0);
            s_oscHandleSetPhase = setPhase;
            if (phase % 2 == 1)
            {
                setPhase.Invoke(phase + 1);
            }
            var handleRef = Hooks.ForwardedRef<IFocusable>();
            var dep = phase % 2 == 1 ? "transient" : "settled";
            Hooks.UseImperativeHandle(
                handleRef,
                () => { s_oscHandleFactoryCount++; return new FocusableHandle(); },
                new object[] { dep });
            return V.Label(text: dep);
        }

        [Component]
        private static VNode HandleRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_handleSetTick = setTick;
            var handleRef = Hooks.ForwardedRef<IFocusable>();
            Hooks.UseImperativeHandle(handleRef, s_handleFactory, s_handleDeps);
            return V.Label(text: "x");
        }

        #endregion

        #region RefSwitcher parent component (swaps componentRef on each tick)

        private static Ref<IFocusable> s_refSwitcherChildRef;
        private static Action<int> s_refSwitcherSetTick;

        private static void ResetRefSwitcher()
        {
            s_refSwitcherChildRef = null;
            s_refSwitcherSetTick = null;
        }

        [Component]
        private static VNode RefSwitcherParentRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_refSwitcherSetTick = setTick;
            return s_refSwitcherChildRef == null
                ? V.Component(HandleRender, key: "child")
                : V.Component(HandleRender, componentRef: s_refSwitcherChildRef, key: "child");
        }

        #endregion

        #region NullFactory component (passes factory: null to Hooks.UseImperativeHandle)

        [Component]
        private static VNode NullFactoryRender()
        {
            var handleRef = Hooks.ForwardedRef<IFocusable>();
            Hooks.UseImperativeHandle<IFocusable>(handleRef, factory: null);
            return V.Label(text: "x");
        }

        #endregion

        #region FactoryToggle component (swaps factory to null mid-test and re-renders)

        private static Func<IFocusable> s_factoryToggleFactory;
        private static Action<int> s_factoryToggleSetTick;

        private static void ResetFactoryToggle()
        {
            s_factoryToggleFactory = null;
            s_factoryToggleSetTick = null;
        }

        [Component]
        private static VNode FactoryToggleRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_factoryToggleSetTick = setTick;
            var handleRef = Hooks.ForwardedRef<IFocusable>();
            Hooks.UseImperativeHandle(handleRef, s_factoryToggleFactory, Array.Empty<object>());
            return V.Label(text: "x");
        }

        #endregion
    }
}
