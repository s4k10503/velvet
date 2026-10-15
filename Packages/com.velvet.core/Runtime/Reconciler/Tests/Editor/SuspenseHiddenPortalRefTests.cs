using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when the callback ref of an element inside a <c>V.Portal</c> attaches, for a component a
    /// <c>V.Suspense</c> keeps hidden: not while the fallback shows, whether the portal mounted before the boundary
    /// hid it or while it was hidden, and once when the boundary reveals the tree. The components inside the portal are
    /// hidden with it.
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
            s_withInner = false;
            s_withSlow = false;
            s_slowLayoutSetups = 0;
            s_setInner = default;
            s_lastInnerAttached = -1;
            s_innerLayoutSetups = 0;
            s_innerLayoutCleanups = 0;
            s_portalDirect = false;
            s_withReader = false;
            s_readerSource = null;
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

        [Test]
        public void Given_AStatefulComponentInAPortalMountedWhileItsPrimaryIsHidden_When_ItsStateChangesAndTheBoundaryReveals_Then_TheNewestRefAttachesOnlyThen()
        {
            // Arrange
            s_withInner = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            ShowPortal(mounted);
            s_setInner.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var whileHidden = s_lastInnerAttached;

            // Act
            Reveal(mounted);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((whileHidden, s_lastInnerAttached), Is.EqualTo((-1, 1)),
                "No ref attaches while the fallback shows, and the reveal attaches the one the last state change gave");
        }

        [Test]
        public void Given_AComponentInAPortalMountedWhileItsPrimaryIsHidden_When_TheBoundaryReveals_Then_ItsLayoutEffectRunsOnlyThen()
        {
            // Arrange
            s_withInner = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            ShowPortal(mounted);
            var whileHidden = s_innerLayoutSetups;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_innerLayoutSetups), Is.EqualTo((0, 1)),
                "The layout effect of a component mounted into a portal under the hidden primary runs with the reveal");
        }

        [Test]
        public void Given_AComponentInAPortalMountedBeforeItsPrimaryIsHidden_When_TheBoundaryHidesAndRevealsItsTree_Then_ItsLayoutEffectIsTornDownAtTheHideAndSetUpAgainAtTheReveal()
        {
            // Arrange
            s_withInner = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            ShowPortal(mounted);
            SetBlocked(mounted);
            var whileHidden = s_innerLayoutCleanups;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_innerLayoutSetups), Is.EqualTo((1, 2)),
                "The hide runs the layout effect's cleanup and the reveal sets it up again");
        }

        [Test]
        public void Given_APortalThatIsADirectChildOfTheBoundaryMountedWhileItsPrimaryIsHidden_When_ItsComponentsStateChangesAndTheBoundaryReveals_Then_TheNewestRefAttachesOnlyThen()
        {
            // Arrange
            s_portalDirect = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            ShowPortal(mounted);
            s_setInner.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var whileHidden = s_lastInnerAttached;

            // Act
            Reveal(mounted);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((whileHidden, s_lastInnerAttached), Is.EqualTo((-1, 1)),
                "No ref attaches while the fallback shows, and the reveal attaches the one the last state change gave");
        }

        // GREEN_ON_BASE(characterization): the base routes the resolved read through the nearest boundary above the fiber.
        // What this pins is that a portal component declared in an offscreen component is rendered by the boundary
        // that hides it while the reader holds the fallback.
        [Test]
        public void Given_AComponentReadingInAPortalMountedWhileItsPrimaryIsHiddenAndSuspendedByAParentRender_When_ItsReadResolves_Then_TheLabelShowsTheValueWhileTheReaderHoldsTheFallback()
        {
            // Arrange
            s_withReader = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            ShowPortal(mounted);
            SetTick(mounted, 1);

            // Act
            s_readerSource.TrySetResult(7);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_outerTarget.Q<Label>("reading-value")?.text, Is.EqualTo("reading:7"),
                "The label shows the resolved value while the reader still holds the fallback");
        }

        // GREEN_ON_BASE(characterization): without the drain's hide nothing overwrites a fiber an inner Suspense hid.
        // What this pins is that the drain leaves such a fiber to the inner Suspense that reveals it. The component
        // renders to completion beside one that never does, so the fallback keeps showing.
        [Test]
        public void Given_ASuspenseAsADirectChildOfAPortalMountedWhileItsPrimaryIsHidden_When_TheOuterBoundaryReveals_Then_TheSuspendedComponentsLayoutEffectStaysDown()
        {
            // Arrange
            s_withSlow = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            ShowPortal(mounted);

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((_root.Q<Label>("reader")?.text, s_outerTarget.Q<Label>("slow-fallback") != null, s_slowLayoutSetups),
                Is.EqualTo(("reader:5", true, 0)),
                "The outer boundary revealed while the inner one still shows its fallback, which keeps its component's layout effect down");
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
        private static bool s_withInner;
        private static bool s_withSlow;
        private static int s_slowLayoutSetups;
        private static StateUpdater<int> s_setInner;
        private static int s_lastInnerAttached;
        private static int s_innerLayoutSetups;
        private static int s_innerLayoutCleanups;
        private static bool s_portalDirect;
        private static bool s_withReader;
        private static VelvetTaskCompletionSource<int> s_readerSource;

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            var (portal, setPortal) = Hooks.UseState(false);
            s_setBlocked = setBlocked;
            s_setTick = setTick;
            s_setPortal = setPortal;
            VNode declared = null;
            if (portal)
            {
                declared = s_portalDirect
                    ? (VNode)V.Portal(s_outerTarget, children: new VNode[] { V.Component(InnerRender, key: "inner") })
                    : V.Component(DeclarerRender, tick, key: "declarer");
            }
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Component(ReaderRender, blocked, key: "reader"),
                        declared,
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
            return V.Label(name: "reader", text: "reader:" + value);
        }

        // Renders to completion, so it has a layout effect for a reveal to set up.
        [Component]
        private static VNode SlowRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_slowLayoutSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "slow");
        }

        // Pending for good: the Suspense around it never leaves its fallback.
        [Component]
        private static VNode StuckRender()
        {
            var value = Hooks.Use<int>(_ => new VelvetTaskCompletionSource<int>().Task, 0);
            return V.Label(text: "stuck:" + value);
        }

        // Reads a resource that stays pending while the tick is above zero and its source is unresolved.
        [Component]
        private static VNode ReadingRender(int tick)
        {
            var value = Hooks.Use<int>(_ => tick > 0
                ? (s_readerSource = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), tick);
            return V.Label(name: "reading-value", text: "reading:" + value);
        }

        [Component]
        private static VNode InnerRender()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setInner = setN;
            Hooks.UseLayoutEffect(() =>
            {
                s_innerLayoutSetups++;
                return () => s_innerLayoutCleanups++;
            }, Array.Empty<object>());
            return V.Div(name: "inner", refCallback: _ =>
            {
                s_lastInnerAttached = n;
                return null;
            });
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
            VNode[] portaled = s_withReader
                ? new VNode[] { V.Component(ReadingRender, tick, key: "reading") }
                : s_withSlow
                ? new VNode[]
                {
                    V.Suspense(
                        fallback: V.Label(name: "slow-fallback", text: "slow"),
                        children: new VNode[]
                        {
                            V.Component(SlowRender, key: "slow"),
                            V.Component(StuckRender, key: "stuck"),
                        }),
                }
                : s_withInner
                ? new VNode[] { V.Component(InnerRender, key: "inner") }
                : s_nested
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
