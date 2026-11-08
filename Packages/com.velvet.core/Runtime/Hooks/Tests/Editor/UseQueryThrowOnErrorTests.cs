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
    /// Specifies <see cref="QueryOptions{TQueryFnData, TData}.ThrowOnError"/>, its function form and
    /// <see cref="QueryErrorResetBoundary"/>, TanStack Query's <c>throwOnError</c> and
    /// <c>QueryErrorResetBoundary</c>.
    /// <list type="bullet">
    /// <item><c>true</c> throws a failed query's error to the nearest error boundary; by default the result
    /// carries it. The function decides in place of the value and is handed the error.</item>
    /// <item>Until the reset boundary is reset, a reader remounting over the failed entry neither fetches it nor
    /// renders, throwing the error again; once it is reset the reader renders pending and fetches, and the
    /// commit clears the reset.</item>
    /// <item><see cref="Hooks.UseQueryErrorResetBoundary"/> reads the boundary the context provides.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The reader sits in an error boundary whose <c>onReset</c> resets <see cref="s_resetBoundary"/> when
    /// <see cref="s_resetsQueries"/> says so, inside a Provider of that reset boundary. Its request fails with
    /// "offline", and <see cref="s_calls"/> counts the requests that ran.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryThrowOnErrorTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static QueryErrorResetBoundary s_resetBoundary = null!;
        private static QueryErrorResetBoundary? s_readBoundary;
        private static ErrorBoundaryReset? s_reset;
        private static bool s_resetsQueries;
        private static bool s_throwOnError;
        private static Func<Exception, QueryInfo, bool>? s_throwOnErrorFn;
        private static string? s_seenError;
        private static bool s_enabled;
        private static int s_calls;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0 });
            s_resetBoundary = new QueryErrorResetBoundary();
            s_readBoundary = null;
            s_reset = null;
            s_resetsQueries = false;
            s_throwOnError = false;
            s_throwOnErrorFn = null;
            s_seenError = null;
            s_enabled = true;
            s_calls = 0;
            s_sources.Clear();
        }

        [Test]
        public void Given_ThrowOnError_When_TheRequestFails_Then_TheBoundaryShowsItsError()
        {
            // Arrange
            s_throwOnError = true;

            // Act
            using var mounted = MountFailed();

            // Assert
            Assert.That(Texts(), Is.EqualTo("failed:offline"), "throwOnError throws the error to the nearest error boundary");
        }

        [Test]
        public void Given_NoThrowOnError_When_TheRequestFails_Then_TheReaderRendersTheError()
        {
            // Arrange — ThrowOnError is false by default.

            // Act
            using var mounted = MountFailed();

            // Assert
            Assert.That(Texts(), Is.EqualTo("reader:Error"), "By default a failure is the result's, as v5's throwOnError: false");
        }

        [Test]
        public void Given_ThrowOnErrorAndAFunctionAnsweringFalse_When_TheRequestFails_Then_TheReaderRendersTheError()
        {
            // Arrange
            s_throwOnError = true;
            s_throwOnErrorFn = (_, _) => false;

            // Act
            using var mounted = MountFailed();

            // Assert
            Assert.That(Texts(), Is.EqualTo("reader:Error"), "ThrowOnErrorFn replaces ThrowOnError");
        }

        [Test]
        public void Given_AThrowOnErrorFunction_When_TheRequestFails_Then_ItIsHandedTheError()
        {
            // Arrange
            s_throwOnErrorFn = (error, _) =>
            {
                s_seenError = error.Message;
                return false;
            };

            // Act
            using var mounted = MountFailed();

            // Assert
            Assert.That(s_seenError, Is.EqualTo("offline"), "The function is handed the error the rule would throw");
        }

        [Test]
        public void Given_ACaughtError_When_TheErrorBoundaryAloneResets_Then_TheRemountingReaderDoesNotFetch()
        {
            // Arrange
            s_throwOnError = true;
            using var mounted = MountFailed();

            // Act
            Settle(mounted, () => s_reset!.Invoke());
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1),
                "Until the reset boundary is reset, a query that throws its errors does not retry an entry that failed on mount");
        }

        [Test]
        public void Given_ACaughtError_When_TheResetBoundaryIsResetWithTheErrorBoundary_Then_TheRemountingReaderFetches()
        {
            // Arrange
            s_throwOnError = true;
            s_resetsQueries = true;
            using var mounted = MountFailed();

            // Act
            Settle(mounted, () => s_reset!.Invoke());
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A reset boundary lets the remounting query fetch the failed entry again");
        }

        [Test]
        public void Given_AResetBoundaryAndAFailedEntry_When_ADisabledReaderMounts_Then_ItRendersTheError()
        {
            // Arrange — the reader is disabled, so it mounts over the failure without fetching it.
            s_throwOnError = true;
            s_enabled = false;
            s_client.PrefetchQuery(new QueryOptions<int>(Todos, Fetch)).Forget();
            s_sources[0].TrySetException(new InvalidOperationException("offline"));
            s_resetBoundary.Reset();

            // Act
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("reader:Error"), "A reset boundary keeps a query below it from throwing");
        }

        [Test]
        public void Given_AResetBoundary_When_TheRemountedReaderCommits_Then_TheResetIsCleared()
        {
            // Arrange
            s_throwOnError = true;
            s_resetsQueries = true;
            using var mounted = MountFailed();
            Settle(mounted, () => s_reset!.Invoke());

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_resetBoundary.IsReset, Is.False, "A query's commit clears the reset, as v5's useClearResetErrorBoundary");
        }

        [Test]
        public void Given_AProvidedResetBoundary_When_AReaderAsksForIt_Then_ItReadsThatInstance()
        {
            // Arrange — the Provider above the reader holds s_resetBoundary.

            // Act
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_readBoundary, Is.SameAs(s_resetBoundary), "UseQueryErrorResetBoundary reads the context");
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private static void Settle(MountedTree mounted, Action action)
        {
            action();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private MountedTree MountFailed()
        {
            var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            Settle(mounted, () => s_sources[0].TrySetException(new InvalidOperationException("offline")));
            return mounted;
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
            s_readBoundary = Hooks.UseQueryErrorResetBoundary();
            var result = Hooks.UseQuery(
                new QueryOptions<int>(Todos, Fetch)
                {
                    ThrowOnError = s_throwOnError,
                    ThrowOnErrorFn = s_throwOnErrorFn,
                    Enabled = s_enabled,
                },
                s_client);
            return V.Label(text: "reader:" + result.Status);
        }

        [Component]
        private static VNode Host()
            => V.Provider(QueryErrorResetBoundaryContext.Ref, s_resetBoundary, new VNode?[]
            {
                V.ErrorBoundary(
                    (error, reset) =>
                    {
                        s_reset = reset;
                        return V.Label(text: "failed:" + error.Message);
                    },
                    new VNode?[] { V.Component(Reader, key: "reader") },
                    onReset: _ =>
                    {
                        if (s_resetsQueries) s_resetBoundary.Reset();
                    },
                    key: "boundary"),
            });
    }
}
