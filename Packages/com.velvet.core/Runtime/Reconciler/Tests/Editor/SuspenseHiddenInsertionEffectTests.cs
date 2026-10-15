using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which effects of a component a <c>V.Suspense</c> keeps hidden still run when its host renders it
    /// again, as React does: the insertion effects run, while the layout effects wait for the reveal.
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
            s_lateInsertionSetups = 0;
            s_probeDepsFollowTick = true;
            s_source = null;
            s_insertionSetups = 0;
            s_layoutSetups = 0;
            s_layoutCleanups = 0;
        }

        [Test]
        public void Given_AHiddenPrimary_When_ItsHostRendersItWithChangedDeps_Then_ItsInsertionEffectRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);

            // Act
            SetTick(mounted);

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(2),
                "React runs the insertion effects of a tree a Suspense hides, as its mutation phase does not skip it");
        }

        // GREEN_ON_BASE(characterization): the base drops a hidden fiber's whole commit, so it runs no layout work.
        // What this pins is that the fix runs the insertion effects without running the layout ones beside them.
        [Test]
        public void Given_AHiddenPrimary_When_ItsHostRendersItWithChangedDeps_Then_ItsLayoutEffectStaysDown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);

            // Act
            SetTick(mounted);

            // Assert
            Assert.That($"{s_layoutSetups}/{s_layoutCleanups}", Is.EqualTo("1/1"),
                "React disconnects the layout effects of a tree a Suspense hides: one setup at mount, one cleanup at the hide");
        }

        // GREEN_ON_BASE(characterization): the base runs a hidden fiber's insertion effects only at the reveal.
        // What this pins is that running them for a hidden update does not run them for a tree never shown.
        [Test]
        public void Given_APrimaryFirstMountedUnderTheFallback_When_ItsHostRendersItAgainWithChangedDepsBeforeTheReveal_Then_ItsInsertionEffectRunsOnlyOnReveal()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NeverShownHostRender, key: "never-shown-host"));
            s_setMounted.Invoke(true);
            mounted.FlushStateForTest();
            SetTick(mounted);
            var whileHidden = s_insertionSetups;

            // Act
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((whileHidden, s_insertionSetups), Is.EqualTo((0, 1)),
                "React commits nothing of a tree that never showed, so its insertion effect runs once, on reveal");
        }

        [Test]
        public void Given_APrimaryFirstMountedUnderTheFallback_When_ItsHostRendersItAgainWithUnchangedDepsBeforeTheReveal_Then_ItsInsertionEffectRunsOnReveal()
        {
            // Arrange
            s_probeDepsFollowTick = false;
            using var mounted = V.Mount(_root, V.Component(NeverShownHostRender, key: "never-shown-host"));
            s_setMounted.Invoke(true);
            mounted.FlushStateForTest();
            SetTick(mounted);
            var whileHidden = s_insertionSetups;

            // Act
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((whileHidden, s_insertionSetups), Is.EqualTo((0, 1)),
                "React commits nothing of a tree that never showed, so its insertion effect runs once, on reveal");
        }

        [Test]
        public void Given_AHiddenPrimaryTheBoundaryHadShown_When_ItsHostMountsAChildInIt_Then_TheChildsInsertionEffectRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);

            // Act
            s_setExtra.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_lateInsertionSetups, Is.EqualTo(1),
                "React commits a child mounted in a primary tree the boundary had shown, hidden as it is");
        }

        private static void SetBlocked(MountedTree mounted)
        {
            s_setBlocked.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void SetTick(MountedTree mounted)
        {
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static StateUpdater<bool> s_setBlocked;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<bool> s_setMounted;
        private static StateUpdater<bool> s_setExtra;
        private static int s_lateInsertionSetups;
        private static bool s_probeDepsFollowTick;
        private static VelvetTaskCompletionSource<int> s_source;
        private static int s_insertionSetups;
        private static int s_layoutSetups;
        private static int s_layoutCleanups;

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(false);
            var (tick, setTick) = Hooks.UseState(0);
            var (extra, setExtra) = Hooks.UseState(false);
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
                            V.Component(PendingReaderRender, key: "reader"),
                        }
                        : Array.Empty<VNode>()),
            });
        }

        [Component]
        private static VNode PendingReaderRender()
        {
            var value = Hooks.Use<int>(_ => (s_source = new VelvetTaskCompletionSource<int>()).Task, "pending-reader");
            return V.Label(text: "reader:" + value);
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

        // Pending for good once blocked.
        [Component]
        private static VNode ReaderRender(bool blocked)
        {
            var value = Hooks.Use<int>(_ => blocked
                ? new VelvetTaskCompletionSource<int>().Task
                : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode ProbeRender(int tick)
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_insertionSetups++;
                return (Action)null;
            }, new object[] { s_probeDepsFollowTick ? tick : 0 });
            Hooks.UseLayoutEffect(() =>
            {
                s_layoutSetups++;
                return (Action)(() => s_layoutCleanups++);
            }, new object[] { tick });
            return V.Label(text: "probe:" + tick);
        }
    }
}
