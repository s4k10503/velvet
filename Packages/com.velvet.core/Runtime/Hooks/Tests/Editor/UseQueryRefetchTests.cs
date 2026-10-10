#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a mounted query fetches again on its own: TanStack Query's <c>refetchInterval</c>,
    /// <c>refetchIntervalInBackground</c>, <c>refetchOnWindowFocus</c> and <c>refetchOnReconnect</c>.
    /// <list type="bullet">
    /// <item>An interval fetches again once it has passed since the entry last changed, not a moment before,
    /// whether or not the data is stale, joining a refetch in flight. A change of the entry starts it over, and
    /// so does a change of the interval or turning the query on, while a render changing neither does not. It
    /// waits while the application is not visible unless it runs in the background, and a tick it skipped is
    /// not made up when the application is shown; a zero interval, a disabled query or an unmounted one
    /// fetches on no interval.</item>
    /// <item>The application becoming visible refetches stale data, and fresh data only under
    /// <c>Always</c>; nothing is fetched under <c>Never</c>, set on the query or as the client's default, while
    /// the application stays visible, or for a disabled query. A request in flight is joined.</item>
    /// <item>The device coming back online refetches the same way under <c>RefetchOnReconnect</c>.</item>
    /// <item>A client stops being watched when its last reader unmounts, and the poll ends once no client is
    /// watched; a client is watched again by its next reader, another client's reader leaving does not stop it
    /// being told, and a reading that throws is logged and taken as visible, by the poll and by an interval,
    /// whose wait an unmount cancels; a signal whose refetch throws is logged, and neither ends the poll nor
    /// keeps another client from being told; a reading failing every frame is logged once, and the statics reset starts
    /// with nothing watched, no poll and no failure logged.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <see cref="NetworkSignals"/> reads <see cref="s_visible"/> and <see cref="s_online"/>, and the client's
    /// clock is <see cref="s_now"/>, each set by hand. Both are polled once a frame, so each case advances
    /// frames with <c>VelvetTask.Yield()</c>. The query function hands back a completion source and counts its
    /// calls in <see cref="s_calls"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryRefetchTests
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static bool s_visible;
        private static bool s_online;
        private static int s_calls;
        private static TimeSpan? s_staleTime;
        private static TimeSpan? s_interval;
        private static bool s_inBackground;
        private static bool s_enabled;
        private static QueryRefetchMode? s_onFocus;
        private static QueryRefetchMode? s_onReconnect;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<CancellationToken> s_tokens = new();
        private static readonly List<QueryResult<int>> s_renders = new();
        private static StateUpdater<bool> s_setShow;
        private static StateUpdater<int> s_setTick;
        private static QueryClient s_otherClient = null!;
        private static int s_otherCalls;
        private static StateUpdater<bool> s_setShowOther;
        private static readonly List<VelvetTaskCompletionSource<int>> s_otherSources = new();
        private static Func<int, QueryKey?, QueryPlaceholder<int>>? s_placeholder;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_visible = true;
            s_online = true;
            s_calls = 0;
            s_staleTime = null;
            s_interval = null;
            s_inBackground = false;
            s_enabled = true;
            s_onFocus = null;
            s_onReconnect = null;
            s_client = NewClient(QueryRefetchMode.IfStale);
            s_sources.Clear();
            s_tokens.Clear();
            s_renders.Clear();
            s_setShow = default;
            s_setTick = default;
            s_otherClient = NewClient(QueryRefetchMode.IfStale);
            s_otherCalls = 0;
            s_setShowOther = default;
            s_otherSources.Clear();
            s_placeholder = null;
            NetworkSignals.IsVisible = () => s_visible;
            NetworkSignals.IsOnline = () => s_online;
            SetSignalsStatic("s_visibleFailureLogged", false);
            SetSignalsStatic("s_onlineFailureLogged", false);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            NetworkSignals.IsVisible = null;
            NetworkSignals.IsOnline = null;
        }

        #region Interval

        [UnityTest]
        public IEnumerator Given_ARefetchInterval_When_ItPassesAfterTheDataLands_Then_TheQueryFetchesAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The interval fetches again once it has passed");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInterval_When_OneMillisecondOfItRemains_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();

            // Act
            await Pass(Interval - TimeSpan.FromMilliseconds(1));

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "The interval fetches once it has passed, not before");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInterval_When_ARefetchLandsPartWay_Then_TheIntervalStartsOverFromIt()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            await Pass(TimeSpan.FromSeconds(6));
            Last().Refetch();
            s_sources[1].TrySetResult(2);
            mounted.FlushStateForTest();

            // Act
            await Pass(TimeSpan.FromSeconds(4));
            var atTheFirstInterval = s_calls;
            await Pass(TimeSpan.FromSeconds(6));

            // Assert
            Assert.That((atTheFirstInterval, s_calls), Is.EqualTo((2, 3)),
                "Each change of the entry restarts the interval, as v5's onQueryUpdate restarts its timers");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInterval_When_ItPassesWhileTheApplicationIsHidden_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            s_visible = false;

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "By default the interval waits while the application is not visible");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchIntervalInTheBackground_When_ItPassesWhileTheApplicationIsHidden_Then_ItFetches()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            s_inBackground = true;
            using var mounted = MountResolved();
            s_visible = false;

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "RefetchIntervalInBackground fetches whether or not the application is visible");
        });

        [UnityTest]
        public IEnumerator Given_ADisabledQueryWithARefetchInterval_When_ItPasses_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            s_enabled = false;
            using var mounted = Mount();

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "A disabled query fetches on no interval");
        });

        [UnityTest]
        public IEnumerator Given_AnUnmountedQueryWithARefetchInterval_When_ItPasses_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            Hide(mounted);

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "The interval ends with the component");
        });

        [UnityTest]
        public IEnumerator Given_FreshCachedDataAndARefetchInterval_When_ItPasses_Then_TheQueryFetchesAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            s_staleTime = TimeSpan.FromMinutes(1);
            s_client.SetQueryData(new QueryKey("todos"), 1);
            using var mounted = Mount();

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "The interval fetches whether or not the data is stale");
        });

        [UnityTest]
        public IEnumerator Given_AZeroRefetchInterval_When_FramesPass_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = TimeSpan.Zero;
            using var mounted = MountResolved();

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "An interval of zero is no interval, as v5 skips one");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInterval_When_TheComponentRendersAgainPartWay_Then_TheIntervalIsNotPushedBack()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            await Pass(TimeSpan.FromSeconds(6));
            Rerender(mounted);

            // Act
            await Pass(TimeSpan.FromSeconds(4));

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A render that changes neither the interval nor enabled leaves the interval running");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInterval_When_TheQueryIsTurnedOffPartWay_Then_NothingIsFetchedWhenItPasses()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            await Pass(TimeSpan.FromSeconds(4));
            s_enabled = false;
            Rerender(mounted);

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Turning the query off ends the interval it was waiting out");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInterval_When_ItIsShortenedPartWay_Then_TheNewIntervalRunsFromThen()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            await Pass(TimeSpan.FromSeconds(2));
            s_interval = TimeSpan.FromSeconds(4);
            Rerender(mounted);

            // Act
            await Pass(TimeSpan.FromSeconds(4));

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A new interval starts over from the commit that set it");
        });

        [UnityTest]
        public IEnumerator Given_ADisabledQueryWithARefetchIntervalOverFreshData_When_ItIsTurnedOn_Then_TheIntervalFetches()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            s_staleTime = TimeSpan.FromMinutes(1);
            s_enabled = false;
            s_client.SetQueryData(new QueryKey("todos"), 1);
            using var mounted = Mount();
            s_enabled = true;
            Rerender(mounted);

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Turning a query on starts its interval, though it fetched nothing");
        });

        [UnityTest]
        public IEnumerator Given_AnIntervalThatPassedWhileHidden_When_TheApplicationIsShownBeforeTheNextTick_Then_ItWaitsForTheNextTick()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            s_staleTime = TimeSpan.FromMinutes(1);
            s_onFocus = QueryRefetchMode.Never;
            using var mounted = MountResolved();
            s_visible = false;
            await Pass(Interval);
            await Pass(TimeSpan.FromSeconds(2));

            // Act
            s_visible = true;
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A tick skipped while hidden is skipped, as v5's setInterval only checks at its ticks");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInFlightOverData_When_TheIntervalPasses_Then_ItJoinsTheRefetch()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            Last().Refetch();

            // Act
            await Pass(Interval);

            // Assert
            Assert.That((s_calls, s_tokens[1].IsCancellationRequested), Is.EqualTo((2, false)),
                "The interval's fetch joins a request in flight rather than starting it over");
        });

        [Test]
        public void Given_AQueryWithARefetchInterval_When_ItUnmounts_Then_ItsIntervalWaitIsCancelled()
        {
            // Arrange
            s_interval = Interval;
            using var mounted = MountResolved();
            var result = Last();

            // Act
            Hide(mounted);

            // Assert
            Assert.That(IntervalWaitOf(result), Is.Null, "Unmounting ends the interval's wait rather than leaving it polling");
        }

        [UnityTest]
        public IEnumerator Given_AVisibilityReadingThatThrows_When_TheIntervalPasses_Then_TheApplicationCountsAsVisible()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the visibility override throws, so the poller logs a failure as well.
            LogAssert.ignoreFailingMessages = true;
            s_interval = Interval;
            using var mounted = MountResolved();
            NetworkSignals.IsVisible = () => throw new InvalidOperationException("visibility-failed");

            // Act
            await Pass(Interval);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A reading that fails is taken as visible, so the interval still fetches");
        });

        #endregion

        #region Window focus

        [UnityTest]
        public IEnumerator Given_StaleData_When_TheApplicationBecomesVisible_Then_TheQueryFetchesAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Stale data is fetched again when the application is shown");
        });

        [UnityTest]
        public IEnumerator Given_FreshData_When_TheApplicationBecomesVisible_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = MountResolved();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "IfStale fetches only data that is stale");
        });

        [UnityTest]
        public IEnumerator Given_FreshDataAndAlways_When_TheApplicationBecomesVisible_Then_TheQueryFetchesAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            s_onFocus = QueryRefetchMode.Always;
            using var mounted = MountResolved();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Always fetches whether or not the data is stale");
        });

        [UnityTest]
        public IEnumerator Given_StaleDataAndNever_When_TheApplicationBecomesVisible_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onFocus = QueryRefetchMode.Never;
            using var mounted = MountResolved();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Never turns the refetch off");
        });

        [UnityTest]
        public IEnumerator Given_AClientDefaultOfNever_When_TheApplicationBecomesVisible_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client = NewClient(QueryRefetchMode.Never);
            using var mounted = MountResolved();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A query that sets nothing takes the client's default");
        });

        [UnityTest]
        public IEnumerator Given_StaleData_When_TheApplicationStaysVisible_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            await Pass(TimeSpan.Zero);
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Only the application becoming visible refetches, not its being visible");
        });

        [UnityTest]
        public IEnumerator Given_ADisabledQueryOverStaleData_When_TheApplicationBecomesVisible_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_enabled = false;
            s_client.SetQueryData(new QueryKey("todos"), 1);
            using var mounted = Mount();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "A disabled query does not refetch on focus");
        });

        [UnityTest]
        public IEnumerator Given_ARefetchInFlightOverData_When_TheApplicationBecomesVisible_Then_ItIsJoined()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            Last().Refetch();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That((s_calls, s_tokens[1].IsCancellationRequested), Is.EqualTo((2, false)),
                "A refetch on focus joins the request in flight rather than starting it over, as v5's cancelRefetch: false");
        });

        #endregion

        #region Reconnect

        [UnityTest]
        public IEnumerator Given_StaleData_When_TheDeviceComesBackOnline_Then_TheQueryFetchesAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            await DisconnectAndReconnect();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Stale data is fetched again when the device reconnects");
        });

        [UnityTest]
        public IEnumerator Given_StaleDataAndNever_When_TheDeviceComesBackOnline_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onReconnect = QueryRefetchMode.Never;
            using var mounted = MountResolved();

            // Act
            await DisconnectAndReconnect();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "RefetchOnReconnect Never turns the refetch off");
        });

        [UnityTest]
        public IEnumerator Given_AClientDefaultOfNeverOnReconnect_When_TheDeviceComesBackOnline_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client = NewClient(QueryRefetchMode.IfStale, QueryRefetchMode.Never);
            using var mounted = MountResolved();

            // Act
            await DisconnectAndReconnect();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A query that sets nothing takes the client's reconnect default");
        });

        [UnityTest]
        public IEnumerator Given_AnOnlineReadingThatThrowsWhenTheClientStartsWatching_When_AFramePasses_Then_NothingIsFetched()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var throws = true;
            NetworkSignals.IsOnline = () =>
            {
                if (!throws) return s_online;
                throws = false;
                throw new InvalidOperationException("online-failed");
            };
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(NetworkSignals), "online-failed");
            using var mounted = MountResolved();

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A first reading that fails is taken as online, so reading online next is no reconnect");
        });

        [UnityTest]
        public IEnumerator Given_StaleDataAndFocusNever_When_TheDeviceComesBackOnline_Then_TheQueryFetchesAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onFocus = QueryRefetchMode.Never;
            using var mounted = MountResolved();

            // Act
            await DisconnectAndReconnect();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Reconnecting reads RefetchOnReconnect, not RefetchOnWindowFocus");
        });

        #endregion

        #region Watching the readings

        [Test]
        public void Given_AClientsLastReader_When_ItUnmounts_Then_TheClientIsNoLongerWatched()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            Hide(mounted);

            // Assert
            Assert.That(IsWatched(s_client), Is.False, "A client with no reader is not polled for");
        }

        [UnityTest]
        public IEnumerator Given_EveryWatchedClientsReadersUnmounted_When_FramesPass_Then_ThePollingStops()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            Hide(mounted);

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That((WatcherCount(), IsPolling()), Is.EqualTo((0, false)),
                "With nothing left to watch, the poll ends rather than reading on every frame");
        });

        [UnityTest]
        public IEnumerator Given_AClientWhoseReadersLeftAndCameBack_When_TheApplicationBecomesVisible_Then_TheQueryFetchesAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            Hide(mounted);
            await Pass(TimeSpan.Zero);
            Show(mounted);
            s_sources[1].TrySetResult(2);
            mounted.FlushStateForTest();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(3), "Polling starts again for a client watched again, after it had stopped");
        });

        [UnityTest]
        public IEnumerator Given_TwoWatchedClients_When_TheLaterOnesReaderUnmounts_Then_TheEarlierIsStillToldOfFocus()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            using var other = V.Mount(new VisualElement(), V.Component(OtherHost, key: "other"));
            other.FlushEffectsForTest();
            s_setShowOther.Invoke(false);
            other.FlushStateForTest();
            other.FlushEffectsForTest();

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Unwatching one client leaves the others watched");
        });

        [UnityTest]
        public IEnumerator Given_AVisibilityReadingThatThrowsOnce_When_TheApplicationIsHiddenAndShown_Then_TheFailureIsLoggedAndTheQueryRefetches()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var throws = true;
            NetworkSignals.IsVisible = () =>
            {
                if (!throws) return s_visible;
                throws = false;
                throw new InvalidOperationException("visibility-failed");
            };
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(NetworkSignals), "visibility-failed");
            using var mounted = MountResolved();
            await Pass(TimeSpan.Zero);

            // Act
            await HideAndShowTheApplication();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2),
                "A reading that fails is logged and taken as visible, so the frame after it reports no change, and polling goes on");
        });

        [UnityTest]
        public IEnumerator Given_AVisibilityReadingThatThrowsEveryFrame_When_FramesPass_Then_ItsFailureIsLoggedOnce()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            NetworkSignals.IsVisible = () => throw new InvalidOperationException("visibility-failed");
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(NetworkSignals), "visibility-failed");
            using var mounted = MountResolved();

            // Act
            await Pass(TimeSpan.Zero);
            await Pass(TimeSpan.Zero);

            // Assert
            LogAssert.NoUnexpectedReceived();
        });

        [UnityTest]
        public IEnumerator Given_AQueryWhosePlaceholderThrowsOnASignal_When_TheDeviceReconnectsTwice_Then_AnotherClientIsToldBothTimes()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the failed entry holds no data, so a signal's refetch makes the snapshot ask for a placeholder.
            var throws = false;
            s_placeholder = (_, _) =>
            {
                if (throws) throw new InvalidOperationException("placeholder-failed");
                return 9;
            };
            using var mounted = Mount();
            s_sources[0].TrySetException(new InvalidOperationException("fetch-failed"));
            mounted.FlushStateForTest();
            using var other = V.Mount(new VisualElement(), V.Component(OtherHost, key: "other"));
            other.FlushEffectsForTest();
            s_otherSources[0].TrySetResult(1);
            other.FlushStateForTest();
            throws = true;
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(QueryClient), "placeholder-failed");
            await DisconnectAndReconnect();
            throws = false;
            s_otherSources[1].TrySetResult(1);
            other.FlushStateForTest();

            // Act
            await DisconnectAndReconnect();

            // Assert
            Assert.That(s_otherCalls, Is.EqualTo(3),
                "A client whose signal throws neither keeps the next client from being told nor ends the polling");
        });

        [UnityTest]
        public IEnumerator Given_AQueryWhosePlaceholderThrowsOnASignal_When_TheDeviceReconnects_Then_TheQueryFunctionRunsAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the failed entry holds no data, so a signal's refetch makes the snapshot ask for a placeholder.
            var throws = false;
            s_placeholder = (_, _) =>
            {
                if (throws) throw new InvalidOperationException("placeholder-failed");
                return 9;
            };
            using var mounted = Mount();
            s_sources[0].TrySetException(new InvalidOperationException("fetch-failed"));
            mounted.FlushStateForTest();
            throws = true;
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(QueryClient), "placeholder-failed");

            // Act
            await DisconnectAndReconnect();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The refetch a throwing observer interrupted still runs the query function");
        });

        [Test]
        public void Given_AWatchedClientAndLoggedFailures_When_TheStaticsAreResetForADomainReload_Then_ThePollStartsOverClean()
        {
            // Arrange
            LogAssert.ignoreFailingMessages = true;
            NetworkSignals.IsVisible = () => throw new InvalidOperationException("visibility-failed");
            NetworkSignals.IsOnline = () => throw new InvalidOperationException("online-failed");
            using var mounted = Mount();

            // Act
            typeof(QueryClientSignals)
                .GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);

            // Assert
            Assert.That(
                (WatcherCount(), IsPolling(), GetSignalsStatic("s_visibleFailureLogged"), GetSignalsStatic("s_onlineFailureLogged"),
                    NetworkSignals.IsVisible == null, NetworkSignals.IsOnline == null),
                Is.EqualTo((0, false, (object)false, (object)false, true, true)),
                "A domain reload starts with no client watched, no poll running, no failure logged and no override");
        }

        #endregion

        #region Components and helpers

        private static void SetSignalsStatic(string name, object value)
            => typeof(QueryClientSignals)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, value);

        private static object GetSignalsStatic(string name)
            => typeof(QueryClientSignals)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

        private static QueryClient NewClient(QueryRefetchMode onFocus, QueryRefetchMode onReconnect = QueryRefetchMode.IfStale)
            => new(new QueryClientOptions
            {
                Retry = 0,
                RefetchOnWindowFocus = onFocus,
                RefetchOnReconnect = onReconnect,
                Clock = () => s_now,
            });

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private MountedTree MountResolved()
        {
            var mounted = Mount();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static void Hide(MountedTree mounted)
        {
            s_setShow.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static void Show(MountedTree mounted)
        {
            s_setShow.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static void Rerender(MountedTree mounted)
        {
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static object? IntervalWaitOf(QueryResult<int> result)
        {
            var observer = typeof(QueryResult<int>)
                .GetField("_observer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(result)!;
            return observer.GetType()
                .GetField("_intervalWait", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(observer);
        }

        private static int WatcherCount()
            => ((IList)typeof(QueryClientSignals)
                .GetField("s_watchers", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!).Count;

        private static bool IsPolling()
            => (bool)typeof(QueryClientSignals)
                .GetField("s_polling", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

        private static bool IsWatched(QueryClient client)
        {
            var watchers = (IList)typeof(QueryClientSignals)
                .GetField("s_watchers", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
            foreach (var watcher in watchers)
            {
                var watched = watcher.GetType()
                    .GetProperty("Client", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(watcher);
                if (ReferenceEquals(watched, client)) return true;
            }
            return false;
        }

        private static async VelvetTask Pass(TimeSpan time)
        {
            s_now += time;
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
        }

        private static async VelvetTask HideAndShowTheApplication()
        {
            s_visible = false;
            await Pass(TimeSpan.Zero);
            s_visible = true;
            await Pass(TimeSpan.Zero);
        }

        private static async VelvetTask DisconnectAndReconnect()
        {
            s_online = false;
            await Pass(TimeSpan.Zero);
            s_online = true;
            await Pass(TimeSpan.Zero);
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            s_tokens.Add(token);
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("todos"), Fetch)
                {
                    Enabled = s_enabled,
                    StaleTime = s_staleTime,
                    RefetchInterval = s_interval,
                    RefetchIntervalInBackground = s_inBackground,
                    RefetchOnWindowFocus = s_onFocus,
                    RefetchOnReconnect = s_onReconnect,
                    PlaceholderData = s_placeholder,
                },
                s_client));
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode OtherReader()
        {
            Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("other"), _ =>
                {
                    s_otherCalls++;
                    var source = new VelvetTaskCompletionSource<int>();
                    s_otherSources.Add(source);
                    return source.Task;
                }),
                s_otherClient);
            return V.Label(text: "other");
        }

        [Component]
        private static VNode OtherHost()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShowOther = setShow;
            return V.Div(children: new VNode[]
            {
                show ? V.Component(OtherReader, key: "other") : V.Fragment(Array.Empty<VNode>()),
            });
        }

        [Component]
        private static VNode Host()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(children: new VNode[]
            {
                show ? V.Component(Reader, key: "reader") : V.Fragment(Array.Empty<VNode>()),
            });
        }

        #endregion
    }
}
