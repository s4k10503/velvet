#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="QueryClient.FetchQuery{T}"/> decides a retry, TanStack Query's retryer: the client's
    /// <c>retry</c> applies when the options set none, and the wait is asked for before whether to retry, so a
    /// failure that is not retried has still asked for one.
    /// </summary>
    /// <remarks>
    /// Written against members both the change and the tree before it declare, so the merge base builds it.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryClientRetryDecisionTests
    {
        private static readonly QueryKey Todos = new("todos");

        private static int s_calls;
        private static int s_delayCalls;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            s_calls = 0;
            s_delayCalls = 0;
            s_sources.Clear();
        }

        [UnityTest]
        public IEnumerator Given_AClientRetryOfOne_When_APrefetchedRequestFails_Then_ItRunsAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var client = new QueryClient(new QueryClientOptions
            {
                Retry = 1,
                RetryDelay = (_, _) => TimeSpan.Zero,
                GcTime = TimeSpan.MaxValue,
            });
            client.PrefetchQuery(new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken))).Forget();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("offline"));
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2),
                "v5's fetchQuery sets retry: false only where neither the options nor the client's defaults set one");
        });

        [Test]
        public void Given_ARetryOfZero_When_TheRequestFails_Then_TheDelayIsAskedForOnce()
        {
            // Arrange
            var client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue });
            client.PrefetchQuery(new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken))
            {
                Retry = 0,
                RetryDelay = (_, _) =>
                {
                    s_delayCalls++;
                    return TimeSpan.Zero;
                },
            }).Forget();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("offline"));

            // Assert
            Assert.That(s_delayCalls, Is.EqualTo(1), "v5's retryer computes the delay before it decides whether to retry");
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }
    }
}
