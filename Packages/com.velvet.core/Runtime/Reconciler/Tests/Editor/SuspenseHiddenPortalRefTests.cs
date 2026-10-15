using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when the callback ref of an element inside a <c>V.Portal</c> attaches, for a component a
    /// <c>V.Suspense</c> keeps hidden: not while the fallback shows, whether the portal mounted before the boundary
    /// hid it or while it was hidden, and once when the boundary reveals the tree.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenPortalRefTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            s_setBlocked = default;
            s_setTick = default;
            s_setPortal = default;
            s_source = null;
            s_outerTarget = new VisualElement();
            s_innerTarget = new VisualElement();
            s_outerAttaches = 0;
            s_outerCleanups = 0;
            s_innerAttaches = 0;
            s_lastAttachedTick = -1;
            s_nested = false;
        }

        [Test]
        public void Given_ADeclarerMountedWhileItsPrimaryIsHiddenWithARefdPortalChild_When_TheBoundaryReveals_Then_TheRefAttachesOnlyThen()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            ShowPortal(mounted);
            var whileHidden = s_outerAttaches;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_outerAttaches), Is.EqualTo((0, 1)),
                "The portal's child mounts after the boundary hid its tree, and its ref waits for the reveal");
        }

        [Test]
        public void Given_APortalInsideAPortalChildMountedWhileItsPrimaryIsHidden_When_TheBoundaryReveals_Then_TheInnerRefAttachesOnlyThen()
        {
            // Arrange
            s_nested = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            ShowPortal(mounted);
            var whileHidden = s_innerAttaches;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_innerAttaches), Is.EqualTo((0, 1)),
                "The inner portal drains in a later turn than the outer one, and its ref waits for the reveal too");
        }

        // GREEN_ON_BASE(characterization): the base detaches an attached portal child's ref at the hide.
        // It does so through the portal slots the boundary's walk records, and attaches it again at the reveal.
        [Test]
        public void Given_AnAttachedPortalChildRef_When_TheBoundaryHidesAndRevealsItsTree_Then_TheRefDetachesAtTheHideAndAttachesAgainAtTheReveal()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            ShowPortal(mounted);
            SetBlocked(mounted);
            var whileHidden = $"{s_outerAttaches}/{s_outerCleanups}";

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_outerAttaches), Is.EqualTo(("1/1", 2)),
                "The hide runs the ref's cleanup and the reveal sets it up again");
        }

        // GREEN_ON_BASE(characterization): the base takes back a portal child's changed callback at the hide.
        // A render of the hidden tree queues that setup, and the reveal attaches the newest callback.
        [Test]
        public void Given_APortalChildRefWhoseCallbackChangesInARenderOfTheHiddenTree_When_TheBoundaryReveals_Then_TheNewestCallbackAttachesOnlyThen()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            ShowPortal(mounted);
            SetBlocked(mounted);
            SetTick(mounted, 1);
            var whileHidden = s_lastAttachedTick;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_lastAttachedTick), Is.EqualTo((0, 1)),
                "No callback attaches while the fallback shows, and the reveal attaches the one the last render gave");
        }

        private static void SetBlocked(MountedTree mounted)
        {
            s_setBlocked.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void SetTick(MountedTree mounted, int tick)
        {
            s_setTick.Invoke(tick);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void ShowPortal(MountedTree mounted)
        {
            s_setPortal.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void Reveal(MountedTree mounted)
        {
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static StateUpdater<int> s_setBlocked;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<bool> s_setPortal;
        private static VelvetTaskCompletionSource<int> s_source;
        private static VisualElement s_outerTarget;
        private static VisualElement s_innerTarget;
        private static bool s_nested;
        private static int s_outerAttaches;
        private static int s_outerCleanups;
        private static int s_innerAttaches;
        private static int s_lastAttachedTick;

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            var (portal, setPortal) = Hooks.UseState(false);
            s_setBlocked = setBlocked;
            s_setTick = setTick;
            s_setPortal = setPortal;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Component(ReaderRender, blocked, key: "reader"),
                        portal ? V.Component(DeclarerRender, tick, key: "declarer") : null,
                    }),
            });
        }

        // Pending for good until s_source resolves, once blocked.
        [Component]
        private static VNode ReaderRender(int blocked)
        {
            var value = Hooks.Use<int>(_ => blocked > 0
                ? (s_source = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        // The callback captures the tick, so each render of the declarer hands the element a new one.
        [Component]
        private static VNode DeclarerRender(int tick)
        {
            Func<VisualElement, Action> outerRef = _ =>
            {
                s_outerAttaches++;
                s_lastAttachedTick = tick;
                return () => s_outerCleanups++;
            };
            VNode[] portaled = s_nested
                ? new VNode[]
                {
                    V.Div(name: "outer", refCallback: outerRef, children: new VNode[]
                    {
                        V.Portal(s_innerTarget, children: new VNode[]
                        {
                            V.Div(name: "inner", refCallback: _ =>
                            {
                                s_innerAttaches++;
                                return null;
                            }),
                        }),
                    }),
                }
                : new VNode[] { V.Div(name: "outer", refCallback: outerRef) };
            return V.Div(children: new VNode[] { V.Portal(s_outerTarget, children: portaled) });
        }
    }
}
