using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseMutation{TVariables, TData}"/> in a function component.
    /// <list type="bullet">
    /// <item>The handle starts <c>Idle</c> with no data, error, or variables.</item>
    /// <item><c>MutateAsync</c> drives the lifecycle Idle → Pending → Success, exposing the result, the latest
    /// variables, and clearing the error on success.</item>
    /// <item>A throwing mutation function transitions to <c>Error</c>, retains the thrown exception, and rethrows it
    /// to the caller's await.</item>
    /// <item><c>Reset</c> restores the handle to <c>Idle</c> and clears data, error, and variables.</item>
    /// <item>The handle reference is stable across re-renders.</item>
    /// <item>Generic positions accept <see cref="Unit"/> for "no variables" / "no return value", with void-return and
    /// no-input overloads that adapt to a <see cref="Unit"/> result.</item>
    /// <item>Concurrent mutations follow latest-call-wins: the superseded call is cancelled and the final status,
    /// data, and variables come from the latest call.</item>
    /// <item>If the component unmounts while a mutation is in flight, the caller's await still observes the function
    /// result but the disposed fiber does not receive a Success state transition.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Lifecycle transitions are observed across renders, so the success / error / reset / concurrency cases run as
    /// coroutine tests that await the mutation and then flush. The mutation function and captured handles are reset
    /// together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseMutationHookTests
    {
        private VisualElement _root = null!;
        private static MutationResult<int, int>? s_captured;
        private static Func<int, CancellationToken, UniTask<int>> s_mutationFn = (v, _) => UniTask.FromResult(v * 2);

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_captured = null;
            s_mutationFn = (v, _) => UniTask.FromResult(v * 2);
            s_onErrorCount = 0;
            s_voidCaptured = null;
            s_noInputCaptured = null;
        }

        [Test]
        public void Given_FirstRender_When_Mounted_Then_StatusIsIdle()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "idle"));
            Assume.That(s_captured, Is.Not.Null, "Precondition: the hook produced a handle");

            // Assert
            Assert.That(s_captured!.Status, Is.EqualTo(MutationStatus.Idle), "The handle starts in the Idle status");
        }

        [Test]
        public void Given_FirstRender_When_Mounted_Then_DataErrorAndIdleFlagAreInitial()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "idle-fields"));
            Assume.That(s_captured, Is.Not.Null, "Precondition: the hook produced a handle");

            // Assert
            Assert.That((s_captured!.IsIdle, s_captured.Data, s_captured.Error), Is.EqualTo((true, default(int), (Exception?)null)),
                "An Idle handle reports IsIdle with default data and no error");
        }

        [UnityTest]
        public IEnumerator Given_IdleMutation_When_MutateAsyncSucceeds_Then_StatusIsSuccess() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "success"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured.Status, Is.EqualTo(MutationStatus.Success), "A completed mutation transitions to Success");
        });

        [UnityTest]
        public IEnumerator Given_IdleMutation_When_MutateAsyncSucceeds_Then_ExposesResultAndVariables() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "success-fields"));

            // Act
            var result = await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((result, s_captured.Data, s_captured.Variables, s_captured.Error),
                Is.EqualTo((42, 42, 21, (Exception?)null)),
                "Success exposes the result as the return value and Data, retains the variables, and clears the error");
        });

        [UnityTest]
        public IEnumerator Given_ThrowingMutation_When_MutateAsync_Then_StatusIsError() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var failingException = new InvalidOperationException("simulated");
            s_mutationFn = (_, _) => throw failingException;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "error"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured!.Status, Is.EqualTo(MutationStatus.Error), "A throwing mutation transitions to Error");
        });

        [UnityTest]
        public IEnumerator Given_ThrowingMutation_When_MutateAsync_Then_RethrowsAndRetainsException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var failingException = new InvalidOperationException("simulated");
            s_mutationFn = (_, _) => throw failingException;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "error-rethrow"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();
            Assume.That(rethrown, Is.SameAs(failingException), "Precondition: MutateAsync rethrew the underlying exception");

            // Assert
            Assert.That(s_captured!.Error, Is.SameAs(failingException), "The handle retains the thrown exception");
        });

        [UnityTest]
        public IEnumerator Given_ThrowingMutation_When_FireAndForgetMutate_Then_DeliversOnErrorWithoutUnobservedException() => UniTask.ToCoroutine(async () =>
        {
            // Fire-and-forget Mutate reports a failure through onError / the Error status
            // only. Unlike MutateAsync it has no awaiter, so it must NOT rethrow — a rethrow on the .Forget()
            // path surfaces as an unobserved exception, which the test framework's implicit log check flags as
            // a failure (the RED signal here).
            // Arrange
            var failingException = new InvalidOperationException("simulated");
            s_mutationFn = (_, _) => throw failingException;
            s_onErrorCount = 0;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithOnErrorRender, key: "fire-and-forget-error"));

            // Act
            s_captured!.Mutate(1);
            await UniTask.Yield();
            await UniTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured!.Status, s_onErrorCount), Is.EqualTo((MutationStatus.Error, 1)),
                "Fire-and-forget Mutate routes the failure to onError and the Error status without an unobserved rethrow");
        });

        [UnityTest]
        public IEnumerator Given_SucceededMutation_When_Reset_Then_RestoresIdleState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "reset"));
            await s_captured!.MutateAsync(10);
            mounted.FlushStateForTest();
            Assume.That(s_captured.Status, Is.EqualTo(MutationStatus.Success), "Precondition: the mutation succeeded before reset");

            // Act
            s_captured.Reset();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.Status, s_captured.Data, s_captured.Variables, s_captured.Error),
                Is.EqualTo((MutationStatus.Idle, default(int), default(int), (Exception?)null)),
                "Reset restores Idle and clears data, variables, and error");
        });

        [Test]
        public void Given_MountedMutation_When_ReRendered_Then_HandleReferenceIsStable()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "stable"));
            var firstHandle = s_captured;

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured, Is.SameAs(firstHandle), "UseMutation returns the same handle instance across renders");
        }

        [Test]
        public void Given_UnitGenerics_When_Mounted_Then_RendersSuccessfully()
        {
            // Arrange — Unit-based generic positions cover the "no variables / no return value" use case.

            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureUnitMutationRender, key: "unit"));

            // Assert
            Assert.That(_root.Q<Label>(), Is.Not.Null, "A Unit-typed mutation hook compiles and mounts");
        }

        [UnityTest]
        public IEnumerator Given_VoidReturnOverload_When_MutateAsync_Then_CompletesWithUnitData() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — the void-return overload adapts to a Unit result so the caller's function returns UniTask.
            var observedInput = 0;
            s_voidCaptured = null;
            using var mounted = V.Mount(_root, V.Component(() =>
            {
                s_voidCaptured = Hooks.UseMutation(new MutationOptions<int>(
                    MutationFn: (v, _) => { observedInput = v; return UniTask.CompletedTask; }));
                return V.Label(text: "void");
            }, key: "void"));

            // Act
            await s_voidCaptured!.MutateAsync(42);
            mounted.FlushStateForTest();
            Assume.That(observedInput, Is.EqualTo(42), "Precondition: the mutation function observed the input");

            // Assert
            Assert.That((s_voidCaptured.Status, s_voidCaptured.Data), Is.EqualTo((MutationStatus.Success, Unit.Default)),
                "A void-return mutation succeeds with Unit data");
        });

        [UnityTest]
        public IEnumerator Given_NoInputVoidOverload_When_MutateAsync_Then_RunsAndSucceeds() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — the no-input void overload adapts to a Unit/Unit result; MutateAsync() takes no argument.
            var invoked = false;
            s_noInputCaptured = null;
            using var mounted = V.Mount(_root, V.Component(() =>
            {
                s_noInputCaptured = Hooks.UseMutation(new MutationOptions(
                    MutationFn: _ => { invoked = true; return UniTask.CompletedTask; }));
                return V.Label(text: "noinput");
            }, key: "noinput"));

            // Act
            await s_noInputCaptured!.MutateAsync();
            mounted.FlushStateForTest();
            Assume.That(invoked, Is.True, "Precondition: the no-input mutation function ran");

            // Assert
            Assert.That(s_noInputCaptured.Status, Is.EqualTo(MutationStatus.Success), "A no-input mutation succeeds");
        });

        [UnityTest]
        public IEnumerator Given_ConcurrentMutations_When_LatestCompletes_Then_FinalStateComesFromLatestCall() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — the first call's token is superseded by the second; the latest call wins.
            var first = new UniTaskCompletionSource<int>();
            var second = new UniTaskCompletionSource<int>();
            var callIndex = 0;
            s_mutationFn = (v, ct) => { callIndex++; return callIndex == 1 ? first.Task : second.Task; };
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "concurrent"));

            // Act
            var firstTask = s_captured!.MutateAsync(1);  // superseded
            var secondTask = s_captured!.MutateAsync(2); // latest
            second.TrySetResult(99);
            var winnerResult = await secondTask;
            mounted.FlushStateForTest();
            first.TrySetCanceled(); // drain the orphaned first call
            await firstTask;

            // Assert
            Assert.That((winnerResult, s_captured.Data, s_captured.Variables, s_captured.Status),
                Is.EqualTo((99, 99, 2, MutationStatus.Success)),
                "The latest call determines the final result, data, variables, and status");
        });

        [UnityTest]
        public IEnumerator Given_InFlightMutation_When_ComponentUnmounted_Then_CallerObservesResult() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var gate = new UniTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "unmount-observe"));
            var captured = s_captured!;
            var inflight = captured.MutateAsync(7);

            // Act
            mounted.Dispose(); // unmount while in flight
            gate.TrySetResult(42);

            // Assert — the caller's await still observes the function result regardless of fiber lifecycle
            var observed = await inflight;
            Assert.That(observed, Is.EqualTo(42), "The caller's MutateAsync await observes the function result after unmount");
        });

        [UnityTest]
        public IEnumerator Given_InFlightMutation_When_ComponentUnmounted_Then_DisposedFiberNotSetToSuccess() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var gate = new UniTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "unmount-nostate"));
            var captured = s_captured!;
            var inflight = captured.MutateAsync(7);

            // Act
            mounted.Dispose(); // unmount while in flight
            gate.TrySetResult(42);
            await inflight;

            // Assert
            Assert.That(captured.Status, Is.Not.EqualTo(MutationStatus.Success),
                "A disposed fiber does not receive a Success state transition");
        });

        [Component]
        public static VNode CaptureMutationRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(MutationFn: s_mutationFn));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureMutationWithOnErrorRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: s_mutationFn,
                OnError: (_, _) => s_onErrorCount++));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureUnitMutationRender()
        {
            _ = Hooks.UseMutation(new MutationOptions<Unit, Unit>(
                MutationFn: (_, _) => UniTask.FromResult(Unit.Default)));
            return V.Label(text: "ok");
        }

        private static int s_onErrorCount;
        private static MutationResult<int, Unit>? s_voidCaptured;
        private static MutationResult<Unit, Unit>? s_noInputCaptured;
    }
}
