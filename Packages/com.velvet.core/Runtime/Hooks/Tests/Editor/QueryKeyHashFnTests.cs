#nullable enable
using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>QueryKeyHashFn</c>, TanStack Query's <c>queryKeyHashFn</c>, on <see cref="QueryClientOptions"/>
    /// and on <see cref="QueryOptions{TQueryFnData, TData}"/>.
    /// <list type="bullet">
    /// <item>Keys the function maps to one string name one entry for the client's own commands, which read the
    /// client's function, and one request is shared by the queries reading them; without it, keys that differ
    /// name two entries.</item>
    /// <item>A filter with <c>Exact</c> compares the hash of its key with the entry's, as v5's <c>matchQuery</c>
    /// does, and one without it matches the entry's key by prefix whatever the function says.</item>
    /// <item>A query that sets a function reads a different entry from a query on the same key that sets none,
    /// the query's function replaces the client's, and a function that returns null is refused.</item>
    /// <item>An entry's key stays the key that first named it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class QueryKeyHashFnTests
    {
        private static readonly Func<QueryKey, string> Lowercase = key => key.ToString().ToLowerInvariant();

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static Func<QueryKey, string>? s_queryHashFn;
        private static int s_calls;
        private static StateUpdater<int> s_setTick;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { QueryKeyHashFn = Lowercase, GcTime = TimeSpan.MaxValue });
            s_queryHashFn = null;
            s_calls = 0;
        }

        [Test]
        public void Given_AClientHashFn_When_DataIsSetUnderOneKeyAndReadUnderAnotherHashingAlike_Then_ItIsFound()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("Todos"), 5);

            // Act
            var read = s_client.GetQueryData<int>(new QueryKey("todos"));

            // Assert
            Assert.That(read, Is.EqualTo(5), "Keys the function maps to one string name one entry");
        }

        // GREEN_ON_BASE(characterization): the base tells two differently spelt keys apart, as a client with no function does.
        [Test]
        public void Given_AClientWithNoHashFn_When_DataIsSetUnderOneKeyAndReadUnderADifferentOne_Then_ItIsNotFound()
        {
            // Arrange
            var client = new QueryClient();
            client.SetQueryData(new QueryKey("Todos"), 5);

            // Act
            var read = client.GetQueryData<int>(new QueryKey("todos"));

            // Assert
            Assert.That(read, Is.EqualTo(0), "Without a function the keys differ, so they name two entries");
        }

        [Test]
        public void Given_AClientHashFn_When_AnExactFilterNamesTheKeyAnotherSpellingHashesTo_Then_TheEntryIsSelected()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("todos"), 5);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { QueryKey = new QueryKey("TODOS"), Exact = true });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(1), "An exact filter compares hashes, as v5's matchQuery does");
        }

        // GREEN_ON_BASE(characterization): a filter without Exact leads the key by its parts on the base as well.
        [Test]
        public void Given_AClientHashFn_When_APrefixFilterNamesTheKeyAnotherSpellingHashesTo_Then_NoEntryIsSelected()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("todos"), 5);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { QueryKey = new QueryKey("TODOS") });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(0), "A filter without Exact matches the entry's key, not its hash");
        }

        [Test]
        public void Given_AnEntryNamedByAHashFn_When_ItsKeyIsRead_Then_ItIsTheKeyThatFirstNamedIt()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("Todos"), 5);
            s_client.SetQueryData(new QueryKey("todos"), 6);

            // Act
            var keys = s_client.GetQueriesData<int>();

            // Assert
            Assert.That(keys[0].Key.ToString(), Is.EqualTo("[Todos]"), "The entry keeps the key that created it");
        }

        [Test]
        public void Given_AnEntryNamedByAQueryHashFn_When_AnExactFilterNamesTheKeyAnotherSpellingHashesTo_Then_TheEntryIsSelected()
        {
            // Arrange
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue });
            s_queryHashFn = Lowercase;
            using var mounted = V.Mount(_root, V.Component(TwoSpellings, key: "two"));
            mounted.FlushEffectsForTest();

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { QueryKey = new QueryKey("TODOS"), Exact = true });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(1), "An exact filter hashes its key with the function the entry was created with");
        }

        [Test]
        public void Given_AnEntryWhoseQueryChangesItsHashFnToOneNamingTheSameEntry_When_AnExactFilterNeedsTheNewOne_Then_TheEntryIsSelected()
        {
            // Arrange
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue });
            s_queryHashFn = Lowercase;
            using var mounted = V.Mount(_root, V.Component(Ticking, key: "ticking"));
            mounted.FlushEffectsForTest();
            s_queryHashFn = key => Lowercase(key).Replace("x", string.Empty);
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { QueryKey = new QueryKey("TODOSX"), Exact = true });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(1), "An exact filter hashes with the function of the options the entry was last handed, as v5 reads query.options");
        }

        [Test]
        public void Given_AHashFnReturningNull_When_DataIsSet_Then_ItIsRefused()
        {
            // Arrange
            var client = new QueryClient(new QueryClientOptions { QueryKeyHashFn = _ => null! });

            // Act
            TestDelegate set = () => client.SetQueryData(new QueryKey("todos"), 1);

            // Assert
            Assert.Throws<InvalidOperationException>(set, "A hash that names no entry is a defect to report, not a key to store");
        }

        [Test]
        public void Given_TwoQueriesWhoseKeysTheClientHashFnMapsAlike_When_Mounted_Then_OneRequestRuns()
        {
            // Arrange
            s_calls = 0;

            // Act
            using var mounted = V.Mount(_root, V.Component(TwoSpellings, key: "two"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Queries reading keys that hash alike share one request");
        }

        // GREEN_ON_BASE(characterization): without a function the two spellings are two entries, so each runs a request on the base too.
        [Test]
        public void Given_TwoQueriesWhoseKeysDifferAndNoHashFn_When_Mounted_Then_TwoRequestsRun()
        {
            // Arrange
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue });

            // Act
            using var mounted = V.Mount(_root, V.Component(TwoSpellings, key: "two"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "Keys that differ name two entries, each fetched");
        }

        [Test]
        public void Given_QueriesSettingTheirOwnHashFn_When_TheirKeysHashAlike_Then_OneRequestRuns()
        {
            // Arrange
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue });
            s_queryHashFn = Lowercase;

            // Act
            using var mounted = V.Mount(_root, V.Component(TwoSpellings, key: "two"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A query's function names its entry as the client's does");
        }

        [Test]
        public void Given_AQuerySettingAHashFnAndAnotherSettingNone_When_TheirKeysAreEqual_Then_TwoRequestsRun()
        {
            // Arrange
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue });

            // Act
            using var mounted = V.Mount(_root, V.Component(HashedAndPlain, key: "mixed"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "An entry named by a function is not the entry the key itself names");
        }

        [Test]
        public void Given_AQueryHashFnThatDiffersFromTheClients_When_TwoSpellingsAreMounted_Then_TheQueryFnDecides()
        {
            // Arrange
            s_queryHashFn = key => key.ToString();

            // Act
            using var mounted = V.Mount(_root, V.Component(TwoSpellings, key: "two"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The query's function replaces the client's");
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            return new VelvetTaskCompletionSource<int>().Task;
        }

        [Component]
        private static VNode TwoSpellings()
        {
            Hooks.UseQuery(Options("Todos"), s_client);
            Hooks.UseQuery(Options("todos"), s_client);
            return V.Label(text: "two");
        }

        [Component]
        private static VNode Ticking()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseQuery(Options("Todos"), s_client);
            return V.Label(text: "ticking");
        }

        [Component]
        private static VNode HashedAndPlain()
        {
            Hooks.UseQuery(Options("todos") with { QueryKeyHashFn = _ => "todos" }, s_client);
            Hooks.UseQuery(Options("todos"), s_client);
            return V.Label(text: "mixed");
        }

        private static QueryOptions<int> Options(string key)
            => new QueryOptions<int>(new QueryKey(key), Fetch) with { QueryKeyHashFn = s_queryHashFn };
    }
}
