using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <see cref="MutationCache"/> a <see cref="Hooks.UseMutation{TVariables, TData}"/> handle keeps
    /// its calls in, TanStack Query's <c>MutationCache</c>.
    /// <list type="bullet">
    /// <item>Each call is a <see cref="Mutation"/> in the client's cache, found by its <c>MutationKey</c> as a query
    /// filter finds a key, by its status and by a predicate; <see cref="MutationCache.Find"/> matches exactly unless
    /// told otherwise, and <see cref="QueryClient.IsMutating"/> counts the pending ones.</item>
    /// <item>The cache's callbacks run before the options' callback of the same name, are handed the context and
    /// the mutation with its <c>Meta</c>, and a throw from <c>OnMutate</c> fails the call while one from
    /// <c>OnError</c> costs no later callback.</item>
    /// <item>A listener hears a call's addition, the handle observing it, each change of state and the handle
    /// leaving it, and hears a commit of unequal options.</item>
    /// <item>A mutation the handle has left is removed once its <c>GcTime</c> has passed on the client's clock, not
    /// while a handle shows it, and one still pending then is checked again a <c>GcTime</c> later.</item>
    /// <item>A <c>MutationKey</c> replaced by an unequal one between commits leaves the handle idle.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class MutationCacheTests
    {
        private static readonly QueryKey Todos = new("todos");
        private static readonly QueryKey TodoOne = new("todos", 1);
        private static readonly TimeSpan GcTime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static MutationCache s_cache = null!;
        private static TimeSpan s_now;
        private static MutationOptions<int, int, string> s_options = null!;
        private static MutationResult<int, int>? s_captured;
        private static StateUpdater<int> s_rerender;
        private static VelvetTaskCompletionSource<int>? s_gate;
        private static readonly List<string> s_log = new();
        private static readonly List<string> s_events = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_log.Clear();
            s_events.Clear();
            s_captured = null;
            s_rerender = default;
            s_gate = null;
            InstallCache(new MutationCache());
            s_options = Logged(TodoOne);
        }

        // The cache's frame loop ends on the first frame after Clear.
        [TearDown]
        public void TearDown()
        {
            s_client.Clear();
            VelvetTaskFrameDriverTestExtensions.DrainEditorUpdateForTest();
        }

        #region Finding mutations

        [UnityTest]
        public IEnumerator Given_AMutationWithAKey_When_ItSucceeds_Then_AFilterLedByThatKeyFindsItsOutcome()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();

            // Act
            await s_captured!.MutateAsync(21);

            // Assert
            Assert.That(Outcomes(new MutationFilters { MutationKey = Todos }), Is.EqualTo(new[] { "Success:42" }));
        });

        [UnityTest]
        public IEnumerator Given_AnExactFilterNamingAPrefixOfTheKey_When_Applied_Then_ItSelectsNothing()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);

            // Act
            var selected = s_cache.FindAll(new MutationFilters { MutationKey = Todos, Exact = true }).Count;

            // Assert — read only over a cache holding the mutation, so a cache holding none cannot pass it.
            Assert.That(s_cache.GetAll().Count == 1 ? selected : -1, Is.EqualTo(0));
        });

        [UnityTest]
        public IEnumerator Given_AnExactFilterNamingTheWholeKey_When_Applied_Then_ItSelectsTheMutation()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);

            // Act
            var selected = s_cache.FindAll(new MutationFilters { MutationKey = TodoOne, Exact = true }).Count;

            // Assert
            Assert.That(selected, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Given_Find_When_ExactIsUnset_Then_APrefixOfTheKeyFindsNothing()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);

            // Act
            var found = s_cache.Find(new MutationFilters { MutationKey = Todos });

            // Assert — read only over a cache holding the mutation, so a cache holding none cannot pass it.
            Assert.That(s_cache.GetAll().Count == 1 ? found : (object)s_cache, Is.Null);
        });

        [UnityTest]
        public IEnumerator Given_Find_When_ExactIsFalse_Then_APrefixOfTheKeyFindsTheMutation()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);

            // Act
            var found = s_cache.Find(new MutationFilters { MutationKey = Todos, Exact = false });

            // Assert
            Assert.That(found, Is.SameAs(s_cache.GetAll().Single()));
        });

        [UnityTest]
        public IEnumerator Given_AMutationWithoutAKey_When_AKeyFilterIsApplied_Then_ItIsLeftOut()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_options = Logged(null);
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);

            // Act
            var selected = s_cache.FindAll(new MutationFilters { MutationKey = Todos }).Count;

            // Assert — read only over a cache holding the mutation, so a cache holding none cannot pass it.
            Assert.That(s_cache.GetAll().Count == 1 ? selected : -1, Is.EqualTo(0));
        });

        [Test]
        public void Given_APendingMutation_When_FilteredByThePendingStatus_Then_ItIsSelected()
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();
            s_captured!.Mutate(1);

            // Act
            var selected = s_cache.FindAll(new MutationFilters { Status = MutationStatus.Pending }).Count;

            // Assert
            Assert.That(selected, Is.EqualTo(1));
        }

        [Test]
        public void Given_APendingMutation_When_FilteredByAnotherStatus_Then_ItIsLeftOut()
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();
            s_captured!.Mutate(1);

            // Act
            var selected = s_cache.FindAll(new MutationFilters { Status = MutationStatus.Success }).Count;

            // Assert — read only over a cache holding the mutation, so a cache holding none cannot pass it.
            Assert.That(s_cache.GetAll().Count == 1 ? selected : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_APredicate_When_ItDeclinesTheMutation_Then_TheMutationIsLeftOut()
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();
            s_captured!.Mutate(1);

            // Act
            var selected = s_cache.FindAll(new MutationFilters { Predicate = _ => false }).Count;

            // Assert — read only over a cache holding the mutation, so a cache holding none cannot pass it.
            Assert.That(s_cache.GetAll().Count == 1 ? selected : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_APredicate_When_ItAcceptsTheMutation_Then_TheMutationIsSelected()
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();
            s_captured!.Mutate(1);

            // Act
            var selected = s_cache.FindAll(new MutationFilters { Predicate = _ => true }).Count;

            // Assert
            Assert.That(selected, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Given_OnePendingAndOneSettledMutation_When_TheClientIsAskedIsMutating_Then_ItCountsThePendingOne()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);
            s_gate = new VelvetTaskCompletionSource<int>();
            s_captured.Mutate(2);

            // Act
            var mutating = s_client.IsMutating(new MutationFilters { Status = MutationStatus.Success });

            // Assert
            Assert.That(mutating, Is.EqualTo(1), "IsMutating replaces the filters' status with Pending");
        });

        #endregion

        #region Cache callbacks

        [UnityTest]
        public IEnumerator Given_CacheCallbacks_When_AMutationSucceeds_Then_EachRunsBeforeTheOptionsCallbackOfItsName()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            InstallCache(LoggingCache());
            using var mounted = Mount();

            // Act
            await s_captured!.MutateAsync(1);

            // Assert
            Assert.That(s_log, Is.EqualTo(new[]
            {
                "cache.onMutate", "onMutate", "fn", "cache.onSuccess", "onSuccess", "cache.onSettled", "onSettled",
            }));
        });

        [UnityTest]
        public IEnumerator Given_CacheCallbacks_When_AMutationFails_Then_EachRunsBeforeTheOptionsCallbackOfItsName()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            InstallCache(LoggingCache());
            s_options = Logged(TodoOne, fails: true);
            using var mounted = Mount();

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }

            // Assert
            Assert.That(s_log, Is.EqualTo(new[]
            {
                "cache.onMutate", "onMutate", "fn", "cache.onError", "onError", "cache.onSettled", "onSettled",
            }));
        });

        [UnityTest]
        public IEnumerator Given_ACacheOnMutateThatThrows_When_AMutationStarts_Then_TheCallFailsBeforeTheOptionsOnMutate()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            InstallCache(LoggingCache(throwOnMutate: true));
            using var mounted = Mount();

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "cache.onMutate", "cache.onError", "onError", "cache.onSettled", "onSettled" }));
        });

        [UnityTest]
        public IEnumerator Given_ACacheOnErrorThatThrows_When_AMutationFails_Then_TheLaterCallbacksStillRun()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            InstallCache(LoggingCache(throwOnError: true));
            s_options = Logged(TodoOne, fails: true);
            using var mounted = Mount();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: cache.onError"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }

            // Assert
            Assert.That(s_log, Is.EqualTo(new[]
            {
                "cache.onMutate", "onMutate", "fn", "cache.onError", "onError", "cache.onSettled", "onSettled",
            }));
        });

        [UnityTest]
        public IEnumerator Given_AMutationWithMetaAndAContext_When_ItSucceeds_Then_TheCacheCallbackIsHandedBoth()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            InstallCache(new MutationCache
            {
                OnSuccess = (data, variables, context, mutation, _) =>
                {
                    s_log.Add($"{data}|{variables}|{context}|{mutation.Meta!["toast"]}");
                    return VelvetTask.CompletedTask;
                },
            });
            s_options = Logged(TodoOne) with { Meta = new Dictionary<string, object?> { ["toast"] = "saved" } };
            using var mounted = Mount();

            // Act
            await s_captured!.MutateAsync(21);

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "onMutate", "fn", "42|21|context-21|saved", "onSuccess", "onSettled" }));
        });

        #endregion

        #region Events

        [UnityTest]
        public IEnumerator Given_AMountedHandle_When_ACallSucceeds_Then_TheListenerHearsItsWholeCourse()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_options = Logged(TodoOne, withContext: false);
            using var mounted = Mount();
            s_cache.Subscribe(Record);

            // Act
            await s_captured!.MutateAsync(1);

            // Assert
            Assert.That(s_events, Is.EqualTo(new[] { "Added 1", "ObserverAdded 1", "Updated:Pending 1", "Updated:Success 1" }));
        });

        [UnityTest]
        public IEnumerator Given_AnOnMutateReturningAContext_When_ACallStarts_Then_TheContextIsAnnouncedAsASecondPending()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            s_cache.Subscribe(Record);

            // Act
            await s_captured!.MutateAsync(1);

            // Assert
            Assert.That(s_events, Is.EqualTo(new[]
            {
                "Added 1", "ObserverAdded 1", "Updated:Pending 1", "Updated:Pending 1", "Updated:Success 1",
            }));
        });

        [UnityTest]
        public IEnumerator Given_AHandleShowingACall_When_ASecondCallStarts_Then_TheHandleLeavesTheFirstBeforeTheSecondIsAdded()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_options = Logged(TodoOne, withContext: false);
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);
            s_cache.Subscribe(Record);

            // Act
            await s_captured.MutateAsync(2);

            // Assert
            Assert.That(s_events, Is.EqualTo(new[]
            {
                "ObserverRemoved 1", "Added 2", "ObserverAdded 2", "Updated:Pending 2", "Updated:Success 2",
            }));
        });

        [UnityTest]
        public IEnumerator Given_AHandleShowingACall_When_Reset_Then_TheListenerHearsTheHandleLeaveIt()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);
            s_cache.Subscribe(Record);

            // Act
            s_captured.Reset();

            // Assert
            Assert.That(s_events, Is.EqualTo(new[] { "ObserverRemoved 1" }));
        });

        [UnityTest]
        public IEnumerator Given_AHandleShowingACall_When_TheComponentUnmounts_Then_TheListenerHearsTheHandleLeaveIt()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var mounted = Mount();
            await s_captured!.MutateAsync(1);
            s_cache.Subscribe(Record);

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_events, Is.EqualTo(new[] { "ObserverRemoved 1" }));
        });

        [Test]
        public void Given_AListener_When_AHandleMounts_Then_ItHearsTheFirstOptionsWithNoMutation()
        {
            // Arrange
            s_cache.Subscribe(Record);

            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(s_events, Is.EqualTo(new[] { "ObserverOptionsUpdated none" }));
        }

        [Test]
        public void Given_AMountedHandle_When_ARenderPassesUnequalOptions_Then_TheCommitIsAnnounced()
        {
            // Arrange
            using var mounted = Mount();
            s_cache.Subscribe(Record);
            s_options = Logged(TodoOne);

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_events, Is.EqualTo(new[] { "ObserverOptionsUpdated none" }));
        }

        [Test]
        public void Given_AMountedHandle_When_ACommitPassesAnEqualRecordAndThenAnUnequalOne_Then_OnlyTheUnequalOneIsAnnounced()
        {
            // Arrange — the unequal commit is what shows the listener hears a commit at all; v5's shallow
            // comparison of the options is what keeps the equal one silent.
            using var mounted = Mount();
            s_cache.Subscribe(Record);

            // Act
            s_options = s_options with { };
            Rerender(mounted);
            s_options = Logged(TodoOne);
            Rerender(mounted);

            // Assert
            Assert.That(s_events, Is.EqualTo(new[] { "ObserverOptionsUpdated none" }));
        }

        [UnityTest]
        public IEnumerator Given_ACacheHoldingAMutation_When_TheClientIsCleared_Then_TheRemovalIsAnnounced()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);
            s_cache.Subscribe(Record);

            // Act
            s_client.Clear();

            // Assert
            Assert.That(s_events, Is.EqualTo(new[] { "Removed 1" }));
        });

        #endregion

        #region Mutation key

        [UnityTest]
        public IEnumerator Given_ASucceededHandle_When_ACommitChangesItsMutationKey_Then_TheHandleIsIdle()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);
            mounted.FlushStateForTest();
            s_options = Logged(new QueryKey("todos", 2));

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_captured.Status, Is.EqualTo(MutationStatus.Idle));
        });

        [UnityTest]
        public IEnumerator Given_ASucceededHandle_When_ACommitPassesNewOptionsUnderTheSameKey_Then_TheHandleKeepsItsOutcome()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);
            mounted.FlushStateForTest();
            s_options = Logged(new QueryKey("todos", 1));

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_captured.Status, Is.EqualTo(MutationStatus.Success));
        });

        [Test]
        public void Given_APendingHandle_When_ACommitPassesNewMeta_Then_ThePendingMutationTakesIt()
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            using var mounted = Mount();
            s_captured!.Mutate(1);
            s_options = s_options with { Meta = new Dictionary<string, object?> { ["toast"] = "later" } };

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_cache.GetAll().Single().Meta?["toast"], Is.EqualTo("later"));
        }

        #endregion

        #region Garbage collection

        [UnityTest]
        public IEnumerator Given_AnUnmountedHandlesSettledCall_When_FramesPassJustShortOfItsGcTime_Then_ItIsKept()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var mounted = Mount();
            await s_captured!.MutateAsync(1);
            mounted.Dispose();

            // Act
            await Pass(GcTime - Tick);

            // Assert
            Assert.That(s_cache.GetAll().Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Given_AnUnmountedHandlesSettledCall_When_ItsGcTimeHasPassed_Then_ItIsRemoved()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var mounted = Mount();
            await s_captured!.MutateAsync(1);
            mounted.Dispose();
            s_cache.Subscribe(Record);

            // Act
            await Pass(GcTime);

            // Assert — the removal's own event, so a mutation never added cannot pass it.
            Assert.That(s_events, Is.EqualTo(new[] { "Removed 1" }));
        });

        [UnityTest]
        public IEnumerator Given_AShortGcTime_When_ItPassesAfterTheHandleLeaves_Then_TheCallIsRemoved()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_options = Logged(TodoOne) with { GcTime = TimeSpan.FromSeconds(1) };
            var mounted = Mount();
            await s_captured!.MutateAsync(1);
            mounted.Dispose();

            // Act
            await Pass(TimeSpan.FromSeconds(1));

            // Assert
            Assert.That(s_cache.GetAll().Count, Is.EqualTo(0));
        });

        [UnityTest]
        public IEnumerator Given_AHandleShowingASettledCall_When_ItsGcTimePasses_Then_TheCallIsKept()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount();
            await s_captured!.MutateAsync(1);

            // Act
            await Pass(GcTime);

            // Assert
            Assert.That(s_cache.GetAll().Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Given_AnUnobservedPendingCall_When_ItsGcTimePasses_Then_ItIsKeptAndRemovedAGcTimeLater()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_gate = new VelvetTaskCompletionSource<int>();
            var mounted = Mount();
            s_captured!.Mutate(1);
            mounted.Dispose();
            s_cache.Subscribe(Record);

            // Act
            await Pass(GcTime);
            s_gate.TrySetResult(1);
            await Pass(GcTime);

            // Assert — one sequence: a removal heard before the success would be the pending call collected.
            Assert.That(s_events.Where(e => e.StartsWith("Removed") || e.StartsWith("Updated:Success")),
                Is.EqualTo(new[] { "Updated:Success 1", "Removed 1" }));
        });

        #endregion

        private static void InstallCache(MutationCache cache)
        {
            s_cache = cache;
            s_client = new QueryClient(new QueryClientOptions { MutationCache = cache, Clock = () => s_now });
        }

        // The mount's passive effects run here, so what a case subscribes afterwards hears none of them.
        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(MutationRender, key: "mutation"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        // FlushStateForTest runs the effects earlier commits left before it renders, so the re-render's own effects,
        // where the observer takes the new options, are flushed after it.
        private static void Rerender(MountedTree mounted)
        {
            s_rerender.Invoke(v => v + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static async VelvetTask Pass(TimeSpan time)
        {
            s_now += time;
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
        }

        private static string[] Outcomes(MutationFilters filters)
            => s_cache.FindAll(filters).Select(mutation => $"{mutation.State.Status}:{mutation.State.Data}").ToArray();

        private static void Record(MutationCacheEvent cacheEvent)
        {
            var action = cacheEvent.Action is { } updated ? ":" + updated : "";
            var mutation = cacheEvent.Mutation?.MutationId.ToString() ?? "none";
            s_events.Add($"{cacheEvent.Type}{action} {mutation}");
        }

        // Doubles the variables, or throws when fails; gated by s_gate when a case sets one. Without a context the
        // options declare no OnMutate, so the mutation's state carries no context.
        private static MutationOptions<int, int, string> Logged(QueryKey? key, bool fails = false, bool withContext = true)
            => new(
                MutationFn: (v, _) =>
                {
                    s_log.Add("fn");
                    if (fails) throw new InvalidOperationException("fn");
                    return s_gate?.Task ?? VelvetTask.FromResult(v * 2);
                },
                OnMutate: withContext
                    ? (v, _) =>
                    {
                        s_log.Add("onMutate");
                        return VelvetTask.FromResult($"context-{v}");
                    }
                    : null,
                OnSuccess: (_, _, _, _) => Log("onSuccess"),
                OnError: (_, _, _, _) => Log("onError"),
                OnSettled: (_, _, _, _, _) => Log("onSettled"))
            {
                MutationKey = key,
            };

        private static MutationCache LoggingCache(bool throwOnMutate = false, bool throwOnError = false) => new()
        {
            OnMutate = (_, _, _) =>
            {
                Log("cache.onMutate");
                return throwOnMutate ? throw new InvalidOperationException("cache.onMutate") : VelvetTask.CompletedTask;
            },
            OnSuccess = (_, _, _, _, _) => Log("cache.onSuccess"),
            OnError = (_, _, _, _, _) =>
            {
                Log("cache.onError");
                return throwOnError ? throw new InvalidOperationException("cache.onError") : VelvetTask.CompletedTask;
            },
            OnSettled = (_, _, _, _, _, _) => Log("cache.onSettled"),
        };

        private static VelvetTask Log(string entry)
        {
            s_log.Add(entry);
            return VelvetTask.CompletedTask;
        }

        [Component]
        private static VNode MutationRender()
        {
            var (_, rerender) = Hooks.UseState(0);
            s_rerender = rerender;
            s_captured = Hooks.UseMutation(s_options, s_client);
            return V.Label(text: s_captured.Status.ToString());
        }
    }
}
