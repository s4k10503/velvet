#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which changes of an entry re-render a component reading it, TanStack Query's tracked result
    /// properties and <c>notifyOnChangeProps</c>.
    /// <list type="bullet">
    /// <item>A component that reads one property of the result re-renders when that property changes and not
    /// when only another does, for each of the ten properties, across a first request that lands, a first
    /// request that fails, a refetch that starts and a refetch that fails.</item>
    /// <item>A component that has read nothing re-renders at every change, and so does one whose options list
    /// <c>QueryProperties.All</c>; a listed set replaces what the component read, and an empty one silences
    /// it.</item>
    /// <item>A refetch that lands the value the entry held changes none of the data a component reads, and
    /// clearing the client re-renders a component whatever it read.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The component reads one property chosen by <see cref="s_property"/> and counts its renders in
    /// <see cref="s_renderCount"/>; the query is stale after a minute, so a landed result is fresh for the whole
    /// case, and retries nothing.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryTrackedPropsTests
    {
        private static readonly string[] Properties =
        {
            "Status", "IsPending", "IsSuccess", "IsError", "Data", "Error", "IsFetching", "IsStale", "FailureCount",
            "FailureReason",
        };

        // For each way an entry changes, the properties whose value that change moves.
        private static readonly Dictionary<string, string[]> Moved = new()
        {
            ["FirstRequestLands"] = new[] { "Status", "IsPending", "IsSuccess", "Data", "IsFetching", "IsStale" },
            ["FirstRequestFails"] = new[]
            {
                "Status", "IsPending", "IsError", "Error", "IsFetching", "FailureCount", "FailureReason",
            },
            ["RefetchStarts"] = new[] { "IsFetching" },
            ["RefetchFails"] = new[]
            {
                "Status", "IsSuccess", "IsError", "Error", "IsFetching", "IsStale", "FailureCount", "FailureReason",
            },
        };

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static string s_property = "";
        private static QueryProperties? s_notify;
        private static int s_renderCount;
        private static QueryResult<int> s_result = null!;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient();
            s_property = "";
            s_notify = null;
            s_renderCount = 0;
            s_sources.Clear();
        }

        private static IEnumerable<TestCaseData> EveryPropertyForEveryChange()
            => Moved.SelectMany(change => Properties.Select(property =>
                new TestCaseData(change.Key, property, change.Value.Contains(property))));

        [TestCaseSource(nameof(EveryPropertyForEveryChange))]
        public void Given_AComponentReadingOneProperty_When_TheEntryChanges_Then_ItRerendersOnlyIfThatPropertyMoved(
            string change, string property, bool rerenders)
        {
            // Arrange
            s_property = property;
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            if (change is "RefetchStarts" or "RefetchFails")
            {
                s_sources[0].TrySetResult(7);
                mounted.FlushStateForTest();
            }

            if (change == "RefetchFails")
            {
                s_result.Refetch();
                mounted.FlushStateForTest();
            }

            var rendersBefore = s_renderCount;

            // Act
            Change(change);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount > rendersBefore, Is.EqualTo(rerenders),
                "A change re-renders the component when a property it read moved, and only then");
        }

        [Test]
        public void Given_AComponentReadingNothing_When_TheRefetchStarts_Then_ItRerenders()
        {
            // Arrange
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;

            // Act
            s_result.Refetch();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount > rendersBefore, Is.True,
                "Until a property is read, every change re-renders, as v5 does with no property tracked");
        }

        [Test]
        public void Given_AllPropertiesListedAndOnlyDataRead_When_TheRefetchStarts_Then_ItRerenders()
        {
            // Arrange
            s_property = "Data";
            s_notify = QueryProperties.All;
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;

            // Act
            s_result.Refetch();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount > rendersBefore, Is.True, "A listed set replaces what the component read");
        }

        [Test]
        public void Given_OnlyDataListedAndIsFetchingRead_When_TheRefetchStarts_Then_ItDoesNotRerender()
        {
            // Arrange
            s_property = "IsFetching";
            s_notify = QueryProperties.Data;
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;

            // Act
            s_result.Refetch();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(rendersBefore),
                "A listed set replaces what the component read, so a property read but not listed is not watched");
        }

        [Test]
        public void Given_NoPropertiesListed_When_TheRefetchLandsNewData_Then_ItDoesNotRerender()
        {
            // Arrange
            s_property = "Data";
            s_notify = QueryProperties.None;
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;
            s_result.Refetch();

            // Act
            s_sources[1].TrySetResult(8);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(rendersBefore), "An empty list watches nothing");
        }

        [Test]
        public void Given_AComponentReadingOnlyData_When_ARefetchLandsTheValueItHeld_Then_ItDoesNotRerender()
        {
            // Arrange
            s_property = "Data";
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;
            s_result.Refetch();

            // Act
            s_sources[1].TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(rendersBefore),
                "The request going into flight and landing move IsFetching, which this component did not read");
        }

        [Test]
        public void Given_AComponentReadingOnlyIsError_When_TheClientIsCleared_Then_ItRerenders()
        {
            // Arrange
            s_property = "IsError";
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;

            // Act
            s_client.Clear();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount > rendersBefore, Is.True,
                "The entry it read is gone, which no property it read describes");
        }

        private static void Change(string change)
        {
            switch (change)
            {
                case "FirstRequestLands":
                    s_sources[0].TrySetResult(7);
                    break;
                case "FirstRequestFails":
                    s_sources[0].TrySetException(new InvalidOperationException("failed"));
                    break;
                case "RefetchStarts":
                    s_result.Refetch();
                    break;
                default:
                    s_sources[1].TrySetException(new InvalidOperationException("failed"));
                    break;
            }
        }

        private MountedTree MountResolved()
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        private static void Read(QueryResult<int> result, string property)
        {
            switch (property)
            {
                case "Status":
                    _ = result.Status;
                    break;
                case "IsPending":
                    _ = result.IsPending;
                    break;
                case "IsSuccess":
                    _ = result.IsSuccess;
                    break;
                case "IsError":
                    _ = result.IsError;
                    break;
                case "Data":
                    _ = result.Data;
                    break;
                case "Error":
                    _ = result.Error;
                    break;
                case "IsFetching":
                    _ = result.IsFetching;
                    break;
                case "IsStale":
                    _ = result.IsStale;
                    break;
                case "FailureCount":
                    _ = result.FailureCount;
                    break;
                case "FailureReason":
                    _ = result.FailureReason;
                    break;
            }
        }

        [Component]
        private static VNode Reader()
        {
            var result = Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("todos"), Fetch)
                {
                    StaleTime = TimeSpan.FromMinutes(1),
                    Retry = 0,
                    NotifyOnChangeProps = s_notify,
                },
                s_client);
            s_renderCount++;
            s_result = result;
            Read(result, s_property);
            return V.Label(text: "reader");
        }
    }
}
