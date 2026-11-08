#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="QueryStaleTime.Static"/> and <see cref="QueryOptionsBase{TQueryFnData, TData}.StaleTimeFn"/>,
    /// TanStack Query's <c>staleTime: 'static'</c> and <c>staleTime</c> as a function.
    /// <list type="bullet">
    /// <item>Static data stays fresh through an invalidation, and neither <c>InvalidateQueries</c>,
    /// <c>RefetchQueries</c> nor a mount asking <c>Always</c> fetches it; <c>ResetQueries</c>, which leaves the
    /// entry without data, does.</item>
    /// <item>A function is asked with the entry and its answer is the stale time; the client's function applies to a
    /// query that sets neither form, and a query's own value stands ahead of it.</item>
    /// <item>A function that throws outside a render is logged once however often it is asked, one that throws
    /// inside <c>FetchQuery</c> throws from it, and one that throws during a render, the first render included,
    /// reaches the error boundary.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The first reader mounts, its request lands 7, and the clock moves ten minutes on, which is stale for any
    /// stale time the cases set but <see cref="TimeSpan.MaxValue"/> and static. <see cref="ShowSecond"/> mounts a
    /// second reader over the same entry, and <see cref="s_calls"/> counts the requests that ran.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryStaleTimeFormsTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static int s_calls;
        private static QueryStaleTime? s_staleTime;
        private static Func<Query, QueryStaleTime>? s_staleTimeFn;
        private static Func<Query, QueryStaleTime>? s_secondStaleTimeFn;
        private static QueryRefetchMode? s_refetchOnMount;
        private static object? s_seenData;
        private static StateUpdater<bool> s_setShowSecond;
        private static StateUpdater<int> s_setTick;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_calls = 0;
            s_staleTime = null;
            s_staleTimeFn = null;
            s_secondStaleTimeFn = null;
            s_refetchOnMount = null;
            s_seenData = null;
            s_setShowSecond = default;
            s_setTick = default;
            s_sources.Clear();
            s_renders.Clear();
            s_client = NewClient(staleTimeFn: null);
        }

        [Test]
        public void Given_StaticData_When_ItIsInvalidated_Then_NothingIsFetched()
        {
            // Arrange
            s_staleTime = QueryStaleTime.Static;
            using var mounted = MountResolved();

            // Act
            s_client.InvalidateQueries().Forget();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "v5's refetchQueries passes over a query a reader holds static");
        }

        [Test]
        public void Given_StaticData_When_RefetchQueriesRuns_Then_NothingIsFetched()
        {
            // Arrange
            s_staleTime = QueryStaleTime.Static;
            using var mounted = MountResolved();

            // Act
            s_client.RefetchQueries().Forget();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "v5's refetchQueries passes over a query a reader holds static");
        }

        [Test]
        public void Given_InvalidatedStaticData_When_TheReaderRendersAgain_Then_ItIsNotStale()
        {
            // Arrange
            s_staleTime = QueryStaleTime.Static;
            using var mounted = MountResolved();
            s_client.InvalidateQueries().Forget();

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsStale, Is.False, "Static data is never stale, not even once invalidated");
        }

        [Test]
        public void Given_StaticData_When_ASecondReaderAskingAlwaysMounts_Then_NothingIsFetched()
        {
            // Arrange
            s_staleTime = QueryStaleTime.Static;
            s_refetchOnMount = QueryRefetchMode.Always;
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "v5's shouldFetchOn refetches no static query, Always included");
        }

        [Test]
        public void Given_StaticData_When_ResetQueriesRuns_Then_TheEntryIsFetchedAgain()
        {
            // Arrange
            s_staleTime = QueryStaleTime.Static;
            using var mounted = MountResolved();

            // Act
            s_client.ResetQueries().Forget();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "An entry with no data is not static, so the reset's refetch runs");
        }

        [Test]
        public void Given_AStaleTimeFunctionKeepingDataFresh_When_ASecondReaderMountsTenMinutesLater_Then_NothingIsFetched()
        {
            // Arrange
            s_staleTimeFn = _ => TimeSpan.MaxValue;
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "The function's answer is the stale time");
        }

        [Test]
        public void Given_AStaleTimeFunction_When_ASecondReaderMounts_Then_ItIsHandedTheEntrysData()
        {
            // Arrange — only the second reader sets the function.
            s_secondStaleTimeFn = info =>
            {
                s_seenData = info.Data;
                return TimeSpan.MaxValue;
            };
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_seenData, Is.EqualTo(7), "A function form is handed the entry its query reads");
        }

        [Test]
        public void Given_AClientStaleTimeFunction_When_ASecondReaderSettingNoneMounts_Then_TheFunctionDecides()
        {
            // Arrange
            s_client = NewClient(staleTimeFn: _ => TimeSpan.MaxValue);
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A query that sets no stale time takes the client's function");
        }

        [Test]
        public void Given_AClientStaleTimeFunctionAndAQueryValue_When_ASecondReaderMounts_Then_TheQuerysValueDecides()
        {
            // Arrange
            s_client = NewClient(staleTimeFn: _ => TimeSpan.MaxValue);
            s_staleTime = TimeSpan.Zero;
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A query's own stale time stands ahead of the client's, in either form");
        }

        [Test]
        public void Given_AStaleTimeFunctionThatThrows_When_TheReaderRenders_Then_TheBoundaryShowsTheFailure()
        {
            // Arrange
            s_staleTimeFn = _ => throw new InvalidOperationException("stale-failed");

            // Act
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("failed:stale-failed"), "A function a render asks throws from the render, as v5's does");
        }

        [Test]
        public void Given_AStaleTimeFunctionThrowingOverData_When_ItIsAskedTwiceAsTheRequestLands_Then_ItsFailureIsLoggedOnce()
        {
            // Arrange — the function throws only once the entry holds data, so the reader mounts and fetches; the
            // landing asks it for the result and again for the stale wait, outside any render.
            s_staleTimeFn = info => info.Data == null ? TimeSpan.Zero : throw new InvalidOperationException("stale-failed");
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(QueryClient), "stale-failed");
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();

            // Act
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_AStaleTimeFunctionThatThrows_When_FetchQueryRuns_Then_TheCallThrowsIt()
        {
            // Arrange
            var options = new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken)) { StaleTimeFn = _ => throw new InvalidOperationException("stale-failed") };

            // Act
            TestDelegate fetch = () => s_client.FetchQuery(options).Forget();

            // Assert
            Assert.That(fetch, Throws.InvalidOperationException, "v5's fetchQuery throws what its staleTime function throws");
        }

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private static QueryClient NewClient(Func<Query, QueryStaleTime>? staleTimeFn)
            => new(new QueryClientOptions
            {
                StaleTimeFn = staleTimeFn,
                GcTime = TimeSpan.MaxValue,
                Retry = 0,
                Clock = () => s_now,
            });

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private MountedTree MountResolved()
        {
            var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();
            s_now = TimeSpan.FromMinutes(10);
            return mounted;
        }

        private static void ShowSecond(MountedTree mounted)
        {
            s_setShowSecond.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader(bool second)
        {
            var result = Hooks.UseQuery(
                new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken))
                {
                    StaleTime = s_staleTime,
                    StaleTimeFn = second && s_secondStaleTimeFn != null ? s_secondStaleTimeFn : s_staleTimeFn,
                    RefetchOnMount = s_refetchOnMount,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client);
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_renders.Add(result);
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode Host()
        {
            var (showSecond, setShowSecond) = Hooks.UseState(false);
            s_setShowSecond = setShowSecond;
            return V.ErrorBoundary(
                error => V.Label(text: "failed:" + error.Message),
                new VNode?[]
                {
                    V.Component(Reader, false, key: "first"),
                    showSecond ? V.Component(Reader, true, key: "second") : null,
                },
                key: "boundary");
        }
    }
}
