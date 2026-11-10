using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="MutationOptionsBase.Scope"/>, TanStack Query's mutation <c>scope</c>: calls whose scopes
    /// share an id run one at a time, in the order they started, each waiting paused until the one ahead of it has
    /// settled, however it settled; calls of different scopes, or of none, run side by side.
    /// </summary>
    /// <remarks>
    /// Every call's function records its variables in <see cref="s_started"/> and waits on its own gate, so what has
    /// started is read straight after a gate opens, before any frame passes.
    /// </remarks>
    [TestFixture]
    internal sealed class MutationScopeTests
    {
        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static MutationScope? s_firstScope;
        private static MutationScope? s_secondScope;
        private static RetryPolicy? s_firstRetry;
        private static VelvetTaskCompletionSource s_retryWait = null!;
        private static System.Threading.CancellationTokenSource s_cancel = null!;
        private static MutationResult<int, int>? s_first;
        private static MutationResult<int, int>? s_second;
        private static readonly List<int> s_started = new();
        private static readonly Dictionary<int, VelvetTaskCompletionSource<int>> s_gates = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient();
            s_firstScope = new MutationScope("todos");
            s_secondScope = new MutationScope("todos");
            s_firstRetry = null;
            s_retryWait = new VelvetTaskCompletionSource();
            s_cancel = new System.Threading.CancellationTokenSource();
            s_first = null;
            s_second = null;
            s_started.Clear();
            s_gates.Clear();
        }

        // Every call a case left waiting is let through, so none is still paused when the next case starts.
        [TearDown]
        public void TearDown()
        {
            bool opened;
            do
            {
                opened = false;
                foreach (var gate in s_gates.Values.ToArray()) opened |= gate.TrySetResult(0);
            }
            while (opened);
            s_cancel.Cancel();
            s_retryWait.TrySetResult();
            s_cancel.Dispose();
            s_client.Clear();
        }

        [Test]
        public void Given_TwoCallsOfOneScope_When_TheFirstIsPending_Then_TheSecondHasNotStarted()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_first!.Mutate(1);
            s_second!.Mutate(2);

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Given_TwoCallsOfOneScope_When_TheFirstIsPending_Then_TheSecondIsPendingAndPaused()
        {
            // Arrange
            using var mounted = Mount();
            s_first!.Mutate(1);

            // Act
            s_second!.Mutate(2);

            // Assert
            Assert.That(States(), Is.EqualTo(new[] { "Pending paused:False", "Pending paused:True" }));
        }

        [UnityTest]
        public IEnumerator Given_TwoCallsOfOneScope_When_FramesPassWhileTheFirstIsPending_Then_TheSecondHasNotStarted()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            s_first!.Mutate(1);
            s_second!.Mutate(2);

            // Act — a paused call reads its conditions again on each frame.
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1 }));
        });

        [Test]
        public void Given_APendingCallOfTheScope_When_ASecondStarts_Then_ItIsAnnouncedPendingAndPausedBeforeItsPause()
        {
            // Arrange
            using var mounted = Mount();
            s_first!.Mutate(1);
            var updates = new List<string>();
            s_client.GetMutationCache().Subscribe(cacheEvent =>
            {
                if (cacheEvent.Type == MutationCacheEventType.Updated)
                {
                    updates.Add($"{cacheEvent.Action} paused:{cacheEvent.Mutation!.State.IsPaused}");
                }
            });

            // Act
            s_second!.Mutate(2);

            // Assert
            Assert.That(updates, Is.EqualTo(new[] { "Pending paused:True", "Pause paused:True" }));
        }

        [Test]
        public void Given_TwoCallsOfOneScope_When_TheFirstSucceeds_Then_TheSecondStartsAtOnce()
        {
            // Arrange
            using var mounted = Mount();
            s_first!.Mutate(1);
            s_second!.Mutate(2);

            // Act
            s_gates[1].TrySetResult(1);

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Given_TwoCallsOfOneScope_When_TheFirstFails_Then_TheSecondStartsAtOnce()
        {
            // Arrange
            using var mounted = Mount();
            s_first!.Mutate(1);
            s_second!.Mutate(2);

            // Act
            s_gates[1].TrySetException(new InvalidOperationException("first"));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Given_ThreeCallsOfOneScope_When_TheFirstSucceeds_Then_OnlyTheSecondStarts()
        {
            // Arrange
            using var mounted = Mount();
            s_first!.Mutate(1);
            s_second!.Mutate(2);
            s_first.Mutate(3);

            // Act
            s_gates[1].TrySetResult(1);

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Given_TwoCallsOfDifferentScopes_When_TheFirstIsPending_Then_TheSecondHasStarted()
        {
            // Arrange
            s_secondScope = new MutationScope("other");
            using var mounted = Mount();

            // Act
            s_first!.Mutate(1);
            s_second!.Mutate(2);

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Given_TwoCallsWithoutAScope_When_TheFirstIsPending_Then_TheSecondHasStarted()
        {
            // Arrange
            s_firstScope = null;
            s_secondScope = null;
            using var mounted = Mount();

            // Act
            s_first!.Mutate(1);
            s_second!.Mutate(2);

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1, 2 }));
        }

        // GREEN_ON_BASE(characterization): the base already asks canRun before a retry; removing `&& (callbacks.CanRun?.Invoke(state) ?? true)` from CanContinue reddens it.
        // A call taken out of the cache while it waits to retry no longer heads its scope; the paused call behind it does.
        [Test]
        public void Given_ARetryingCallRemovedAheadOfAPausedOne_When_ItsRetryIsDue_Then_ItDoesNotStartAgain()
        {
            // Arrange
            s_firstRetry = new RetryPolicy
            {
                Retry = 1,
                IsOnline = static () => true,
                IsFocused = static () => true,
                Wait = static (_, _) => s_retryWait.Task,
            };
            using var mounted = Mount();
            s_first!.Mutate(1, new MutateOptions<int, int> { CancellationToken = s_cancel.Token });
            s_second!.Mutate(2);
            s_gates[1].TrySetException(new InvalidOperationException("first attempt"));
            var cache = s_client.GetMutationCache();
            cache.Remove(cache.GetAll()[0]);

            // Act
            s_retryWait.TrySetResult();

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { 1 }));
        }

        private MountedTree Mount() => V.Mount(_root, V.Component(TwoMutationsRender, key: "scope"));

        private static string[] States() => s_client.GetMutationCache().GetAll()
            .Select(mutation => $"{mutation.State.Status} paused:{mutation.State.IsPaused}")
            .ToArray();

        private static MutationOptions<int, int> Gated(MutationScope? scope, RetryPolicy? retry = null) => new(MutationFn: (v, _) =>
        {
            s_started.Add(v);
            var gate = new VelvetTaskCompletionSource<int>();
            s_gates[v] = gate;
            return gate.Task;
        })
        {
            Scope = scope,
            Retry = retry,
        };

        [Component]
        private static VNode TwoMutationsRender()
        {
            s_first = Hooks.UseMutation(Gated(s_firstScope, s_firstRetry), s_client);
            s_second = Hooks.UseMutation(Gated(s_secondScope), s_client);
            return V.Label(text: "scope");
        }
    }
}
