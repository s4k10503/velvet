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
    /// Specifies which query function <see cref="QueryClient.InvalidateQueries"/> refetches an entry with when
    /// its readers' functions differ: the options the entry was last handed, TanStack Query's
    /// <c>query.options</c>, which v5's <c>refetchQueries</c> runs the query with.
    /// <list type="bullet">
    /// <item>A reader subscribing hands the entry its options, so the later of two readers' function runs.</item>
    /// <item>A reader starting a request, or committing again, hands them over again, so the earlier reader's
    /// function runs after either.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Reader A is mounted first and its request lands; reader B mounts after it, under a root of its own,
    /// over the fresh entry, so B starts no request. Each query function records its reader in <see cref="s_fetched"/> and hands back a
    /// completion source the case settles itself.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryInvalidateOptionsTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private VisualElement _rootB = null!;
        private static QueryClient s_client = null!;
        private static readonly List<string> s_fetched = new();
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_rendersA = new();
        private static StateUpdater<int> s_setTickA;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _rootB = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { Retry = 0 });
            s_fetched.Clear();
            s_sources.Clear();
            s_rendersA.Clear();
            s_setTickA = default;
        }

        [Test]
        public void Given_TwoReadersWithDifferentFunctions_When_TheEntryIsInvalidated_Then_TheLaterReadersFunctionRuns()
        {
            // Arrange
            using var mounted = MountA();
            using var other = MountB();

            // Act
            s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("b"), "Subscribing hands the entry the reader's options");
        }

        // GREEN_ON_BASE(characterization): the base refetches with the first reader's function, which is A's here too; this case pins that A's request hands its options back over B's.
        [Test]
        public void Given_TheEarlierReaderRefetchingAfterTheLaterSubscribed_When_TheEntryIsInvalidated_Then_ItsFunctionRuns()
        {
            // Arrange
            using var mounted = MountA();
            using var other = MountB();
            s_rendersA[s_rendersA.Count - 1].Refetch();
            s_fetched.Clear();

            // Act
            s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("a"), "A request a reader starts hands the entry its options");
        }

        // GREEN_ON_BASE(characterization): the base refetches with the first reader's function, which is A's here too; this case pins that A's commit hands its options back over B's.
        [Test]
        public void Given_TheEarlierReaderCommittingAfterTheLaterSubscribed_When_TheEntryIsInvalidated_Then_ItsFunctionRuns()
        {
            // Arrange
            using var mounted = MountA();
            using var other = MountB();
            s_setTickA.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
            s_fetched.Clear();

            // Act
            s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("a"), "Each commit hands the entry the reader's options");
        }

        #region Components and helpers

        // Mounts A and lands its request.
        private MountedTree MountA()
        {
            var mounted = V.Mount(_root, V.Component(ReaderA, key: "a"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();
            return mounted;
        }

        // Mounts B under a root of its own, after A, over the fresh entry, and forgets what was fetched so far.
        private MountedTree MountB()
        {
            var mounted = V.Mount(_rootB, V.Component(ReaderB, key: "b"));
            mounted.FlushEffectsForTest();
            s_fetched.Clear();
            return mounted;
        }

        private static QueryOptions<int> Options(string reader)
            => new(Todos, _ =>
            {
                s_fetched.Add(reader);
                var source = new VelvetTaskCompletionSource<int>();
                s_sources.Add(source);
                return source.Task;
            })
            {
                StaleTime = TimeSpan.FromMinutes(1),
            };

        [Component]
        private static VNode ReaderA()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTickA = setTick;
            s_rendersA.Add(Hooks.UseQuery(Options("a"), s_client));
            return V.Label(text: "a");
        }

        [Component]
        private static VNode ReaderB()
        {
            Hooks.UseQuery(Options("b"), s_client);
            return V.Label(text: "b");
        }

        #endregion
    }
}
