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
    /// Specifies <see cref="QueryOptions{TQueryFnData, TData}.Retry"/> and <see cref="QueryClientOptions.Retry"/>
    /// given as a <see cref="RetryRule"/> other than a number: TanStack Query's <c>retry</c> as <c>true</c>,
    /// <c>false</c> or <c>(failureCount, error) =&gt; boolean</c>.
    /// <list type="bullet">
    /// <item><c>true</c> retries past the default three.</item>
    /// <item>A function decides each retry, handed the failures before this one and this failure's exception,
    /// and one that throws fails the request with its own exception.</item>
    /// <item><c>false</c> on the client retries nothing.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Every retry waits a delay of zero, which lasts a frame, so <see cref="Pass"/> lets one run. The query
    /// function hands back a completion source the case fails itself, and <see cref="s_calls"/> counts the
    /// requests that ran.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryRetryRuleTests
    {
        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static RetryRule? s_retry;
        private static int s_calls;
        private static readonly List<string> s_decisions = new();
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_retry = null;
            s_calls = 0;
            s_decisions.Clear();
            s_sources.Clear();
            s_renders.Clear();
            s_client = NewClient(retry: null);
        }

        [UnityTest]
        public IEnumerator Given_RetryTrue_When_FiveRequestsFail_Then_ASixthRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retry = true;
            using var mounted = Mount();

            // Act
            for (var call = 0; call < 5; call++)
            {
                Fail(call);
                await Pass();
            }

            // Assert
            Assert.That(s_calls, Is.EqualTo(6), "true retries without limit, past the default of three");
        });

        [UnityTest]
        public IEnumerator Given_ARetryFunctionAllowingOneRetry_When_TwoRequestsFail_Then_NoThirdRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retry = RetryRule.When((failureCount, _) => failureCount < 1);
            using var mounted = Mount();

            // Act
            Fail(0);
            await Pass();
            Fail(1);
            await Pass();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The function decides each retry in place of the default count");
        });

        [UnityTest]
        public IEnumerator Given_ARetryFunction_When_TwoRequestsFail_Then_ItIsHandedTheFailuresBeforeEachAndItsException()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retry = RetryRule.When((failureCount, error) =>
            {
                s_decisions.Add(failureCount + ":" + error.Message);
                return failureCount < 1;
            });
            using var mounted = Mount();

            // Act
            Fail(0);
            await Pass();
            Fail(1);
            await Pass();

            // Assert
            Assert.That(string.Join(",", s_decisions), Is.EqualTo("0:fail-1,1:fail-2"),
                "v5's retry function is handed failureCount before it is counted up, and the error");
        });

        [Test]
        public void Given_ARetryFunctionThatThrows_When_TheRequestFails_Then_TheQueryReportsTheFunctionsException()
        {
            // Arrange
            s_retry = RetryRule.When((_, _) => throw new InvalidOperationException("retry-failed"));
            using var mounted = Mount();

            // Act
            Fail(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Error?.Message, Is.EqualTo("retry-failed"),
                "A retry function that fails ends the request with its failure, as a throwing RetryDelay does");
        }

        [UnityTest]
        public IEnumerator Given_AClientRetryOfFalse_When_TheRequestFails_Then_NoSecondRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client = NewClient(retry: false);
            using var mounted = Mount();

            // Act
            Fail(0);
            await Pass();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "false on the client retries nothing for a query that sets no retry");
        });

        private static QueryClient NewClient(RetryRule? retry)
            => new(new QueryClientOptions
            {
                Retry = retry,
                RetryDelay = (_, _) => TimeSpan.Zero,
                GcTime = TimeSpan.MaxValue,
            });

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private static void Fail(int call)
            => s_sources[call].TrySetException(new InvalidOperationException("fail-" + (call + 1)));

        private static async VelvetTask Pass()
        {
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
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
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("todos"), Fetch)
                {
                    Retry = s_retry,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "reader");
        }
    }
}
