#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="Hooks.UseInfiniteQuery{TQueryFnData, TPageParam, TData}"/>,
    /// <see cref="Hooks.UseSuspenseInfiniteQuery{TQueryFnData, TPageParam, TData}"/> and
    /// <see cref="QueryClient.FetchInfiniteQuery{TQueryFnData, TPageParam}"/>, TanStack Query's infinite queries.
    /// <list type="bullet">
    /// <item>The first request fetches the initial page parameter's page; fetching the next or previous page adds
    /// one at that end, handing the function the parameter and the direction, and fetches nothing where the paging
    /// names none.</item>
    /// <item>The result reports whether a page lies either side, and which end a request in flight or the last
    /// failure was for.</item>
    /// <item>A refetch fetches every page again from the first, in order, stopping where the paging names none;
    /// <c>MaxPages</c> drops the page at the other end.</item>
    /// <item><see cref="QueryClient.FetchInfiniteQuery{TQueryFnData, TPageParam}"/> fetches as many pages as
    /// <c>Pages</c> asks.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Page <c>n</c> is <c>"p" + n</c>, fetched through a completion source the case settles itself; pages run from
    /// <see cref="s_firstPage"/> to <see cref="s_lastPage"/>, nothing retries and data stays fresh.
    /// </remarks>
    [TestFixture]
    internal sealed class UseInfiniteQueryTests
    {
        private static readonly QueryKey Feed = new("feed");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static readonly List<int> s_requested = new();
        private static readonly List<FetchDirection> s_directions = new();
        private static readonly List<VelvetTaskCompletionSource<string>> s_sources = new();
        private static readonly List<InfiniteQueryResult<InfiniteData<string, int>>> s_renders = new();
        private static int s_firstPage;
        private static int s_lastPage;
        private static int? s_maxPages;
        private static bool s_withPrevious;
        private static int s_retry;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => TimeSpan.Zero });
            s_requested.Clear();
            s_directions.Clear();
            s_sources.Clear();
            s_renders.Clear();
            s_firstPage = -5;
            s_lastPage = 5;
            s_maxPages = null;
            s_withPrevious = true;
            s_retry = 0;
        }

        #region Paging

        [Test]
        public void Given_NoEntry_When_TheQueryMounts_Then_ItFetchesTheInitialPage()
        {
            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(string.Join(",", s_requested), Is.EqualTo("0"));
        }

        [Test]
        public void Given_TheFirstPage_When_FetchNextPageRuns_Then_ThePageIsAddedAfterIt()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            NextPage(mounted, "p1");

            // Assert
            Assert.That(string.Join(",", Last().Data!.Pages), Is.EqualTo("p0,p1"));
        }

        [Test]
        public void Given_TheFirstPage_When_FetchPreviousPageRuns_Then_ThePageIsAddedBeforeIt()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            _ = Last().FetchPreviousPage();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult("p-1"));

            // Assert
            Assert.That(string.Join(",", Last().Data!.PageParams), Is.EqualTo("-1,0"));
        }

        [Test]
        public void Given_TheFirstPage_When_FetchPreviousPageRuns_Then_TheFunctionIsHandedTheBackwardDirection()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            _ = Last().FetchPreviousPage();

            // Assert
            Assert.That(s_directions[s_directions.Count - 1], Is.EqualTo(FetchDirection.Backward));
        }

        [Test]
        public void Given_ThePagingNamesNoNextPage_When_FetchNextPageRuns_Then_NothingIsFetched()
        {
            // Arrange
            s_lastPage = 0;
            using var mounted = MountResolved();

            // Act
            _ = Last().FetchNextPage();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_requested.Count, Is.EqualTo(1), "v5's fetchPage returns the pages held when the parameter is none");
        }

        [Test]
        public void Given_ThePagingNamesNoNextPage_When_TheResultIsRead_Then_ItHasNoNextPage()
        {
            // Act
            s_lastPage = 0;
            using var mounted = MountResolved();

            // Assert
            Assert.That(Last().HasNextPage, Is.False);
        }

        [Test]
        public void Given_ThePagingNamesANextPage_When_TheResultIsRead_Then_ItHasANextPage()
        {
            // Act
            using var mounted = MountResolved();

            // Assert
            Assert.That(Last().HasNextPage, Is.True);
        }

        [Test]
        public void Given_NoPreviousPageFunction_When_TheResultIsRead_Then_ItHasNoPreviousPage()
        {
            // Act
            s_withPrevious = false;
            using var mounted = MountResolved();

            // Assert
            Assert.That(Last().HasPreviousPage, Is.False);
        }

        [Test]
        public void Given_ANextPageInFlight_When_TheComponentRenders_Then_ItIsFetchingTheNextPage()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            _ = Last().FetchNextPage();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsFetchingNextPage, Is.True);
        }

        [Test]
        public void Given_ANextPageInFlight_When_ItFails_Then_TheResultIsAFetchNextPageError()
        {
            // Arrange
            using var mounted = MountResolved();
            _ = Last().FetchNextPage();

            // Act
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetException(new InvalidOperationException("offline")));

            // Assert
            Assert.That(Last().IsFetchNextPageError, Is.True);
        }

        [Test]
        public void Given_APreviousPageInFlight_When_TheComponentRenders_Then_ItIsNotFetchingTheNextPage()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            _ = Last().FetchPreviousPage();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsFetchingNextPage, Is.False);
        }

        [Test]
        public void Given_ARequestInFlightOverPages_When_FetchNextPageRuns_Then_ThatRequestIsCancelled()
        {
            // Arrange
            using var mounted = MountResolved();
            Last().Refetch();
            var refetch = s_sources[s_sources.Count - 1];

            // Act
            _ = Last().FetchNextPage();
            Settle(mounted, () => refetch.TrySetResult("stale"));

            // Assert
            Assert.That(string.Join(",", s_client.GetQueryData<InfiniteData<string, int>>(Feed)!.Pages), Is.EqualTo("p0"),
                "v5's fetchNextPage cancels a fetch in flight over data unless cancelRefetch is false");
        }

        #endregion

        #region Refetching every page

        [Test]
        public void Given_ThreePages_When_TheQueryRefetches_Then_EachIsFetchedAgainInOrder()
        {
            // Arrange
            using var mounted = MountResolved();
            NextPage(mounted, "p1");
            NextPage(mounted, "p2");
            s_requested.Clear();

            // Act
            Last().Refetch();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult("p0"));
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult("p1"));

            // Assert
            Assert.That(string.Join(",", s_requested), Is.EqualTo("0,1,2"), "Each page is asked for once the page before it has landed");
        }

        [Test]
        public void Given_ThreePages_When_TheRefetchedPagingNamesNoThirdPage_Then_TheRefetchStopsAtTwo()
        {
            // Arrange
            using var mounted = MountResolved();
            NextPage(mounted, "p1");
            NextPage(mounted, "p2");
            s_lastPage = 1;

            // Act
            Last().Refetch();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult("p0"));
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult("p1"));

            // Assert
            Assert.That(string.Join(",", Last().Data!.Pages), Is.EqualTo("p0,p1"));
        }

        [UnityTest]
        public IEnumerator Given_ARefetchWhoseSecondPageFails_When_ItRetries_Then_ItCarriesOnFromThatPage()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retry = 1;
            using var mounted = MountResolved();
            NextPage(mounted, "p1");
            s_requested.Clear();
            Last().Refetch();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult("p0"));

            // Act
            s_sources[s_sources.Count - 1].TrySetException(new InvalidOperationException("offline"));
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();

            // Assert
            Assert.That(string.Join(",", s_requested), Is.EqualTo("0,1,1"),
                "v5's fetchFn keeps the pages fetched and the page reached across its retryer's attempts");
        });

        [Test]
        public void Given_MaxPagesOfTwo_When_AThirdPageIsFetchedAfterTheLast_Then_TheFirstIsDropped()
        {
            // Arrange
            s_maxPages = 2;
            using var mounted = MountResolved();
            NextPage(mounted, "p1");

            // Act
            NextPage(mounted, "p2");

            // Assert
            Assert.That(string.Join(",", Last().Data!.PageParams), Is.EqualTo("1,2"));
        }

        [Test]
        public void Given_MaxPagesOfTwo_When_AThirdPageIsFetchedBeforeTheFirst_Then_TheLastIsDropped()
        {
            // Arrange
            s_maxPages = 2;
            using var mounted = MountResolved();
            NextPage(mounted, "p1");

            // Act
            _ = Last().FetchPreviousPage();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult("p-1"));

            // Assert
            Assert.That(string.Join(",", Last().Data!.PageParams), Is.EqualTo("-1,0"));
        }

        #endregion

        #region The client and suspense

        [Test]
        public void Given_PagesOfTwo_When_FetchInfiniteQueryRuns_Then_TwoPagesAreFetched()
        {
            // Arrange
            var task = s_client.FetchInfiniteQuery(Options() with { Pages = 2 });

            // Act
            s_sources[0].TrySetResult("p0");
            s_sources[1].TrySetResult("p1");

            // Assert
            Assert.That(string.Join(",", task.GetAwaiter().GetResult().Pages), Is.EqualTo("p0,p1"));
        }

        [Test]
        public void Given_PagesHeld_When_EnsureInfiniteQueryDataRuns_Then_ItHandsThemBackWithoutFetching()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            var pages = s_client.EnsureInfiniteQueryData(Options()).GetAwaiter().GetResult();

            // Assert
            Assert.That(s_requested.Count == 1 ? string.Join(",", pages.Pages) : "fetched", Is.EqualTo("p0"));
        }

        [Test]
        public void Given_NoEntry_When_EnsureInfiniteQueryDataRuns_Then_ItFetchesTheFirstPage()
        {
            // Act
            var task = s_client.EnsureInfiniteQueryData(Options());
            s_sources[0].TrySetResult("p0");

            // Assert
            Assert.That(string.Join(",", task.GetAwaiter().GetResult().Pages), Is.EqualTo("p0"));
        }

        [Test]
        public void Given_ANextPageInFlight_When_TheComponentRenders_Then_ItIsNotRefetching()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            _ = Last().FetchNextPage();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsRefetching, Is.False, "v5's infinite observer takes a page fetch out of isRefetching");
        }

        [Test]
        public void Given_NoClient_When_TheQueryRenders_Then_TheErrorNamesUseInfiniteQuery()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ClientlessHost, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Does.StartWith("failed:UseInfiniteQuery found no QueryClient"));
        }

        [Test]
        public void Given_NothingCached_When_ASuspendingQueryMounts_Then_TheSuspenseShowsItsFallback()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(SuspendingHost, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("loading"));
        }

        [Test]
        public void Given_ASuspendedQuery_When_ItsFirstPageLands_Then_ItRendersThePage()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SuspendingHost, key: "host"), CaughtErrors.Unlogged);

            // Act
            Settle(mounted, () => s_sources[0].TrySetResult("p0"));

            // Assert
            Assert.That(Texts(), Is.EqualTo("pages:p0"));
        }

        #endregion

        #region Components and helpers

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private MountedTree MountResolved()
        {
            var mounted = Mount();
            Settle(mounted, () => s_sources[0].TrySetResult("p0"));
            return mounted;
        }

        private static void NextPage(MountedTree mounted, string page)
        {
            _ = Last().FetchNextPage();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetResult(page));
        }

        private static void Settle(MountedTree mounted, Action action)
        {
            action();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            mounted.FlushEffectsForTest();
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private static InfiniteQueryResult<InfiniteData<string, int>> Last() => s_renders[s_renders.Count - 1];

        private static VelvetTask<string> FetchPage(QueryFunctionContext<int> context)
        {
            s_requested.Add(context.PageParam);
            s_directions.Add(context.Direction);
            var source = new VelvetTaskCompletionSource<string>();
            s_sources.Add(source);
            return source.Task;
        }

        private static readonly Func<string, IReadOnlyList<string>, int, IReadOnlyList<int>, PageParam<int>> s_next
            = (_, _, lastParam, _) => lastParam < s_lastPage ? lastParam + 1 : PageParam<int>.None;

        private static readonly Func<string, IReadOnlyList<string>, int, IReadOnlyList<int>, PageParam<int>> s_previous
            = (_, _, firstParam, _) => firstParam > s_firstPage ? firstParam - 1 : PageParam<int>.None;

        private static InfiniteQueryOptions<string, int> Options() => new(Feed, FetchPage, 0, s_next)
        {
            GetPreviousPageParam = s_withPrevious ? s_previous : null,
            MaxPages = s_maxPages,
            Retry = s_retry,
            RetryDelay = (_, _) => TimeSpan.Zero,
            StaleTime = TimeSpan.MaxValue,
            NotifyOnChangeProps = QueryProperties.All,
        };

        [Component]
        private static VNode Reader()
        {
            s_renders.Add(Hooks.UseInfiniteQuery(Options(), s_client));
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode SuspendingReader()
        {
            var result = Hooks.UseSuspenseInfiniteQuery(Options(), s_client);
            return V.Label(text: "pages:" + string.Join(",", result.Data!.Pages));
        }

        [Component]
        private static VNode ClientlessReader()
        {
            Hooks.UseInfiniteQuery(Options());
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode ClientlessHost()
            => V.ErrorBoundary(
                error => V.Label(text: "failed:" + error.Message),
                new VNode?[] { V.Component(ClientlessReader, key: "reader") },
                key: "boundary");

        [Component]
        private static VNode SuspendingHost()
            => V.ErrorBoundary(
                error => V.Label(text: "failed:" + error.Message),
                new VNode?[]
                {
                    V.Suspense(V.Label(text: "loading"), new VNode?[] { V.Component(SuspendingReader, key: "reader") }, key: "suspense"),
                },
                key: "boundary");

        #endregion
    }
}
