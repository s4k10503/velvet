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
    /// Specifies <see cref="QueryClient.GetQueryData{T}"/> and <see cref="QueryClient.SetQueryData{T}(QueryKey, T)"/>,
    /// TanStack Query's <c>getQueryData</c> and <c>setQueryData</c>.
    /// <list type="bullet">
    /// <item>Reading gives the entry's data, and default for a key with no entry or an entry unread past its
    /// garbage-collection time, and not before; an entry of another type throws.</item>
    /// <item>Writing creates the entry when there is none, which expires unread after the client's
    /// garbage-collection time. Written data is fresh from the moment of the write and ages from it, clears an
    /// invalidation, keeps the instance held where it is deeply equal, and an updater is handed the data held;
    /// an updater returning null writes nothing. A write over an expired entry is readable, and a null key or
    /// updater throws.</item>
    /// <item>A mounted query re-renders with written data as a success without error, a request in flight
    /// keeps running and lands over the written data.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The client's clock is <see cref="s_now"/>, advanced by hand. The query function hands back a completion
    /// source the case settles itself and retries nothing.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryClientDataTests
    {
        private static readonly TimeSpan GcTime = TimeSpan.FromMinutes(5);
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private VisualElement _secondRoot = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static TimeSpan? s_staleTime;
        private static int s_calls;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _secondRoot = new VisualElement();
            s_now = TimeSpan.Zero;
            s_staleTime = null;
            s_calls = 0;
            s_client = new QueryClient(new QueryClientOptions { GcTime = GcTime, Clock = () => s_now });
            s_sources.Clear();
            s_renders.Clear();
        }

        #region Reading

        [Test]
        public void Given_NoEntry_When_GetQueryDataReads_Then_ItIsDefault()
        {
            // Act
            var data = s_client.GetQueryData<string>(Todos);

            // Assert
            Assert.That(data, Is.Null, "A key with no entry has no data");
        }

        [Test]
        public void Given_AResolvedQuery_When_GetQueryDataReads_Then_ItIsTheEntrysData()
        {
            // Arrange
            using var mounted = MountResolved(7);

            // Act
            var data = s_client.GetQueryData<int>(Todos);

            // Assert
            Assert.That(data, Is.EqualTo(7), "The entry's data is what a request landed");
        }

        [Test]
        public void Given_AnEntryUnreadPastItsGcTime_When_GetQueryDataReads_Then_ItIsDefault()
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");
            s_now = GcTime;

            // Act
            var data = s_client.GetQueryData<string>(Todos);

            // Assert
            Assert.That(data, Is.Null, "An entry nothing read for its gcTime reads as absent, as one v5 removed would");
        }

        [Test]
        public void Given_AnEntryUnreadForLessThanItsGcTime_When_GetQueryDataReads_Then_ItIsTheData()
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");
            s_now = GcTime - TimeSpan.FromMilliseconds(1);

            // Act
            var data = s_client.GetQueryData<string>(Todos);

            // Assert
            Assert.That(data, Is.EqualTo("held"), "An entry expires once its gcTime has passed, not before");
        }

        [Test]
        public void Given_AnEntryOfAnotherType_When_GetQueryDataReads_Then_ItThrows()
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");

            // Act
            TestDelegate read = () => s_client.GetQueryData<int>(Todos);

            // Assert
            Assert.That(read, Throws.InvalidOperationException, "A key names one entry of one data type");
        }

        #endregion

        #region Writing

        [Test]
        public void Given_NoEntry_When_SetQueryDataWrites_Then_GetQueryDataReturnsIt()
        {
            // Act
            s_client.SetQueryData(Todos, "written");

            // Assert
            Assert.That(s_client.GetQueryData<string>(Todos), Is.EqualTo("written"), "Writing creates the entry");
        }

        [Test]
        public void Given_AnEqualListHeld_When_SetQueryDataWritesANewOne_Then_TheHeldInstanceIsKept()
        {
            // Arrange
            var held = new List<string> { "a", "b" };
            s_client.SetQueryData(Todos, held);

            // Act
            var kept = s_client.SetQueryData(Todos, new List<string> { "a", "b" });

            // Assert
            Assert.That(ReferenceEquals(kept, held), Is.True,
                "Written data is shared structurally with what the entry held, as v5's setData replaces it");
        }

        [Test]
        public void Given_HeldData_When_SetQueryDataRunsAnUpdater_Then_TheUpdaterIsHandedIt()
        {
            // Arrange
            s_client.SetQueryData(Todos, 2);

            // Act
            s_client.SetQueryData<int>(Todos, held => held + 3);

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(5), "The updater makes the new data out of the held data");
        }

        [Test]
        public void Given_HeldData_When_AnUpdaterReturnsNull_Then_TheHeldDataStays()
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");

            // Act
            s_client.SetQueryData<string>(Todos, _ => null!);

            // Assert
            Assert.That(s_client.GetQueryData<string>(Todos), Is.EqualTo("held"),
                "An updater returning nothing writes nothing, as v5's returning undefined does");
        }

        [Test]
        public void Given_AnEntryUnreadPastItsGcTime_When_SetQueryDataWrites_Then_TheWriteIsReadable()
        {
            // Arrange
            s_client.SetQueryData(Todos, "old");
            s_now = GcTime;

            // Act
            s_client.SetQueryData(Todos, "new");

            // Assert
            Assert.That(s_client.GetQueryData<string>(Todos), Is.EqualTo("new"),
                "A write over an expired entry writes into a new one rather than the one that had expired");
        }

        [Test]
        public void Given_ANullKey_When_GetQueryDataReads_Then_ItThrowsNamingTheKey()
        {
            // Act
            TestDelegate read = () => s_client.GetQueryData<string>(null!);

            // Assert
            Assert.That(read, Throws.ArgumentNullException.With.Property("ParamName").EqualTo("queryKey"));
        }

        [Test]
        public void Given_ANullKey_When_SetQueryDataWrites_Then_ItThrowsNamingTheKey()
        {
            // Act
            TestDelegate write = () => s_client.SetQueryData(null!, "data");

            // Assert
            Assert.That(write, Throws.ArgumentNullException.With.Property("ParamName").EqualTo("queryKey"));
        }

        [Test]
        public void Given_ANullUpdater_When_SetQueryDataRuns_Then_ItThrowsNamingTheUpdater()
        {
            // Act
            TestDelegate write = () => s_client.SetQueryData<string>(Todos, (Func<string?, string>)null!);

            // Assert
            Assert.That(write, Throws.ArgumentNullException.With.Property("ParamName").EqualTo("updater"));
        }

        [Test]
        public void Given_DataWrittenByHand_When_AQueryMountsWithinItsStaleTime_Then_NothingIsFetched()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            s_now = TimeSpan.FromSeconds(30);
            s_client.SetQueryData(Todos, 4);
            s_now = TimeSpan.FromSeconds(89);

            // Act
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That((s_calls, Last().Data), Is.EqualTo((0, 4)), "Written data is fresh from the moment it was written");
        }

        [Test]
        public void Given_DataWrittenByHand_When_AQueryMountsOnceItsStaleTimeHasPassed_Then_ItFetches()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            s_now = TimeSpan.FromSeconds(30);
            s_client.SetQueryData(Todos, 4);
            s_now = TimeSpan.FromSeconds(90);

            // Act
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Written data ages like fetched data");
        }

        [Test]
        public void Given_AnInvalidatedEntry_When_SetQueryDataWrites_Then_AQueryMountingOverItFetchesNothing()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            s_client.SetQueryData(Todos, 1);
            s_client.InvalidateQueries(Todos);

            // Act
            s_client.SetQueryData(Todos, 2);
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "A write clears the invalidation, as v5's success action does");
        }

        [Test]
        public void Given_AMountedQueryThatFailed_When_SetQueryDataWrites_Then_ItRendersASuccessWithoutError()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetException(new InvalidOperationException("failed"));
            mounted.FlushStateForTest();

            // Act
            s_client.SetQueryData(Todos, 8);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Status, Last().Data, Last().Error), Is.EqualTo((QueryStatus.Success, 8, (Exception?)null)),
                "A write is a success: it replaces the error and re-renders the readers");
        }

        [Test]
        public void Given_ARequestInFlight_When_SetQueryDataWrites_Then_TheRequestKeepsRunning()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            s_client.SetQueryData(Todos, 8);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Data, Last().IsFetching), Is.EqualTo((8, true)),
                "A write by hand leaves the request in flight running, as v5's manual setData does");
        }

        [Test]
        public void Given_DataWrittenOverARequestInFlight_When_TheRequestLands_Then_ItsDataReplacesTheWrite()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            s_client.SetQueryData(Todos, 8);

            // Act
            s_sources[0].TrySetResult(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Data, Last().IsFetching), Is.EqualTo((9, false)), "The request lands over the written data");
        }

        [Test]
        public void Given_TwoReadersWithDifferentSharingFunctions_When_DataIsWritten_Then_TheLaterReadersFunctionSharesIt()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            s_client.SetQueryData(Todos, 1);
            using var first = MountUnder(_root, AddingTen);
            using var second = MountUnder(_secondRoot, AddingHundred);

            // Act
            s_client.SetQueryData(Todos, 2);

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(102),
                "A write shares with the options the entry was last handed, as v5's setData uses the query's own");
        }

        #endregion

        #region Components and helpers

        private static MountedTree MountUnder(VisualElement root, Func<VNode> reader)
        {
            var mounted = V.Mount(root, V.Component(reader, key: "reader"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        [Component]
        private static VNode AddingTen()
        {
            Hooks.UseQuery(
                new QueryOptions<int>(Todos, Fetch) { StaleTime = s_staleTime, StructuralSharing = (_, arrived) => arrived + 10 },
                s_client);
            return V.Label(text: "ten");
        }

        [Component]
        private static VNode AddingHundred()
        {
            Hooks.UseQuery(
                new QueryOptions<int>(Todos, Fetch) { StaleTime = s_staleTime, StructuralSharing = (_, arrived) => arrived + 100 },
                s_client);
            return V.Label(text: "hundred");
        }

        private MountedTree MountResolved(int data)
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(data);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

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
                new QueryOptions<int>(Todos, Fetch)
                {
                    StaleTime = s_staleTime,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "reader");
        }

        #endregion
    }
}
