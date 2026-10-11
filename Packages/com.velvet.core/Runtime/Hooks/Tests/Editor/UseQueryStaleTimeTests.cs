#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a query's data turns stale on its own, TanStack Query's stale timeout.
    /// <list type="bullet">
    /// <item>A component reading <c>IsStale</c> re-renders when the age of fresh data reaches the stale time,
    /// measured on the client's clock, not a moment before.</item>
    /// <item>A component that read only <c>IsFetching</c> does not re-render then, and data that lands again moves
    /// the moment to its own age.</item>
    /// <item>The wait ends when the reader unmounts, when the client is cleared and when the query is
    /// disabled.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The client's clock is <see cref="s_now"/>, advanced by hand, and <see cref="s_clockReads"/> counts how
    /// often it is read, since a wait polls it once a frame. The stale time is ten seconds.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryStaleTimeTests
    {
        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static int s_clockReads;
        private static bool s_readsStale;
        private static TimeSpan s_staleTime;
        private static bool s_enabled;
        private static StateUpdater<int> s_setTick;
        private static int s_renderCount;
        private static QueryResult<int> s_result = null!;
        private static StateUpdater<bool> s_setShow;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_clockReads = 0;
            s_readsStale = true;
            s_staleTime = Seconds(10);
            s_enabled = true;
            s_setTick = default;
            s_renderCount = 0;
            s_setShow = default;
            s_sources.Clear();
            s_client = new QueryClient(new QueryClientOptions
            {
                Clock = () =>
                {
                    s_clockReads++;
                    return s_now;
                },
            });
        }

        [UnityTest]
        public IEnumerator Given_AComponentReadingIsStale_When_TheStaleTimeElapses_Then_ItRerendersAsStale()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;

            // Act
            await Pass(Seconds(9.999));
            mounted.FlushStateForTest();
            var early = s_renderCount;
            await Pass(Seconds(0.001));
            mounted.FlushStateForTest();

            // Assert
            Assert.That((early == rendersBefore, s_result.IsStale), Is.EqualTo((true, true)),
                "Fresh data turns stale when its age reaches the stale time, and not before");
        });

        [UnityTest]
        public IEnumerator Given_AComponentReadingOnlyIsFetching_When_FramesPass_Then_NoWaitReadsTheClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_readsStale = false;
            using var mounted = MountResolved();
            await Pass(TimeSpan.Zero);
            var readsAfterTheFirstFrames = s_clockReads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_clockReads, Is.EqualTo(readsAfterTheFirstFrames),
                "A change to IsStale would not be this component's, so nothing waits for it");
        });

        [UnityTest]
        public IEnumerator Given_AComponentReadingOnlyIsFetching_When_TheStaleTimeElapses_Then_ItDoesNotRerender()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_readsStale = false;
            using var mounted = MountResolved();
            var rendersBefore = s_renderCount;

            // Act
            await Pass(Seconds(10));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(rendersBefore), "A property the component did not read is not its change");
        });

        [UnityTest]
        public IEnumerator Given_AnInfiniteStaleTime_When_FramesPass_Then_NoWaitReadsTheClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_staleTime = TimeSpan.MaxValue;
            using var mounted = MountResolved();
            await Pass(TimeSpan.Zero);
            var readsAfterTheFirstFrames = s_clockReads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_clockReads, Is.EqualTo(readsAfterTheFirstFrames), "Data that never turns stale is waited for by no one");
        });

        [UnityTest]
        public IEnumerator Given_AComponentThatReadsIsStaleOnlyFromALaterRender_When_TheStaleTimeElapses_Then_ItRerendersAsStale()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_readsStale = false;
            using var mounted = MountResolved();
            s_readsStale = true;
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Act
            await Pass(Seconds(10));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_result.IsStale, Is.True, "The commit that follows the first read of IsStale starts the wait");
        });

        [UnityTest]
        public IEnumerator Given_FreshDataLandingAgainLater_When_TheFirstStaleTimeElapses_Then_ItIsStillFreshUntilTheSecond()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            await Pass(Seconds(5));
            s_result.Refetch();
            s_sources[1].TrySetResult(8);
            mounted.FlushStateForTest();
            var rendersBefore = s_renderCount;

            // Act
            await Pass(Seconds(5));
            mounted.FlushStateForTest();
            var atTheFirstDeadline = s_renderCount;
            await Pass(Seconds(5));
            mounted.FlushStateForTest();

            // Assert
            Assert.That((atTheFirstDeadline == rendersBefore, s_result.IsStale), Is.EqualTo((true, true)),
                "Data that landed later is stale a stale time after it did");
        });

        [UnityTest]
        public IEnumerator Given_AWaitingComponent_When_ItUnmounts_Then_TheWaitStopsReadingTheClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            Hide(mounted);
            await Pass(TimeSpan.Zero);
            var readsAfterTheFirstFrames = s_clockReads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_clockReads, Is.EqualTo(readsAfterTheFirstFrames), "Nothing is waiting for data nobody reads");
        });

        [UnityTest]
        public IEnumerator Given_AWaitingComponent_When_TheClientIsCleared_Then_TheWaitStopsReadingTheClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            s_client.Clear();
            await Pass(TimeSpan.Zero);
            var readsAfterTheFirstFrames = s_clockReads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_clockReads, Is.EqualTo(readsAfterTheFirstFrames),
                "The data the wait was for is gone, whether or not the component has rendered since");
        });

        [UnityTest]
        public IEnumerator Given_AWaitingComponent_When_TheQueryIsDisabled_Then_TheWaitStopsReadingTheClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = MountResolved();
            s_enabled = false;
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
            await Pass(TimeSpan.Zero);
            var readsAfterTheFirstFrames = s_clockReads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_clockReads, Is.EqualTo(readsAfterTheFirstFrames), "A disabled query reports no staleness, so nothing waits for it");
        });

        private static TimeSpan Seconds(double count) => TimeSpan.FromSeconds(count);

        private static async VelvetTask Pass(TimeSpan time)
        {
            s_now += time;
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
        }

        private static void Hide(MountedTree mounted)
        {
            s_setShow.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private MountedTree MountResolved()
        {
            var mounted = V.Mount(_root, V.Component(Host, key: "host"));
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

        [Component]
        private static VNode Reader()
        {
            var result = Hooks.UseQuery(
                new QueryOptions<int>(new QueryKey("todos"), Fetch) { StaleTime = s_staleTime, Enabled = s_enabled, Retry = 0 },
                s_client);
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_renderCount++;
            s_result = result;
            _ = s_readsStale ? result.IsStale : result.IsFetching;
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode Host()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(children: new VNode[]
            {
                show ? V.Component(Reader, key: "reader") : V.Fragment(Array.Empty<VNode>()),
            });
        }
    }
}
