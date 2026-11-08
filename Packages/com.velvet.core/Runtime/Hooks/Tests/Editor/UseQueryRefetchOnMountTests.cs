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
    /// Specifies <see cref="QueryOptionsBase{TQueryFnData, TData}.RefetchOnMount"/>, its function form and the
    /// client's default, TanStack Query's <c>refetchOnMount</c>.
    /// <list type="bullet">
    /// <item>A reader mounting over stale data does not fetch under <c>Never</c>, and fetches fresh data under
    /// <c>Always</c>; its first render already reports what its subscription will do.</item>
    /// <item>The function decides in place of the value and is handed the entry; the client's setting applies to a
    /// query that sets none.</item>
    /// <item>An entry with no data is fetched on mount whatever the setting, and a key change is not a mount: it
    /// fetches a stale entry under <c>Never</c> too, as v5's <c>shouldFetchOptionally</c> does.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The first reader mounts over "todos", its request lands 7, and the clock moves ten minutes on, past the
    /// one-minute stale time. <see cref="ShowSecond"/> mounts a second reader over the same entry, and
    /// <see cref="s_calls"/> counts the requests that ran.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryRefetchOnMountTests
    {
        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static int s_calls;
        private static TimeSpan s_staleTime;
        private static QueryRefetchMode? s_secondMode;
        private static Func<Query, QueryRefetchMode>? s_secondFn;
        private static object? s_seenData;
        private static StateUpdater<bool> s_setShowSecond;
        private static StateUpdater<string> s_setFirstKey;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_secondRenders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_calls = 0;
            s_staleTime = TimeSpan.FromMinutes(1);
            s_secondMode = null;
            s_secondFn = null;
            s_seenData = null;
            s_setShowSecond = default;
            s_setFirstKey = default;
            s_sources.Clear();
            s_secondRenders.Clear();
            s_client = NewClient(QueryRefetchMode.IfStale);
        }

        [Test]
        public void Given_RefetchOnMountNever_When_ASecondReaderMountsOverStaleData_Then_NothingIsFetched()
        {
            // Arrange
            s_secondMode = QueryRefetchMode.Never;
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Never keeps a mounting reader from fetching data it finds stale");
        }

        [Test]
        public void Given_RefetchOnMountNever_When_ASecondReaderMountsOverStaleData_Then_ItsFirstRenderIsNotFetching()
        {
            // Arrange
            s_secondMode = QueryRefetchMode.Never;
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_secondRenders[0].IsFetching, Is.False,
                "The render before the subscription reports the fetch the subscription will not start");
        }

        [Test]
        public void Given_RefetchOnMountAlways_When_ASecondReaderMountsOverFreshData_Then_ItFetches()
        {
            // Arrange
            s_staleTime = TimeSpan.MaxValue;
            s_secondMode = QueryRefetchMode.Always;
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Always fetches on mount whatever the data's age");
        }

        [Test]
        public void Given_ARefetchOnMountFunctionAnsweringNever_When_ASecondReaderAlsoSettingAlwaysMounts_Then_NothingIsFetched()
        {
            // Arrange
            s_secondMode = QueryRefetchMode.Always;
            s_secondFn = _ => QueryRefetchMode.Never;
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "RefetchOnMountFn replaces RefetchOnMount");
        }

        [Test]
        public void Given_ARefetchOnMountFunction_When_ASecondReaderMounts_Then_ItIsHandedTheEntrysData()
        {
            // Arrange
            s_secondFn = info =>
            {
                s_seenData = info.Data;
                return QueryRefetchMode.Never;
            };
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_seenData, Is.EqualTo(7), "A function form is handed the entry its query reads");
        }

        [Test]
        public void Given_AClientRefetchOnMountOfNever_When_ASecondReaderSettingNoneMounts_Then_NothingIsFetched()
        {
            // Arrange
            s_client = NewClient(QueryRefetchMode.Never);
            using var mounted = MountResolved();

            // Act
            ShowSecond(mounted);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A query that sets no refetchOnMount takes the client's");
        }

        [Test]
        public void Given_RefetchOnMountNever_When_TheFirstReaderMountsOverNoData_Then_ItFetches()
        {
            // Arrange
            s_client = NewClient(QueryRefetchMode.Never);

            // Act
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "An entry with no data is loaded on mount, as v5's shouldLoadOnMount");
        }

        [Test]
        public void Given_RefetchOnMountNever_When_TheKeyMovesToStaleData_Then_ItFetches()
        {
            // Arrange — "done" holds data as old as "todos" does.
            s_client = NewClient(QueryRefetchMode.Never);
            s_client.SetQueryData(new QueryKey("done"), 5);
            using var mounted = MountResolved();

            // Act
            s_setFirstKey.Invoke("done");
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A key change fetches stale data whatever refetchOnMount says");
        }

        private static QueryClient NewClient(QueryRefetchMode refetchOnMount)
            => new(new QueryClientOptions
            {
                RefetchOnMount = refetchOnMount,
                GcTime = TimeSpan.MaxValue,
                Retry = 0,
                Clock = () => s_now,
            });

        private MountedTree MountResolved()
        {
            var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();
            s_now = TimeSpan.FromMinutes(10);
            return mounted;
        }

        private static void ShowSecond(MountedTree mounted)
        {
            s_setShowSecond.Invoke(true);
            mounted.FlushStateForTest();
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
        private static VNode FirstReader(string key)
        {
            Hooks.UseQuery(new QueryOptions<int>(new QueryKey(key), (QueryFunctionContext context) => Fetch(context.CancellationToken)) { StaleTime = s_staleTime }, s_client);
            return V.Label(text: "first");
        }

        [Component]
        private static VNode SecondReader()
        {
            s_secondRenders.Add(Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("todos"), (QueryFunctionContext context) => Fetch(context.CancellationToken))
                {
                    StaleTime = s_staleTime,
                    RefetchOnMount = s_secondMode,
                    RefetchOnMountFn = s_secondFn,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "second");
        }

        [Component]
        private static VNode Host()
        {
            var (showSecond, setShowSecond) = Hooks.UseState(false);
            var (firstKey, setFirstKey) = Hooks.UseState("todos");
            s_setShowSecond = setShowSecond;
            s_setFirstKey = setFirstKey;
            return V.Div(children: new VNode?[]
            {
                V.Component(FirstReader, firstKey, key: "first"),
                showSecond ? V.Component(SecondReader, key: "second") : null,
            });
        }
    }
}
