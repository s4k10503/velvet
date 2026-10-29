using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a transition's time-sliced pass does when the Portal it mounts suspends with no Suspense above
    /// it, in the two places that happen: the first slice, whose walk parks with rows still to place, and a later
    /// slice that parks again before the drain at its end suspends. The parked pass waits for the retry the
    /// resolve asks for rather than resuming, and that retry's commit is the one that takes <c>isPending</c> down
    /// and runs the pass's effects, once.
    /// </summary>
    [TestFixture]
    internal sealed class ParkedTransitionPassSuspendTests
    {
        // The Portal sits at the first row where the first slice's drain mounts it, and at the twenty-first where a
        // later slice's does. Both leave rows behind it unplaced under a one-row budget.
        private const int PortalInTheFirstSlice = 0;
        private const int PortalInALaterSlice = 20;
        private const int EffectRowAt = 3;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_fiber = null;
            s_start = default;
            s_setTick = default;
            s_target = null;
            s_source = null;
            s_portalAt = 0;
            s_readerRenders = 0;
            s_isPending = false;
            s_hostEffectTicks.Clear();
            s_rowEffectSetups = 0;
        }

        // GREEN_ON_BASE(characterization): the merge base already takes isPending down when the retry commits.
        // This pins that the flag stays lit while the first slice's pass is suspended and parked.
        [Test]
        public void Given_ATransitionWhoseFirstSliceParkedBeforeItsPortalChildSuspended_When_TheResourceResolvesAndThePassCommits_Then_IsPendingClears()
        {
            // Arrange
            using var mounted = MountAndSuspendInTheFirstSlice();
            var pendingWhileSuspended = s_fiber.IsTransitionPending;

            // Act
            ResumeWhileSuspended();
            ResolveAndCommit(mounted);

            // Assert
            Assert.That((pendingWhileSuspended, s_isPending), Is.EqualTo((true, false)),
                "The transition stays pending while its pass is suspended and parked, and the commit that finally lands it takes the indicator down");
        }

        // GREEN_ON_BASE(characterization): the merge base already takes isPending down when the retry commits.
        // This pins that the flag stays lit through a later slice that parked again and then suspended.
        [Test]
        public void Given_ATransitionWhoseLaterSliceParkedAndThenSuspendedInThePortalDrain_When_TheResourceResolvesAndThePassCommits_Then_IsPendingClears()
        {
            // Arrange
            using var mounted = MountAndSuspendInALaterSlice();
            var pendingWhileSuspended = s_fiber.IsTransitionPending;

            // Act
            ResolveAndCommit(mounted);

            // Assert
            Assert.That((pendingWhileSuspended, s_isPending), Is.EqualTo((true, false)),
                "A slice that parks again and then suspends leaves the transition pending until the retry commits it");
        }

        // GREEN_ON_BASE(characterization): the merge base already runs the host's effect once per committed tick.
        // This pins that the scheduled resume of the suspended pass adds no run.
        [Test]
        public void Given_ATransitionWhoseFirstSliceParkedBeforeItsPortalChildSuspended_When_TheScheduledResumeRunsAndTheResourceResolves_Then_TheHostsEffectRunsOncePerTick()
        {
            // Arrange
            using var mounted = MountAndSuspendInTheFirstSlice();

            // Act
            ResumeWhileSuspended();
            ResolveAndCommit(mounted);

            // Assert
            Assert.That(string.Join("|", s_hostEffectTicks), Is.EqualTo("0|1"),
                "The pass's own effect runs once, with the commit of the retry, and neither the suspended pass nor its resume runs it");
        }

        // GREEN_ON_BASE(characterization): the merge base already runs the host's effect once per committed tick.
        // This pins that the resumes of a pass that parked again add no run.
        [Test]
        public void Given_ATransitionWhoseLaterSliceParkedAndThenSuspendedInThePortalDrain_When_TheResourceResolves_Then_TheHostsEffectRunsOncePerTick()
        {
            // Arrange
            using var mounted = MountAndSuspendInALaterSlice();

            // Act
            ResolveAndCommit(mounted);

            // Assert
            Assert.That(string.Join("|", s_hostEffectTicks), Is.EqualTo("0|1"),
                "The slices that resumed before the suspend and the retry after it run the pass's effect once between them");
        }

        // GREEN_ON_BASE(characterization): the merge base already runs a mounted component's effect once.
        // This pins that the scheduled resume of the suspended pass does not run it as well.
        [Test]
        public void Given_AComponentTheFirstSlicesPassMounted_When_TheScheduledResumeRunsAndTheResourceResolves_Then_ItsEffectRunsOnce()
        {
            // Arrange
            using var mounted = MountAndSuspendInTheFirstSlice();

            // Act
            ResumeWhileSuspended();
            ResolveAndCommit(mounted);

            // Assert
            Assert.That(s_rowEffectSetups, Is.EqualTo(1),
                "A component mounted by a pass that suspended runs its effect with the commit that keeps it, once");
        }

        // GREEN_ON_BASE(characterization): the merge base already runs a mounted component's effect once.
        // This pins that the slices before the suspend do not run it as well.
        [Test]
        public void Given_AComponentASliceThatParkedAgainMounted_When_TheResourceResolves_Then_ItsEffectRunsOnce()
        {
            // Arrange
            using var mounted = MountAndSuspendInALaterSlice();

            // Act
            ResolveAndCommit(mounted);

            // Assert
            Assert.That(s_rowEffectSetups, Is.EqualTo(1),
                "A component mounted by slices that parked before the suspend runs its effect with the retry's commit, once");
        }

        // The first slice's budget runs out after the Portal row, so its drain mounts the Portal with the rows
        // behind it unplaced.
        private MountedTree MountAndSuspendInTheFirstSlice()
        {
            var mounted = MountAndStartTheTransition(PortalInTheFirstSlice);
            s_fiber.FlushStateWithTinyBudgetForTest();
            return mounted;
        }

        // The Portal is the twenty-first row, so a slice that has placed it parks with rows behind it, and the
        // drain at the end of that slice is the one that suspends.
        private MountedTree MountAndSuspendInALaterSlice()
        {
            var mounted = MountAndStartTheTransition(PortalInALaterSlice);
            s_fiber.FlushStateWithTinyBudgetForTest();
            for (var slices = 0; slices < 100 && s_fiber.HasPendingReconcileWorkForTest() && s_readerRenders == 0; slices++)
            {
                FiberWorkLoop.ContinueReconcile(s_fiber);
            }
            return mounted;
        }

        // The render that shows isPending drains first, as it does ahead of the transition's own render, so the flush
        // the caller makes next is the transition lane's alone.
        private MountedTree MountAndStartTheTransition(int portalAt)
        {
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            s_target = new VisualElement { name = "portal-target" };
            s_source = new VelvetTaskCompletionSource<int>();
            s_portalAt = portalAt;
            var mounted = V.Mount(_root, V.Component(TransitionHostRender, key: "transition-host"));
            mounted.FlushEffectsForTest();
            s_start.Invoke(() => s_setTick.Invoke(1));
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted;
        }

        // What the UIToolkit scheduler runs on the next frame for a parked pass, which EditMode does not tick.
        private static void ResumeWhileSuspended() => FiberWorkLoop.ContinueReconcile(s_fiber);

        private static void ResolveAndCommit(MountedTree mounted)
        {
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();
            s_fiber.DrainTimeSlicedReconcileForTest();
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static ComponentFiber s_fiber;
        private static TransitionStarter s_start;
        private static StateUpdater<int> s_setTick;
        private static VisualElement s_target;
        private static VelvetTaskCompletionSource<int> s_source;
        private static int s_portalAt;
        private static int s_readerRenders;
        private static bool s_isPending;
        private static readonly List<int> s_hostEffectTicks = new();
        private static int s_rowEffectSetups;

        // Tick zero is one row. The transition's write makes it forty-one, with the Portal at s_portalAt and a
        // component with an effect at EffectRowAt.
        [Component]
        private static VNode TransitionHostRender()
        {
            s_fiber = FiberAmbientStack.Current;
            var (isPending, start) = Hooks.UseTransition();
            var (tick, setTick) = Hooks.UseState(0);
            s_start = start;
            s_setTick = setTick;
            s_isPending = isPending;
            Hooks.UseEffect(() =>
            {
                s_hostEffectTicks.Add(tick);
                return (Action)null;
            }, new object[] { tick });
            var rowCount = tick == 0 ? 1 : 41;
            var children = new VNode[rowCount];
            for (var i = 0; i < rowCount; i++)
            {
                if (tick != 0 && i == s_portalAt)
                {
                    children[i] = V.Portal(s_target, new VNode[] { V.Component(TransitionReaderRender, key: "reader") });
                }
                else if (tick != 0 && i == EffectRowAt)
                {
                    children[i] = V.Component(TransitionEffectRowRender, key: "effect-row");
                }
                else
                {
                    children[i] = V.Label(text: "row-" + i);
                }
            }
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode TransitionReaderRender()
        {
            s_readerRenders++;
            var value = Hooks.Use<int>(_ => s_source.Task, 1);
            return V.Label(text: "child:" + value);
        }

        [Component]
        private static VNode TransitionEffectRowRender()
        {
            Hooks.UseEffect(() =>
            {
                s_rowEffectSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "effect-row");
        }
    }
}
