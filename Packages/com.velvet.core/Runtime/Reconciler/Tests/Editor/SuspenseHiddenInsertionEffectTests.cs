using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what happens to the effects of a component a <c>V.Suspense</c> keeps hidden when its host renders it
    /// while the fallback shows. None of its insertion, layout or passive effects runs meanwhile, and the commit
    /// that reveals it runs once each insertion and passive effect such a render queued, those of a tree that
    /// never showed included, and the layout effects all over again. An effect whose deps changed and changed back
    /// while hidden runs at the reveal too, since the hidden children render at all.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenInsertionEffectTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            s_setBlocked = default;
            s_setTick = default;
            s_setMounted = default;
            s_setExtra = default;
            s_setPortal = default;
            s_portalTarget = null;
            s_source = null;
            s_insertionSetups = 0;
            s_layoutSetups = 0;
            s_layoutCleanups = 0;
            s_passiveSetups = 0;
            s_lateInsertionSetups = 0;
            s_portalInsertionSetups = 0;
            s_depsOf = static tick => tick;
        }

        [Test]
        public void Given_AShownProbeHiddenAndRenderedTwice_When_TheBoundaryReveals_Then_ItsInsertionEffectRunsOnlyThen()
        {
            // Arrange — the second render leaves the deps as the first promoted them
            s_depsOf = static tick => tick > 0 ? 1 : 0;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);
            SetTick(mounted, 2);
            var whileHidden = s_insertionSetups;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_insertionSetups), Is.EqualTo((1, 2)),
                "A Suspense showing its fallback commits nothing of its hidden children, and the reveal commits the change");
        }

        // GREEN_ON_BASE(characterization): the base holds the passive effects of a hidden fiber and queues them at
        // the reveal. What this pins is that the hold added for insertion effects leaves them alone.
        [Test]
        public void Given_AShownProbeHiddenAndRenderedTwice_When_TheBoundaryReveals_Then_ItsPassiveEffectRunsOnlyThen()
        {
            // Arrange
            s_depsOf = static tick => tick > 0 ? 1 : 0;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.FlushEffectsForTest();
            SetBlocked(mounted);
            SetTick(mounted, 1);
            SetTick(mounted, 2);
            mounted.FlushEffectsForTest();
            var whileHidden = s_passiveSetups;

            // Act
            Reveal(mounted);
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That((whileHidden, s_passiveSetups), Is.EqualTo((1, 2)),
                "A Suspense showing its fallback commits nothing of its hidden children, and the reveal commits the change");
        }

        // GREEN_ON_BASE(characterization): the base cleans the layout effects up at the hide and queues them all at
        // the reveal. What this pins is that the hold added for insertion effects leaves them alone.
        [Test]
        public void Given_AShownProbeHiddenAndRendered_When_TheBoundaryReveals_Then_ItsLayoutEffectRunsOnlyThen()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);
            var whileHidden = $"{s_layoutSetups}/{s_layoutCleanups}";

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, $"{s_layoutSetups}/{s_layoutCleanups}"), Is.EqualTo(("1/1", "2/1")),
                "The hide cleans the layout effect up and nothing sets it up again before the reveal");
        }

        [Test]
        public void Given_AChildMountedWhileItsPrimaryIsHidden_When_TheBoundaryReveals_Then_ItsInsertionEffectRunsOnlyThen()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            s_setExtra.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var whileHidden = s_lateInsertionSetups;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_lateInsertionSetups), Is.EqualTo((0, 1)),
                "A child mounted into a primary the Suspense hides is committed with the reveal");
        }

        [Test]
        public void Given_APrimaryFirstMountedUnderTheFallback_When_ItsHostRendersItAgainWithChangedDeps_Then_ItsInsertionEffectRunsOnlyOnReveal()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NeverShownHostRender, key: "never-shown-host"));
            s_setMounted.Invoke(true);
            mounted.FlushStateForTest();
            SetTick(mounted, 1);
            var whileHidden = s_insertionSetups;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_insertionSetups), Is.EqualTo((0, 1)),
                "A tree that never showed commits nothing until the reveal, which runs its effect once");
        }

        [Test]
        public void Given_APrimaryFirstMountedUnderTheFallback_When_ItsHostRendersItAgainWithUnchangedDeps_Then_ItsInsertionEffectRunsOnlyOnReveal()
        {
            // Arrange
            s_depsOf = static _ => 0;
            using var mounted = V.Mount(_root, V.Component(NeverShownHostRender, key: "never-shown-host"));
            s_setMounted.Invoke(true);
            mounted.FlushStateForTest();
            SetTick(mounted, 1);
            var whileHidden = s_insertionSetups;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_insertionSetups), Is.EqualTo((0, 1)),
                "A tree that never showed commits nothing until the reveal, which runs its effect once");
        }

        [Test]
        public void Given_AShownProbeHiddenAndRevealedOnce_When_ItIsHiddenAgainWithItsDepsUnchangedAndRevealed_Then_ItsInsertionEffectDoesNotRunAgain()
        {
            // Arrange
            s_depsOf = static tick => tick > 0 ? 1 : 0;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);
            Reveal(mounted);
            SetBlocked(mounted, 2);

            // Act
            SetTick(mounted, 2);
            Reveal(mounted);

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(2),
                "The mount and the first reveal ran the effect, and nothing queued it again");
        }

        [Test]
        public void Given_AComponentMountedWhileItsPrimaryIsHiddenWithARefdPortalChild_When_TheBoundaryReveals_Then_ItsInsertionEffectRunsOnlyThen()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            s_setPortal.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var whileHidden = s_portalInsertionSetups;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That((whileHidden, s_portalInsertionSetups), Is.EqualTo((0, 1)),
                "A ref attaching under a component the boundary hides does not run that component's insertion effect");
        }

        private static void SetBlocked(MountedTree mounted, int block = 1)
        {
            s_setBlocked.Invoke(block);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void SetTick(MountedTree mounted, int tick)
        {
            s_setTick.Invoke(tick);
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
        private static StateUpdater<bool> s_setMounted;
        private static StateUpdater<bool> s_setExtra;
        private static StateUpdater<bool> s_setPortal;
        private static VisualElement s_portalTarget;
        private static VelvetTaskCompletionSource<int> s_source;
        private static Func<int, int> s_depsOf;
        private static int s_insertionSetups;
        private static int s_layoutSetups;
        private static int s_layoutCleanups;
        private static int s_passiveSetups;
        private static int s_lateInsertionSetups;
        private static int s_portalInsertionSetups;

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            var (extra, setExtra) = Hooks.UseState(false);
            var (portal, setPortal) = Hooks.UseState(false);
            s_setPortal = setPortal;
            s_setBlocked = setBlocked;
            s_setTick = setTick;
            s_setExtra = setExtra;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Component(ProbeRender, tick, key: "probe"),
                        V.Component(ReaderRender, blocked, key: "reader"),
                        extra ? V.Component(LateRender, key: "late") : null,
                        portal ? V.Component(PortalDeclarerRender, key: "portal-declarer") : null,
                    }),
            });
        }

        [Component]
        private static VNode NeverShownHostRender()
        {
            var (isMounted, setMounted) = Hooks.UseState(false);
            var (tick, setTick) = Hooks.UseState(0);
            s_setMounted = setMounted;
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: isMounted
                        ? new VNode[]
                        {
                            V.Component(ProbeRender, tick, key: "probe"),
                            V.Component(ReaderRender, 1, key: "reader"),
                        }
                        : Array.Empty<VNode>()),
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

        // The ref sits in a Portal's child, which the portal drain mounts after the boundary hid the primary.
        [Component]
        private static VNode PortalDeclarerRender()
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_portalInsertionSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div(children: new VNode[]
            {
                V.Portal(s_portalTarget, children: new VNode[]
                {
                    V.Div(name: "portaled", refCallback: _ => null),
                }),
            });
        }

        [Component]
        private static VNode LateRender()
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_lateInsertionSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "late");
        }

        [Component]
        private static VNode ProbeRender(int tick)
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_insertionSetups++;
                return (Action)null;
            }, new object[] { s_depsOf(tick) });
            Hooks.UseLayoutEffect(() =>
            {
                s_layoutSetups++;
                return (Action)(() => s_layoutCleanups++);
            }, new object[] { tick });
            Hooks.UseEffect(() =>
            {
                s_passiveSetups++;
                return (Action)null;
            }, new object[] { s_depsOf(tick) });
            return V.Label(text: "probe:" + tick);
        }
    }
}
