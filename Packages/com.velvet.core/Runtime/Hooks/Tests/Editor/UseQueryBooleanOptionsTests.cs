#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="QueryOptions{TQueryFnData, TData}.EnabledFn"/>,
    /// <see cref="QueryOptions{TQueryFnData, TData}.RetryOnMount"/> and its function form, TanStack Query's
    /// <c>enabled</c> as a function and <c>retryOnMount</c>.
    /// <list type="bullet">
    /// <item>An enabled function answering false keeps the mounting query from fetching, in place of
    /// <c>Enabled</c>, and is handed the entry.</item>
    /// <item><c>RetryOnMount</c> false keeps a query mounting over an entry that failed with no data from fetching
    /// it, and its function decides in place of the value.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <see cref="s_calls"/> counts the requests that ran. A failed entry is made by a prefetch whose request
    /// fails, before the reader mounts.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryBooleanOptionsTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static Func<Query, bool>? s_enabledFn;
        private static bool s_retryOnMount;
        private static Func<Query, bool>? s_retryOnMountFn;
        private static QueryKey? s_seenKey;
        private static int s_calls;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0 });
            s_enabledFn = null;
            s_retryOnMount = true;
            s_retryOnMountFn = null;
            s_seenKey = null;
            s_calls = 0;
            s_sources.Clear();
        }

        [Test]
        public void Given_AnEnabledFunctionAnsweringFalse_When_TheQueryMounts_Then_NothingIsFetched()
        {
            // Arrange
            s_enabledFn = _ => false;

            // Act
            MountAndCommit();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "EnabledFn replaces Enabled, which is true here");
        }

        [Test]
        public void Given_AnEnabledFunction_When_TheQueryMounts_Then_ItIsHandedTheEntry()
        {
            // Arrange
            s_enabledFn = info =>
            {
                s_seenKey = info.QueryKey;
                return true;
            };

            // Act
            MountAndCommit();

            // Assert
            Assert.That(s_seenKey, Is.EqualTo(Todos), "A function form is handed the entry its query reads");
        }

        [Test]
        public void Given_RetryOnMountFalse_When_AQueryMountsOverAFailedEntry_Then_ItDoesNotFetchIt()
        {
            // Arrange
            s_retryOnMount = false;
            FailedEntry();

            // Act
            MountAndCommit();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Only the prefetch ran: retryOnMount false leaves the failure as it is");
        }

        [Test]
        public void Given_RetryOnMountTrue_When_AQueryMountsOverAFailedEntry_Then_ItFetchesIt()
        {
            // Arrange
            FailedEntry();

            // Act
            MountAndCommit();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "retryOnMount is true by default, as in v5");
        }

        [Test]
        public void Given_ARetryOnMountFunctionAnsweringFalse_When_AQueryMountsOverAFailedEntry_Then_ItDoesNotFetchIt()
        {
            // Arrange
            s_retryOnMountFn = _ => false;
            FailedEntry();

            // Act
            MountAndCommit();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "RetryOnMountFn replaces RetryOnMount, which is true here");
        }

        private static void FailedEntry()
        {
            s_client.PrefetchQuery(new QueryOptions<int>(Todos, Fetch)).Forget();
            s_sources[0].TrySetException(new InvalidOperationException("offline"));
        }

        private void MountAndCommit()
        {
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
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
        private static VNode Reader()
        {
            Hooks.UseQuery(
                new QueryOptions<int>(Todos, Fetch)
                {
                    EnabledFn = s_enabledFn,
                    RetryOnMount = s_retryOnMount,
                    RetryOnMountFn = s_retryOnMountFn,
                },
                s_client);
            return V.Label(text: "reader");
        }
    }
}
