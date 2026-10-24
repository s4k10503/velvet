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
    /// Specifies which query's <see cref="QueryOptions{TQueryFnData, TData}.StructuralSharing"/> shares the data a
    /// write or a landing leaves in an entry two readers share: the options the entry was last handed, as v5's
    /// <c>setData</c> and its fetch read them.
    /// </summary>
    /// <remarks>
    /// The query function hands back a completion source the case settles itself and retries nothing.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryClientSharingOptionsTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private VisualElement _secondRoot = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan? s_staleTime;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _secondRoot = new VisualElement();
            s_staleTime = null;
            s_client = new QueryClient();
            s_sources.Clear();
        }

        // GREEN_ON_BASE(characterization): the base shares a write and a landing with the options the entry was last handed too; these cases moved here from QueryClientDataTests.
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

        // GREEN_ON_BASE(characterization): the base shares a write and a landing with the options the entry was last handed too; these cases moved here from QueryClientDataTests.
        [Test]
        public void Given_ARequestStartedByOneReader_When_AnotherSubscribesBeforeItLands_Then_TheLaterReadersFunctionSharesIt()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var first = MountUnder(_root, AddingTen);
            using var second = MountUnder(_secondRoot, AddingHundred);

            // Act
            s_sources[0].TrySetResult(1);

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(101),
                "A landing shares with the options the entry holds when it settles, as v5's fetch reads this.options then");
        }

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
                new QueryOptions<int>(Todos, Fetch) { StaleTime = s_staleTime, StructuralSharing = (_, arrived) => (int)arrived + 10 },
                s_client);
            return V.Label(text: "ten");
        }

        [Component]
        private static VNode AddingHundred()
        {
            Hooks.UseQuery(
                new QueryOptions<int>(Todos, Fetch) { StaleTime = s_staleTime, StructuralSharing = (_, arrived) => (int)(int)arrived + 100 },
                s_client);
            return V.Label(text: "hundred");
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }
    }
}
