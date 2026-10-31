#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>NetworkMode</c> on a mounted <see cref="Hooks.UseQuery{T}"/>: TanStack Query's
    /// <c>networkMode</c>, with the fetch status it reports while a request waits.
    /// <list type="bullet">
    /// <item>Under the default, <c>Online</c>, a request does not start while <see cref="NetworkSignals"/> reads
    /// offline: the query function is not called, the query reports <c>Paused</c> and not fetching, and the
    /// request starts once the device is online, without a second request being started beside it.</item>
    /// <item><c>Always</c> starts the request whatever the connection and does not wait before a retry;
    /// <c>OfflineFirst</c> starts it whatever the connection and waits for the connection before a retry.</item>
    /// <item>A retry waits for the application to be visible in every mode, and for the connection in all but
    /// <c>Always</c>.</item>
    /// <item>A mode on the query replaces the client's, which replaces the default.</item>
    /// <item>A request the client starts itself, as <c>FetchQuery</c> does, pauses the same way.</item>
    /// <item>A paused entry is a request in flight: a filter reads it as paused, a state reads it as paused, the
    /// entry is not collected, and the last reader leaving ends it as it ends any request whose function takes
    /// the token.</item>
    /// <item>A change from paused to running re-renders a query that reads only <c>IsPaused</c>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <see cref="NetworkSignals"/> reads <see cref="s_online"/> and <see cref="s_visible"/>, and the client's
    /// clock is <see cref="s_now"/>, each set by hand. Waits and the pause are polled once a frame, so each case
    /// advances frames with <c>VelvetTask.Yield()</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryNetworkModeTests
    {
        private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static bool s_online;
        private static bool s_visible;
        private static NetworkMode? s_mode;
        private static NetworkMode s_clientMode;
        private static QueryProperties s_notify;
        private static bool s_takesToken;
        private static int s_calls;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<CancellationToken> s_tokens = new();
        private static readonly List<QueryResult<int>> s_renders = new();
        private static StateUpdater<bool> s_setShow;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_online = true;
            s_visible = true;
            s_mode = null;
            s_clientMode = NetworkMode.Online;
            s_notify = QueryProperties.All;
            s_takesToken = true;
            s_calls = 0;
            s_sources.Clear();
            s_tokens.Clear();
            s_renders.Clear();
            s_setShow = default;
            s_client = NewClient(TimeSpan.MaxValue);
            NetworkSignals.IsOnline = () => s_online;
            NetworkSignals.IsVisible = () => s_visible;
        }

        [TearDown]
        public void TearDown() => MutationNetworkFixture.Reset();

        [Test]
        public void Given_AnOnlineQuery_When_MountedWhileOffline_Then_ItReportsAPausedFetchStatus()
        {
            // Arrange
            s_online = false;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Last().FetchStatus, Is.EqualTo(QueryFetchStatus.Paused),
                "A request that cannot start is paused, as v5's fetchState reports it");
        }

        [Test]
        public void Given_AnOnlineQuery_When_MountedWhileOffline_Then_ItDoesNotCountAsFetching()
        {
            // Arrange
            s_online = false;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Last().IsFetching, Is.False, "A paused query is not fetching, as v5's isFetching reads fetchStatus");
        }

        [Test]
        public void Given_AnOnlineQuery_When_MountedWhileOffline_Then_ItReportsIsPaused()
        {
            // Arrange
            s_online = false;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Last().IsPaused, Is.True, "isPaused is true while the fetch status is paused");
        }

        [Test]
        public void Given_AnOnlineQuery_When_MountedWhileOffline_Then_TheQueryFunctionIsNotCalled()
        {
            // Arrange
            s_online = false;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "Online waits for the connection before the first request");
        }

        [UnityTest]
        public IEnumerator Given_APausedQuery_When_TheDeviceComesBackOnline_Then_TheQueryFunctionIsCalledOnce()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            s_online = true;
            await Frames();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1),
                "Reconnecting continues the paused request, and refetch-on-reconnect joins it rather than starting another");
        });

        [UnityTest]
        public IEnumerator Given_APausedQuery_When_TheDeviceComesBackOnline_Then_ItReportsFetching()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            s_online = true;
            await Frames();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().FetchStatus, Is.EqualTo(QueryFetchStatus.Fetching), "The continued request is running");
        });

        [UnityTest]
        public IEnumerator Given_APausedQuery_When_TheDeviceStaysOffline_Then_TheQueryFunctionIsStillNotCalled()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            await Frames();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "Frames passing do not start a request the connection does not allow");
        });

        [UnityTest]
        public IEnumerator Given_APausedQuery_When_TheRequestLandsAfterReconnecting_Then_TheQueryHoldsItsData()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();
            s_online = true;
            await Frames();

            // Act
            s_sources[0].TrySetResult(7);
            await Frames();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Data, Is.EqualTo(7), "A request that waited lands as any other does");
        });

        [Test]
        public void Given_AnAlwaysQuery_When_MountedWhileOffline_Then_TheQueryFunctionIsCalled()
        {
            // Arrange
            s_online = false;
            s_mode = NetworkMode.Always;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Always never waits for the connection");
        }

        [Test]
        public void Given_AnAlwaysQuery_When_MountedWhileOffline_Then_ItReportsFetching()
        {
            // Arrange
            s_online = false;
            s_mode = NetworkMode.Always;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Last().FetchStatus, Is.EqualTo(QueryFetchStatus.Fetching), "A request that runs is fetching whatever the connection");
        }

        [Test]
        public void Given_AnOfflineFirstQuery_When_MountedWhileOffline_Then_TheQueryFunctionIsCalled()
        {
            // Arrange
            s_online = false;
            s_mode = NetworkMode.OfflineFirst;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "OfflineFirst makes the first request whatever the connection");
        }

        [UnityTest]
        public IEnumerator Given_AnOnlineQuery_When_ARequestFailsAndTheDeviceGoesOfflineBeforeTheRetry_Then_TheRetryPauses()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            Fail(0);
            await Frames();
            s_online = false;

            // Act
            await Pass(Second);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().FetchStatus, Is.EqualTo(QueryFetchStatus.Paused), "A retry waits for the connection once its delay is over");
        });

        [UnityTest]
        public IEnumerator Given_AnOnlineQuery_When_ARetryPausesAndTheDeviceComesBack_Then_TheRetryRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            Fail(0);
            await Frames();
            s_online = false;
            await Pass(Second);

            // Act
            s_online = true;
            await Frames();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The retry goes ahead once the connection is back");
        });

        [UnityTest]
        public IEnumerator Given_AnOnlineQuery_When_ARetryIsDueWhileOffline_Then_TheQueryFunctionIsNotCalledAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            Fail(0);
            await Frames();
            s_online = false;

            // Act
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A retry does not start without the connection");
        });

        [UnityTest]
        public IEnumerator Given_AnAlwaysQuery_When_ARetryIsDueWhileOffline_Then_TheRetryRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mode = NetworkMode.Always;
            using var mounted = Mount();
            Fail(0);
            await Frames();
            s_online = false;

            // Act
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Always retries whatever the connection");
        });

        [UnityTest]
        public IEnumerator Given_AnOfflineFirstQuery_When_ARetryIsDueWhileOffline_Then_TheRetryPauses()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            s_mode = NetworkMode.OfflineFirst;
            using var mounted = Mount();
            Fail(0);
            await Frames();

            // Act
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "OfflineFirst waits for the connection before a retry");
        });

        [UnityTest]
        public IEnumerator Given_AnAlwaysQuery_When_ARetryIsDueWhileTheApplicationIsHidden_Then_TheRetryPauses()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mode = NetworkMode.Always;
            using var mounted = Mount();
            Fail(0);
            await Frames();
            s_visible = false;

            // Act
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A retry waits for the application to be visible in every mode");
        });

        [UnityTest]
        public IEnumerator Given_ARetryPausedWhileHidden_When_TheApplicationIsVisibleAgain_Then_TheRetryRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mode = NetworkMode.Always;
            using var mounted = Mount();
            Fail(0);
            await Frames();
            s_visible = false;
            await Pass(Second);

            // Act
            s_visible = true;
            await Frames();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The retry goes ahead once the application is visible");
        });

        [Test]
        public void Given_AClientDefaultOfAlways_When_AQueryMountsOffline_Then_TheQueryFunctionIsCalled()
        {
            // Arrange
            s_online = false;
            s_clientMode = NetworkMode.Always;
            s_client = NewClient(TimeSpan.MaxValue);

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "The client's mode applies to a query that sets none");
        }

        [Test]
        public void Given_AClientDefaultOfAlways_When_TheQuerySetsOnline_Then_TheQueryPauses()
        {
            // Arrange
            s_online = false;
            s_clientMode = NetworkMode.Always;
            s_mode = NetworkMode.Online;
            s_client = NewClient(TimeSpan.MaxValue);

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Last().FetchStatus, Is.EqualTo(QueryFetchStatus.Paused), "The query's mode replaces the client's");
        }

        [Test]
        public void Given_AFetchQueryOverAnOnlineClient_When_CalledWhileOffline_Then_TheEntryIsPaused()
        {
            // Arrange
            s_online = false;

            // Act
            _ = s_client.FetchQuery(new QueryOptions<int>(new QueryKey("todos"), Fetch));

            // Assert
            Assert.That(s_client.GetQueryState<int>(new QueryKey("todos"))?.FetchStatus, Is.EqualTo(QueryFetchStatus.Paused),
                "A request the client starts waits for the connection as a mounted query's does");
        }

        [Test]
        public void Given_APausedEntry_When_AFilterAsksForPaused_Then_ItSelectsTheEntry()
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { FetchStatus = QueryFetchStatus.Paused });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(1), "The paused fetch status selects an entry whose request waits");
        }

        [Test]
        public void Given_APausedEntry_When_AFilterAsksForFetching_Then_ItSelectsNothing()
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { FetchStatus = QueryFetchStatus.Fetching });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(0), "A paused request is not fetching");
        }

        [Test]
        public void Given_APausedEntry_When_AFilterAsksForIdle_Then_ItSelectsNothing()
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { FetchStatus = QueryFetchStatus.Idle });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(0), "A paused request is in flight, so the entry is not idle");
        }

        [Test]
        public void Given_APausedEntry_When_ItsStateIsRead_Then_TheFetchStatusIsPaused()
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            var state = s_client.GetQueryState<int>(new QueryKey("todos"));

            // Assert
            Assert.That(state?.FetchStatus, Is.EqualTo(QueryFetchStatus.Paused), "The entry's state carries the fetch status");
        }

        [UnityTest]
        public IEnumerator Given_APausedEntryNothingReads_When_ItsGarbageCollectionTimeHasPassed_Then_TheEntryIsKept()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            s_takesToken = false;
            s_client = NewClient(Second);
            using var mounted = Mount();
            Hide(mounted);

            // Act
            await Pass(Second + Second);

            // Assert
            Assert.That(s_client.GetQueryState<int>(new QueryKey("todos")), Is.Not.Null,
                "v5 removes a query only while its fetch status is idle, and a paused one is not");
        });

        [UnityTest]
        public IEnumerator Given_ARetryPausedForTheConnection_When_TheLastReaderLeaves_Then_TheTokenIsCancelled()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            s_mode = NetworkMode.OfflineFirst;
            using var mounted = Mount();
            Fail(0);
            await Frames();
            await Pass(Second);

            // Act
            Hide(mounted);

            // Assert
            Assert.That(s_tokens[0].IsCancellationRequested, Is.True,
                "A request that has started is cancelled with its last reader, paused between attempts or not");
        });

        [Test]
        public void Given_APausedRequestOfAFunctionTakingTheToken_When_TheLastReaderLeaves_Then_TheEntryIsIdle()
        {
            // Arrange
            s_online = false;
            using var mounted = Mount();

            // Act
            Hide(mounted);

            // Assert
            Assert.That(s_client.GetQueryState<int>(new QueryKey("todos"))?.FetchStatus, Is.EqualTo(QueryFetchStatus.Idle),
                "Leaving ends a request still waiting to start, which has no function running to keep");
        }

        [UnityTest]
        public IEnumerator Given_AQueryNotifyingOnlyOnIsPaused_When_ThePausedRequestContinues_Then_ItRerendersAsNotPaused()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_online = false;
            s_notify = QueryProperties.IsPaused;
            using var mounted = Mount();

            // Act
            s_online = true;
            await Frames();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsPaused, Is.False, "A change of the property listed re-renders the component");
        });

        private MountedTree Mount()
        {
            var tree = V.Mount(_root, V.Component(Host, key: "host"));
            tree.FlushEffectsForTest();
            tree.FlushStateForTest();
            return tree;
        }

        private static QueryClient NewClient(TimeSpan gcTime)
            => new(new QueryClientOptions
            {
                GcTime = gcTime,
                NetworkMode = s_clientMode,
                Clock = () => s_now,
            });

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private static void Fail(int call)
            => s_sources[call].TrySetException(new InvalidOperationException("fail-" + (call + 1)));

        private static async VelvetTask Frames()
        {
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
        }

        private static async VelvetTask Pass(TimeSpan time)
        {
            s_now += time;
            await Frames();
        }

        private static void Hide(MountedTree mounted)
        {
            s_setShow.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
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
            var key = new QueryKey("todos");
            var options = s_takesToken
                ? new QueryOptions<int>(key, Fetch)
                : new QueryOptions<int>(key, () => Fetch(default));
            s_renders.Add(Hooks.UseQuery(
                options with { NetworkMode = s_mode, NotifyOnChangeProps = s_notify },
                s_client));
            return V.Label(text: "reader");
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
    }
}
