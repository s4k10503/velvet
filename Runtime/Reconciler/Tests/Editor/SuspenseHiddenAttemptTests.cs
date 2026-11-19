using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a render of the children a <c>V.Suspense</c> keeps hidden is an attempt, as React's attempt at a
    /// primary that suspends again is: it commits no effect, so the deps the reveal compares its effects against
    /// are those of the last render a commit showed, and the reveal's own render is the one that queues what is owed.
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
            s_setReads = default;
            s_setTheme = default;
            s_consumerTheme = null;
            s_memoFactoryRuns = 0;
            s_source = null;
            s_insertionSetups = 0;
            s_passiveSetups = 0;
            s_probeRenders = 0;
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

        // GREEN_ON_BASE(characterization): the base commits the context reads of every render. What this pins is that a
        // render under a shown Suspense does the same, so a context first read there still notifies the component.
        [Test]
        public void Given_AMemoizedConsumerThatFirstReadsAContextInARenderUnderAShownBoundary_When_TheProviderValueChanges_Then_ItShowsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextHostRender, key: "context-host"));
            s_setReads.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_consumerTheme, Is.EqualTo("dark"),
                "The render that first read the context is the one its later change is delivered to");
        }

        // GREEN_ON_BASE(characterization): the base runs a memo factory once per render under a double render too.
        // What this pins is that a render under a shown Suspense, whose effect deps are committed later, is
        // repeated by the double render against the memo it just committed rather than one it has yet to.
        [Test]
        public void Given_StrictModeAndAMemoizingProbeUnderAShownBoundary_When_ItsPropChanges_Then_ItsMemoFactoryRunsOncePerRender()
        {
            // Arrange
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(MemoHostRender, key: "memo-host"));
            var runsAfterMount = s_memoFactoryRuns;

            // Act
            SetTick(mounted, 1);

            // Assert
            Assert.That((runsAfterMount, s_memoFactoryRuns), Is.EqualTo((1, 2)),
                "A render under a shown Suspense runs the factory once, and the double render reuses its result");
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
        private static StateUpdater<int> s_setReads;
        private static StateUpdater<string> s_setTheme;
        private static string s_consumerTheme;
        private static int s_memoFactoryRuns;
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("light");
        private static bool s_memoProbe;
        private static VelvetTaskCompletionSource<int> s_source;
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
        private static VNode ContextHostRender()
        {
            var (reads, setReads) = Hooks.UseState(0);
            var (theme, setTheme) = Hooks.UseState("light");
            s_setReads = setReads;
            s_setTheme = setTheme;
            return V.Provider(ThemeContext, theme, new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(ThemeConsumerRender, reads, key: "consumer") }),
            });
        }

        // Reads the context from its second kind of render on, which the hook-order rule exists to forbid.
#pragma warning disable VEL101
        [Component(Memoize = true)]
        private static VNode ThemeConsumerRender(int reads)
        {
            var theme = "unread";
            if (reads > 0) theme = Hooks.UseContext(ThemeContext);
            s_consumerTheme = theme;
            return V.Label(text: theme);
        }
#pragma warning restore VEL101

        [Component]
        private static VNode MemoHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(MemoizingProbeRender, tick, key: "probe") }),
            });
        }

        [Component]
        private static VNode MemoizingProbeRender(int tick)
        {
            var value = Hooks.UseMemo(() =>
            {
                s_memoFactoryRuns++;
                return tick * 2;
            }, new object[] { tick });
            return V.Label(text: "memo:" + value);
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
