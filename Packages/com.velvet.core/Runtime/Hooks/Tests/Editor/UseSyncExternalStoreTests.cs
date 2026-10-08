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
    /// notification is held to, and the snapshot readers share across a batch drain wave.
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
            ResetReader();
            ResetPair();
            ResetSwitching();
            ResetThrowing();
            ResetLane();
            ResetTearing();
            ResetUncached();
            ResetNullArgument();
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

        [Test]
        public void Given_MountedReader_When_StoreChanges_Then_NewSnapshotIsRendered()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));

            // Act
            s_counter.Set(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(5, s_readerValue);
        }

        [Test]
        public void Given_MountedReader_When_StoreNotifiesWithAnEqualSnapshot_Then_NoRerender()
        {
            // Arrange
            s_counter = new ExternalSource<int>(3);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            var rendersBefore = s_readerRenders;

            // Act
            s_counter.Notify();
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(rendersBefore, s_readerRenders);
        }

        [Test]
        public void Given_MountedReader_When_RerenderedWithAnEqualSubscribeDelegate_Then_SubscribesOnce()
        {
            // Arrange — the method group hands the hook a new delegate instance on every render
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));

            // Act
            s_counter.Set(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual("1 render(s) after mount, 1 subscribe(s)",
                $"{s_readerRenders - 1} render(s) after mount, {s_counter.SubscribeCalls} subscribe(s)");
        }

        [Test]
        public void Given_MountedReader_When_Unmounted_Then_Unsubscribes()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));

            // Act
            mounted.Dispose();

            // Assert
            Assert.AreEqual(0, s_counter.ListenerCount);
        }

        [Test]
        public void Given_FirstOfTwoSubscriptionsThrowsOnUnsubscribe_When_Unmounted_Then_TheSecondIsStillRemoved()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("unsubscribe failed"));
            s_sourceA = new ExternalSource<int>(0) { ThrowOnUnsubscribe = true };
            s_sourceB = new ExternalSource<int>(0);
            var mounted = V.Mount(_root, V.Component(TwoStoresRender, key: "two"));

            // Act
            mounted.Dispose();

            // Assert
            Assert.AreEqual(0, s_sourceB.ListenerCount);
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

        [Test]
        public void Given_SnapshotProjectingOneField_When_AnotherFieldChanges_Then_NoRerender()
        {
            // Arrange
            s_pair = new ExternalSource<PairState>(new PairState(1, "a"));
            using var mounted = V.Mount(_root, V.Component(PairNumberRender, key: "pair"));
            var rendersBefore = s_pairRenders;

            // Act
            s_pair.Set(new PairState(1, "b"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(rendersBefore, s_pairRenders);
        }

        [Test]
        public void Given_SubscribeThatChangesTheStore_When_Mounted_Then_RendersTheChangedSnapshot()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            s_counter.DuringSubscribe = () => s_counter.Set(7);

            // Act
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));

            // Assert
            Assert.AreEqual(7, s_readerValue);
        }

        [Test]
        public void Given_ReaderSwitchedToAnotherStore_When_Rerendered_Then_MovesItsSubscriptionAndRendersTheNewStore()
        {
            // Arrange
            s_sourceA = new ExternalSource<int>(10);
            s_sourceB = new ExternalSource<int>(20);
            using var mounted = V.Mount(_root, V.Component(SwitchingReaderRender, key: "switching"));

            // Act
            s_setUseB.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual("A listeners 0, B listeners 1, rendered 20",
                $"A listeners {s_sourceA.ListenerCount}, B listeners {s_sourceB.ListenerCount}, rendered {s_switchingValue}");
        }

        [Test]
        public void Given_ReaderSwitchedToAStoreThatChangesWhileSubscribed_When_Rerendered_Then_RendersTheChangeOnce()
        {
            // Arrange
            s_sourceA = new ExternalSource<int>(10);
            s_sourceB = new ExternalSource<int>(20);
            s_sourceB.DuringSubscribe = () => s_sourceB.Set(30);
            using var mounted = V.Mount(_root, V.Component(SwitchingReaderRender, key: "switching"));
            var rendersBefore = s_switchingRenders;

            // Act
            s_setUseB.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual("rendered 30 in 1 render(s)",
                $"rendered {s_switchingValue} in {s_switchingRenders - rendersBefore} render(s)");
        }

        [Test]
        public void Given_ListenerThatChangesTheStoreAgainFromItsNotification_When_StoreChanges_Then_ReaderRendersTheLastSnapshotOnce()
        {
            // Arrange — a listener subscribed ahead of the reader nests a second notification inside the first
            s_counter = new ExternalSource<int>(0);
            s_counter.Subscribe(() =>
            {
                if (s_counter.GetSnapshot() == 1) s_counter.Set(2);
            });
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
            var rendersBefore = s_readerRenders;

            // Act
            s_counter.Set(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual("rendered 2 in 1 render(s)",
                $"rendered {s_readerValue} in {s_readerRenders - rendersBefore} render(s)");
        }

        [Test]
        public void Given_GetSnapshotThatThrowsOnNotify_When_StoreChanges_Then_ExceptionReachesErrorBoundary()
        {
            // Arrange
            s_throwingIndex = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ThrowingBoundaryRender, key: "boundary"), CaughtErrors.Unlogged);
            var shownBefore = s_throwingFallbackShown;

            // Act — the index leaves the array, so the next getSnapshot throws
            s_throwingIndex.Set(5);
            mounted.FlushStateForTest();

            // Assert — folded with the mount's own state, which a fallback already showing would satisfy alone
            Assert.AreEqual((false, true), (shownBefore, s_throwingFallbackShown));
        }

        [Test]
        public void Given_StoreChangedInsideStartTransition_When_ReaderIsNotified_Then_ItIsScheduledOnTheNormalLaneOnly()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(TransitionHostRender, key: "host"));

            // Act
            s_startTransition.Invoke(() => s_counter.Set(1));

            // Assert
            Assert.AreEqual((false, true),
                (s_laneReaderFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_laneReaderFiber.LaneQueue.Contains(FiberUpdatePriority.Normal)));
        }

        [Test]
        public void Given_MountedReader_When_StoreNotifiesFromAnotherThread_Then_TheNotificationThrowsInvalidOperationException()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(ReaderRender, key: "reader"));
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

        [Test]
        public void Given_SubscribeBuiltAfreshEachRenderUnderStrictMode_When_Mounted_Then_TheDiagnosticRenderDoesNotResubscribe()
        {
            // Arrange
            s_counter = new ExternalSource<int>(0);
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(CapturingHostRender, key: "host"));

            // Assert
            Assert.AreEqual(1, s_counter.SubscribeCalls);
        }

        [Test]
        public void Given_GetSnapshotBuildingANewValuePerRead_When_RenderedTwice_Then_LogsTheCachingErrorOnce()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex("getSnapshot returned a different value on two consecutive reads"));
            s_uncached = new ExternalSource<int>(0);
            using var mounted = V.Mount(_root, V.Component(UncachedRender, key: "uncached"));

            // Act — every snapshot differs from the last, so the notification re-renders
            s_uncached.Notify();
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect takes the one error; a second fails the case as an unexpected log
        }

        [Test]
        public void Given_AncestorImmediateAndDescendantDelayed_When_StoreChangesBetweenDrains_Then_DescendantRendersTheWaveSnapshot()
        {
            // Arrange
            s_tearSource = new ExternalSource<int>(0);
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_tearSource.Set(1);
            var rendersBefore = s_descendantRenders;
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the snapshot the immediate drain's ancestor rendered, not the live 1, read by a render
            // the delayed drain made
            Assert.AreEqual("0, rendered in the delayed drain: True",
                $"{s_descendantValue}, rendered in the delayed drain: {s_descendantRenders > rendersBefore}");
        }

        [Test]
        public void Given_StoreChangedMidWave_When_TheNextWaveDrains_Then_BothReadersRenderTheLatestSnapshot()
        {
            // Arrange
            s_tearSource = new ExternalSource<int>(0);
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_tearSource.Set(1);
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.AreEqual("1/1", $"{s_ancestorValue}/{s_descendantValue}");
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

            // Runs once, inside the next Subscribe call, after the listener is registered.
            public Action DuringSubscribe { get; set; }

            // Makes an unsubscribe throw once it has removed its listener.
            public bool ThrowOnUnsubscribe { get; set; }

            public Action Subscribe(Action onStoreChange)
            {
                SubscribeCalls++;
                _listeners.Add(onStoreChange);
                var during = DuringSubscribe;
                DuringSubscribe = null;
                during?.Invoke();
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

        #region Uncached component (getSnapshot boxes a new object per read)

        private static ExternalSource<int> s_uncached;

        private static void ResetUncached()
        {
            s_uncached = null;
        }

        [Component]
        private static VNode UncachedRender()
        {
            var boxed = Hooks.UseSyncExternalStore(s_uncached.Subscribe, () => (object)s_uncached.GetSnapshot());
            return V.Label(text: boxed.ToString());
        }

        #endregion
    }
}
