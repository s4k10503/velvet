using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the release of what was held while a root rendered or committed: in the order it was asked for, what an
    /// item holds behind what was waiting, one item's exception logged without stopping the rest or replacing the entry's
    /// own, held work of a root that earlier held work took down left unmade, a request that keeps asking for itself
    /// bounded per release while the rest still runs, a root listed as owing the sync lane flushed only while it still
    /// owes it, and a render or commit step reached with no entry on the stack asserted.
    /// </summary>
    [TestFixture]
    internal sealed class HeldWorkReleaseTests
    {
        private Reconciler _walker = null!;
        private Reconciler _holder = null!;
        private readonly List<MountedTree> _roots = new();

        [SetUp]
        public void CreateTheReconcilers()
        {
            FiberStrictMode.Enabled = false;
            _walker = new Reconciler();
            _holder = new Reconciler();
        }

        [TearDown]
        public void DisposeTheReconcilers()
        {
            foreach (var root in _roots) root.Dispose();
            _roots.Clear();
            _walker.Dispose();
            _holder.Dispose();
        }

        private FiberBatchScheduler Holder => _holder.Context.BatchScheduler;

        // A walk of the walker's reconciler whose element creation runs ask, inside the entry the walk opens.
        private void WalkAsking(Action ask)
            => _walker.Reconcile(new VisualElement(), Array.Empty<VNode>(), new VNode[] { V.ScrollView(onCreated: _ => ask()) });

        private static void HoldFor(FiberBatchScheduler owner, Action run)
            => FiberExecutionContext.Hold(FiberExecutionContext.Request.Flush, null, owner, run);

        // Held work that, as it is made, holds more: the release runs it with nothing set, so it opens its own entry.
        private static Action Entering(Action ask) => () => FiberExecutionContext.Enter<object?>(null, null, _ => ask());

        private void MountOn(VisualElement host) => _roots.Add(V.Mount(host, V.Label(text: "root")));

        // --- An item that throws ---

        [Test]
        public void Given_AHeldItemThatThrows_When_TheOwnersPassEndsInAnException_Then_TheOwnersExceptionPropagates()
        {
            // Arrange — the held item's exception is logged
            LogAssert.Expect(LogType.Exception, new Regex("held"));
            var patcher = new FiberNodePatcher(_walker.Context);
            Action ask = () =>
            {
                HoldFor(Holder, () => throw new InvalidOperationException("held"));
                ChildListWrites.RecordPatchPre(new PendingPatch(patcher, new Label(), null, new ElementNode(), 0));
            };

            // Act
            TestDelegate act = () => WalkAsking(ask);

            // Assert
            Assert.Throws<NullReferenceException>(act);
        }

        [Test]
        public void Given_AHeldItemThatThrows_When_ThePassEnds_Then_TheItemAfterItStillRendered()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("held"));
            var host = new VisualElement();

            // Act
            WalkAsking(() =>
            {
                HoldFor(Holder, () => throw new InvalidOperationException("held"));
                MountOn(host);
            });

            // Assert
            Assert.That(host.childCount, Is.EqualTo(1));
        }

        // React reports an error a root's work throws where that work ran, not to whatever ended the entry before it.
        [Test]
        public void Given_AHeldItemThatThrows_When_ThePassEnds_Then_ItsExceptionIsLoggedRatherThanThrown()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("held"));

            // Act
            TestDelegate act = () => WalkAsking(() => HoldFor(Holder, () => throw new InvalidOperationException("held")));

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        private static FiberBatchScheduler s_holdIn = null!;

        // Holds work that throws, from inside its own first walk.
        [Component]
        private static VNode HoldsWorkThatThrows()
            => V.ScrollView(onCreated: _ => HoldFor(s_holdIn, () => throw new InvalidOperationException("held")));

        // Holds work that throws, and then writes its state from every layout effect it runs, past the nested-update limit.
        [Component]
        private static VNode HoldsWorkThatThrowsAndRunsAway()
        {
            var (n, setN) = Hooks.UseState(0);
            Hooks.UseLayoutEffect(() =>
            {
                setN.Invoke(n + 1);
                return (Action)null!;
            }, new object[] { n });
            return V.ScrollView(onCreated: _ => HoldFor(s_holdIn, () => throw new InvalidOperationException("held")));
        }

        [Test]
        public void Given_HeldWorkThatThrows_When_TheMountThatHeldItEnds_Then_VMountReturnsItsHandle()
        {
            // Arrange
            s_holdIn = Holder;
            LogAssert.Expect(LogType.Exception, new Regex("held"));

            // Act
            var mounted = V.Mount(new VisualElement(), V.Component(HoldsWorkThatThrows));
            _roots.Add(mounted);

            // Assert
            Assert.That(mounted, Is.Not.Null);
        }

        [Test]
        public void Given_HeldWorkThatThrows_When_TheMountThatHeldItEndsInAnExceptionOfItsOwn_Then_ItsOwnPropagates()
        {
            // Arrange — the held work's exception is logged, since the mount's is the one in flight
            s_holdIn = Holder;
            LogAssert.Expect(LogType.Exception, new Regex("held"));

            // Act
            TestDelegate act = () => _roots.Add(V.Mount(new VisualElement(), V.Component(HoldsWorkThatThrowsAndRunsAway)));

            // Assert
            Assert.That(act, Throws.InvalidOperationException.With.Message.StartsWith("Maximum update depth exceeded"));
        }

        // --- Order ---

        [Test]
        public void Given_HeldWorkThatAsksForMoreWhileItIsMade_When_ThePassEnds_Then_TheMoreRunsAfterWhatWasAheadOfIt()
        {
            // Arrange
            var log = new List<string>();

            // Act
            WalkAsking(() =>
            {
                HoldFor(Holder, Entering(() =>
                {
                    log.Add("a");
                    HoldFor(Holder, () => log.Add("c"));
                }));
                HoldFor(Holder, () => log.Add("b"));
            });

            // Assert
            Assert.That(string.Join(",", log), Is.EqualTo("a,b,c"));
        }

        [Test]
        public void Given_HeldWorkThatRunsAnEntryOfItsOwn_When_ThatEntryEnds_Then_TheWorkBehindItWaitsForItToReturn()
        {
            // Arrange
            var log = new List<string>();

            // Act
            WalkAsking(() =>
            {
                HoldFor(Holder, () =>
                {
                    FiberExecutionContext.Enter<object?>(null, null, _ => log.Add("entry"));
                    log.Add("after the entry");
                });
                HoldFor(Holder, () => log.Add("behind"));
            });

            // Assert
            Assert.That(string.Join(",", log), Is.EqualTo("entry,after the entry,behind"));
        }

        [Test]
        public void Given_TwoBareReconcilesAskedForInsideAWalk_When_ThePassEnds_Then_BothHaveRendered()
        {
            // Arrange — neither carries a key to be held once under
            var first = new VisualElement();
            var second = new VisualElement();

            // Act
            WalkAsking(() =>
            {
                _holder.Reconcile(first, Array.Empty<VNode>(), new VNode[] { V.Div() });
                _holder.Reconcile(second, Array.Empty<VNode>(), new VNode[] { V.Div() });
            });

            // Assert
            Assert.That(second.childCount, Is.EqualTo(1));
        }

        private static StateUpdater<int> s_setCounter;
        private static int s_counterRenders;

        [Component]
        private static VNode CountsItsUpdates()
        {
            var (_, set) = Hooks.UseState(0);
            s_setCounter = set;
            s_counterRenders++;
            return V.Label(text: "counted");
        }

        // A mount held for a scheduler is a request of another kind from its flush, which is still held.
        [Test]
        public void Given_AContextWithAMountHeldWhoseFlushIsAskedForInsideAWalk_When_ThePassEnds_Then_ItHasRendered()
        {
            // Arrange
            var counted = V.Mount(new VisualElement(), V.Component(CountsItsUpdates));
            _roots.Add(counted);
            var scheduler = counted.Root.Reconciler!.Context.BatchScheduler;
            var before = s_counterRenders;

            // Act
            WalkAsking(() =>
            {
                FiberExecutionContext.Hold(FiberExecutionContext.Request.Mount, scheduler, scheduler, () => { });
                s_setCounter.Invoke(1);
                scheduler.FlushImmediate();
            });

            // Assert
            Assert.That(s_counterRenders - before, Is.EqualTo(1));
        }

        private static int s_disposedRootRenders;

        [Component]
        private static VNode CountsItsFirstRender()
        {
            s_disposedRootRenders++;
            return V.Label(text: "first");
        }

        // The mount is held ahead of the unmount its handle's Dispose holds, so only taking it out of the queue keeps the
        // root from rendering.
        [Test]
        public void Given_ARootMountedAndDisposedInOneWalk_When_ThePassEnds_Then_ItNeverRenders()
        {
            // Arrange
            s_disposedRootRenders = 0;

            // Act
            WalkAsking(() => V.Mount(new VisualElement(), V.Component(CountsItsFirstRender)).Dispose());

            // Assert
            Assert.That(s_disposedRootRenders, Is.Zero);
        }

        [Test]
        public void Given_HeldWorkOfARootThatEarlierHeldWorkDisposed_When_TheReleaseReachesIt_Then_ItDoesNotRun()
        {
            // Arrange
            var disposed = MountALabel();
            var ran = false;

            // Act
            WalkAsking(() =>
            {
                HoldFor(_walker.Context.BatchScheduler, () => disposed.Dispose());
                HoldFor(disposed.GetSchedulerForTest(), () => ran = true);
            });

            // Assert
            Assert.That(ran, Is.False);
        }

        // The fiber shares the holder's context, which its unmount leaves alive, and the unmount takes its reconciler.
        [Test]
        public void Given_AHeldMountOfAFiberThatEarlierHeldWorkUnmounted_When_TheReleaseReachesIt_Then_NothingIsLogged()
        {
            // Arrange
            var fiber = FiberRenderer.CreateRoot(CountsItsFirstRender);

            // Act
            WalkAsking(() =>
            {
                HoldFor(_walker.Context.BatchScheduler, () => FiberRenderer.Unmount(fiber));
                FiberRenderer.Mount(fiber, new VisualElement(), _holder.Context);
            });

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        // --- The bound ---

        // One request, a flush of the holder's scheduler, held under the key a held flush carries.
        private void HoldTheRepeatedRequest(Action run)
            => FiberExecutionContext.Hold(FiberExecutionContext.Request.Flush, Holder, Holder, run);

        // Held work that asks for itself again as that one request while it is made, times runs in all, finite where the
        // limit is not kept. At the fiftieth run, then is called after it has asked again.
        private Action AskingForItself(int times, Action? eachRun = null, Action? then = null)
            => () =>
            {
                var left = times;
                Action again = null!;
                again = Entering(() =>
                {
                    eachRun?.Invoke();
                    if (--left == 0) return;
                    HoldTheRepeatedRequest(again);
                    if (left == times - 50) then?.Invoke();
                });
                HoldTheRepeatedRequest(again);
            };

        [Test]
        public void Given_HeldWorkThatAsksForItselfAgainSixtyTimes_When_ThePassEnds_Then_TheNestedUpdateLimitIsLogged()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("Maximum update depth exceeded"));

            // Act
            TestDelegate act = () => WalkAsking(AskingForItself(60));

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_HeldWorkThatAsksForItselfAgainSixtyTimes_When_ThePassEnds_Then_ItRanAsOftenAsTheLimitAllows()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("Maximum update depth exceeded"));
            var runs = 0;

            // Act
            WalkAsking(AskingForItself(60, () => runs++));

            // Assert
            Assert.That(runs, Is.EqualTo(50));
        }

        // Requests a root makes once each, as sixty suspended readers resuming together do, are not one request asking for
        // itself again.
        [Test]
        public void Given_SixtyRequestsOfOneRootEachUnderItsOwnKey_When_ThePassEnds_Then_EveryOneRuns()
        {
            // Arrange
            var runs = 0;

            // Act
            WalkAsking(() =>
            {
                for (var i = 0; i < 60; i++)
                {
                    FiberExecutionContext.Hold(FiberExecutionContext.Request.Resolve, new object(), Holder, () => runs++);
                }
            });

            // Assert
            Assert.That(runs, Is.EqualTo(60));
        }

        [Test]
        public void Given_SixtyRequestsOfOneRootWithNoKey_When_ThePassEnds_Then_EveryOneRuns()
        {
            // Arrange
            var runs = 0;

            // Act
            WalkAsking(() =>
            {
                for (var i = 0; i < 60; i++) HoldFor(Holder, () => runs++);
            });

            // Assert
            Assert.That(runs, Is.EqualTo(60));
        }

        // The fiftieth run holds another request of the same root behind the run the bound refuses.
        [Test]
        public void Given_ARequestPastTheBound_When_ItsRootAsksForAnotherInTheSameRelease_Then_ThatOneRuns()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("Maximum update depth exceeded"));
            var ran = false;

            // Act
            WalkAsking(AskingForItself(60, then: () =>
                FiberExecutionContext.Hold(FiberExecutionContext.Request.Resolve, new object(), Holder, () => ran = true)));

            // Assert
            Assert.That(ran, Is.True);
        }

        // The fiftieth run holds a mount which, as it is made, asks for the refused request once more.
        [Test]
        public void Given_ARequestPastTheBound_When_ItIsAskedForAgainInTheSameRelease_Then_TheLimitIsLoggedOnce()
        {
            // Arrange — one expected log, so a second is unexpected
            LogAssert.Expect(LogType.Exception, new Regex("Maximum update depth exceeded"));

            // Act
            WalkAsking(AskingForItself(60, then: () => FiberExecutionContext.Hold(
                FiberExecutionContext.Request.Mount, null, Holder, Entering(() => HoldTheRepeatedRequest(() => { })))));

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_ARequestStoppedAtTheBound_When_ALaterReleaseAsksForIt_Then_ItRuns()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("Maximum update depth exceeded"));
            WalkAsking(AskingForItself(60));
            var ran = false;

            // Act
            WalkAsking(() => HoldTheRepeatedRequest(() => ran = true));

            // Assert
            Assert.That(ran, Is.True);
        }

        [Test]
        public void Given_ARequestThatRanThirtyTimesInARelease_When_TheNextReleaseRunsItThirtyTimesMore_Then_EveryRunIsMade()
        {
            // Arrange
            var runs = 0;
            WalkAsking(AskingForItself(30, () => runs++));

            // Act
            WalkAsking(AskingForItself(30, () => runs++));

            // Assert
            Assert.That(runs, Is.EqualTo(60));
        }

        // --- The roots owing the sync lane ---

        private static Button s_echoButton;
        private static int s_echoRenders;

        // On a click asks for its own render, whose UseEffect asks for one more, off the sync lane.
        [Component]
        private static VNode EchoesItsClickFromAnEffect()
        {
            var (count, setCount) = Hooks.UseState(0);
            var (_, setEcho) = Hooks.UseState(0);
            s_echoRenders++;
            Hooks.UseEffect(() =>
            {
                if (count > 0) setEcho.Invoke(count);
                return (Action)null!;
            }, new object[] { count });
            return V.Button(text: "echo", onClick: () => setCount.Invoke(count + 1), refCallback: element =>
            {
                s_echoButton = (Button)element;
                return () => { };
            });
        }

        private MountedTree MountTheEcho()
        {
            var echo = V.Mount(new VisualElement(), V.Component(EchoesItsClickFromAnEffect));
            _roots.Add(echo);
            echo.FlushEffectsForTest();
            return echo;
        }

        private static List<FiberBatchScheduler> OwingTheSyncLane()
            => (List<FiberBatchScheduler>)typeof(FiberExecutionContext)
                .GetField("s_owingSyncLane", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        // The held click's drain leaves its root listed as owing the sync lane, with only a Normal-lane update queued.
        [Test]
        public void Given_AHeldClickWhoseEffectAsksForAnotherRender_When_ThePassEnds_Then_ThatRenderWaitsForTheRootsNextDrain()
        {
            // Arrange
            MountTheEcho();
            var before = s_echoRenders;

            // Act
            WalkAsking(() => s_echoButton.SimulateClick());

            // Assert
            Assert.That(s_echoRenders - before, Is.EqualTo(1));
        }

        [Test]
        public void Given_AClickOutsideEveryEntry_When_ItsFlushEnds_Then_ItsRootIsNoLongerListedAsOwingTheSyncLane()
        {
            // Arrange
            var echo = MountTheEcho();

            // Act
            s_echoButton.SimulateClick();

            // Assert
            Assert.That(OwingTheSyncLane(), Has.No.Member(echo.GetSchedulerForTest()));
        }

        // --- A render or commit step reached with no entry on the stack ---

        private MountedTree MountALabel()
        {
            var mounted = V.Mount(new VisualElement(), V.Label(text: "mounted"));
            _roots.Add(mounted);
            return mounted;
        }

        private static void InvokePrivate(Type type, string name, object argument)
            => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { argument });

        [Test]
        public void Given_AnEntryOpenedInsideAnother_When_ItBegins_Then_ItAssertsThatOneWasOpen()
        {
            // Arrange
            LogAssert.Expect(LogType.Assert, "An entry began while another renders or commits.");

            // Act
            TestDelegate act = () => FiberExecutionContext.Enter<object?>(null, null,
                _ => FiberExecutionContext.Enter<object?>(null, null, _ => { }));

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_WorkHeldWithNoEntryOnTheStack_When_ItIsHeld_Then_ItAssertsThatNoneWasOpen()
        {
            // Arrange — keyed, so that the case can take it back out before anything else meets it
            var key = new object();
            LogAssert.Expect(LogType.Assert, "Work was held with no entry on the stack to release it.");

            // Act
            TestDelegate act = () =>
            {
                FiberExecutionContext.Hold(FiberExecutionContext.Request.Flush, key, null, () => { });
                FiberExecutionContext.Drop(FiberExecutionContext.Request.Flush, key);
            };

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_APassReachedWithNoEntryOnTheStack_When_ItBegins_Then_ItAssertsThatItRanWithNone()
        {
            // Arrange
            LogAssert.Expect(LogType.Assert, "A reconcile pass ran with no entry on the stack.");

            // Act
            TestDelegate act = () => _holder.ReconcileCatching(new VisualElement(), Array.Empty<VNode>(),
                new VNode[] { V.Div() }, 0, 0, int.MaxValue);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_AResumeReachedWithNoEntryOnTheStack_When_ItContinues_Then_ItAssertsThatItRanWithNone()
        {
            // Arrange — a budget the first node already overruns parks the pass
            var rows = new VNode[50];
            for (var i = 0; i < rows.Length; i++) rows[i] = V.Div(key: "row-" + i);
            _holder.Reconcile(new VisualElement(), Array.Empty<VNode>(), rows, frameBudgetMs: 0.0001);
            LogAssert.Expect(LogType.Assert, "A resumed pass ran with no entry on the stack.");

            // Act
            TestDelegate act = () => _holder.ContinueReconcileWork(0);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_ARenderReachedWithNoEntryOnTheStack_When_ItRuns_Then_ItAssertsThatItRanWithNone()
        {
            // Arrange — the render's own pass asserts as well
            var mounted = MountALabel();
            LogAssert.Expect(LogType.Assert, "A render ran with no entry on the stack.");
            LogAssert.Expect(LogType.Assert, "A reconcile pass ran with no entry on the stack.");

            // Act
            TestDelegate act = () => FiberRenderer.RenderAndReconcile(mounted.Root);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_ALayoutCommitReachedWithNoEntryOnTheStack_When_ItRuns_Then_ItAssertsThatItRanWithNone()
        {
            // Arrange
            var mounted = MountALabel();
            LogAssert.Expect(LogType.Assert, "A layout commit ran with no entry on the stack.");

            // Act
            TestDelegate act = () => FiberEffects.CommitSubtreeEffects(mounted.Root);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_APassiveDrainReachedWithNoEntryOnTheStack_When_ItRuns_Then_ItAssertsThatItRanWithNone()
        {
            // Arrange
            var mounted = MountALabel();
            LogAssert.Expect(LogType.Assert, "A passive flush ran with no entry on the stack.");

            // Act
            TestDelegate act = () => InvokePrivate(typeof(FiberEffects), "DrainPassiveEffects", mounted.Root.Reconciler!.Context);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_AFibersOwnEffectsRunWithNoEntryOnTheStack_When_TheyRun_Then_ItAssertsThatTheyRanWithNone()
        {
            // Arrange
            var mounted = MountALabel();
            LogAssert.Expect(LogType.Assert, "A passive flush ran with no entry on the stack.");

            // Act
            TestDelegate act = () => InvokePrivate(typeof(FiberEffects), "RunEffects", mounted.Root);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        [Test]
        public void Given_ARangeReachedWithNoEntryOnTheStack_When_ItRenders_Then_ItAssertsThatItRanWithNone()
        {
            // Arrange
            var root = new VisualElement();
            var items = new string[100];
            for (var i = 0; i < items.Length; i++) items[i] = "item-" + i;
            var mounted = V.Mount(root, V.VirtualList(items, item => item, itemHeight: 50f,
                renderer: item => V.Label(text: item), overscan: 0));
            _roots.Add(mounted);
            var controller = mounted.Root.Reconciler!.Context.VirtualListControllers[root.Q<ScrollView>()];
            var body = typeof(FiberVirtualListController).GetMethod(
                "UpdateVisibleRangeWork", BindingFlags.Instance | BindingFlags.NonPublic)!;
            LogAssert.Expect(LogType.Assert, "A VirtualList range ran with no entry on the stack.");

            // Act
            TestDelegate act = () => body.Invoke(controller, new object[] { 2500f, 200f });

            // Assert
            Assert.That(act, Throws.Nothing);
        }
    }
}
