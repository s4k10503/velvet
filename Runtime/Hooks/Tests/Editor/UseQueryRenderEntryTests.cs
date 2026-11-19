#nullable enable
using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a query's render builds the entry it reads when there is none, as v5's
    /// <c>getOptimisticResult</c> builds its query, and that the commit after it takes that entry up, a zero
    /// garbage-collection time included, however many renders ran in between and through one walking poll of
    /// the client's clock.
    /// </summary>
    /// <remarks>
    /// The client's clock stands still, and <see cref="s_initialDataCalls"/> counts how often an entry took the
    /// first reader's initial data, once for each entry made for it.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryRenderEntryTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static bool s_enabled;
        private static int s_initialDataCalls;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_enabled = true;
            s_initialDataCalls = 0;
        }

        [Test]
        public void Given_NoEntry_When_ADisabledQueryRenders_Then_TheClientHoldsItsEntry()
        {
            // Arrange
            s_client = NewClient(TimeSpan.MaxValue);
            s_enabled = false;

            // Act
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Assert
            Assert.That(s_client.GetQueryState<int>(Todos), Is.Not.Null, "Every render finds or builds its entry, as v5's observer does");
        }

        [Test]
        public void Given_AZeroGcTimeAndASiblingRenderingAfter_When_TheReaderCommits_Then_ItKeepsTheEntryItsRenderBuilt()
        {
            // Arrange — the sibling's render advances the client's render count between the reader's render and its commit.
            s_client = NewClient(TimeSpan.Zero);

            // Act
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_initialDataCalls, Is.EqualTo(1),
                "The commit takes up the entry its render built rather than letting it expire and building another");
        }

        [Test]
        public void Given_AZeroGcTime_When_OneFramesPollRunsBetweenTheRenderAndItsCommit_Then_TheCommitKeepsTheEntryItsRenderBuilt()
        {
            // Arrange — the client's frame loop polls once, as a frame does between a render and the panel tick that
            // runs its passive effects.
            s_client = NewClient(TimeSpan.Zero);
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Act
            PollOnce(s_client);
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_initialDataCalls, Is.EqualTo(1),
                "A render's entry outlasts the first walking poll, so the commit after it subscribes to it");
        }

        private static void PollOnce(QueryClient client)
            => typeof(QueryClient).GetMethod("Poll", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(client, null);

        private static QueryClient NewClient(TimeSpan gcTime)
            => new(new QueryClientOptions { GcTime = gcTime, Retry = 0, Clock = () => TimeSpan.Zero });

        private static VelvetTask<int> Fetch(CancellationToken token) => new VelvetTaskCompletionSource<int>().Task;

        private static int InitialData()
        {
            s_initialDataCalls++;
            return 3;
        }

        [Component]
        private static VNode Reader()
        {
            Hooks.UseQuery(
                new QueryOptions<int>(Todos, (QueryFunctionContext context) => Fetch(context.CancellationToken))
                {
                    Enabled = s_enabled,
                    InitialData = InitialData,
                    StaleTime = TimeSpan.MaxValue,
                },
                s_client);
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode Sibling()
        {
            Hooks.UseQuery(new QueryOptions<int>(new QueryKey("other"), (QueryFunctionContext context) => Fetch(context.CancellationToken)) { StaleTime = TimeSpan.MaxValue }, s_client);
            return V.Label(text: "sibling");
        }

        [Component]
        private static VNode Host()
            => V.Div(children: new VNode?[]
            {
                V.Component(Reader, key: "reader"),
                V.Component(Sibling, key: "sibling"),
            });
    }
}
