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
    /// Specifies <see cref="Hooks.UseMutationState{TResult}"/> and <see cref="Hooks.UseIsMutating"/>, TanStack Query's
    /// <c>useMutationState</c> and <c>useIsMutating</c>.
    /// <list type="bullet">
    /// <item>The hook returns what the selector makes of each mutation the filters select, in the order the
    /// mutations were added, and each mutation's state without a selector.</item>
    /// <item>A component reading it re-renders when a selected mutation changes, and keeps the very list it
    /// returned when a change leaves what it selects deeply equal.</item>
    /// <item><c>UseIsMutating</c> counts the pending mutations its filters select.</item>
    /// <item>A selector is required where the result type cannot hold a mutation's state, and a client is required
    /// as it is for <c>UseMutation</c>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UseMutationStateTests
    {
        private static readonly QueryKey Todos = new("todos");
        private static readonly QueryKey Users = new("users");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static MutationResult<int, int>? s_todos;
        private static MutationResult<int, int>? s_users;
        private static IReadOnlyList<object?>? s_selected;
        private static IReadOnlyList<MutationState>? s_states;
        private static int s_mutating;
        private static int s_freshRenders;
        private static VelvetTaskCompletionSource<int>? s_gate;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient();
            s_todos = null;
            s_users = null;
            s_selected = null;
            s_states = null;
            s_mutating = -1;
            s_freshRenders = 0;
            s_gate = null;
        }

        [TearDown]
        public void TearDown()
        {
            s_gate?.TrySetResult(0);
            s_client.Clear();
        }

        [UnityTest]
        public IEnumerator Given_MutationsUnderTwoKeys_When_OneKeyIsSelected_Then_TheSelectorRunsOverItsMutationsInOrder()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_todos!.MutateAsync(1);
            await s_users!.MutateAsync(2);
            await s_todos.MutateAsync(3);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_selected, Is.EqualTo(new object[] { 1, 3 }));
        });

        [UnityTest]
        public IEnumerator Given_NoSelector_When_MutationsAreSelected_Then_EachMutationsStateIsReturned()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_users!.MutateAsync(2);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_states!.Select(state => $"{state.Status}:{state.Variables}:{state.Data}"), Is.EqualTo(new[] { "Success:2:4" }));
        });

        [Test]
        public void Given_AReadingComponent_When_ASelectedMutationStarts_Then_TheComponentRendersIt()
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();

            // Act
            s_todos!.Mutate(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_selected, Is.EqualTo(new object[] { 5 }));
        }

        [UnityTest]
        public IEnumerator Given_AReaderWhoseSelectorBuildsAFreshValue_When_AnUnselectedMutationChanges_Then_ItIsNotRenderedAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — a fresh object is never deeply equal to the last one, so only leaving the selected
            // mutations unread keeps the list, and the render, from changing.
            using var mounted = Mount();
            await s_todos!.MutateAsync(1);
            mounted.FlushStateForTest();
            var before = s_freshRenders;

            // Act
            await s_users!.MutateAsync(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_freshRenders, Is.EqualTo(before));
        });

        [UnityTest]
        public IEnumerator Given_ASelectedMutation_When_ItsStateMovesButTheSelectionStaysEqual_Then_TheReturnedListIsTheSameInstance()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();
            s_todos!.Mutate(1);
            mounted.FlushStateForTest();
            var pending = s_selected;

            // Act
            s_gate.TrySetResult(2);
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_selected, Is.SameAs(pending), "The variables a pending and a settled call select are equal");
        });

        [Test]
        public void Given_APendingMutationUnderEachKey_When_TheCountIsFilteredByOneKey_Then_ItCountsOne()
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();

            // Act
            s_todos!.Mutate(1);
            s_users!.Mutate(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_mutating, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_APendingMutation_When_ItSettles_Then_TheCountReturnsToZero()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();
            s_todos!.Mutate(1);
            mounted.FlushStateForTest();
            var whilePending = s_mutating;

            // Act
            s_gate.TrySetResult(1);
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert — read only when the pending call was counted, so a count that never moved cannot pass it.
            Assert.That(whilePending == 1 ? s_mutating : -1, Is.EqualTo(0));
        });

        [Test]
        public void Given_AResultTypeAStateCannotBe_When_NoSelectorIsGiven_Then_TheHookThrows()
        {
            // Act + Assert — the argument is checked before the hook's render check, so this runs outside a render.
            Assert.Throws<ArgumentException>(() => Hooks.UseMutationState(new MutationStateOptions<int>(), s_client));
        }

        [Test]
        public void Given_NoClient_When_TheHookRenders_Then_ItThrows()
        {
            // Arrange
            Exception? caught = null;

            // Act
            using var mounted = V.Mount(_root, V.ErrorBoundary(exception =>
            {
                caught = exception;
                return V.Label(text: "caught");
            }, new VNode[] { V.Component(ClientlessRender, key: "clientless") }), CaughtErrors.Unlogged);

            // Assert
            Assert.That(caught?.Message, Does.StartWith("UseMutationState found no QueryClient"));
        }

        private MountedTree Mount() => V.Mount(_root, V.Provider(QueryClientContext.Ref, s_client,
            children: new VNode[]
            {
                V.Component(MutatingRender, key: "mutating"),
                V.Component(ReadingRender, key: "reading"),
                V.Component(FreshReadingRender, key: "fresh"),
            }));

        private static MutationOptions<int, int> Keyed(QueryKey key) => new(
            MutationFn: (v, _) => s_gate?.Task ?? VelvetTask.FromResult(v * 2))
        {
            MutationKey = key,
        };

        [Component]
        private static VNode MutatingRender()
        {
            s_todos = Hooks.UseMutation(Keyed(Todos));
            s_users = Hooks.UseMutation(Keyed(Users));
            return V.Label(text: "mutating");
        }

        [Component]
        private static VNode ReadingRender()
        {
            s_selected = Hooks.UseMutationState(new MutationStateOptions<object?>
            {
                Filters = new MutationFilters { MutationKey = Todos },
                Select = mutation => mutation.State.Variables,
            });
            s_states = Hooks.UseMutationState(new MutationFilters { MutationKey = Users });
            s_mutating = Hooks.UseIsMutating(new MutationFilters { MutationKey = Todos });
            return V.Label(text: s_mutating.ToString());
        }

        [Component]
        private static VNode FreshReadingRender()
        {
            s_freshRenders++;
            var fresh = Hooks.UseMutationState(new MutationStateOptions<object>
            {
                Filters = new MutationFilters { MutationKey = Todos },
                Select = _ => new object(),
            });
            return V.Label(text: fresh.Count.ToString());
        }

        [Component]
        private static VNode ClientlessRender()
        {
            _ = Hooks.UseMutationState();
            return V.Label(text: "never");
        }
    }
}
