using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="Hooks.UseSyncExternalStore{T}"/> against a store Velvet does not own: what a render
    /// returns, which notifications re-render, when the hook subscribes and unsubscribes, the thread and lane a
    /// notification is held to, and the snapshot readers share across one batch drain pass. The hook subscribes in
    /// the commit's passive effects, so a case about a notification flushes them before the store changes.
    /// </summary>
    [TestFixture]
    internal sealed class UseSyncExternalStoreTests
    {
        private VisualElement _root;
        private bool _strictModeBefore;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _strictModeBefore = FiberStrictMode.Enabled;
            FiberStrictMode.Enabled = false;
            FiberWorkLoop.IsInDiscreteEvent = false;
            ResetReader();
            ResetPair();
            ResetSwitching();
            ResetThrowing();
            ResetLane();
            ResetTearing();
            ResetUncached();
            ResetNullArgument();
            ResetLateMount();
            ResetResume();
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = _strictModeBefore;
        }

        [Test]
        public void Given_ExternalStore_When_FirstRender_Then_ReturnsCurrentSnapshot()
        {
            // Arrange
            s_counter = new ExternalSource<int>(100);

            // Act
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));

            // Assert
            Assert.AreEqual(100, s_readerValue);
        }

        [Test]
        public void Given_NullSubscribe_When_Rendered_Then_ThrowsArgumentNullExceptionNamingIt()
        {
            // Arrange
            s_nullArgumentCall = () => Hooks.UseSyncExternalStore(null, () => 0);

            // Act
            using var mounted = V.Mount(_root, InBoundary(V.Component(NullArgumentRender, key: "null")), CaughtErrors.Unlogged);

            // Assert
            Assert.AreEqual("ArgumentNullException: subscribe", DescribeCaught());
        }

        [Test]
        public void Given_NullGetSnapshot_When_Rendered_Then_ThrowsArgumentNullExceptionNamingIt()
        {
            // Arrange
            s_nullArgumentCall = () => Hooks.UseSyncExternalStore<int>(onStoreChange => () => { }, null);

            // Act
            using var mounted = V.Mount(_root, InBoundary(V.Component(NullArgumentRender, key: "null")), CaughtErrors.Unlogged);

            // Assert
            Assert.AreEqual("ArgumentNullException: getSnapshot", DescribeCaught());
        }

        // GREEN_ON_BASE(characterization): the base's render subscribed, so its notification already re-rendered.
        [Test]
        public void Given_MountedReader_When_StoreChanges_Then_NewSnapshotIsRendered()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            s_counter.Set(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(5, s_readerValue);
        }

        // GREEN_ON_BASE(characterization): the base's render subscribed, and an equal snapshot already rendered nothing.
        [Test]
        public void Given_MountedReader_When_StoreNotifiesWithAnEqualSnapshot_Then_NoRerender()
        {
            // Arrange
            s_counter = new ExternalSource<int>(3);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            mounted.FlushEffectsForTest();
            var rendersBefore = s_readerRenders;

            // Act
            s_counter.Notify();
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(rendersBefore, s_readerRenders);
        }

        // GREEN_ON_BASE(characterization): the base's render already compared subscribe with delegate equality.
        [Test]
        public void Given_MountedReader_When_RerenderedWithAnEqualSubscribeDelegate_Then_SubscribesOnce()
        {
            // Arrange — the method group hands the hook a new delegate instance on every render
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            s_counter.Set(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — two renders over one subscribe; a reader that resubscribed on its re-render, or that never
            // re-rendered, leaves no render past the subscribes
            Assert.AreEqual(1, s_readerRenders - s_counter.SubscribeCalls);
        }

        // GREEN_ON_BASE(characterization): the base's render subscribed, and its unmount already removed that.
        [Test]
        public void Given_MountedReader_When_Unmounted_Then_Unsubscribes()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.AreEqual(0, s_counter.ListenerCount);
        }

        // GREEN_ON_BASE(characterization): the base's unmount already contained the first unsubscribe's throw.
        [Test]
        public void Given_FirstOfTwoSubscriptionsThrowsOnUnsubscribe_When_Unmounted_Then_TheSecondIsStillRemoved()
        {
            // Arrange — the root being disposed reports the contained exception as uncaught
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: unsubscribe failed");
            s_sourceA = new ExternalSource<int>(0) { ThrowOnUnsubscribe = true };
            s_sourceB = new ExternalSource<int>(0);
            var mounted = V.Mount(_root, V.Component(TwoStoresRender, key: "two"));
            mounted.FlushEffectsForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.AreEqual(0, s_sourceB.ListenerCount);
        }

        [Test]
        public void Given_UnsubscribeThatThrows_When_TheReaderAloneUnmounts_Then_TheExceptionIsLogged()
        {
            // Arrange — with the host still mounted, the contained exception's walk reaches the live root, which
            // unmounts the tree and logs it
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: unsubscribe failed");
            s_sourceA = new ExternalSource<int>(0) { ThrowOnUnsubscribe = true };
            using var mounted = V.Mount(_root, V.Component(ToggledReaderHostRender, key: "host"));

            // Act
            s_setShowToggledReader.Invoke(false);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect takes the one exception the contained unsubscribe reports
        }

        [Test]
        public void Given_TwoReadersOfOneStore_When_StoreChanges_Then_BothRenderTheNewSnapshot()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(ReaderRender, key: "first"),
                V.Component(SecondReaderRender, key: "second"),
            }));

            // Act
            s_counter.Set(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual("3/3", $"{s_readerValue}/{s_secondReaderValue}");
        }

        // GREEN_ON_BASE(characterization): the base's notification already compared the projected field alone.
        [Test]
        public void Given_SnapshotProjectingOneField_When_AnotherFieldChanges_Then_NoRerender()
        {
            // Arrange
            s_pair = new ExternalSource<PairState>(new PairState(1, "a"));
            using var mounted = V.Mount(_root, V.Component(PairNumberRender, key: "pair"));
            mounted.FlushEffectsForTest();
            var rendersBefore = s_pairRenders;

            // Act
            s_pair.Set(new PairState(1, "b"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(rendersBefore, s_pairRenders);
        }

        // GREEN_ON_BASE(characterization): the base's render subscribed, so the change reached it as a notification.
        // On the head nothing is subscribed until the passive effects run, and the comparison the subscription
        // makes once it exists is what finds the change.
        [Test]
        public void Given_StoreChangedBetweenTheMountAndItsPassiveEffects_When_TheyRun_Then_TheChangedSnapshotRenders()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            s_counter.Set(5);

            // Act
            mounted.FlushEffectsForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.AreEqual(5, s_readerValue);
        }

        [Test]
        public void Given_ReaderWhoseRenderThrowsAfterReadingTheStore_When_EffectsFlush_Then_ItNeverSubscribes()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, InBoundary(V.Component(ThrowingAfterReadRender, key: "throws")), CaughtErrors.Unlogged);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.AreEqual(0, s_counter.SubscribeCalls);
        }

        [Test]
        public void Given_ARenderAttemptARenderPhaseUpdateDiscards_When_EffectsFlush_Then_OnlyTheSettledAttemptsStoreIsSubscribed()
        {
            // Arrange — the first attempt reads A and sets state while it renders; the attempt that settles reads B
            s_sourceA = new ExternalSource<int>(1);
            s_sourceB = new ExternalSource<int>(2);
            using var mounted = V.Mount(_root, V.Component(SupersededReaderRender, key: "superseded"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.AreEqual(0, s_sourceA.SubscribeCalls);
        }

        // GREEN_ON_BASE(characterization): the base's render already removed the first store's subscription.
        [Test]
        public void Given_ReaderSwitchedToAnotherStore_When_TheSwitchsEffectsRun_Then_TheFirstStoresSubscriptionIsRemoved()
        {
            // Arrange
            s_sourceA = new ExternalSource<int>(10);
            s_sourceB = new ExternalSource<int>(20);
            using var mounted = V.Mount(_root, V.Component(SwitchingReaderRender, key: "switching"));
            mounted.FlushEffectsForTest();

            // Act
            s_setUseB.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.AreEqual(0, s_sourceA.ListenerCount);
        }

        // GREEN_ON_BASE(characterization): the base's render already subscribed to the store it switched to.
        [Test]
        public void Given_ReaderSwitchedToAnotherStore_When_TheSwitchsEffectsRun_Then_TheSecondStoreIsSubscribed()
        {
            // Arrange
            s_sourceA = new ExternalSource<int>(10);
            s_sourceB = new ExternalSource<int>(20);
            using var mounted = V.Mount(_root, V.Component(SwitchingReaderRender, key: "switching"));
            mounted.FlushEffectsForTest();

            // Act
            s_setUseB.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.AreEqual(1, s_sourceB.ListenerCount);
        }

        // GREEN_ON_BASE(characterization): the base's two notifications already coalesced into one render.
        [Test]
        public void Given_ListenerThatChangesTheStoreAgainFromItsNotification_When_StoreChanges_Then_ReaderRendersOnce()
        {
            // Arrange — a listener subscribed ahead of the reader nests a second notification inside the first
            s_counter = new ExternalSource<int>(0);
            s_counter.Subscribe(() =>
            {
                if (s_counter.GetSnapshot() == 1) s_counter.Set(2);
            });
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            mounted.FlushEffectsForTest();
            var rendersBefore = s_readerRenders;

            // Act
            s_counter.Set(1);
            mounted.FlushStateForTest();

            // Assert — the one render reads the store, which by then holds 2
            Assert.AreEqual(1, s_readerRenders - rendersBefore);
        }

        // GREEN_ON_BASE(characterization): the base's notification already counted a throwing getSnapshot as a change.
        [Test]
        public void Given_GetSnapshotThatThrowsOnNotify_When_StoreChanges_Then_ExceptionReachesErrorBoundary()
        {
            // Arrange
            s_throwingIndex = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ThrowingBoundaryRender, key: "boundary"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();

            // Act — the index leaves the array, so the next getSnapshot throws
            s_throwingIndex.Set(5);
            mounted.FlushStateForTest();

            // Assert — the mount reads index 0, which the array holds, so only the change shows the fallback
            Assert.IsTrue(s_throwingFallbackShown);
        }

        // GREEN_ON_BASE(characterization): the base's notification already took the Urgent lane inside a transition.
        [Test]
        public void Given_StoreChangedInsideStartTransition_When_ReaderIsNotified_Then_ItIsScheduledOnTheUrgentLane()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(TransitionHostRender, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            s_startTransition.Invoke(() => s_counter.Set(1));

            // Assert
            Assert.IsTrue(s_laneReaderFiber.LaneQueue.Contains(FiberUpdatePriority.Urgent));
        }

        // GREEN_ON_BASE(characterization): the base's notification outside any event already took the Urgent lane.
        [Test]
        public void Given_StoreChangedOutsideAnyEvent_When_ReaderIsNotified_Then_ItIsScheduledOnTheUrgentLane()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(TransitionHostRender, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            s_counter.Set(1);

            // Assert
            Assert.IsTrue(s_laneReaderFiber.LaneQueue.Contains(FiberUpdatePriority.Urgent));
        }

        // GREEN_ON_BASE(characterization): the base's notification already posted the flush to the main thread.
        [Test]
        public void Given_StoreChangedOutsideAnyEvent_When_TheMainThreadServicesItsPostedWork_Then_TheReaderRendersTheNewSnapshotWithoutADrain()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            s_counter.Set(5);
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.AreEqual(5, s_readerValue);
        }

        // GREEN_ON_BASE(characterization): the base's drain already settles the owed flush once the tier is empty.
        // What this pins is that the line doing so survives its move into DrainImmediate's finally.
        [Test]
        public void Given_ADrainThatEmptiedTheImmediateTier_When_TheStoreChangesAgain_Then_TheMainThreadsPostedWorkRendersIt()
        {
            // Arrange — the first change is committed by a drain, which is when the owed flush is settled
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            s_counter.Set(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            VelvetMainThread.RunHandoffs();

            // Act
            s_counter.Set(6);
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.AreEqual(6, s_readerValue);
        }

        // GREEN_ON_BASE(refactor): the immediate drain after the call renders the pending state on the head and
        // finds the tier empty on the base, where the transition lane stays queued for the flush that parks.
        [Test]
        public void Given_StoreChangedWhileASliceIsParked_When_ThePassResumes_Then_ReadersItCommittedEarlierShowTheSnapshotALaterRowRenders()
        {
            // Arrange — the resume's first slice renders row 1; row 0 was committed by the slice before it
            s_resumeSource = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ResumeListHost, key: "resume-list"));
            s_resumeStart.Invoke(() => s_resumeSetGen.Invoke(1));
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_resumeListFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_resumeListFiber.HasPendingReconcileWorkForTest();
            s_resumeSource.Set(1);

            // Act
            FiberWorkLoop.ContinueReconcile(s_resumeListFiber);

            // Assert — the parked state is folded in because a pass that never parked has no earlier slice
            Assert.AreEqual((true, 1, 1), (parked, s_resumeSeen[0], s_resumeSeen[1]));
        }

        // GREEN_ON_BASE(characterization): the base's notification already refused a call off the main thread.
        [Test]
        public void Given_MountedReader_When_StoreNotifiesFromAnotherThread_Then_TheNotificationThrowsInvalidOperationException()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            mounted.FlushEffectsForTest();
            Exception caught = null;
            var worker = new Thread(() =>
            {
                try
                {
                    s_counter.Set(9);
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });

            // Act
            worker.Start();
            worker.Join();

            // Assert
            Assert.That(caught, Is.InstanceOf<InvalidOperationException>().With.Message.Contains("main thread"));
        }

        // The mount's passive effects run once and again under StrictMode, so subscribe is made, removed and made
        // again, as React double-invokes a mount effect; the diagnostic render, whose subscribe is another
        // closure, adds no call of its own.
        [Test]
        public void Given_SubscribeBuiltAfreshEachRenderUnderStrictMode_When_TheMountsEffectsRun_Then_SubscribeIsCalledTwice()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(CapturingHostRender, key: "host"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.AreEqual(2, s_counter.SubscribeCalls);
        }

        // GREEN_ON_BASE(characterization): the base's layout-phase check already ran the mount into the limit.
        // On the head the check is a passive effect, and the sync-lane drain renders the re-render it asks for.
        [Test]
        public void Given_GetSnapshotBuildingANewValuePerRead_When_ItsPassiveCheckRuns_Then_ItRerendersPastTheUpdateDepthLimit()
        {
            // Arrange — the caching error is logged once however many renders follow: a second one fails the case
            // as an unexpected log
            LogAssert.Expect(LogType.Error, new Regex("getSnapshot returned a different value on two consecutive reads"));
            LogAssert.Expect(LogType.Error, new Regex("Maximum update depth exceeded"));
            s_uncached = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(UncachedRender, key: "uncached"));

            // Act — the drain finds its tier empty and runs the mount's effects first
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — more renders than the drain's nested-update cap, which only the limit ends; a drain that
            // left the check's re-render to its next call would stop at two
            Assert.That(s_uncachedRenders, Is.GreaterThan(50));
        }

        [Test]
        public void Given_AncestorImmediateAndDescendantDelayed_When_StoreChangesBetweenDrains_Then_TheDelayedDrainCommitsTheChangeToBoth()
        {
            // Arrange — the change re-schedules both readers on the immediate tier
            s_tearSource = new ExternalSource<int>(0);
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_tearSource.Set(1);

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.AreEqual("1/1", $"{s_ancestorValue}/{s_descendantValue}");
        }

        [Test]
        public void Given_AncestorImmediateAndDescendantDelayed_When_AChangeInsideATransitionLandsBetweenDrains_Then_TheDelayedDrainCommitsItToBoth()
        {
            // Arrange — the change is made inside startTransition, which must not move the readers' re-render
            // onto the Transition lane
            s_tearSource = new ExternalSource<int>(0);
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            FiberWorkLoop.StartTransition(s_ancestorFiber, new HookTransitionSlot(), () => s_tearSource.Set(1));

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.AreEqual("1/1", $"{s_ancestorValue}/{s_descendantValue}");
        }

        [Test]
        public void Given_ReaderMountedInADrainPassAfterAChange_When_TheDrainEnds_Then_ItRendersTheLatestSnapshot()
        {
            // Arrange — the ancestor pins 0 in the pass; the host changes the store and then mounts the reader
            s_lateSource = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Div(name: "late", children: new VNode[]
            {
                V.Component(LateAncestorRender, key: "ancestor"),
                V.Component(LateHostRender, key: "host"),
            }));
            s_showLateReader = true;
            s_lateAncestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_lateHostFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act — the reader subscribes after the change, so no notification is left to reach it
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — it first rendered the pass's pin, then the store's value
            Assert.AreEqual("first 0, last 1", $"first {s_lateReaderFirstValue}, last {s_lateReaderValue}");
        }

        private sealed record PairState(int Number, string Text);

        // A minimal store outside Velvet: a value, a listener list, and nothing that knows about components.
        private sealed class ExternalSource<T>
        {
            private readonly List<Action> _listeners = new();
            private T _value;

            public ExternalSource(T initial)
            {
                _value = initial;
            }

            public int SubscribeCalls { get; private set; }

            public int ListenerCount => _listeners.Count;

            // Makes an unsubscribe throw once it has removed its listener.
            public bool ThrowOnUnsubscribe { get; set; }

            public Action Subscribe(Action onStoreChange)
            {
                SubscribeCalls++;
                _listeners.Add(onStoreChange);
                return () =>
                {
                    _listeners.Remove(onStoreChange);
                    if (ThrowOnUnsubscribe) throw new InvalidOperationException("unsubscribe failed");
                };
            }

            public T GetSnapshot() => _value;

            public void Set(T value)
            {
                _value = value;
                Notify();
            }

            public void Notify()
            {
                foreach (var listener in _listeners.ToArray())
                {
                    listener();
                }
            }
        }

        #region Null-argument component (the hook call is the arrangement)

        private static Action s_nullArgumentCall;
        private static Exception s_caught;

        private static void ResetNullArgument()
        {
            s_nullArgumentCall = null;
            s_caught = null;
        }

        private static VNode InBoundary(VNode child)
            => V.ErrorBoundary(exception =>
            {
                s_caught = exception;
                return V.Label(text: "caught");
            }, new[] { child });

        private static string DescribeCaught()
            => $"{s_caught?.GetType().Name}: {(s_caught as ArgumentNullException)?.ParamName}";

        [Component]
        private static VNode NullArgumentRender()
        {
            s_nullArgumentCall();
            return V.Label(text: "rendered");
        }

        #endregion

        #region Reader components (one store read through method groups)

        private static ExternalSource<int> s_counter;
        private static int s_readerValue;
        private static int s_readerRenders;
        private static int s_secondReaderValue;

        private static void ResetReader()
        {
            s_counter = null;
            s_readerValue = 0;
            s_readerRenders = 0;
            s_secondReaderValue = 0;
        }

        [Component]
        private static VNode ReaderRender()
        {
            s_readerRenders++;
            s_readerValue = Hooks.UseSyncExternalStore(s_counter.Subscribe, s_counter.GetSnapshot);
            return V.Label(text: s_readerValue.ToString());
        }

        [Component]
        private static VNode SecondReaderRender()
        {
            s_secondReaderValue = Hooks.UseSyncExternalStore(s_counter.Subscribe, s_counter.GetSnapshot);
            return V.Label(text: s_secondReaderValue.ToString());
        }

        [Component]
        private static VNode ThrowingAfterReadRender()
        {
            Hooks.UseSyncExternalStore(s_counter.Subscribe, s_counter.GetSnapshot);
            throw new InvalidOperationException("render failed after reading the store");
        }

        [Component]
        private static VNode CapturingHostRender() => V.Component(CapturingReaderRender, key: "child");

        // The subscribe lambda captures a local, so every render hands the hook a new, unequal delegate.
        [Component]
        private static VNode CapturingReaderRender()
        {
            var source = s_counter;
            var value = Hooks.UseSyncExternalStore(onStoreChange => source.Subscribe(onStoreChange), source.GetSnapshot);
            return V.Label(text: value.ToString());
        }

        #endregion

        #region Pair component (a snapshot projecting one field)

        private static ExternalSource<PairState> s_pair;
        private static int s_pairRenders;

        private static void ResetPair()
        {
            s_pair = null;
            s_pairRenders = 0;
        }

        [Component]
        private static VNode PairNumberRender()
        {
            s_pairRenders++;
            var number = Hooks.UseSyncExternalStore(s_pair.Subscribe, () => s_pair.GetSnapshot().Number);
            return V.Label(text: number.ToString());
        }

        #endregion

        #region Switching component (the store read depends on state)

        private static ExternalSource<int> s_sourceA;
        private static ExternalSource<int> s_sourceB;
        private static StateUpdater<bool> s_setUseB;
        private static int s_switchingValue;
        private static int s_switchingRenders;

        private static void ResetSwitching()
        {
            s_sourceA = null;
            s_sourceB = null;
            s_setUseB = default;
            s_setShowToggledReader = default;
            s_switchingValue = 0;
            s_switchingRenders = 0;
        }

        [Component]
        private static VNode SwitchingReaderRender()
        {
            s_switchingRenders++;
            var (useB, setUseB) = Hooks.UseState(false);
            s_setUseB = setUseB;
            var source = useB ? s_sourceB : s_sourceA;
            s_switchingValue = Hooks.UseSyncExternalStore(source.Subscribe, source.GetSnapshot);
            return V.Label(text: s_switchingValue.ToString());
        }

        private static StateUpdater<bool> s_setShowToggledReader;

        [Component]
        private static VNode ToggledReaderHostRender()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShowToggledReader = setShow;
            return show ? V.Component(ToggledReaderRender, key: "reader") : V.Label(text: "gone");
        }

        [Component]
        private static VNode ToggledReaderRender()
        {
            var value = Hooks.UseSyncExternalStore(s_sourceA.Subscribe, s_sourceA.GetSnapshot);
            return V.Label(text: value.ToString());
        }

        // The first attempt reads A and asks for a render while it renders, so that attempt is discarded.
        [Component(Compiler = false)]
        private static VNode SupersededReaderRender()
        {
            var (attempt, setAttempt) = Hooks.UseState(0);
            var source = attempt == 0 ? s_sourceA : s_sourceB;
            var value = Hooks.UseSyncExternalStore(source.Subscribe, source.GetSnapshot);
            if (attempt == 0) setAttempt.Invoke(1);
            return V.Label(text: value.ToString());
        }

        [Component]
        private static VNode TwoStoresRender()
        {
            var first = Hooks.UseSyncExternalStore(s_sourceA.Subscribe, s_sourceA.GetSnapshot);
            var second = Hooks.UseSyncExternalStore(s_sourceB.Subscribe, s_sourceB.GetSnapshot);
            return V.Label(text: $"{first}/{second}");
        }

        #endregion

        #region Throwing component (getSnapshot indexes past the array)

        private static readonly int[] s_items = { 10, 20 };
        private static ExternalSource<int> s_throwingIndex;
        private static bool s_throwingFallbackShown;

        private static void ResetThrowing()
        {
            s_throwingIndex = null;
            s_throwingFallbackShown = false;
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ThrowingBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_throwingFallbackShown = true;
                return V.Label(text: "caught");
            });
            return V.Fragment(new VNode[] { V.Component(ThrowingChildRender, key: "child") });
        }

        [Component]
        private static VNode ThrowingChildRender()
        {
            var value = Hooks.UseSyncExternalStore(s_throwingIndex.Subscribe, () => s_items[s_throwingIndex.GetSnapshot()]);
            return V.Label(text: value.ToString());
        }

        #endregion

        #region Lane components (a reader beneath a component holding a transition)

        private static TransitionStarter s_startTransition;
        private static ComponentFiber s_laneReaderFiber;

        private static void ResetLane()
        {
            s_startTransition = default;
            s_laneReaderFiber = null;
        }

        [Component]
        private static VNode TransitionHostRender()
        {
            var (_, startTransition) = Hooks.UseTransition();
            s_startTransition = startTransition;
            return V.Fragment(new VNode[] { V.Component(LaneReaderRender, key: "reader") });
        }

        [Component]
        private static VNode LaneReaderRender()
        {
            s_laneReaderFiber = FiberAmbientStack.Current;
            var value = Hooks.UseSyncExternalStore(s_counter.Subscribe, s_counter.GetSnapshot);
            return V.Label(text: value.ToString());
        }

        #endregion

        #region Tearing components (two readers on different tiers)

        private static ExternalSource<int> s_tearSource;
        private static int s_ancestorValue;
        private static int s_descendantValue;
        private static int s_descendantRenders;
        private static ComponentFiber s_ancestorFiber;
        private static ComponentFiber s_descendantFiber;

        private static void ResetTearing()
        {
            s_tearSource = null;
            s_ancestorValue = 0;
            s_descendantValue = 0;
            s_descendantRenders = 0;
            s_ancestorFiber = null;
            s_descendantFiber = null;
        }

        private MountedTree MountAncestorDescendant()
            => V.Mount(_root, V.Div(name: "host", children: new VNode[]
            {
                V.Component(AncestorRender, key: "ancestor"),
                V.Component(DescendantRender, key: "descendant"),
            }));

        [Component]
        private static VNode AncestorRender()
        {
            s_ancestorFiber = FiberAmbientStack.Current;
            s_ancestorValue = Hooks.UseSyncExternalStore(s_tearSource.Subscribe, s_tearSource.GetSnapshot);
            return V.Label(text: s_ancestorValue.ToString());
        }

        [Component]
        private static VNode DescendantRender()
        {
            s_descendantFiber = FiberAmbientStack.Current;
            s_descendantRenders++;
            s_descendantValue = Hooks.UseSyncExternalStore(s_tearSource.Subscribe, s_tearSource.GetSnapshot);
            return V.Label(text: s_descendantValue.ToString());
        }

        #endregion

        #region LateMount components (a reader mounted partway through a drain pass)

        private static ExternalSource<int> s_lateSource;
        private static bool s_showLateReader;
        private static ComponentFiber s_lateAncestorFiber;
        private static ComponentFiber s_lateHostFiber;
        private static int? s_lateReaderFirstValue;
        private static int s_lateReaderValue;

        private static void ResetLateMount()
        {
            s_lateSource = null;
            s_showLateReader = false;
            s_lateAncestorFiber = null;
            s_lateHostFiber = null;
            s_lateReaderFirstValue = null;
            s_lateReaderValue = 0;
        }

        [Component]
        private static VNode LateAncestorRender()
        {
            s_lateAncestorFiber = FiberAmbientStack.Current;
            var value = Hooks.UseSyncExternalStore(s_lateSource.Subscribe, s_lateSource.GetSnapshot);
            return V.Label(text: value.ToString());
        }

        [Component]
        private static VNode LateHostRender()
        {
            s_lateHostFiber = FiberAmbientStack.Current;
            if (s_showLateReader && s_lateSource.GetSnapshot() == 0) s_lateSource.Set(1);
            return s_showLateReader ? V.Component(LateReaderRender, key: "reader") : V.Label(text: "empty");
        }

        [Component]
        private static VNode LateReaderRender()
        {
            s_lateReaderValue = Hooks.UseSyncExternalStore(s_lateSource.Subscribe, s_lateSource.GetSnapshot);
            s_lateReaderFirstValue ??= s_lateReaderValue;
            return V.Label(text: s_lateReaderValue.ToString());
        }

        #endregion

        #region Uncached component (getSnapshot boxes a new object per read)

        private static ExternalSource<int> s_uncached;
        private static int s_uncachedRenders;

        private static void ResetUncached()
        {
            s_uncached = null;
            s_uncachedRenders = 0;
        }

        [Component]
        private static VNode UncachedRender()
        {
            s_uncachedRenders++;
            var boxed = Hooks.UseSyncExternalStore(s_uncached.Subscribe, () => (object)s_uncached.GetSnapshot());
            return V.Label(text: boxed.ToString());
        }

        #endregion

        #region Resume components (a keyed list of readers a Transition pass parks inside)

        private static readonly ComponentContext<string> ResumeCtx = ComponentContext<string>.Create("DEFAULT");
        private const int ResumeRowCount = 4;
        private static ExternalSource<int> s_resumeSource;
        private static ComponentFiber s_resumeListFiber;
        private static StateUpdater<int> s_resumeSetGen;
        private static TransitionStarter s_resumeStart;
        private static readonly Dictionary<int, int> s_resumeSeen = new();

        private static void ResetResume()
        {
            s_resumeSource = null;
            s_resumeListFiber = null;
            s_resumeSetGen = default;
            s_resumeStart = default;
            s_resumeSeen.Clear();
        }

        // The context read is what re-renders a row when the host's transition changes its Provider's value.
        private static VNode ResumeRowBody(int index)
        {
            Hooks.UseContext(ResumeCtx);
            s_resumeSeen[index] = Hooks.UseSyncExternalStore(s_resumeSource.Subscribe, s_resumeSource.GetSnapshot);
            return V.Label(name: "resume-row-" + index, text: s_resumeSeen[index].ToString());
        }

        // Keyed Divs at the top level, so the fiber's own reconcile takes the time-sliceable keyed path; each
        // row's Provider and reader sit one level down, reached once that row is committed.
        [Component]
        private static VNode ResumeListHost()
        {
            var (gen, setGen) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_resumeSetGen = setGen;
            s_resumeStart = start;
            s_resumeListFiber = FiberAmbientStack.Current;
            var children = new VNode[ResumeRowCount];
            for (var i = 0; i < ResumeRowCount; i++)
            {
                children[i] = V.Div(key: "rs" + i, name: "resume-cell-" + i, children: new VNode[]
                {
                    V.Provider(ResumeCtx, $"g{gen}-{i}", new VNode[] { V.Component(ResumeRowBody, i, key: "row") }),
                });
            }
            return V.Fragment(children: children);
        }

        #endregion
    }
}
