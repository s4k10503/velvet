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
    /// Specifies <see cref="QueryResult{T}.IsRefetching"/> and <see cref="QueryResult{T}.IsRefetchError"/>, TanStack
    /// Query's <c>isRefetching</c> and <c>isRefetchError</c>: a request running over a result that is not pending, and
    /// an error over an entry that holds data.
    /// </summary>
    /// <remarks>
    /// The query function hands back a completion source the case settles itself; nothing retries.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryRefetchFlagsTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => TimeSpan.Zero });
            s_sources.Clear();
            s_renders.Clear();
            s_selected.Clear();
        }

        [Test]
        public void Given_AFirstRequestInFlight_When_TheComponentRenders_Then_ItIsNotRefetching()
        {
            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(Last().IsRefetching, Is.False, "v5's isRefetching is false while the result is pending");
        }

        [Test]
        public void Given_DataAndARefetchInFlight_When_TheComponentRenders_Then_ItIsRefetching()
        {
            // Arrange
            using var mounted = MountResolved();

            // Act
            Last().Refetch();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsRefetching, Is.True);
        }

        [Test]
        public void Given_DataAndARefetchThatFails_When_TheComponentRenders_Then_ItIsARefetchError()
        {
            // Arrange
            using var mounted = MountResolved();
            Last().Refetch();

            // Act
            s_sources[s_sources.Count - 1].TrySetException(new InvalidOperationException("offline"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsRefetchError, Is.True);
        }

        [Test]
        public void Given_NoData_When_TheFirstRequestFails_Then_ItIsNotARefetchError()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("offline"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsRefetchError, Is.False, "v5's isRefetchError needs data to have landed before");
        }

        [Test]
        public void Given_ASelectThatThrows_When_TheDataLands_Then_ItIsNotARefetchError()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(FailingSelectReader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_selected[s_selected.Count - 1].IsRefetchError, Is.False,
                "v5 reads the result's data, which a select that threw leaves without");
        }

        private static readonly List<QueryResult<string>> s_selected = new();

        [Component]
        private static VNode FailingSelectReader()
        {
            s_selected.Add(Hooks.UseQuery(
                new QueryOptions<int, string>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken))
                {
                    Select = _ => throw new InvalidOperationException("select"),
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "reader");
        }

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            mounted.FlushStateForTest();
            return mounted;
        }

        private MountedTree MountResolved()
        {
            var mounted = Mount();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken)) { StaleTime = TimeSpan.MaxValue, NotifyOnChangeProps = QueryProperties.All },
                s_client));
            return V.Label(text: "reader");
        }
    }
}
