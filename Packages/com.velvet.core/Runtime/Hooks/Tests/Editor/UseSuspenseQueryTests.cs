#nullable enable
using System;
using System.Collections;
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
    /// Specifies <see cref="Hooks.UseSuspenseQuery{TQueryFnData, TData}"/>, TanStack Query's <c>useSuspenseQuery</c>.
    /// <list type="bullet">
    /// <item>A reader with nothing cached suspends to the nearest Suspense, its render starting the request, and
    /// renders the data once it lands; a failure with no data reaches the error boundary instead, and undoes a
    /// reset of the reset boundary.</item>
    /// <item>A reader over fresh cached data renders it without suspending.</item>
    /// <item>The query is enabled whatever its options say, and a stale time under a second counts as a second, so
    /// the commit after the suspension does not fetch again.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The reader sits in a Suspense inside an error boundary inside a Provider of <see cref="s_resetBoundary"/>.
    /// A suspended reader is told of its request settling on the next frame, so the cases that settle one let
    /// frames pass; <see cref="s_calls"/> counts the requests that ran.
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
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => TimeSpan.Zero });
            s_resetBoundary = new QueryErrorResetBoundary();
            s_enabled = true;
            s_calls = 0;
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

        [UnityTest]
        public IEnumerator Given_ASuspendedReader_When_ItsRequestLands_Then_ItRendersTheData()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_sources[0].TrySetResult(7);
            await Settle(mounted);

            // Assert
            Assert.That(Texts(), Is.EqualTo("data:7"), "The reader renders again once the entry holds data");
        });

        [UnityTest]
        public IEnumerator Given_ASuspendedReader_When_ItsRequestFails_Then_TheErrorBoundaryShowsTheError()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("offline"));
            await Settle(mounted);

            // Assert
            Assert.That(Texts(), Is.EqualTo("failed:offline"), "An error with no data is thrown to the error boundary");
        });

        [UnityTest]
        public IEnumerator Given_AResetBoundary_When_TheSuspendedRequestFails_Then_TheResetIsUndone()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_resetBoundary.Reset();
            using var mounted = Mount();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("offline"));
            await Settle(mounted);

            // Assert
            Assert.That(s_resetBoundary.IsReset, Is.False, "v5's fetchOptimistic clears the reset when its fetch fails");
        });

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

        [UnityTest]
        public IEnumerator Given_AZeroStaleTime_When_TheReaderCommitsAfterItsSuspension_Then_NothingIsFetchedAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            s_sources[0].TrySetResult(7);
            await Settle(mounted);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A suspending query's stale time is a second at least, as v5's ensureSuspenseTimers");
        });

        [UnityTest]
        public IEnumerator Given_OptionsTurningTheQueryOff_When_TheMountedReadersEntryIsInvalidated_Then_ItIsFetchedAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_enabled = false;
            using var mounted = Mount();
            s_sources[0].TrySetResult(7);
            await Settle(mounted);
            mounted.FlushEffectsForTest();

            // Act
            s_client.InvalidateQueries().Forget();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "useSuspenseQuery is always enabled");
        });

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private MountedTree Mount() => V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);

        private static async VelvetTask Settle(MountedTree mounted)
        {
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
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
                new QueryOptions<int>(Todos, Fetch) { StaleTime = TimeSpan.Zero, Enabled = s_enabled },
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
