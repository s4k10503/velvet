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
    /// Specifies what a <see cref="QueryOptionsBase{TQueryFnData, TData}.StructuralSharing"/> function is handed as the
    /// data held when a query whose result is a value type had none: null, as v5's <c>replaceData</c> is handed
    /// <c>undefined</c>, and never the type's default.
    /// </summary>
    /// <remarks>
    /// The function records each call's held data. One request lands 5; the entry's landing and the
    /// <see cref="QueryOptionsBase{TQueryFnData, TData}.Select"/> over it each call the function once, since the select
    /// is one instance and runs again only for new data.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQuerySharingPreviousTests
    {
        private static readonly Func<int, int> Double = data => data * 2;

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static readonly List<string> s_held = new();
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0 });
            s_held.Clear();
            s_sources.Clear();
        }

        // GREEN_ON_BASE(characterization): the base already hands no held data here; this pins the literal that starts a result without data, which no case measured.
        [Test]
        public void Given_AValueTypeResultThatHadNoData_When_TheFirstRequestLands_Then_SharingIsHandedNoHeldData()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            s_sources[0].TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", s_held), Is.EqualTo("none,none"),
                "Neither the entry nor the result held data before the first landing");
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        private static object Share(object? held, object arrived)
        {
            s_held.Add(held == null ? "none" : held.ToString()!);
            return arrived;
        }

        [Component]
        private static VNode Reader()
        {
            Hooks.UseQuery(
                new QueryOptions<int, int>(new QueryKey("todos"), (QueryFunctionContext context) => Fetch(context.CancellationToken))
                {
                    Select = Double,
                    StructuralSharing = Share,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client);
            return V.Label(text: "reader");
        }
    }
}
