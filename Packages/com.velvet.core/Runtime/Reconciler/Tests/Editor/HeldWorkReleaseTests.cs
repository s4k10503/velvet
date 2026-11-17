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
            // First, since a root its own throw left owing the sync lane is drained where any other entry ends.
            if (s_rootAMountThrewOutOf != null) FiberRenderer.Dispose(s_rootAMountThrewOutOf);
            s_rootAMountThrewOutOf = null;
            foreach (var root in _roots) root.Dispose();
            _roots.Clear();
            foreach (var root in s_mountedFromCommits) root.Dispose();
            s_mountedFromCommits.Clear();
            _walker.Dispose();
            _holder.Dispose();
        }

        private static readonly List<MountedTree> s_mountedFromCommits = new();
        private static ComponentFiber? s_rootAMountThrewOutOf;

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

        private static int s_firstRenders;

        [Component]
        private static VNode CountsItsFirstRender()
        {
            s_firstRenders++;
            return V.Label(text: "first");
        }

        // The mount is held ahead of the unmount its handle's Dispose holds, so only taking it out of the queue keeps the
        // root from rendering.
        [Test]
        public void Given_ARootMountedAndDisposedInOneWalk_When_ThePassEnds_Then_ItNeverRenders()
        {
            // Arrange
            s_firstRenders = 0;

            // Act
            WalkAsking(() => V.Mount(new VisualElement(), V.Component(CountsItsFirstRender)).Dispose());

            // Assert
            Assert.That(s_firstRenders, Is.Zero);
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

        // --- Doors reached inside a walk ---

        [Test]
        public void Given_AFiberTheRendererMountsInsideAWalk_When_ThePassEnds_Then_ItHasRendered()
        {
            // Arrange
            s_firstRenders = 0;
            var fiber = FiberRenderer.CreateRoot(CountsItsFirstRender);

            // Act
            WalkAsking(() => FiberRenderer.Mount(fiber, new VisualElement(), _holder.Context));

            // Assert
            Assert.That(s_firstRenders, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARootsDrainAskedForInsideAWalk_When_ThePassEnds_Then_ItsQueuedUpdateHasRendered()
        {
            // Arrange
            var counted = V.Mount(new VisualElement(), V.Component(CountsItsUpdates));
            _roots.Add(counted);
            var scheduler = counted.GetSchedulerForTest();
            var before = s_counterRenders;

            // Act
            WalkAsking(() =>
            {
                s_setCounter.Invoke(1);
                scheduler.DrainImmediateForTest();
            });

            // Assert
            Assert.That(s_counterRenders - before, Is.EqualTo(1));
        }

        private static int s_effectRuns;

        [Component]
        private static VNode CountsItsEffectRuns()
        {
            Hooks.UseEffect(() =>
            {
                s_effectRuns++;
                return (Action)null!;
            }, Array.Empty<object>());
            return V.Label(text: "effects");
        }

        // Only the root's own passive flush runs its effect: the walker's, which the release runs ahead of it, does not.
        [Test]
        public void Given_ARootsPassiveFlushAskedForInsideAWalk_When_ThePassEnds_Then_ItsPendingEffectHasRun()
        {
            // Arrange
            var root = V.Mount(new VisualElement(), V.Component(CountsItsEffectRuns));
            _roots.Add(root);
            s_effectRuns = 0;

            // Act
            WalkAsking(() => root.GetSchedulerForTest().FlushPendingPassiveEffects());

            // Assert
            Assert.That(s_effectRuns, Is.EqualTo(1));
        }

        // --- The bound ---

        // One request, a flush of root's scheduler, the holder's where none is named, held under the key a held flush
        // carries.
        private void HoldTheRepeatedRequest(Action run, FiberBatchScheduler? root = null)
            => FiberExecutionContext.Hold(FiberExecutionContext.Request.Flush, root ?? Holder, root ?? Holder, run);

        // Held work that asks for itself again as that one request while it is made, times runs in all, finite where the
        // limit is not kept. At the fiftieth run, then is called after it has asked again.
        private Action AskingForItself(int times, Action? eachRun = null, Action? then = null, FiberBatchScheduler? root = null)
            => () =>
            {
                var left = times;
                Action again = null!;
                again = Entering(() =>
                {
                    eachRun?.Invoke();
                    if (--left == 0) return;
                    HoldTheRepeatedRequest(again, root);
                    if (left == times - 50) then?.Invoke();
                });
                HoldTheRepeatedRequest(again, root);
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

        // As above, the refused request asked for once more by a mount the fiftieth run holds.
        [Test]
        public void Given_ARequestPastTheBound_When_ItIsAskedForAgainInTheSameRelease_Then_ItDoesNotRunAgain()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("Maximum update depth exceeded"));
            var ranAgain = false;

            // Act
            WalkAsking(AskingForItself(60, then: () => FiberExecutionContext.Hold(
                FiberExecutionContext.Request.Mount, null, Holder, Entering(() => HoldTheRepeatedRequest(() => ranAgain = true)))));

            // Assert
            Assert.That(ranAgain, Is.False);
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

        // The click leaves the root owing the sync lane, and its flush joins the request already held under the same key,
        // which drains nothing; so the root still owes once the bound has refused that request and nothing else is held.
        [Test]
        public void Given_ARootOwingTheSyncLaneWhoseFlushTheBoundRefused_When_ThePassEnds_Then_TheReleaseReturns()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("Maximum update depth exceeded"));
            var root = MountTheEcho().GetSchedulerForTest();

            // Act
            TestDelegate act = () => WalkAsking(() =>
            {
                AskingForItself(60, root: root)();
                s_echoButton.SimulateClick();
            });

            // Assert — a release that took the refused root again would not return
            Assert.That(act, Throws.Nothing);
        }

        private static int s_hostRendered;
        private static int s_hostEffect;
        private static StateUpdater<int> s_setHost;
        private static ComponentFiber s_counterReaderFiber = null!;

        [Component]
        private static VNode ReadsTheCounter()
        {
            s_counterReaderFiber = FiberAmbientStack.Current!;
            Hooks.UseStore(s_counter, value => value);
            return V.Label(text: "reads");
        }

        [Component]
        private static VNode HostsACounterReader()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setHost = setN;
            s_hostRendered = n;
            Hooks.UseEffect(() =>
            {
                s_hostEffect = n;
                return (Action)null!;
            }, new object[] { n });
            return V.Div(children: new VNode[] { V.Component(ReadsTheCounter, key: "reader") });
        }

        // The counter's change raises the root's sync-lane flag beside its reader's update, and the unmount takes that
        // update out of the tier without a drain, which leaves the flag raised over an empty tier: nothing is owed.
        [Test]
        public void Given_ARootWhoseOnlySyncLaneUpdateIsUnmountedUndrained_When_TheUnmountEnds_Then_ItsPendingEffectHasNotRun()
        {
            // Arrange — the host's second render commits with its effect left pending
            s_counter = new Counter();
            var host = V.Mount(new VisualElement(), V.Component(HostsACounterReader));
            _roots.Add(host);
            host.FlushEffectsForTest();
            s_setHost.Invoke(1);
            host.FlushStateForTest();
            s_counter.Set(1);

            // Act
            FiberRenderer.Unmount(s_counterReaderFiber);

            // Assert — 0 where the release ran that effect
            Assert.That(s_hostRendered - s_hostEffect, Is.EqualTo(1));
        }

        private static readonly Ref<string> s_throwingHandle = new();
        private static StateUpdater<int> s_setThrower;

        [Component]
        private static VNode MountsQueuesAndThrows()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setThrower = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick > 0) s_mountedFromCommits.Add(V.Mount(new VisualElement(), V.Label(text: "mounted")));
                return null!;
            }), new object[] { tick });
            Hooks.UseImperativeHandle(s_throwingHandle, () =>
            {
                if (tick == 0) return "handle";
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Label(text: "thrower");
        }

        // The held mount makes the release proceed past an entry the exception is leaving, whose own root owes the sync
        // lane for the update the factory queued.
        [Test]
        public void Given_ADrainThatHoldsAMountAndThrowsAfterQueueingItsOwnUpdate_When_TheExceptionLeaves_Then_TheUpdateIsStillQueued()
        {
            // Arrange
            var thrower = V.Mount(new VisualElement(), V.Component(MountsQueuesAndThrows));
            _roots.Add(thrower);
            var scheduler = thrower.GetSchedulerForTest();
            s_setThrower.Invoke(1);

            // Act
            try
            {
                scheduler.DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }

            // Assert — 0 where the release drained the root again
            Assert.That(scheduler.ImmediatePendingCount, Is.EqualTo(1));
        }

        [Component]
        private static VNode QueuesAndThrowsFromItsFirstCommit()
        {
            var (tick, setTick) = Hooks.UseState(0);
            var root = FiberAmbientStack.Current!;
            while (root.Parent != null) root = root.Parent;
            s_rootAMountThrewOutOf = root;
            Hooks.UseImperativeHandle(s_throwingHandle, () =>
            {
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Label(text: "thrower");
        }

        // V.Mount's entry is the one an exception is leaving here: the first commit threw before the mount's own closing
        // flush, with its update owed the sync lane.
        [Test]
        public void Given_AMountWhoseFirstCommitThrowsAfterQueueingAnUpdate_When_TheExceptionLeavesVMount_Then_NothingIsLogged()
        {
            // Arrange
            TestDelegate mount = () => V.Mount(new VisualElement(), V.Component(QueuesAndThrowsFromItsFirstCommit));

            // Act
            try
            {
                mount();
            }
            catch (InvalidOperationException)
            {
            }

            // Assert — a drain the release ran again would have its throw logged
            LogAssert.NoUnexpectedReceived();
        }

        private static StateUpdater<int> s_setQueuesAndThrows;

        private static ComponentFiber s_queuesAndThrowsFiber = null!;
        private static int s_queuesAndThrowsTick;

        [Component]
        private static VNode QueuesAndThrowsOnEveryUpdate()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setQueuesAndThrows = setTick;
            s_queuesAndThrowsFiber = FiberAmbientStack.Current!;
            s_queuesAndThrowsTick = tick;
            Hooks.UseImperativeHandle(s_throwingHandle, () =>
            {
                if (tick == 0) return "handle";
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Label(text: "thrower");
        }

        private static int ThrowingDrainsOf(FiberBatchScheduler scheduler)
            => (int)typeof(FiberBatchScheduler)
                .GetField("_consecutiveThrowingDrains", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(scheduler)!;

        // The held flush's own entry is the one the exception leaves, inside the release, whose root it leaves owing the
        // sync lane for the update the factory queued.
        [Test]
        public void Given_AHeldFlushWhoseDrainThrowsAfterQueueingSyncLaneWork_When_ThePassEnds_Then_ItsRootHasThrownOnce()
        {
            // Arrange — the throw is logged where the release runs the held flush
            LogAssert.Expect(LogType.Exception, new Regex("handle failed"));
            var thrower = V.Mount(new VisualElement(), V.Component(QueuesAndThrowsOnEveryUpdate));
            _roots.Add(thrower);
            var scheduler = thrower.GetSchedulerForTest();

            // Act
            WalkAsking(() =>
            {
                s_setQueuesAndThrows.Invoke(1);
                scheduler.FlushImmediate();
            });

            // Assert — the bound of throwing drains where the release drained the root again
            Assert.That(ThrowingDrainsOf(scheduler), Is.EqualTo(1));
        }

        [Test]
        public void Given_AHeldFlushOfARootThatThrows_When_AHeldFlushOfItsFiberFollows_Then_ThatFiberIsNotRenderedAgain()
        {
            // Arrange — the throw is logged where the release runs the held flush
            LogAssert.Expect(LogType.Exception, new Regex("handle failed"));
            var thrower = V.Mount(new VisualElement(), V.Component(QueuesAndThrowsOnEveryUpdate));
            _roots.Add(thrower);
            var scheduler = thrower.GetSchedulerForTest();

            // Act
            WalkAsking(() =>
            {
                s_setQueuesAndThrows.Invoke(1);
                scheduler.FlushImmediate();
                FiberWorkLoop.FlushState(s_queuesAndThrowsFiber);
            });

            // Assert — 2 where the release rendered the fiber again
            Assert.That(s_queuesAndThrowsTick, Is.EqualTo(1));
        }

        // The Transition-lane update comes first: queued behind the Normal one, it would leave the fiber off the
        // Transition tier, whose drain would then have nothing of the root's to render.
        [Test]
        public void Given_AHeldFlushOfARootThatThrows_When_AHeldDrainOfItsTransitionTierFollows_Then_ItsFiberIsNotRenderedAgain()
        {
            // Arrange — the throw is logged where the release runs the held flush
            LogAssert.Expect(LogType.Exception, new Regex("handle failed"));
            var thrower = V.Mount(new VisualElement(), V.Component(QueuesAndThrowsOnEveryUpdate));
            _roots.Add(thrower);
            var scheduler = thrower.GetSchedulerForTest();

            // Act
            WalkAsking(() =>
            {
                s_queuesAndThrowsFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
                s_setQueuesAndThrows.Invoke(1);
                scheduler.FlushImmediate();
                scheduler.DrainDelayedForTest();
            });

            // Assert — 2 where the Transition tier's drain rendered the fiber again
            Assert.That(s_queuesAndThrowsTick, Is.EqualTo(1));
        }

        private static bool s_childsEffectRan;
        private static string? s_mountedSaw;

        [Component]
        private static VNode EffectChild()
        {
            Hooks.UseEffect(() =>
            {
                s_childsEffectRan = true;
                return (Action)null!;
            }, Array.Empty<object>());
            return V.Label(text: "child");
        }

        [Component]
        private static VNode ReadsWhetherTheChildsEffectRan()
        {
            s_mountedSaw = s_childsEffectRan ? "ran" : "pending";
            return V.Label(text: "mounted");
        }

        private static StateUpdater<int> s_setChildMounter;

        [Component]
        private static VNode MountsAnEffectChildHoldsARootAndThrows()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setChildMounter = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick > 0) s_mountedFromCommits.Add(V.Mount(new VisualElement(), V.Component(ReadsWhetherTheChildsEffectRan)));
                return null!;
            }), new object[] { tick });
            Hooks.UseImperativeHandle(s_throwingHandle, () =>
            {
                if (tick == 0) return "handle";
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Div(children: tick > 0 ? new VNode[] { V.Component(EffectChild, key: "child") } : Array.Empty<VNode>());
        }

        private FiberBatchScheduler MountTheChildMounterWithAnUpdateQueued()
        {
            var thrower = V.Mount(new VisualElement(), V.Component(MountsAnEffectChildHoldsARootAndThrows));
            _roots.Add(thrower);
            s_childsEffectRan = false;
            s_mountedSaw = null;
            s_setChildMounter.Invoke(1);
            return thrower.GetSchedulerForTest();
        }

        [Test]
        public void Given_AThrowingDrainThatLeftAnInlineChildsEffectPending_When_ItsReleaseRunsAHeldMount_Then_ItsRootHasThrownOnce()
        {
            // Arrange
            var scheduler = MountTheChildMounterWithAnUpdateQueued();

            // Act
            try
            {
                scheduler.DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }

            // Assert — 2 where the release's passive flush drained the root again
            Assert.That(ThrowingDrainsOf(scheduler), Is.EqualTo(1));
        }

        [Test]
        public void Given_AThrowingDrainThatLeftAnInlineChildsEffectPending_When_ItsReleaseRunsAHeldMount_Then_TheEffectRanBeforeTheMountRendered()
        {
            // Arrange
            var scheduler = MountTheChildMounterWithAnUpdateQueued();

            // Act
            try
            {
                scheduler.DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }

            // Assert — "pending" where the release left out the passive flush of the root that threw
            Assert.That(s_mountedSaw, Is.EqualTo("ran"));
        }

        // --- Held work that would render a root whose work threw ---

        private static readonly string[] SixItems = { "item-0", "item-1", "item-2", "item-3", "item-4", "item-5" };
        private static Ref<VirtualListHandle> s_rowListRef = null!;
        private static readonly Ref<string> s_rowHandle = new();
        private static int s_rowHandleRuns;
        private static bool s_rowThrows;

        [Component]
        private static VNode ScrollsItsListThenThrowsOnce(string item)
        {
            var (tick, setTick) = Hooks.UseState(0);
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick == 1) s_rowListRef.Current.ScrollToItem(1, VirtualListAlign.Start);
                return null!;
            }), new object[] { tick });
            Hooks.UseImperativeHandle(s_rowHandle, () =>
            {
                if (tick == 0) return item;
                s_rowHandleRuns++;
                if (!s_rowThrows) return item;
                s_rowThrows = false;
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Button(name: "bump-" + item, text: item, onClick: () => setTick.Invoke(1));
        }

        // On a panel, so that the scroller's change reaches the list. Each row's component mounts inline in an element
        // row, where a range that patches the row renders it.
        private static MountedTree MountARowThatScrollsItsList(HeadlessEditorPanelHost host)
        {
            s_rowListRef = new Ref<VirtualListHandle>();
            s_rowHandleRuns = 0;
            s_rowThrows = true;
            var mounted = V.Mount(host.Root, V.VirtualList(SixItems, item => item, itemHeight: 50f,
                renderer: item => V.Div(children: new VNode[] { V.Component(ScrollsItsListThenThrowsOnce, item) }),
                overscan: 0, listRef: s_rowListRef));
            var scrollView = host.Root.Q<ScrollView>();
            var list = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];
            typeof(FiberVirtualListController).GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(list, 200f);
            scrollView.verticalScroller.highValue = 4800f;
            list.UpdateVisibleRange(0f, 200f);
            return mounted;
        }

        private static void ClickTheSecondRow(HeadlessEditorPanelHost host)
        {
            try
            {
                host.Root.Q<Button>("bump-item-1").SimulateClick();
            }
            catch (InvalidOperationException)
            {
            }
        }

        [Test]
        public void Given_ARowWhoseLayoutEffectScrollsItsListAndWhoseHandleThrows_When_ItsClickReturns_Then_TheHandleHasRunOnce()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            using var mounted = MountARowThatScrollsItsList(host);

            // Act
            ClickTheSecondRow(host);

            // Assert — 2 where the release's range rendered the update the handle queued
            Assert.That(s_rowHandleRuns, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARowWhoseLayoutEffectScrollsItsListAndWhoseHandleThrows_When_TheRootsNextFrameRuns_Then_TheRangeTheScrollerAsksForHasRendered()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            using var mounted = MountARowThatScrollsItsList(host);
            ClickTheSecondRow(host);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(host.Panel);

            // Assert — null where the release dropped the range rather than handing it to the frame
            Assert.That(host.Root.Q<Button>("bump-item-4"), Is.Not.Null);
        }

        private static VelvetTaskCompletionSource<string> s_read = null!;
        private static int s_readsReturned;
        private static StateUpdater<int> s_setResolver;

        [Component(Compiler = false)]
        private static VNode ReadsTheResource()
        {
            var text = Hooks.Use(_ => s_read.Task);
            s_readsReturned++;
            return V.Label(text: text);
        }

        [Component(Memoize = true)]
        private static VNode SuspendsOnTheRead()
            => V.Suspense(fallback: V.Label(text: "loading"), children: new VNode[] { V.Component(ReadsTheResource) });

        [Component]
        private static VNode ResolvesTheReadThenThrows()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setResolver = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick > 0) s_read.TrySetResult("read");
                return null!;
            }), new object[] { tick });
            Hooks.UseImperativeHandle(s_throwingHandle, () =>
            {
                if (tick == 0) return "handle";
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Div(children: new VNode[] { V.Component(SuspendsOnTheRead) });
        }

        [Test]
        public void Given_ADrainThatThrowsAfterResolvingARead_When_TheExceptionLeaves_Then_TheReaderHasNotRenderedItsValue()
        {
            // Arrange
            s_read = new VelvetTaskCompletionSource<string>();
            s_readsReturned = 0;
            var thrower = V.Mount(new VisualElement(), V.Component(ResolvesTheReadThenThrows));
            _roots.Add(thrower);
            var scheduler = thrower.GetSchedulerForTest();
            s_setResolver.Invoke(1);

            // Act
            try
            {
                scheduler.DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }

            // Assert — 1 where the release rendered the resolved reader
            Assert.That(s_readsReturned, Is.EqualTo(0));
        }

        private static MountedTree s_disposedFromItsCommit = null!;
        private static StateUpdater<int> s_setDisposer;

        [Component]
        private static VNode DisposesItsRootThenThrows()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setDisposer = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick > 0) s_disposedFromItsCommit.Dispose();
                return null!;
            }), new object[] { tick });
            Hooks.UseImperativeHandle(s_throwingHandle, () =>
            {
                if (tick == 0) return "handle";
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Label(text: "disposes");
        }

        [Test]
        public void Given_ADrainThatThrowsAfterDisposingItsRoot_When_TheExceptionLeaves_Then_TheRootIsUnmounted()
        {
            // Arrange
            var host = new VisualElement();
            s_disposedFromItsCommit = V.Mount(host, V.Component(DisposesItsRootThenThrows));
            _roots.Add(s_disposedFromItsCommit);
            var scheduler = s_disposedFromItsCommit.GetSchedulerForTest();
            s_setDisposer.Invoke(1);

            // Act
            try
            {
                scheduler.DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }

            // Assert — 1 where the release handed the unmount to the root's next frame
            Assert.That(host.childCount, Is.EqualTo(0));
        }

        private sealed class Counter : Store<int>
        {
            public Counter() : base(0) { }

            public void Set(int value) => SetState(_ => value);

            protected override void ResetCore() => SetState(_ => 0);
        }

        private static Counter s_counter = null!;
        private static int s_counterRead;
        private static bool s_throwOnce;
        private static StateUpdater<int> s_setCounterReader;

        [Component]
        private static VNode ReadsTheCounterAndThrowsOnceWhenAsked()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setCounterReader = setTick;
            s_counterRead = Hooks.UseStore(s_counter, value => value);
            Hooks.UseImperativeHandle(s_throwingHandle, () =>
            {
                if (tick == 0 || !s_throwOnce) return "handle";
                s_throwOnce = false;
                throw new InvalidOperationException("handle failed");
            });
            return V.Label(text: "reader");
        }

        private MountedTree MountTheCounterReader()
        {
            s_counter = new Counter();
            var reader = V.Mount(new VisualElement(), V.Component(ReadsTheCounterAndThrowsOnceWhenAsked));
            _roots.Add(reader);
            reader.FlushEffectsForTest();
            s_throwOnce = true;
            s_setCounterReader.Invoke(1);
            return reader;
        }

        private VisualElement MountAnIdleButton()
        {
            var host = new VisualElement();
            _roots.Add(V.Mount(host, V.Button(name: "idle", onClick: () => { })));
            return host;
        }

        // A drain that threw marks its root for the rest of the release it ended in, here one with nothing else to do.
        [Test]
        public void Given_ARootWhoseOwnEntryThrew_When_ALaterEntryEndsOwingItsSyncLane_Then_ItIsFlushed()
        {
            // Arrange
            var reader = MountTheCounterReader();
            var idle = MountAnIdleButton();
            try
            {
                reader.GetSchedulerForTest().DrainImmediateForTest();
            }
            catch (InvalidOperationException)
            {
            }
            s_counter.Set(1);

            // Act
            idle.Q<Button>("idle").SimulateClick();

            // Assert
            Assert.That(s_counterRead, Is.EqualTo(1));
        }

        // As above, for a drain a held flush ran, inside a release that went on past it.
        [Test]
        public void Given_ARootWhoseHeldFlushThrew_When_ALaterEntryEndsOwingItsSyncLane_Then_ItIsFlushed()
        {
            // Arrange — the throw is logged where the release runs the held flush
            LogAssert.Expect(LogType.Exception, new Regex("handle failed"));
            var reader = MountTheCounterReader();
            var idle = MountAnIdleButton();
            WalkAsking(() => reader.GetSchedulerForTest().FlushImmediate());
            s_counter.Set(1);

            // Act
            idle.Q<Button>("idle").SimulateClick();

            // Assert
            Assert.That(s_counterRead, Is.EqualTo(1));
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
        public void Given_WorkHeldWithNoEntryOnTheStack_When_AnEntryBegins_Then_ItAssertsThatTheWorkHadNoEntryToReleaseIt()
        {
            // Arrange — the hold asserts as well, and the entry's end runs what it held
            LogAssert.Expect(LogType.Assert, "Work was held with no entry on the stack to release it.");
            FiberExecutionContext.Hold(FiberExecutionContext.Request.Flush, null, null, () => { });
            LogAssert.Expect(LogType.Assert, "Work is held with no entry left to release it.");

            // Act
            TestDelegate act = () => FiberExecutionContext.Enter<object?>(null, null, _ => { });

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
