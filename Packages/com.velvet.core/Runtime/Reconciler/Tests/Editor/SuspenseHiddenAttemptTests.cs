using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a render of the children a <c>V.Suspense</c> keeps hidden is an attempt, as React's attempt at a
    /// primary that suspends again is: it commits nothing, so the deps the reveal compares against are those of
    /// the last render a commit showed, and the reveal's own render is the one that queues what is owed.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenAttemptTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            s_setBlocked = default;
            s_setTick = default;
            s_setOther = default;
            s_memoProbe = false;
            s_source = null;
            s_insertionSetups = 0;
            s_passiveSetups = 0;
            s_probeRenders = 0;
            s_callback = null;
        }

        [Test]
        public void Given_AShownProbeHiddenAndItsDepsChangedAndRestored_When_TheBoundaryReveals_Then_ItsInsertionEffectDoesNotRunAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);
            SetTick(mounted, 0);

            // Act
            Reveal(mounted);

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(1),
                "The reveal compares against the deps the last committed render left, and they are the restored ones");
        }

        [Test]
        public void Given_AShownProbeHiddenAndItsDepsChangedAndRestored_When_TheBoundaryReveals_Then_ItsPassiveEffectDoesNotRunAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.FlushEffectsForTest();
            SetBlocked(mounted);
            SetTick(mounted, 1);
            SetTick(mounted, 0);
            mounted.FlushEffectsForTest();

            // Act
            Reveal(mounted);
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_passiveSetups, Is.EqualTo(1),
                "The reveal compares against the deps the last committed render left, and they are the restored ones");
        }

        [Test]
        public void Given_AShownProbeHiddenAndItsDepsChangedAndRestored_When_TheBoundaryReveals_Then_ItsCallbackIsTheOneItCommittedBeforeHiding()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var committed = s_callback;
            SetBlocked(mounted);
            SetTick(mounted, 1);
            SetTick(mounted, 0);

            // Act
            Reveal(mounted);

            // Assert
            Assert.That(ReferenceEquals(committed, s_callback), Is.True,
                "A render the reveal does not show leaves the callback's deps at the committed render's");
        }

        [Test]
        public void Given_AShownProbeInsideAnInnerBoundaryThatShowsItsChildrenUnderAHidingOuterOne_When_TheOuterRevealsAfterTheDepsChangedAndRestored_Then_ItsInsertionEffectDoesNotRunAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestedHostRender, key: "nested-host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);
            SetTick(mounted, 0);

            // Act
            Reveal(mounted);

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(1),
                "The outer boundary decides for the renders of the boundary inside it, which shows its children");
        }

        // GREEN_ON_BASE(characterization): the base renders the probe from the host's render that reveals it, once.
        // What this pins is that the render the attempts owe for the reveal is that one and no second.
        [Test]
        public void Given_AShownProbeRenderedWhileHidden_When_TheBoundaryReveals_Then_ItRendersOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);
            var rendersBefore = s_probeRenders;

            // Act
            Reveal(mounted);

            // Assert
            Assert.That(s_probeRenders - rendersBefore, Is.EqualTo(1),
                "The reveal renders a component the boundary kept hidden once");
        }

        // GREEN_ON_BASE(characterization): the base runs the effect the hidden render queued, at the reveal. What this
        // pins is that a memoized probe, which bails on unchanged props, still renders for the reveal and queues it.
        [Test]
        public void Given_AMemoizedShownProbeRenderedWhileHidden_When_TheBoundaryReveals_Then_ItsInsertionEffectRuns()
        {
            // Arrange
            s_memoProbe = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);

            // Act
            Reveal(mounted);

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(2),
                "The attempt queued nothing, so the reveal renders the probe again for the effect it owes");
        }

        // GREEN_ON_BASE(characterization): the base promotes the reveal's render the same way. What this pins is that
        // the commit of a render the Suspense shows promotes its deps.
        [Test]
        public void Given_AShownProbeRevealedAfterItsDepsChangedWhileHidden_When_TheHostRendersAgainWithTheSameDeps_Then_ItsInsertionEffectDoesNotRunAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            SetBlocked(mounted);
            SetTick(mounted, 1);
            Reveal(mounted);

            // Act
            s_setOther.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(2),
                "The reveal committed the changed deps, so the next render with them queues nothing");
        }

        // GREEN_ON_BASE(characterization): the base clears what a render that threw queued. What this pins is that
        // an insertion effect queued by a render that suspended does not run at the reveal of a later render whose
        // deps match the committed ones.
        [Test]
        public void Given_AShownProbeWhoseRenderWithChangedDepsSuspended_When_ItsDepsAreRestoredAndTheBoundaryReveals_Then_ItsInsertionEffectDoesNotRunAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SuspendingHostRender, key: "suspending-host"));
            SetTick(mounted, 1);
            SetTick(mounted, 0);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_insertionSetups, Is.EqualTo(1),
                "A render that threw committed nothing, and its queued effect is not owed to the render after it");
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

        private static void Reveal(MountedTree mounted)
        {
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static StateUpdater<int> s_setBlocked;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setOther;
        private static bool s_memoProbe;
        private static VelvetTaskCompletionSource<int> s_source;
        private static Action s_callback;
        private static int s_insertionSetups;
        private static int s_passiveSetups;
        private static int s_probeRenders;

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setBlocked = setBlocked;
            s_setTick = setTick;
            s_setOther = setOther;
            var probe = s_memoProbe
                ? V.Component(MemoProbeRender, tick, key: "probe")
                : V.Component(ProbeRender, tick, key: "probe");
            return V.Div(name: "host-" + other, children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        probe,
                        V.Component(ReaderRender, blocked, key: "reader"),
                    }),
            });
        }

        [Component]
        private static VNode SuspendingHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(SuspendingProbeRender, tick, key: "probe") }),
            });
        }

        [Component]
        private static VNode NestedHostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            s_setBlocked = setBlocked;
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer loading"),
                    children: new VNode[]
                    {
                        V.Suspense(
                            fallback: V.Label(text: "inner loading"),
                            children: new VNode[] { V.Component(ProbeRender, tick, key: "probe") }),
                        V.Component(ReaderRender, blocked, key: "reader"),
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

        // Suspends on its first render with tick 1 and never resolves, so tick 0 is the only way back.
        [Component]
        private static VNode SuspendingProbeRender(int tick)
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_insertionSetups++;
                return (Action)null;
            }, new object[] { tick });
            Hooks.Use<int>(_ => tick == 1
                ? (s_source = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), tick);
            return V.Label(text: "suspending:" + tick);
        }

        [Component(Memoize = true)]
        private static VNode MemoProbeRender(int tick)
        {
            s_probeRenders++;
            s_callback = Hooks.UseCallback<Action>(() => { }, new object[] { tick });
            Hooks.UseInsertionEffect(() =>
            {
                s_insertionSetups++;
                return (Action)null;
            }, new object[] { tick });
            Hooks.UseEffect(() =>
            {
                s_passiveSetups++;
                return (Action)null;
            }, new object[] { tick });
            return V.Label(text: "probe:" + tick);
        }

        [Component]
        private static VNode ProbeRender(int tick)
        {
            s_probeRenders++;
            s_callback = Hooks.UseCallback<Action>(() => { }, new object[] { tick });
            Hooks.UseInsertionEffect(() =>
            {
                s_insertionSetups++;
                return (Action)null;
            }, new object[] { tick });
            Hooks.UseEffect(() =>
            {
                s_passiveSetups++;
                return (Action)null;
            }, new object[] { tick });
            return V.Label(text: "probe:" + tick);
        }
    }
}
