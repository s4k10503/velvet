#nullable enable
using System;
using System.Collections.Generic;
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
    /// <item>A reader moving to another key leaves the entry the options it handed it, not its new key's, and
    /// one moving away whose options the entry no longer holds leaves the entry's as they are.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Reader A mounts first and its request lands; reader B mounts after it, under a root of its own, over the
    /// fresh entry, so B starts no request, and what was fetched so far is forgotten. Each query function
    /// records its reader and page in <see cref="s_fetched"/>, as <c>a1</c> or <c>b2</c>, and hands back a
    /// completion source the case settles itself.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryInvalidateOptionsTests
    {
        private VisualElement _root = null!;
        private VisualElement _rootB = null!;
        private static QueryClient s_client = null!;
        private static readonly List<string> s_fetched = new();
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_rendersA = new();
        private static StateUpdater<int> s_setPageA;
        private static StateUpdater<int> s_setPageB;
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
            s_setPageA = default;
            s_setPageB = default;
            s_setTickA = default;
        }

        [Test]
        public void Given_TwoReadersWithDifferentFunctions_When_TheEntryIsInvalidated_Then_TheLaterReadersFunctionRuns()
        {
            // Arrange
            using var mounted = MountA();
            using var other = MountB();

            // Act
            s_client.InvalidateQueries(Page(1));

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("b1"), "Subscribing hands the entry the reader's options");
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
            s_client.InvalidateQueries(Page(1));

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("a1"), "A request a reader starts hands the entry its options");
        }

        // GREEN_ON_BASE(characterization): the base refetches with the first reader's function, which is A's here too; this case pins that A's commit hands its options back over B's.
        [Test]
        public void Given_TheEarlierReaderCommittingAfterTheLaterSubscribed_When_TheEntryIsInvalidated_Then_ItsFunctionRuns()
        {
            // Arrange
            using var mounted = MountA();
            using var other = MountB();
            s_setTickA.Invoke(tick => tick + 1);
            Flush(mounted);

            // Act
            s_client.InvalidateQueries(Page(1));

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("a1"), "Each commit hands the entry the reader's options");
        }

        [Test]
        public void Given_TheLaterReaderMovingToAnotherKey_When_TheKeyItLeftIsInvalidated_Then_ItsFunctionForThatKeyRuns()
        {
            // Arrange
            using var mounted = MountA();
            using var other = MountB();
            s_setPageB.Invoke(2);
            Flush(other);
            s_fetched.Clear();

            // Act
            s_client.InvalidateQueries(Page(1));

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("b1"),
                "The entry keeps a copy of what it was handed, so the reader's next key's function does not run for it");
        }

        // GREEN_ON_BASE(characterization): after A leaves, the base's first reader is B, whose function is the expected one; this case pins that A leaving does not replace the options B handed the entry.
        [Test]
        public void Given_TheEarlierReaderMovingToAnotherKey_When_TheKeyItLeftIsInvalidated_Then_TheLaterReadersFunctionRuns()
        {
            // Arrange
            using var mounted = MountA();
            using var other = MountB();
            s_setPageA.Invoke(2);
            Flush(mounted);
            s_fetched.Clear();

            // Act
            s_client.InvalidateQueries(Page(1));

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("b1"),
                "A reader leaving replaces only the options it handed itself");
        }

        #region Components and helpers

        private static QueryKey Page(int page) => new("todos", page);

        private MountedTree MountA()
        {
            var mounted = V.Mount(_root, V.Component(ReaderA, key: "a"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();
            return mounted;
        }

        private MountedTree MountB()
        {
            var mounted = V.Mount(_rootB, V.Component(ReaderB, key: "b"));
            mounted.FlushEffectsForTest();
            s_fetched.Clear();
            return mounted;
        }

        private static void Flush(MountedTree mounted)
        {
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static QueryOptions<int> Options(string reader, int page)
            => new(Page(page), _ =>
            {
                s_fetched.Add(reader + page);
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
            var (page, setPage) = Hooks.UseState(1);
            var (_, setTick) = Hooks.UseState(0);
            s_setPageA = setPage;
            s_setTickA = setTick;
            s_rendersA.Add(Hooks.UseQuery(Options("a", page), s_client));
            return V.Label(text: "a");
        }

        [Component]
        private static VNode ReaderB()
        {
            var (page, setPage) = Hooks.UseState(1);
            s_setPageB = setPage;
            Hooks.UseQuery(Options("b", page), s_client);
            return V.Label(text: "b");
        }

        #endregion
    }
}
