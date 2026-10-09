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
    /// Specifies <see cref="QueryOptions{TQueryFnData, TData}.Enabled"/>, TanStack Query's <c>enabled</c>.
    /// <list type="bullet">
    /// <item>A disabled query fetches nothing when it mounts and reports itself pending and idle with nothing
    /// cached, and not stale over stale data.</item>
    /// <item>Invalidating its key fetches nothing for it, and an entry read by a disabled and an enabled query is
    /// fetched with the options it was last handed; <c>Refetch</c> still fetches.</item>
    /// <item>Turning it on fetches stale data, joining a refetch in flight, and the render that turns it on
    /// already reports the fetch, while fresh data stays as it is.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The query function records which reader's function ran in <see cref="s_fetched"/> and hands back a
    /// completion source the case settles itself. The client's clock is <see cref="s_now"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryEnabledTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static TimeSpan? s_staleTime;
        private static readonly List<string> s_fetched = new();
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<CancellationToken> s_tokens = new();
        private static readonly List<QueryResult<int>> s_renders = new();
        private static StateUpdater<bool> s_setEnabled;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_staleTime = null;
            s_client = new QueryClient(new QueryClientOptions { Clock = () => s_now });
            s_fetched.Clear();
            s_sources.Clear();
            s_tokens.Clear();
            s_renders.Clear();
            s_setEnabled = default;
        }

        [Test]
        public void Given_ADisabledQuery_When_ItMounts_Then_NothingIsFetched()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_fetched, Is.Empty, "A disabled query does not fetch on mount");
        }

        [Test]
        public void Given_ADisabledQueryWithNothingCached_When_ItFirstRenders_Then_ItIsPendingAndNotFetching()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));

            // Assert
            Assert.That((s_renders[0].Status, s_renders[0].IsFetching), Is.EqualTo((QueryStatus.Pending, false)),
                "A render whose subscription will not fetch does not report a fetch");
        }

        [Test]
        public void Given_ADisabledQueryOverStaleData_When_ItRenders_Then_ItIsNotStale()
        {
            // Arrange
            s_client.SetQueryData(Todos, 3);
            s_now = TimeSpan.FromMinutes(1);

            // Act
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That((Last().Data, Last().IsStale), Is.EqualTo((3, false)),
                "v5's isStale is false for a disabled query, whatever the age of its data");
        }

        [Test]
        public void Given_ADisabledQuery_When_ItsKeyIsInvalidated_Then_NothingIsFetched()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();

            // Act
            s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(s_fetched, Is.Empty, "Invalidation refetches active queries, and a disabled one is not active");
        }

        [Test]
        public void Given_AnEntryReadByADisabledThenAnEnabledQuery_When_ItIsInvalidated_Then_TheEnabledQuerysFunctionRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DisabledThenEnabled, key: "pair"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();
            s_fetched.Clear();

            // Act
            s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("enabled"),
                "An enabled reader makes the entry active, and the options the entry was last handed are the enabled query's");
        }

        [Test]
        public void Given_ADisabledQuery_When_RefetchIsCalled_Then_ItFetches()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();

            // Act
            Last().Refetch();

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("toggled"), "refetch() fetches whether or not the query is enabled");
        }

        [Test]
        public void Given_ADisabledQueryWithNothingCached_When_ItIsTurnedOn_Then_ItFetches()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();

            // Act
            TurnOn(mounted);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("toggled"), "A query turned on over no data fetches");
        }

        [Test]
        public void Given_ADisabledQuery_When_TheRenderTurningItOnReads_Then_ItReportsTheFetch()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();

            // Act
            s_setEnabled.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_fetched.Count, Last().IsFetching), Is.EqualTo((0, true)),
                "The render before the commit's effect already reports the fetch that effect will start");
        }

        [Test]
        public void Given_ADisabledQueryOverFreshData_When_ItIsTurnedOn_Then_NothingIsFetched()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            s_client.SetQueryData(Todos, 3);
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();

            // Act
            TurnOn(mounted);

            // Assert
            Assert.That(s_fetched, Is.Empty, "A query turned on fetches only data that is stale");
        }

        [Test]
        public void Given_ARefetchInFlightOverData_When_ADisabledQueryIsTurnedOn_Then_ItJoinsTheRefetch()
        {
            // Arrange
            s_client.SetQueryData(Todos, 3);
            using var mounted = V.Mount(_root, V.Component(Toggled, key: "toggled"));
            mounted.FlushEffectsForTest();
            Last().Refetch();

            // Act
            TurnOn(mounted);

            // Assert
            Assert.That((s_fetched.Count, s_tokens[0].IsCancellationRequested), Is.EqualTo((1, false)),
                "Turning a query on joins a request in flight, as v5's executeFetch passes no cancelRefetch");
        }

        #region Components and helpers

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private static void TurnOn(MountedTree mounted)
        {
            s_setEnabled.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static QueryOptions<int> Options(string reader, bool enabled)
            => new(Todos, token =>
            {
                s_fetched.Add(reader);
                s_tokens.Add(token);
                var source = new VelvetTaskCompletionSource<int>();
                s_sources.Add(source);
                return source.Task;
            })
            {
                Enabled = enabled,
                StaleTime = s_staleTime,
                Retry = 0,
                NotifyOnChangeProps = QueryProperties.All,
            };

        [Component]
        private static VNode Toggled()
        {
            var (enabled, setEnabled) = Hooks.UseState(false);
            s_setEnabled = setEnabled;
            s_renders.Add(Hooks.UseQuery(Options("toggled", enabled), s_client));
            return V.Label(text: "toggled");
        }

        [Component]
        private static VNode DisabledReader()
        {
            Hooks.UseQuery(Options("disabled", enabled: false), s_client);
            return V.Label(text: "disabled");
        }

        [Component]
        private static VNode EnabledReader()
        {
            Hooks.UseQuery(Options("enabled", enabled: true), s_client);
            return V.Label(text: "enabled");
        }

        [Component]
        private static VNode DisabledThenEnabled()
            => V.Div(children: new VNode[]
            {
                V.Component(DisabledReader, key: "disabled"),
                V.Component(EnabledReader, key: "enabled"),
            });

        #endregion
    }
}
