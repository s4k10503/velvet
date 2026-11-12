using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Context propagation into a consumer rendered inside a <c>V.Portal</c>. Behavior: Portal children
    /// inherit context from their tree position (where <c>V.Portal</c> sits), not their mount location (the
    /// target element). The children are reconciled by a deferred drain after the main pass has unwound the
    /// context cursor, so the drain restores the context that enclosed the Portal's tree position.
    /// </summary>
    /// <remarks>
    /// Covers both MOUNT-time inheritance and an ISOLATED re-render of a portal-hosted consumer (its own
    /// setState, without the host re-rendering), which reads the Providers around it as the host's output holds
    /// them then: a value the host's render changed after the mount, above the Portal, inside it, or around a Portal
    /// a memo of the host's keeps, and the value the host kept where a boundary aborted a render of it that patched the
    /// Portal. The VirtualList counterpart is exercised by VirtualListTests and VirtualListRowScopeTests.
    /// </remarks>
    [TestFixture]
    internal sealed class PortalContextInheritanceTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("default");

        private VisualElement _root;
        private VisualElement _portalTarget;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _portalTarget = new VisualElement();
            RuntimeStateProbe.ClearPortalRegistry();
            FiberPortalRegistry.Register("ctx-portal-target", _portalTarget);
            s_lastSeen = null;
        }

        [TearDown]
        public void TearDown() => RuntimeStateProbe.ClearPortalRegistry();

        private static string s_lastSeen;
        private static StateUpdater<int> s_setCount;
        private static StateUpdater<int> s_setHostTick;
        private static StateUpdater<string> s_setHostValue;

        [Test]
        public void Given_ProviderAbovePortal_When_ChildMounts_Then_ReadsProvidedValue()
        {
            // Arrange / Act
            using var mounted = V.Mount(_root, V.Component(PortalHostRender, key: "host"));

            // Assert
            Assert.That(s_lastSeen, Is.EqualTo("provided"),
                "A portal child inherits the context enclosing the Portal's tree position, even though it mounts via a deferred drain into the target element");
        }

        [Test]
        public void Given_MotionContextAbovePortal_When_ChildMounts_Then_InheritsActiveLabel()
        {
            // Arrange / Act: a Motion establishes the active variant label; a Portal child Motion inherits it.
            using var mounted = V.Mount(_root, V.Component(MotionPortalHostRender, key: "host"));

            // Assert: the captured snapshot carries MotionContext too, so the inherited label is not lost.
            Assert.That(s_lastSeen, Is.EqualTo("active"),
                "A portal child inherits the enclosing MotionContext active label (snapshot carries every context)");
        }

        [Test]
        public void Given_ProviderAbovePortal_When_ChildReRendersInIsolation_Then_StillReadsProvidedValue()
        {
            // Arrange: a portal child that mounted under an enclosing Provider.
            using var mounted = V.Mount(_root, V.Component(RerenderHostRender, key: "host"));
            Assume.That(s_lastSeen, Is.EqualTo("provided"));

            // Act: the child re-renders on its own setState (the host does not re-render).
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert: the spine rebuilds the enclosing Provider for the isolated re-render.
            Assert.That(s_lastSeen, Is.EqualTo("provided"),
                "An isolated re-render of a portal child reconstructs the context enclosing the Portal's tree position");
        }

        // The Provider sits INSIDE the Portal's children. The host re-renders between the mount and the
        // child's own setState, which builds fresh nodes for the same positions.
        // GREEN_ON_BASE(characterization): the base reads the in-portal Provider here and this must keep
        // it. The node comparison this change adds is what an ungated form breaks: it would weigh a node
        // the drain stamped once against a record every patch rewrites, and read the context default
        // from the declaring component's second render onward.
        [Test]
        public void Given_AProviderInsideThePortal_When_TheHostReRendersAndThenTheChildReRendersAlone_Then_ItStillReadsTheInPortalValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(InPortalProviderHostRender, key: "host"));
            var atMount = s_lastSeen;
            s_setHostTick.Invoke(1);
            mounted.FlushStateForTest();
            var afterHostRender = s_lastSeen;

            // Act
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the two earlier readings are folded in because they are what put a wrong value
            // below on the isolated re-render rather than on the mount or on the host's own pass; and the
            // field is cleared first so a consumer that did not re-render at all reads as null rather
            // than as the value its previous render left.
            Assert.That(
                (atMount, afterHostRender, s_lastSeen),
                Is.EqualTo(("inside", "inside", "inside")));
        }

        [Test]
        public void Given_AProviderAboveThePortalWhoseValueTheHostChanged_When_TheChildReRendersAlone_Then_ItReadsTheNewValue()
        {
            // Arrange — the host's render changes the value, which its walk hands the child as it patches the Portal.
            using var mounted = V.Mount(_root, V.Component(ValueAbovePortalHostRender, key: "host"));
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();

            // Act
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastSeen, Is.EqualTo("second"));
        }

        [Test]
        public void Given_AProviderInsideThePortalWhoseValueTheHostChanged_When_TheChildReRendersAlone_Then_ItReadsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ValueInsidePortalHostRender, key: "host"));
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();

            // Act
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastSeen, Is.EqualTo("second"));
        }

        [Test]
        public void Given_APortalAMemoKeepsUnderAProviderWhoseValueTheHostChanged_When_TheChildReRendersAlone_Then_ItReadsTheNewValue()
        {
            // Arrange — the memo's dependencies do not change, so the host's render keeps the Portal it built first.
            using var mounted = V.Mount(_root, V.Component(MemoizedPortalUnderValueHostRender, key: "host"));
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();

            // Act
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastSeen, Is.EqualTo("second"));
        }

        [Test]
        public void Given_APortalPatchedInAHostRenderItsBoundaryAborted_When_TheChildReRendersAlone_Then_ItReadsTheValueTheHostKept()
        {
            // Arrange — a committed render changes the value; the next changes it again and patches the Portal, and the
            // boundary after the Portal then aborts that render, so the host keeps the output of the committed one.
            using var mounted = V.Mount(_root, V.Component(AbortablePortalHostRender, key: "host"), CaughtErrors.Unlogged);
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();
            s_setHostValue.Invoke("aborting");
            mounted.FlushStateForTest();

            // Act
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastSeen, Is.EqualTo("second"));
        }

        [Test]
        public void Given_APortalInAPortalPatchedInAHostRenderItsBoundaryAborted_When_TheInnerChildReRendersAlone_Then_ItReadsTheValueTheHostKept()
        {
            // Arrange — as for a single Portal, with the child's Portal inside another one.
            s_innerPortalTarget = new VisualElement();
            using var mounted = V.Mount(
                _root, V.Component(AbortableNestedPortalHostRender, key: "host"), CaughtErrors.Unlogged);
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();
            s_setHostValue.Invoke("aborting");
            mounted.FlushStateForTest();

            // Act
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_lastSeen, Is.EqualTo("second"));
        }

        [Test]
        public void Given_MotionContextAbovePortal_When_ChildReRendersInIsolation_Then_StillReadsActiveLabel()
        {
            // Arrange: a portal child Motion consumer that mounted under an enclosing Motion's active label.
            using var mounted = V.Mount(_root, V.Component(RerenderMotionHostRender, key: "host"));
            Assume.That(s_lastSeen, Is.EqualTo("active"));

            // Act: the child re-renders on its own setState.
            s_lastSeen = null;
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert: the rebuilt snapshot still carries the enclosing MotionContext label.
            Assert.That(s_lastSeen, Is.EqualTo("active"),
                "An isolated re-render of a portal child reconstructs the enclosing MotionContext active label");
        }

        #region Components

        [Component]
        private static VNode ConsumerRender()
        {
            s_lastSeen = Hooks.UseContext(ThemeContext);
            return V.Label(text: s_lastSeen);
        }

        [Component]
        private static VNode PortalHostRender()
            => V.Provider(ThemeContext, "provided", new VNode[]
            {
                V.Portal("ctx-portal-target", children: new VNode[]
                {
                    V.Component(ConsumerRender, key: "consumer"),
                }),
            });

        [Component]
        private static VNode MotionLabelConsumerRender()
        {
            s_lastSeen = Hooks.UseContext(MotionContext.ActiveLabel);
            return V.Label(text: s_lastSeen);
        }

        [Component]
        private static VNode MotionPortalHostRender()
            => V.Motion(className: "m", animate: "active", children: new VNode[]
            {
                V.Portal("ctx-portal-target", children: new VNode[]
                {
                    V.Component(MotionLabelConsumerRender, key: "consumer"),
                }),
            }, enterOnMount: false);

        [Component]
        private static VNode RerenderConsumerRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            s_lastSeen = Hooks.UseContext(ThemeContext);
            return V.Label(text: $"{s_lastSeen}{count}");
        }

        [Component]
        private static VNode RerenderHostRender()
            => V.Provider(ThemeContext, "provided", new VNode[]
            {
                V.Portal("ctx-portal-target", children: new VNode[]
                {
                    V.Component(RerenderConsumerRender, key: "consumer"),
                }),
            });

        // A tick the host renders is what makes its re-render build a fresh tree rather than bail, so the
        // portal's children on the second pass are new node instances at the same positions.
        [Component]
        private static VNode InPortalProviderHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHostTick = setTick;
            return V.Div(name: "shell", children: new VNode?[]
            {
                V.Label(name: "tick", text: tick.ToString()),
                V.Portal("ctx-portal-target", children: new VNode?[]
                {
                    V.Provider(ThemeContext, "inside", new VNode[]
                    {
                        V.Component(RerenderConsumerRender, key: "consumer"),
                    }),
                }),
            });
        }

        [Component(Compiler = false)]
        private static VNode ValueAbovePortalHostRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(ThemeContext, value, new VNode[]
            {
                V.Portal("ctx-portal-target", children: new VNode[]
                {
                    V.Component(RerenderConsumerRender, key: "consumer"),
                }),
            });
        }

        [Component(Compiler = false)]
        private static VNode ValueInsidePortalHostRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Portal("ctx-portal-target", children: new VNode[]
            {
                V.Provider(ThemeContext, value, new VNode[] { V.Component(RerenderConsumerRender, key: "consumer") }),
            });
        }

        [Component(Compiler = false)]
        private static VNode MemoizedPortalUnderValueHostRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(ThemeContext, value, new VNode[] { V.Memoized(ConsumerPortal, Array.Empty<object>()) });
        }

        private static readonly Func<VisualElement, Action> s_refusingCleanup =
            _ => () => throw new InvalidOperationException("ref cleanup refused");

        // Takes its label's ref away once told to, and catches the cleanup that throws.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CleanupRefusingBoundary(bool holdRef)
        {
            Hooks.UseFallback(_ => V.Label(name: "refused", text: "fallback"));
            return V.Label(name: "held", refCallback: holdRef ? s_refusingCleanup : null);
        }

        [Component(Compiler = false)]
        private static VNode AbortablePortalHostRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(ThemeContext, value, new VNode[]
            {
                V.Div(name: "abortable", children: new VNode[]
                {
                    V.Portal("ctx-portal-target", children: new VNode[]
                    {
                        V.Component(RerenderConsumerRender, key: "consumer"),
                    }),
                    V.Component(CleanupRefusingBoundary, value != "aborting", key: "boundary"),
                }),
            });
        }

        private static VisualElement s_innerPortalTarget;

        [Component(Compiler = false)]
        private static VNode AbortableNestedPortalHostRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(ThemeContext, value, new VNode[]
            {
                V.Div(name: "abortable", children: new VNode[]
                {
                    V.Portal("ctx-portal-target", children: new VNode[]
                    {
                        V.Portal(s_innerPortalTarget, children: new VNode[]
                        {
                            V.Component(RerenderConsumerRender, key: "consumer"),
                        }),
                    }),
                    V.Component(CleanupRefusingBoundary, value != "aborting", key: "boundary"),
                }),
            });
        }

        private static VNode ConsumerPortal()
            => V.Portal("ctx-portal-target", children: new VNode[]
            {
                V.Component(RerenderConsumerRender, key: "consumer"),
            });

        [Component]
        private static VNode RerenderMotionConsumerRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            s_lastSeen = Hooks.UseContext(MotionContext.ActiveLabel);
            return V.Label(text: $"{s_lastSeen}{count}");
        }

        [Component]
        private static VNode RerenderMotionHostRender()
            => V.Motion(className: "m", animate: "active", children: new VNode[]
            {
                V.Portal("ctx-portal-target", children: new VNode[]
                {
                    V.Component(RerenderMotionConsumerRender, key: "consumer"),
                }),
            }, enterOnMount: false);

        #endregion
    }
}
