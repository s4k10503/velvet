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
    /// Specifies what a <see cref="Hooks.UseMutation{TVariables, TData}"/> handle shares with TanStack Query v5's
    /// <c>MutationObserver</c> and <c>QueryClient</c> beyond the cache itself.
    /// <list type="bullet">
    /// <item>A call keeps the options it was built with once a newer call supersedes it, while the call the handle
    /// shows takes a later commit's options.</item>
    /// <item>A call made after the component unmounts is observed and so not collected, and the mutation the handle
    /// last showed hears the observer leave it again.</item>
    /// <item>The function and the callbacks are handed a <see cref="MutationFunctionContext"/> of the client and the
    /// options' meta and key; the per-call callbacks one built from the options the handle last committed.</item>
    /// <item>Observer events name the observer.</item>
    /// <item>Options are filled in from <see cref="QueryClientOptions.Mutations"/> and from the defaults registered
    /// for a key leading theirs, a later registration winning, and a function can come from there.</item>
    /// <item><see cref="QueryClient.ResumePausedMutations"/> continues a paused call at once while online.</item>
    /// <item><see cref="MutationOptionsBase.ThrowOnError"/> throws the failure the handle shows to the nearest
    /// error boundary.</item>
    /// <item>A scoped call that the scope frees while the device is offline stays paused.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class MutationObserverParityTests
    {
        private static readonly QueryKey Todos = new("todos");
        private static readonly QueryKey TodoOne = new("todos", 1);
        private static readonly TimeSpan GcTime = TimeSpan.FromMinutes(5);

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static MutationOptions<int, int, string> s_options = null!;
        private static MutationResult<int, int>? s_captured;
        private static StateUpdater<int> s_rerender;
        private static readonly List<string> s_log = new();
        private static readonly Dictionary<int, VelvetTaskCompletionSource<int>> s_gates = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_log.Clear();
            s_gates.Clear();
            s_captured = null;
            s_rerender = default;
            s_handed = null;
            s_client = NewClient(null);
            s_options = Labelled("A");
        }

        [TearDown]
        public void TearDown()
        {
            MutationNetworkFixture.Reset();
            foreach (var gate in s_gates.Values.ToArray()) gate.TrySetResult(0);
            s_client.Clear();
            VelvetTaskFrameDriverTestExtensions.DrainEditorUpdateForTest();
        }

        #region Options a call keeps

        [Test]
        public void Given_TwoCallsBuiltWithOneOptions_When_ACommitPassesNewOptionsAndBothSettle_Then_OnlyTheShownCallTakesThem()
        {
            // Arrange
            s_options = Labelled("A", gated: true);
            using var mounted = Mount();
            s_captured!.Mutate(1);
            s_captured.Mutate(2);
            s_options = Labelled("B", gated: true);
            Rerender(mounted);

            // Act
            s_gates[1].TrySetResult(1);
            s_gates[2].TrySetResult(2);

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "A:1", "B:2" }));
        }

        #endregion

        #region Mutate after unmount

        [UnityTest]
        public IEnumerator Given_AnUnmountedHandle_When_ItMutatesAndItsGcTimePasses_Then_TheCallIsKept()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var mounted = Mount();
            mounted.Dispose();
            await s_captured!.MutateAsync(1);

            // Act
            s_now += GcTime + GcTime;
            await VelvetTask.Yield();
            await VelvetTask.Yield();

            // Assert
            Assert.That(s_client.GetMutationCache().GetAll().Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Given_AnUnmountedHandleThatShowedACall_When_ItMutates_Then_TheObserverLeavesTheOldCallAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var mounted = Mount();
            await s_captured!.MutateAsync(1);
            mounted.Dispose();
            var events = new List<string>();
            s_client.GetMutationCache().Subscribe(cacheEvent =>
            {
                if (cacheEvent.Type != MutationCacheEventType.Updated) events.Add($"{cacheEvent.Type} {cacheEvent.Mutation!.MutationId}");
            });

            // Act
            await s_captured.MutateAsync(2);

            // Assert
            Assert.That(events, Is.EqualTo(new[] { "ObserverRemoved 1", "Added 2", "ObserverAdded 2" }));
        });

        #endregion

        #region Function context

        [UnityTest]
        public IEnumerator Given_AMountedHandle_When_TheFunctionRuns_Then_ItIsHandedTheClient()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount(KeyedAndCarryingMeta());

            // Act
            await s_captured!.MutateAsync(1);

            // Assert
            Assert.That(s_handed?.Client, Is.SameAs(s_client));
        });

        [UnityTest]
        public IEnumerator Given_OptionsWithMeta_When_TheFunctionRuns_Then_ItIsHandedTheMeta()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount(KeyedAndCarryingMeta());

            // Act
            await s_captured!.MutateAsync(1);

            // Assert
            Assert.That(s_handed?.Meta?["toast"], Is.EqualTo("saved"));
        });

        [UnityTest]
        public IEnumerator Given_OptionsWithAKey_When_TheFunctionRuns_Then_ItIsHandedTheKey()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = Mount(KeyedAndCarryingMeta());

            // Act
            await s_captured!.MutateAsync(1);

            // Assert
            Assert.That(s_handed?.MutationKey, Is.EqualTo(TodoOne));
        });

        [UnityTest]
        public IEnumerator Given_ACacheCallbackAndAnOptionsCallback_When_ACallSucceeds_Then_TheyAreHandedTheFunctionsContext()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var handed = new List<MutationFunctionContext>();
            s_client = NewClient(new MutationCache
            {
                OnSuccess = (_, _, _, _, context) =>
                {
                    handed.Add(context);
                    return VelvetTask.CompletedTask;
                },
            });
            s_options = new MutationOptions<int, int, string>(
                MutationFn: (v, context) =>
                {
                    handed.Add(context);
                    return VelvetTask.FromResult(v);
                },
                OnSuccess: (_, _, _, context) =>
                {
                    handed.Add(context);
                    return VelvetTask.CompletedTask;
                });
            using var mounted = Mount();

            // Act
            await s_captured!.MutateAsync(1);

            // Assert — counted only once all three were handed one, so a callback that never ran cannot pass it.
            Assert.That(handed.Count == 3 ? handed.Distinct().Count() : -1, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Given_APerCallCallback_When_TheCallSucceeds_Then_ItsContextCarriesTheCommittedMeta()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            object? handedMeta = null;
            s_options = Labelled("A") with { Meta = new Dictionary<string, object?> { ["toast"] = "committed" } };
            using var mounted = Mount();
            mounted.FlushEffectsForTest();

            // Act
            await s_captured!.MutateAsync(1, new MutateOptions<int, int>
            {
                OnSuccess = (_, _, _, context) => handedMeta = context.Meta?["toast"],
            });

            // Assert
            Assert.That(handedMeta, Is.EqualTo("committed"));
        });

        #endregion

        [UnityTest]
        public IEnumerator Given_AMountedHandle_When_ACallStarts_Then_OnlyTheObserverEventsNameTheObserver()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_options = new MutationOptions<int, int, string>(MutationFn: (v, _) => VelvetTask.FromResult(v));
            using var mounted = Mount();
            var events = new List<string>();
            s_client.GetMutationCache().Subscribe(cacheEvent => events.Add($"{cacheEvent.Type}:{cacheEvent.Observer != null}"));

            // Act
            await s_captured!.MutateAsync(1);

            // Assert
            Assert.That(events, Is.EqualTo(new[] { "Added:False", "ObserverAdded:True", "Updated:False", "Updated:False" }));
        });

        #region Defaults

        [Test]
        public void Given_ClientDefaults_When_NeitherTheKeyDefaultsNorTheOptionsSetAField_Then_TheClientsValueIsTaken()
        {
            // Arrange
            LayerDefaults();

            // Act
            var options = s_client.DefaultMutationOptions(s_options);

            // Assert
            Assert.That(options.Meta?["layer"], Is.EqualTo("client"));
        }

        [Test]
        public void Given_KeyDefaults_When_TheOptionsLeaveAFieldTheClientAlsoSets_Then_TheKeysValueIsTaken()
        {
            // Arrange
            LayerDefaults();

            // Act
            var options = s_client.DefaultMutationOptions(s_options);

            // Assert
            Assert.That(options.Scope?.Id, Is.EqualTo("key-scope"));
        }

        [Test]
        public void Given_OptionsSettingAField_When_BothDefaultsSetItToo_Then_TheOptionsValueIsKept()
        {
            // Arrange
            LayerDefaults();

            // Act
            var options = s_client.DefaultMutationOptions(s_options);

            // Assert
            Assert.That(options.GcTime, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }

        [UnityTest]
        public IEnumerator Given_AFunctionInTheClientsTypedDefaults_When_AKeyRegistrationOfAnotherTypeAlsoMatches_Then_TheClientsFunctionRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the key registration sets no function, so v5's spread keeps the client's too; a record of
            // another type laid over the client's must not take away the function it supplies.
            s_client = NewClient(null, new MutationOptions<int, int, string>(MutationFn: (v, _) => VelvetTask.FromResult(v * 5)));
            s_client.SetMutationDefaults(Todos, new MutationOptions<string, string>(MutationFn: null) { GcTime = TimeSpan.FromSeconds(1) });
            s_options = new MutationOptions<int, int, string>(MutationFn: null) { MutationKey = TodoOne };
            using var mounted = Mount();

            // Act
            var result = await s_captured!.MutateAsync(3);

            // Assert
            Assert.That(result, Is.EqualTo(15));
        });

        [Test]
        public void Given_TwoRegistrationsLeadingAKey_When_TheDefaultsAreRead_Then_TheLaterOneWins()
        {
            // Arrange
            s_client.SetMutationDefaults(TodoOne, new MutationDefaults { Meta = new Dictionary<string, object?> { ["by"] = "first" } });
            s_client.SetMutationDefaults(Todos, new MutationDefaults { Meta = new Dictionary<string, object?> { ["by"] = "second" } });

            // Act
            var defaults = s_client.GetMutationDefaults(TodoOne);

            // Assert
            Assert.That(defaults.Meta?["by"], Is.EqualTo("second"));
        }

        [Test]
        public void Given_AKeyRegisteredAgain_When_TheDefaultsAreRead_Then_ItKeepsItsPlaceInTheOrder()
        {
            // Arrange
            s_client.SetMutationDefaults(TodoOne, new MutationDefaults { Meta = new Dictionary<string, object?> { ["by"] = "first" } });
            s_client.SetMutationDefaults(Todos, new MutationDefaults { Meta = new Dictionary<string, object?> { ["by"] = "second" } });
            s_client.SetMutationDefaults(TodoOne, new MutationDefaults { Meta = new Dictionary<string, object?> { ["by"] = "first again" } });

            // Act
            var defaults = s_client.GetMutationDefaults(TodoOne);

            // Assert
            Assert.That(defaults.Meta?["by"], Is.EqualTo("second"));
        }

        [UnityTest]
        public IEnumerator Given_AFunctionRegisteredForTheKey_When_OptionsWithoutOneMutate_Then_TheRegisteredFunctionRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client.SetMutationDefaults(Todos, new MutationOptions<int, int, string>(MutationFn: (v, _) => VelvetTask.FromResult(v * 7)));
            s_options = new MutationOptions<int, int, string>(MutationFn: null) { MutationKey = TodoOne };
            using var mounted = Mount();

            // Act
            var result = await s_captured!.MutateAsync(3);

            // Assert
            Assert.That(result, Is.EqualTo(21));
        });

        [UnityTest]
        public IEnumerator Given_NoFunctionAnywhere_When_TheHandleMutates_Then_TheCallFailsAsV5Does()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_options = new MutationOptions<int, int, string>(MutationFn: null);
            using var mounted = Mount();
            Exception? failure = null;

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException caught) { failure = caught; }

            // Assert
            Assert.That(failure?.Message, Is.EqualTo("No mutationFn found"));
        });

        #endregion

        #region Resuming paused mutations

        [Test]
        public void Given_ACallPausedOffline_When_TheDeviceIsOnlineAndTheClientResumes_Then_TheFunctionRunsBeforeAnyFrame()
        {
            // Arrange
            var online = false;
            NetworkSignals.IsOnline = () => online;
            s_options = Labelled("A", gated: true);
            using var mounted = Mount();
            s_captured!.Mutate(1);
            online = true;

            // Act
            s_client.ResumePausedMutations().Forget();

            // Assert
            Assert.That(s_gates.Keys, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Given_ACallPausedOffline_When_TheClientResumesWhileStillOffline_Then_TheTaskCompletesWithoutWaitingForIt()
        {
            // Arrange — woken while offline, the call would pause again and the task would wait on its function.
            NetworkSignals.IsOnline = () => false;
            s_options = Labelled("A", gated: true);
            using var mounted = Mount();
            s_captured!.Mutate(1);

            // Act
            var resumed = s_client.ResumePausedMutations();

            // Assert
            Assert.That(resumed.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        #endregion

        #region throwOnError

        [UnityTest]
        public IEnumerator Given_ThrowOnErrorTrue_When_TheCallFails_Then_TheErrorBoundaryCatchesTheFailure()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var failure = new InvalidOperationException("save failed");
            s_options = Failing(failure) with { ThrowOnError = true };
            Exception? caught = null;
            using var mounted = V.Mount(_root, V.ErrorBoundary(exception =>
            {
                caught = exception;
                return V.Label(text: "caught");
            }, new VNode[] { V.Component(MutationRender, key: "mutation") }), CaughtErrors.Unlogged);

            // Act
            s_captured!.Mutate(1);
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(caught, Is.SameAs(failure));
        });

        [UnityTest]
        public IEnumerator Given_ThrowOnErrorDecliningTheFailure_When_TheCallFails_Then_TheHandleShowsIt()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var failure = new InvalidOperationException("save failed");
            s_options = Failing(failure) with { ThrowOnError = ThrowOnErrorRule.When(error => error is TimeoutException) };
            Exception? caught = null;
            using var mounted = V.Mount(_root, V.ErrorBoundary(exception =>
            {
                caught = exception;
                return V.Label(text: "caught");
            }, new VNode[] { V.Component(MutationRender, key: "mutation") }), CaughtErrors.Unlogged);

            // Act
            s_captured!.Mutate(1);
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            // Assert — read only once the handle shows the failure, so a call that never failed cannot pass it.
            Assert.That(ReferenceEquals(s_captured.Error, failure) ? caught : (object)s_captured, Is.Null);
        });

        #endregion

        #region Scope freed while offline

        [Test]
        public void Given_AScopedCallWaitingInOnMutate_When_TheScopeFreesWhileOffline_Then_ItPausesWithoutContinuing()
        {
            // Arrange — the call ahead settles while the second is still in its OnMutate, and the device is offline
            // by the time OnMutate returns.
            var online = true;
            NetworkSignals.IsOnline = () => online;
            var onMutateGate = new VelvetTaskCompletionSource<string>();
            var scope = new MutationScope("todos");
            s_options = Labelled("A", gated: true) with { Scope = scope };
            using var mounted = Mount();
            s_captured!.Mutate(1);
            s_options = new MutationOptions<int, int, string>(
                MutationFn: (v, _) => VelvetTask.FromResult(v),
                OnMutate: (_, _) => onMutateGate.Task)
            {
                Scope = scope,
            };
            Rerender(mounted);
            s_captured.Mutate(2);
            online = false;
            s_gates[1].TrySetResult(1);
            var updates = new List<string>();
            s_client.GetMutationCache().Subscribe(cacheEvent =>
            {
                if (cacheEvent.Type == MutationCacheEventType.Updated && cacheEvent.Mutation!.MutationId == 2)
                {
                    updates.Add($"{cacheEvent.Action} paused:{cacheEvent.Mutation.State.IsPaused}");
                }
            });

            // Act
            onMutateGate.TrySetResult("context");

            // Assert
            Assert.That(updates, Is.EqualTo(new[] { "Pending paused:True", "Pause paused:True" }));
        }

        #endregion

        private static MutationFunctionContext? s_handed;

        private static MutationOptions<int, int, string> KeyedAndCarryingMeta() => new(MutationFn: (v, context) =>
        {
            s_handed = context;
            return VelvetTask.FromResult(v);
        })
        {
            MutationKey = TodoOne,
            Meta = new Dictionary<string, object?> { ["toast"] = "saved" },
        };

        // Client defaults set Meta, Scope and GcTime; key defaults set Scope and GcTime; the options set GcTime.
        private static void LayerDefaults()
        {
            s_client = NewClient(null, new MutationDefaults
            {
                Meta = new Dictionary<string, object?> { ["layer"] = "client" },
                Scope = new MutationScope("client-scope"),
                GcTime = TimeSpan.FromSeconds(1),
            });
            s_client.SetMutationDefaults(Todos, new MutationDefaults { Scope = new MutationScope("key-scope"), GcTime = TimeSpan.FromSeconds(2) });
            s_options = Labelled("A") with { MutationKey = TodoOne, GcTime = TimeSpan.FromSeconds(3) };
        }

        private static QueryClient NewClient(MutationCache? cache, MutationOptionsBase? mutations = null)
            => new(new QueryClientOptions { MutationCache = cache, Mutations = mutations, Clock = () => s_now });

        // The mount's passive effects run here, so the observer holds the first render's options committed.
        private MountedTree Mount(MutationOptions<int, int, string> options)
        {
            s_options = options;
            return Mount();
        }

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

        // Logs "{label}:{data}" from OnSuccess; gated calls wait on s_gates[variables].
        private static MutationOptions<int, int, string> Labelled(string label, bool gated = false) => new(
            MutationFn: (v, _) =>
            {
                if (!gated) return VelvetTask.FromResult(v);
                var gate = new VelvetTaskCompletionSource<int>();
                s_gates[v] = gate;
                return gate.Task;
            },
            OnSuccess: (data, _, _, _) =>
            {
                s_log.Add($"{label}:{data}");
                return VelvetTask.CompletedTask;
            });

        private static MutationOptions<int, int, string> Failing(Exception failure)
            => new(MutationFn: (_, _) => VelvetTask.FromException<int>(failure));

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
