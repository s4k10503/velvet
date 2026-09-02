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
    /// setState, without the host re-rendering). The spine's parent-walk reaches the declaring component but
    /// not the Provider inside it, because that component's committed tree carries the Portal as an opaque
    /// leaf; instead the drain stamps each top-level portal child with the context that enclosed the Portal
    /// (ComponentFiber.DetachedMountContext) and FiberContextSpine rebuilds it on the isolated re-render.
    /// The VirtualList counterpart of the same mechanism is exercised by VirtualListTests.
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

        // The Provider sits INSIDE the Portal's children, which is the layer the enclosing snapshot does
        // not carry — FiberContextSpine recovers it by walking DetachedMountContext.DescendantNodes. The
        // host re-renders between the mount and the child's own setState: a drain stamps only the fibers
        // it newly created, so a child that survives that re-render keeps the DescendantNodes captured at
        // its mount while the declaring body's next render builds fresh nodes for the same positions.
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
            });

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
            });

        #endregion
    }
}
