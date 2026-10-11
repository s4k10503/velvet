#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="QueryOptions{TQueryFnData, TData}.Select"/>, TanStack Query's <c>select</c>.
    /// <list type="bullet">
    /// <item>The result holds what the select makes of the entry's data, while the entry keeps the data the
    /// query function returned.</item>
    /// <item>The select runs again only when the entry's data or the select's instance changes, and what it
    /// returns keeps the instance the result held where the two are deeply equal.</item>
    /// <item>A component listing only <c>Data</c> re-renders when a selection replaces no data, and not when a
    /// select that had worked throws, which leaves the last selection in place beside the error; a select
    /// that works again clears the error.</item>
    /// <item>A select that throws makes the result an error carrying its exception, until a key change to an
    /// uncached entry leaves the result pending, and again when the key comes back, whether the selection is a
    /// reference or a value type; options whose two types differ need a select.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The query function returns a completion source the case settles itself and retries nothing; the select
    /// counts its runs in <see cref="s_selects"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQuerySelectTests
    {
        private static readonly QueryKey Todos = new("todos");
        private static readonly Func<int[], List<int>> Failing = _ => throw new InvalidOperationException("select-failed");
        private static readonly Func<int[], int> FailingCount = _ => throw new InvalidOperationException("select-failed");
        private static readonly Func<int[], List<int>> NotEmpty = data =>
            data.Length == 0 ? throw new InvalidOperationException("select-failed") : data.ToList();
        private static readonly Func<int[], List<int>> AtMostTwo = data =>
            data.Length > 2 ? throw new InvalidOperationException("select-failed") : data.ToList();
        private static readonly Func<int[], List<int>> AboveOne = data =>
        {
            s_selects++;
            return data.Where(item => item > 1).ToList();
        };

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static int s_selects;
        private static bool s_freshSelectEachRender;
        private static readonly List<VelvetTaskCompletionSource<int[]>> s_sources = new();
        private static readonly List<QueryResult<List<int>>> s_renders = new();
        private static readonly List<QueryResult<int>> s_counts = new();
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setPage;
        private static int s_watchRenders;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient();
            s_selects = 0;
            s_freshSelectEachRender = false;
            s_sources.Clear();
            s_renders.Clear();
            s_counts.Clear();
            s_setTick = default;
            s_setPage = default;
            s_watchRenders = 0;
        }

        [Test]
        public void Given_ASelect_When_TheRequestLands_Then_TheResultHoldsTheSelection()
        {
            // Arrange
            using var mounted = Mount(Selecting);

            // Act
            Land(mounted, 1, 2, 3);

            // Assert
            Assert.That(string.Join(",", Last().Data!), Is.EqualTo("2,3"), "The result's data is what the select made of the entry's");
        }

        [Test]
        public void Given_ASelectChangingTheType_When_TheRequestLands_Then_TheResultHoldsTheSelection()
        {
            // Arrange
            using var mounted = Mount(Counting);

            // Act
            Land(mounted, 1, 2, 3);

            // Assert
            Assert.That(s_counts[s_counts.Count - 1].Data, Is.EqualTo(3), "A select can turn the data into another type");
        }

        [Test]
        public void Given_ASelect_When_GetQueryDataReads_Then_ItIsTheDataTheQueryFunctionReturned()
        {
            // Arrange
            using var mounted = Mount(Selecting);

            // Act
            Land(mounted, 1, 2, 3);

            // Assert
            Assert.That(string.Join(",", s_client.GetQueryData<int[]>(Todos)!), Is.EqualTo("1,2,3"),
                "The entry keeps what the query function returned; only the result is selected");
        }

        [Test]
        public void Given_AStableSelect_When_TheComponentRendersAgain_Then_ItDoesNotRunAgain()
        {
            // Arrange
            using var mounted = Mount(Selecting);
            Land(mounted, 1, 2, 3);
            var runs = s_selects;

            // Act
            Rerender(mounted);

            // Assert
            Assert.That((runs, s_selects), Is.EqualTo((1, 1)), "The select runs again only for new data or a new select");
        }

        [Test]
        public void Given_ASelectBuiltEachRender_When_TheComponentRendersAgain_Then_ItRunsAgain()
        {
            // Arrange
            s_freshSelectEachRender = true;
            using var mounted = Mount(Selecting);
            Land(mounted, 1, 2, 3);
            var runs = s_selects;

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_selects, Is.EqualTo(runs + 1), "A select is remembered by instance, as v5 compares it with ===");
        }

        [Test]
        public void Given_ASelectionEqualToTheLast_When_NewDataLands_Then_TheResultKeepsTheSelectedInstance()
        {
            // Arrange
            using var mounted = Mount(Selecting);
            Land(mounted, 1, 2, 3);
            var selected = Last().Data;
            Last().Refetch();

            // Act
            s_sources[1].TrySetResult(new[] { 0, 2, 3 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_selects, ReferenceEquals(Last().Data, selected)), Is.EqualTo((2, true)),
                "What the select returns is shared structurally with the result's data, as v5's replaceData does");
        }

        [Test]
        public void Given_ASelectThatThrows_When_TheRequestLands_Then_TheResultIsAnErrorCarryingItsException()
        {
            // Arrange
            using var mounted = Mount(Throwing);

            // Act
            Land(mounted, 1);

            // Assert
            Assert.That((Last().Status, Last().Error?.Message), Is.EqualTo((QueryStatus.Error, "select-failed")),
                "A failing select is the result's error");
        }

        [Test]
        public void Given_AComponentListingOnlyData_When_TheRequestLandsAndTheSelectMakesTheData_Then_ItRerenders()
        {
            // Arrange
            using var mounted = Mount(WatchingSelection);
            mounted.FlushStateForTest();
            var rendersBefore = s_watchRenders;

            // Act
            Land(mounted, 1, 2, 3);

            // Assert
            Assert.That(s_watchRenders, Is.GreaterThan(rendersBefore), "The selection replacing no data is a change of data");
        }

        [Test]
        public void Given_ASelectThatThrewOnOneData_When_NewDataLandsThatItSelects_Then_TheResultIsNoLongerAnError()
        {
            // Arrange
            using var mounted = Mount(Recovering);
            Land(mounted);
            Last().Refetch();

            // Act
            s_sources[1].TrySetResult(new[] { 5 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Status, Is.EqualTo(QueryStatus.Success), "A select that works clears the error an earlier run of it left");
        }

        [Test]
        public void Given_AComponentListingOnlyData_When_ASelectThatSucceededThrowsOnNewData_Then_ItDoesNotRerender()
        {
            // Arrange
            using var mounted = Mount(WatchingBreakingSelection);
            Land(mounted, 1, 2);
            Rerender(mounted);
            var rendersBefore = s_watchRenders;
            Last().Refetch();

            // Act
            s_sources[1].TrySetResult(new[] { 1, 2, 3 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_watchRenders, Is.EqualTo(rendersBefore),
                "The result keeps the last selection beside the error, so its data has not changed");
        }

        [Test]
        public void Given_ASelectThatThrewOverOneKey_When_TheKeyChangesToAnUncachedOne_Then_TheResultIsPending()
        {
            // Arrange
            using var mounted = Mount(ThrowingPager);
            Land(mounted, 1);

            // Act
            s_setPage.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Status, Is.EqualTo(QueryStatus.Pending),
                "A select error goes with the data it ran on, as v5 clears it once the data is undefined");
        }

        [Test]
        public void Given_AStableSelectThatThrewOverOneKey_When_TheKeyMovesToAnUncachedOneAndBack_Then_TheResultIsAnErrorAgain()
        {
            // Arrange
            using var mounted = Mount(ThrowingPager);
            Land(mounted, 1);
            s_setPage.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setPage.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Status, Last().Error?.Message), Is.EqualTo((QueryStatus.Error, "select-failed")),
                "The memo goes with the cleared error, so the data that comes back is selected and fails again");
        }

        [Test]
        public void Given_AStableValueTypeSelectThatThrewOverOneKey_When_TheKeyMovesToAnUncachedOneAndBack_Then_TheResultIsAnErrorAgain()
        {
            // Arrange
            using var mounted = Mount(CountingPager);
            Land(mounted, 1);
            s_setPage.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setPage.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_counts[s_counts.Count - 1].Status, Is.EqualTo(QueryStatus.Error),
                "A value-type selection fails again too, rather than reading as a success holding default");
        }

        [Test]
        public void Given_OptionsOfTwoTypesWithoutASelect_When_AQueryRenders_Then_ItThrowsNamingSelect()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("needs a Select"));

            // Act
            using var mounted = V.Mount(_root, V.Component(Unselected, key: "unselected"));

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        #region Components and helpers

        private static QueryResult<List<int>> Last() => s_renders[s_renders.Count - 1];

        private MountedTree Mount(Func<VNode> component)
        {
            var mounted = V.Mount(_root, V.Component(component, key: "reader"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private static void Land(MountedTree mounted, params int[] data)
        {
            s_sources[0].TrySetResult(data);
            mounted.FlushStateForTest();
        }

        private static void Rerender(MountedTree mounted)
        {
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static VelvetTask<int[]> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int[]>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Selecting()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            // A copy of the field's delegate is a new instance, as a lambda capturing a render's values would be.
            var select = s_freshSelectEachRender ? (Func<int[], List<int>>)AboveOne.Invoke : AboveOne;
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int[], List<int>>(Todos, Fetch)
                {
                    Select = select,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "selecting");
        }

        [Component]
        private static VNode WatchingSelection()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_watchRenders++;
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int[], List<int>>(Todos, Fetch)
                {
                    Select = AboveOne,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.Data,
                },
                s_client));
            return V.Label(text: "watching");
        }

        [Component]
        private static VNode WatchingBreakingSelection()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_watchRenders++;
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int[], List<int>>(Todos, Fetch)
                {
                    Select = AtMostTwo,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.Data,
                },
                s_client));
            return V.Label(text: "watching");
        }

        [Component]
        private static VNode Recovering()
        {
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int[], List<int>>(Todos, Fetch)
                {
                    Select = NotEmpty,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "recovering");
        }

        [Component]
        private static VNode Counting()
        {
            s_counts.Add(Hooks.UseQuery(
                new QueryOptions<int[], int>(Todos, Fetch) { Select = data => data.Length, Retry = 0 },
                s_client));
            return V.Label(text: "counting");
        }

        [Component]
        private static VNode Throwing()
        {
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int[], List<int>>(Todos, Fetch)
                {
                    Select = _ => throw new InvalidOperationException("select-failed"),
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "throwing");
        }

        [Component]
        private static VNode ThrowingPager()
        {
            var (page, setPage) = Hooks.UseState(1);
            s_setPage = setPage;
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int[], List<int>>(new QueryKey("todos", page), Fetch)
                {
                    Select = Failing,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "throwing");
        }

        [Component]
        private static VNode CountingPager()
        {
            var (page, setPage) = Hooks.UseState(1);
            s_setPage = setPage;
            s_counts.Add(Hooks.UseQuery(
                new QueryOptions<int[], int>(new QueryKey("todos", page), Fetch)
                {
                    Select = FailingCount,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "counting");
        }

        [Component]
        private static VNode Unselected()
        {
            Hooks.UseQuery(new QueryOptions<int[], int>(Todos, Fetch), s_client);
            return V.Label(text: "unselected");
        }

        #endregion
    }
}
