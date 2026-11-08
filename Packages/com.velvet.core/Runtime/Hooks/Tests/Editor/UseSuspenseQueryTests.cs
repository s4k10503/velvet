#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="Hooks.UseSuspenseQuery{TQueryFnData, TData}"/>, TanStack Query's <c>useSuspenseQuery</c>.
    /// <list type="bullet">
    /// <item>A reader with nothing cached suspends to the nearest Suspense, its render starting the request, and
    /// renders the data once it lands, or once the data is written by hand; a failure with no data reaches the
    /// error boundary instead, and undoes a reset of the reset boundary.</item>
    /// <item>The update that settles the entry resumes the reader itself, with no frame passing. A reader whose own
    /// render's fetch gave the entry data suspends all the same and resumes once the main thread turns, so it never
    /// returns a pending result.</item>
    /// <item>A reader over fresh cached data renders it without suspending.</item>
    /// <item>The query is enabled whatever its options say, and a stale time under a second counts as a second, so
    /// the commit after the suspension does not fetch again.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The reader sits in a Suspense inside an error boundary inside a Provider of <see cref="s_resetBoundary"/>,
    /// and <see cref="s_calls"/> counts the requests that ran.
    /// </remarks>
    [TestFixture]
    internal sealed class UseSuspenseQueryTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static QueryErrorResetBoundary s_resetBoundary = null!;
        private static bool s_enabled;
        private static int s_calls;
        private static Func<int>? s_initialData;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => TimeSpan.Zero });
            s_resetBoundary = new QueryErrorResetBoundary();
            s_enabled = true;
            s_calls = 0;
            s_initialData = null;
            s_sources.Clear();
        }

        [Test]
        public void Given_NothingCached_When_TheReaderMounts_Then_TheSuspenseShowsItsFallback()
        {
            // Arrange — the client holds no entry.

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Texts(), Is.EqualTo("loading"), "A pending result suspends to the nearest Suspense");
        }

        [Test]
        public void Given_NothingCached_When_TheReaderMounts_Then_ItsRenderStartsTheRequest()
        {
            // Arrange — the client holds no entry.

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "The suspending render fetches, as v5's fetchOptimistic does");
        }

        [Test]
        public void Given_ASuspendedReader_When_ItsRequestLands_Then_ItRendersTheDataWithoutAFramePassing()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            Settle(mounted, () => s_sources[0].TrySetResult(7));

            // Assert
            Assert.That(Texts(), Is.EqualTo("data:7"), "The landing resumes the reader, as v5's promise settles with the fetch");
        }

        [Test]
        public void Given_ASuspendedReader_When_TheDataIsWrittenByHandWhileTheRequestIsInFlight_Then_ItRendersThatData()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            Settle(mounted, () => s_client.SetQueryData(Todos, 5));

            // Assert
            Assert.That(Texts(), Is.EqualTo("data:5"), "Data reaching the entry resumes the reader, as v5's fetchOptimistic races the cache");
        }

        [Test]
        public void Given_ASuspendedReader_When_ItsRequestFails_Then_TheErrorBoundaryShowsTheError()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            Settle(mounted, () => s_sources[0].TrySetException(new InvalidOperationException("offline")));

            // Assert
            Assert.That(Texts(), Is.EqualTo("failed:offline"), "An error with no data is thrown to the error boundary");
        }

        [Test]
        public void Given_AResetBoundary_When_TheSuspendedRequestFails_Then_TheResetIsUndone()
        {
            // Arrange
            s_resetBoundary.Reset();
            using var mounted = Mount();

            // Act
            Settle(mounted, () => s_sources[0].TrySetException(new InvalidOperationException("offline")));

            // Assert
            Assert.That(s_resetBoundary.IsReset, Is.False, "v5's fetchOptimistic clears the reset when its fetch fails");
        }

        [Test]
        public void Given_AnIdleEntryWithNoData_When_AReaderWhoseFetchGivesItInitialDataMountsAndTheMainThreadTurns_Then_ItRendersThatData()
        {
            // Arrange — a prefetch whose request is cancelled leaves the entry pending, idle and without data.
            s_client.PrefetchQuery(new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken))).Forget();
            s_client.CancelQueries().Forget();
            s_initialData = () => 3;
            using var mounted = Mount();

            // Act — the release the suspending render posted runs here, where the main thread would run it.
            Settle(mounted, VelvetMainThread.RunHandoffs);

            // Assert — a pending result returned instead of suspending shows data:0, and a reader never resumed shows loading.
            Assert.That(Texts(), Is.EqualTo("data:3"),
                "The render whose fetch gave the entry data suspends and renders again, as v5's thrown promise settles later");
        }

        [Test]
        public void Given_FreshCachedData_When_TheReaderMounts_Then_ItRendersItAtOnce()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Texts(), Is.EqualTo("data:5"), "A result that is not pending does not suspend");
        }

        [Test]
        public void Given_AZeroStaleTime_When_TheReaderCommitsAfterItsSuspension_Then_NothingIsFetchedAgain()
        {
            // Arrange
            using var mounted = Mount();
            Settle(mounted, () => s_sources[0].TrySetResult(7));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A suspending query's stale time is a second at least, as v5's ensureSuspenseTimers");
        }

        [Test]
        public void Given_OptionsTurningTheQueryOff_When_TheMountedReadersEntryIsInvalidated_Then_ItIsFetchedAgain()
        {
            // Arrange
            s_enabled = false;
            using var mounted = Mount();
            Settle(mounted, () => s_sources[0].TrySetResult(7));
            mounted.FlushEffectsForTest();

            // Act
            s_client.InvalidateQueries().Forget();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "useSuspenseQuery is always enabled");
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private MountedTree Mount() => V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);

        private static void Settle(MountedTree mounted, Action action)
        {
            action();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            var result = Hooks.UseSuspenseQuery(
                new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken)) { StaleTime = TimeSpan.Zero, Enabled = s_enabled, InitialData = s_initialData },
                s_client);
            return V.Label(text: "data:" + result.Data);
        }

        [Component]
        private static VNode Host()
            => V.Provider(QueryErrorResetBoundaryContext.Ref, s_resetBoundary, new VNode?[]
            {
                V.ErrorBoundary(
                    error => V.Label(text: "failed:" + error.Message),
                    new VNode?[]
                    {
                        V.Suspense(V.Label(text: "loading"), new VNode?[] { V.Component(Reader, key: "reader") }, key: "suspense"),
                    },
                    key: "boundary"),
            });
    }
}
