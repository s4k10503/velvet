#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a <see cref="QueryClient"/> removes an entry nothing reads: TanStack Query's
    /// <c>scheduleGc</c> timer, with no client call to sweep it.
    /// <list type="bullet">
    /// <item>An unread entry is removed on the first frame at or after the point its garbage-collection time
    /// has passed on the client's clock, and not before; a zero time removes it on the next frame, and an
    /// infinite one never.</item>
    /// <item>A reader arriving before the deadline takes the entry back, and the time runs again from the
    /// moment the reader leaves.</item>
    /// <item>An entry with a request in flight is kept, and is removed a whole garbage-collection time after the
    /// request settles.</item>
    /// <item>The client reads its clock once a frame while an unread entry can still expire, in one loop however
    /// many entries are unread, and not otherwise.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The client's clock is <see cref="s_now"/>, advanced by hand between frames, and every reading of it is
    /// counted in <see cref="s_reads"/>, which is how polling is observed. An entry's presence is read from the
    /// client's dictionary rather than through <c>Peek</c>, which reports an expired entry as absent before
    /// anything removes it.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryClientGcTimerTests
    {
        private static readonly TimeSpan GcTime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);
        private static readonly QueryKey Todos = new("todos");
        private static readonly QueryKey Forever = new("forever");
        private static readonly QueryKey Other = new("other");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static int s_reads;
        private static bool s_clockThrows;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static StateUpdater<bool> s_setShow;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_reads = 0;
            s_clockThrows = false;
            s_client = NewClient(GcTime);
            s_sources.Clear();
            s_setShow = default;
        }

        // The client's loop ends on the first frame after Clear, so the drain leaves nothing queued for the next fixture.
        [TearDown]
        public void TearDown()
        {
            s_client.Clear();
            DrainEditorUpdateForTest();
        }

        [UnityTest]
        public IEnumerator Given_AnUnreadEntry_When_FramesPassAtAndJustBeforeItsGcTime_Then_ItIsRemovedOnlyAtIt()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");

            // Act
            await Pass(GcTime - Tick);
            var keptJustBefore = EntryCount();
            await Pass(Tick);

            // Assert
            Assert.That((keptJustBefore, EntryCount()), Is.EqualTo((1, 0)),
                "No client call follows the write, so the removal is the client's own, and lands once the time has passed, not before");
        });

        [UnityTest]
        public IEnumerator Given_AZeroGcTime_When_AFramePasses_Then_TheUnreadEntryIsRemoved()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client = NewClient(TimeSpan.Zero);
            s_client.SetQueryData(Todos, "held");

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(EntryCount(), Is.EqualTo(0), "A zero time is a timer of zero, which still fires, as v5's setTimeout does");
        });

        [UnityTest]
        public IEnumerator Given_AnInfiniteGcTimeBesideAFiniteOne_When_FramesPassPastTheFiniteOne_Then_OnlyTheInfiniteEntryRemains()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the infinite entry cannot be what reddens this, since nothing removes it on the base either;
            // it is here so that working out its deadline is exercised, which a time of MaxValue overflows.
            s_client.Ensure<int>(Forever, TimeSpan.MaxValue);
            s_client.Ensure<int>(Todos, GcTime);

            // Act
            await Pass(TimeSpan.FromDays(1));

            // Assert
            Assert.That(EntryKeys(), Is.EqualTo(new[] { Forever.ToString() }),
                "An infinite time sets no timer, so only the entry with a finite one goes");
        });

        [UnityTest]
        public IEnumerator Given_AReaderThatLeftAndCameBackBeforeTheDeadline_When_ItLeavesAgain_Then_TheTimeRunsFromThen()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = await MountResolved();
            Hide(mounted);
            await Pass(GcTime - Tick);
            Show(mounted);
            Hide(mounted);

            // Act
            await Pass(GcTime - Tick);
            var keptJustBefore = EntryCount();
            await Pass(Tick);

            // Assert
            Assert.That((keptJustBefore, EntryCount()), Is.EqualTo((1, 0)),
                "The entry outlived the deadline of its first leaving, and went a whole time after the second");
        });

        [UnityTest]
        public IEnumerator Given_AnUnreadEntryWithARequestInFlight_When_ItLandsAfterTheGcTime_Then_ItIsRemovedAWholeGcTimeLater()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            Hide(mounted);
            await Pass(GcTime);
            var keptWhileFetching = EntryCount();
            s_sources[0].TrySetResult(7);
            await Pass(TimeSpan.Zero);
            await Pass(GcTime - Tick);
            var keptJustBefore = EntryCount();

            // Act
            await Pass(Tick);

            // Assert
            Assert.That((keptWhileFetching, keptJustBefore, EntryCount()), Is.EqualTo((1, 1, 0)),
                "A fetching query is not removed, and its time starts over when the request settles");
        });

        [UnityTest]
        public IEnumerator Given_ASweepThatFoundOnlyInfiniteEntries_When_AFiniteEntryIsWrittenAndItsTimePasses_Then_ItIsRemoved()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the sweep leaves the client with no deadline to wait for.
            s_client.Ensure<int>(Forever, TimeSpan.MaxValue);
            s_client.InvalidateQueries(Other);
            s_client.SetQueryData(Todos, "held");

            // Act
            await Pass(GcTime);

            // Assert
            Assert.That(EntryKeys(), Is.EqualTo(new[] { Forever.ToString() }),
                "An entry that went unread after a sweep is still removed at its own deadline");
        });

        [UnityTest]
        public IEnumerator Given_AnEntryRemovedByTheLoop_When_AnotherIsWrittenLater_Then_ItIsRemovedToo()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");
            await Pass(GcTime);
            s_client.SetQueryData(Other, "held");

            // Act
            await Pass(GcTime);

            // Assert
            Assert.That(EntryCount(), Is.EqualTo(0), "A loop that ended is started again by the next entry that can expire");
        });

        [UnityTest]
        public IEnumerator Given_AFrameWhoseClockReadingThrows_When_AnotherEntryIsWrittenLater_Then_TheClientRemovesBothAtTheirDeadlines()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the loop's fault is logged as an exception when its task completes unobserved.
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: clock"));
            s_client.SetQueryData(Todos, "held");
            s_clockThrows = true;
            await Pass(TimeSpan.Zero);
            s_clockThrows = false;
            s_client.SetQueryData(Other, "held");

            // Act
            await Pass(GcTime);

            // Assert
            Assert.That(EntryCount(), Is.EqualTo(0), "A loop that failed leaves the client able to start another");
        });

        [UnityTest]
        public IEnumerator Given_ALazySweepThatRemovedTheLastExpirableEntry_When_FramesPass_Then_TheClientStopsReadingTheClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the sweep comes after the finite deadline and before the loop's next frame, with an infinite
            // entry left. The first half of the assertion is what the base cannot do: read the clock while waiting.
            s_client.Ensure<int>(Forever, TimeSpan.MaxValue);
            s_client.SetQueryData(Todos, "held");
            var beforeWaiting = s_reads;
            await Pass(TimeSpan.Zero);
            var polledWhileWaiting = s_reads > beforeWaiting;
            s_now = GcTime;
            s_client.InvalidateQueries(Other);
            await Pass(TimeSpan.Zero);
            var before = s_reads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That((polledWhileWaiting, s_reads - before), Is.EqualTo((true, 0)),
                "Only an entry that never expires is left, so nothing is waited for, whoever removed the other");
        });

        [UnityTest]
        public IEnumerator Given_AClearedClientWhoseLoopIsRunning_When_FramesPass_Then_TheClientStopsReadingTheClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");
            var beforeWaiting = s_reads;
            await Pass(TimeSpan.Zero);
            var polledWhileWaiting = s_reads > beforeWaiting;
            s_client.Clear();
            await Pass(TimeSpan.Zero);
            var before = s_reads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That((polledWhileWaiting, s_reads - before), Is.EqualTo((true, 0)),
                "Clear leaves nothing to wait for, well before the deadline the removed entry had");
        });

        // GREEN_ON_BASE(characterization): the base starts no loop, so it reads the clock no more often; the
        // paired case below is what a loop that never started cannot pass.
        [UnityTest]
        public IEnumerator Given_AnEntryOfInfiniteGcTime_When_FramesPass_Then_TheClientReadsNoClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.Ensure<int>(Forever, TimeSpan.MaxValue);
            var before = s_reads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_reads - before, Is.EqualTo(0), "Nothing can expire, so no loop runs, as scheduleGc sets no timer");
        });

        [UnityTest]
        public IEnumerator Given_TwoUnreadEntriesOfFiniteGcTime_When_FramesPass_Then_OneLoopReadsTheClientsClockEachFrame()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");
            s_client.Ensure<int>(Other, GcTime);
            var before = s_reads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert — three frames pass: a single loop reads at most three times, two loops at least four.
            Assert.That(s_reads - before, Is.InRange(1, 3), "The removal is the client's own, in one loop however many entries wait");
        });

        // GREEN_ON_BASE(characterization): the base removes nothing and starts no loop, so it reads the clock no more
        // often; the paired case above is what a loop that never started cannot pass.
        [UnityTest]
        public IEnumerator Given_AnInfiniteEntryBesideAFiniteOneRemoved_When_FramesPass_Then_TheClientReadsNoClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.Ensure<int>(Forever, TimeSpan.MaxValue);
            s_client.Ensure<int>(Todos, GcTime);
            await Pass(GcTime);
            var before = s_reads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_reads - before, Is.EqualTo(0), "An entry that never expires is no reason to keep reading the clock");
        });

        [UnityTest]
        public IEnumerator Given_TheOnlyUnreadEntryRemoved_When_FramesPassAfter_Then_TheClientReadsNoClock()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");
            await Pass(GcTime);
            var before = s_reads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That((EntryCount(), s_reads - before), Is.EqualTo((0, 0)),
                "With nothing left to expire, the loop ends rather than reading the clock on every frame");
        });

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private async VelvetTask<MountedTree> MountResolved()
        {
            var mounted = Mount();
            s_sources[0].TrySetResult(1);
            await Pass(TimeSpan.Zero);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static void Hide(MountedTree mounted)
        {
            s_setShow.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static void Show(MountedTree mounted)
        {
            s_setShow.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static QueryClient NewClient(TimeSpan gcTime)
            => new(new QueryClientOptions
            {
                GcTime = gcTime,
                Clock = () =>
                {
                    s_reads++;
                    if (s_clockThrows) throw new InvalidOperationException("clock");
                    return s_now;
                },
            });

        private static async VelvetTask Pass(TimeSpan time)
        {
            s_now += time;
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
        }

        private static IDictionary<QueryKey, QueryEntry> Entries()
            => (IDictionary<QueryKey, QueryEntry>)typeof(QueryClient)
                .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(s_client)!;

        private static int EntryCount() => Entries().Count;

        private static string[] EntryKeys() => Entries().Keys.Select(key => key.ToString()).ToArray();

        [Component]
        private static VNode Solo()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(children: new VNode[]
            {
                show ? V.Component(Reader, key: "reader") : V.Fragment(Array.Empty<VNode>()),
            });
        }

        // A function taking no token, so a request in flight survives its reader leaving.
        [Component]
        private static VNode Reader()
        {
            Hooks.UseQuery(new QueryOptions<int>(Todos, () =>
            {
                var source = new VelvetTaskCompletionSource<int>();
                s_sources.Add(source);
                return source.Task;
            }) { StaleTime = TimeSpan.MaxValue, Retry = 0 }, s_client);
            return V.Label(text: "reader");
        }
    }
}
