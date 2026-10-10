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
    /// Specifies <see cref="QueryOptions{TQueryFnData, TData}.PlaceholderData"/> and
    /// <see cref="QueryPlaceholder.KeepPreviousData{T}"/>, TanStack Query's <c>placeholderData</c> and
    /// <c>keepPreviousData</c>.
    /// <list type="bullet">
    /// <item>A query with nothing cached shows the placeholder as a success flagged
    /// <c>IsPlaceholderData</c>, still fetching, until its own data lands; the placeholder never reaches the
    /// entry, and a failed entry shows its error rather than the placeholder.</item>
    /// <item><c>KeepPreviousData</c> shows the old key's data after a key change, and nothing on a first
    /// load; the function is handed the previous key, and is not asked again while its placeholder shows.</item>
    /// <item>A placeholder is asked for again on a key change once data had landed in between, and a component
    /// listing only <c>IsPlaceholderData</c> re-renders when the entry's data replaces the placeholder.</item>
    /// <item>A placeholder is kept only while the function that made it is the one given, and a function
    /// given in its place is handed the data of the last entry read that had some.</item>
    /// <item>A select applies to the placeholder as to the entry's data, and one that throws on it makes the
    /// result an error that is not flagged as placeholder data, as v5's main does; a select that works on the
    /// placeholder afterwards clears that error.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The query function returns a completion source the case settles itself and retries nothing; the
    /// placeholder function counts its calls in <see cref="s_placeholderCalls"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryPlaceholderDataTests
    {
        private static readonly Func<int, QueryKey?, QueryPlaceholder<int>> Nine = (_, _) =>
        {
            s_placeholderCalls++;
            return 9;
        };

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static Func<int, QueryKey?, QueryPlaceholder<int>>? s_placeholder;
        private static int s_placeholderCalls;
        private static string? s_previousKey;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();
        private static readonly List<QueryResult<string>> s_selected = new();
        private static StateUpdater<int> s_setPage;
        private static StateUpdater<int> s_setTick;
        private static Func<int, string> s_select = data => "#" + data;
        private static int s_flagRenders;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient();
            s_placeholder = Nine;
            s_placeholderCalls = 0;
            s_previousKey = null;
            s_sources.Clear();
            s_renders.Clear();
            s_selected.Clear();
            s_setPage = default;
            s_setTick = default;
            s_select = data => "#" + data;
            s_flagRenders = 0;
        }

        [Test]
        public void Given_APlaceholder_When_NothingIsCached_Then_TheResultShowsItAsASuccess()
        {
            // Act
            using var mounted = Mount();

            // Assert
            Assert.That((Last().Status, Last().Data, Last().IsPlaceholderData, Last().IsFetching),
                Is.EqualTo((QueryStatus.Success, 9, true, true)),
                "The placeholder shows as a success while the query fetches its own data");
        }

        [Test]
        public void Given_APlaceholderShowing_When_TheRequestLands_Then_TheResultShowsTheData()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_sources[0].TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Data, Last().IsPlaceholderData), Is.EqualTo((5, false)), "The entry's own data replaces the placeholder");
        }

        [Test]
        public void Given_APlaceholderShowing_When_GetQueryDataReads_Then_ItIsDefault()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            var cached = s_client.GetQueryData<int>(new QueryKey("page", 1));

            // Assert
            Assert.That((Last().Data, cached), Is.EqualTo((9, 0)), "Placeholder data is never written to the cache");
        }

        [Test]
        public void Given_AFailedEntryWithoutData_When_APlaceholderIsSet_Then_TheResultIsTheError()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("failed"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Status, Last().IsPlaceholderData), Is.EqualTo((QueryStatus.Error, false)),
                "A placeholder stands in only while the query is pending");
        }

        [Test]
        public void Given_APlaceholderShowing_When_TheComponentRendersAgain_Then_TheFunctionIsNotAskedAgain()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_placeholderCalls, Is.EqualTo(1), "The placeholder is kept while the same function made it");
        }

        [Test]
        public void Given_KeepPreviousData_When_TheKeyChanges_Then_TheOldKeysDataShows()
        {
            // Arrange
            s_placeholder = QueryPlaceholder.KeepPreviousData;
            using var mounted = Mount();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();

            // Act
            TurnTo(mounted, 2);

            // Assert
            Assert.That((Last().Data, Last().IsPlaceholderData, Last().IsFetching), Is.EqualTo((1, true, true)),
                "The previous key's data stays on screen while the new key's request is in flight");
        }

        [Test]
        public void Given_KeepPreviousData_When_NothingWasLoadedBefore_Then_TheQueryIsPending()
        {
            // Arrange
            s_placeholder = QueryPlaceholder.KeepPreviousData;

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That((Last().Status, Last().IsPlaceholderData), Is.EqualTo((QueryStatus.Pending, false)),
                "With no previous data there is nothing to keep");
        }

        [Test]
        public void Given_KeepPreviousDataShowingTheOldKey_When_TheNewKeysRequestLands_Then_ItShowsTheNewData()
        {
            // Arrange
            s_placeholder = QueryPlaceholder.KeepPreviousData;
            using var mounted = Mount();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();
            TurnTo(mounted, 2);

            // Act
            s_sources[1].TrySetResult(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Data, Last().IsPlaceholderData), Is.EqualTo((2, false)), "The new key's data replaces the kept data");
        }

        [Test]
        public void Given_APlaceholderFunction_When_TheKeyChanges_Then_ItIsHandedThePreviousKey()
        {
            // Arrange
            s_placeholder = (previous, previousKey) =>
            {
                s_previousKey = previousKey?.ToString();
                return previous;
            };
            using var mounted = Mount();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();

            // Act
            TurnTo(mounted, 2);

            // Assert
            Assert.That(s_previousKey, Is.EqualTo("[page, 1]"), "The function is handed the key of the entry the previous data came from");
        }

        [Test]
        public void Given_APlaceholderAndASelect_When_ThePlaceholderShows_Then_ItIsSelected()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(SelectingPager, key: "pager"));

            // Assert
            Assert.That(s_selected[s_selected.Count - 1].Data, Is.EqualTo("#9"), "The placeholder goes through the select");
        }

        [Test]
        public void Given_DataThatLandedAfterAPlaceholder_When_TheKeyChanges_Then_ThePlaceholderIsAskedForAgain()
        {
            // Arrange
            using var mounted = Mount();
            s_sources[0].TrySetResult(5);
            mounted.FlushStateForTest();

            // Act
            TurnTo(mounted, 2);

            // Assert
            Assert.That((Last().Data, s_placeholderCalls), Is.EqualTo((9, 2)),
                "The placeholder is kept only while the result before still showed it");
        }

        [Test]
        public void Given_AComponentListingOnlyIsPlaceholderData_When_TheRequestLands_Then_ItRerenders()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(FlagReader, key: "flag"));
            mounted.FlushEffectsForTest();
            mounted.FlushStateForTest();
            var rendersBefore = s_flagRenders;

            // Act
            s_sources[0].TrySetResult(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_flagRenders, Is.GreaterThan(rendersBefore),
                "IsPlaceholderData turning false is a change of that property, even when the data it shows is equal");
        }

        [Test]
        public void Given_APlaceholderShowing_When_TheFunctionIsReplaced_Then_TheNewFunctionIsAsked()
        {
            // Arrange
            using var mounted = Mount();
            s_placeholder = (_, _) => 7;

            // Act
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Data, Is.EqualTo(7), "A placeholder is kept only while the same function made it");
        }

        [Test]
        public void Given_KeepPreviousDataShowingTheOldKey_When_TheFunctionIsReplaced_Then_ItIsHandedTheDataOfTheEntryThatHadSome()
        {
            // Arrange
            s_placeholder = QueryPlaceholder.KeepPreviousData;
            using var mounted = Mount();
            s_sources[0].TrySetResult(1);
            mounted.FlushStateForTest();
            TurnTo(mounted, 2);
            s_placeholder = (previous, previousKey) => QueryPlaceholder.KeepPreviousData(previous, previousKey);

            // Act
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Data, Last().IsPlaceholderData), Is.EqualTo((1, true)),
                "The entry moved to has no data, so the placeholder is made from the data of the entry the component left");
        }

        [Test]
        public void Given_APlaceholderWhoseSelectThrew_When_ASelectThatSucceedsReplacesIt_Then_TheResultShowsThePlaceholder()
        {
            // Arrange
            s_select = _ => throw new InvalidOperationException("select-failed");
            using var mounted = V.Mount(_root, V.Component(SelectingPager, key: "pager"));
            mounted.FlushEffectsForTest();
            s_select = data => "#" + data;

            // Act
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_selected[s_selected.Count - 1].Status, s_selected[s_selected.Count - 1].Data),
                Is.EqualTo((QueryStatus.Success, "#9")), "A select that works on the placeholder clears the error a failing one left");
        }

        [Test]
        public void Given_APlaceholderAndASelectThatThrows_When_ThePlaceholderShows_Then_TheResultIsAnError()
        {
            // Arrange
            s_select = _ => throw new InvalidOperationException("select-failed");

            // Act
            using var mounted = V.Mount(_root, V.Component(SelectingPager, key: "pager"));

            // Assert
            Assert.That(s_selected[s_selected.Count - 1].Error?.Message, Is.EqualTo("select-failed"),
                "A select failing on the placeholder is the result's error");
        }

        [Test]
        public void Given_APlaceholderAndASelectThatThrows_When_ThePlaceholderShows_Then_TheResultIsNotFlaggedAsPlaceholderData()
        {
            // Arrange
            s_select = _ => throw new InvalidOperationException("select-failed");

            // Act
            using var mounted = V.Mount(_root, V.Component(SelectingPager, key: "pager"));

            // Assert
            Assert.That(s_selected[s_selected.Count - 1].IsPlaceholderData, Is.False,
                "A select error replaces the placeholder, and v5's main unflags the result with it");
        }

        #region Components and helpers

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Pager, key: "pager"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private static void TurnTo(MountedTree mounted, int page)
        {
            s_setPage.Invoke(page);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Pager()
        {
            var (page, setPage) = Hooks.UseState(1);
            s_setPage = setPage;
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("page", page), Fetch)
                {
                    PlaceholderData = s_placeholder,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "pager");
        }

        [Component]
        private static VNode SelectingPager()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_selected.Add(Hooks.UseQuery(
                new QueryOptions<int, string>(new QueryKey("page", 1), Fetch)
                {
                    PlaceholderData = Nine,
                    Select = s_select,
                    Retry = 0,
                },
                s_client));
            return V.Label(text: "selecting");
        }

        [Component]
        private static VNode FlagReader()
        {
            s_flagRenders++;
            Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("page", 1), Fetch)
                {
                    PlaceholderData = Nine,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.IsPlaceholderData,
                },
                s_client);
            return V.Label(text: "flag");
        }

        #endregion
    }
}
