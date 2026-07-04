using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.Use"/> and its backing <see cref="FiberAsyncResource{T}"/>
    /// state machine in a function component.
    /// <list type="bullet">
    /// <item><see cref="FiberSuspendSignal.Instance"/> is a singleton <see cref="Exception"/> used to suspend a
    /// pending render.</item>
    /// <item>A resource starts <c>Pending</c> and stores its resource key; it transitions to <c>Success</c> for a
    /// completed task or <c>Error</c> for a faulted one, and stays <c>Pending</c> while the task is in flight.</item>
    /// <item>The completion callback fires when an in-flight task settles (success or failure) but is suppressed for
    /// a synchronously completed task, whose value is returned to the caller directly.</item>
    /// <item>Cancelling or disposing a pending resource cancels the factory's token.</item>
    /// <item>A synchronously completed factory returns its value on the first render; a faulted factory surfaces its
    /// exception; a pending factory without a Suspense boundary suspends, warns, and leaves the value unset until
    /// the task completes and re-renders with the value.</item>
    /// <item>The resource is keyed by reference identity: an unchanged key reuses the same resource slot, a changed
    /// key replaces it.</item>
    /// <item>When no explicit resource key is passed and the factory delegate identity changes across renders, the
    /// resource restarts and a footgun warning is emitted; an explicit key silences that warning.</item>
    /// <item>Unmounting a component with a pending resource cancels the factory's token.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// A component without a fallback emits render exceptions and suspense signals through the root path via
    /// <c>Debug.LogException</c> / <c>Debug.LogWarning</c>, so those cases are captured with <c>LogAssert</c>. The
    /// component captures its owning fiber (via internals) so tests can inspect the async slots. Static captures are
    /// reset together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseAsyncTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetUseAsync();
        }

        #region FiberSuspendSignal

        [Test]
        public void Given_SuspendSignal_When_InstanceAccessed_Then_IsSingleton()
        {
            // Act
            var a = FiberSuspendSignal.Instance;
            var b = FiberSuspendSignal.Instance;

            // Assert
            Assert.That(a, Is.SameAs(b), "The suspend signal exposes a single shared instance");
        }

        [Test]
        public void Given_SuspendSignal_When_InstanceAccessed_Then_IsException()
        {
            // Act + Assert
            Assert.That(FiberSuspendSignal.Instance, Is.InstanceOf<Exception>(), "The suspend signal is an Exception");
        }

        #endregion

        #region FiberAsyncResource state machine

        [Test]
        public void Given_NewResource_When_ConstructedWithKey_Then_StoresResourceKey()
        {
            // Arrange
            var resourceKey = new object();

            // Act
            var resource = new FiberAsyncResource<int>(resourceKey);

            // Assert
            Assert.That(resource.ResourceKey, Is.SameAs(resourceKey), "The resource stores its resource key");
        }

        [Test]
        public void Given_NewResource_When_Constructed_Then_StatusIsPending()
        {
            // Act
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());

            // Assert
            Assert.That(resource.Status, Is.EqualTo(FiberAsyncResourceStatus.Pending), "A freshly constructed resource is Pending");
        }

        [Test]
        public void Given_Resource_When_StartedWithSyncCompletedTask_Then_StatusIsSuccess()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());

            // Act
            resource.Start(_ => UniTask.FromResult(42));

            // Assert
            Assert.That(resource.Status, Is.EqualTo(FiberAsyncResourceStatus.Success), "A sync-completed task transitions to Success");
        }

        [Test]
        public void Given_Resource_When_StartedWithSyncCompletedTask_Then_ResultIsTaskValue()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());

            // Act
            resource.Start(_ => UniTask.FromResult(42));

            // Assert
            Assert.That(resource.Result, Is.EqualTo(42), "The result holds the task value");
        }

        [Test]
        public void Given_Resource_When_StartedWithSyncFaultedTask_Then_StatusIsError()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());

            // Act
            resource.Start(_ => UniTask.FromException<int>(new InvalidOperationException("boom")));

            // Assert
            Assert.That(resource.Status, Is.EqualTo(FiberAsyncResourceStatus.Error), "A sync-faulted task transitions to Error");
        }

        [Test]
        public void Given_Resource_When_StartedWithSyncFaultedTask_Then_ErrorIsTaskException()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());
            var ex = new InvalidOperationException("boom");

            // Act
            resource.Start(_ => UniTask.FromException<int>(ex));

            // Assert
            Assert.That(resource.Error, Is.SameAs(ex), "The error holds the task exception");
        }

        [Test]
        public void Given_Resource_When_StartedWithPendingTask_Then_StaysPending()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());
            var source = new UniTaskCompletionSource<int>();

            // Act
            resource.Start(_ => source.Task);

            // Assert
            Assert.That(resource.Status, Is.EqualTo(FiberAsyncResourceStatus.Pending), "An in-flight task keeps the resource Pending");
        }

        [Test]
        public void Given_PendingResource_When_Cancelled_Then_CancelsFactoryToken()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());
            CancellationToken capturedToken = default;
            var source = new UniTaskCompletionSource<int>();
            resource.Start(ct => { capturedToken = ct; return source.Task; });

            // Act
            resource.Cancel();

            // Assert
            Assert.That(capturedToken.IsCancellationRequested, Is.True, "Cancel cancels the factory token");
        }

        [Test]
        public void Given_PendingResource_When_Disposed_Then_CancelsFactoryToken()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());
            CancellationToken capturedToken = default;
            var source = new UniTaskCompletionSource<int>();
            resource.Start(ct => { capturedToken = ct; return source.Task; });

            // Act
            resource.Dispose();

            // Assert
            Assert.That(capturedToken.IsCancellationRequested, Is.True, "Dispose cancels the factory token");
        }

        [Test]
        public void Given_Resource_When_SyncCompletedTask_Then_CompletionCallbackDoesNotFire()
        {
            // Arrange — a sync-completed task returns its value to the caller directly, so the completion
            // notification is suppressed to avoid a re-entrant render.
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());
            var fired = false;
            resource.OnCompleted = () => fired = true;

            // Act
            resource.Start(_ => UniTask.FromResult(7));

            // Assert
            Assert.That(fired, Is.False, "The completion callback is suppressed for a synchronously completed task");
        }

        [Test]
        public void Given_PendingResource_When_TaskSucceeds_Then_CompletionCallbackFires()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());
            var source = new UniTaskCompletionSource<int>();
            var fired = false;
            resource.OnCompleted = () => fired = true;
            resource.Start(_ => source.Task);

            // Act
            source.TrySetResult(7);

            // Assert
            Assert.That(fired, Is.True, "The completion callback fires when an in-flight task succeeds");
        }

        [Test]
        public void Given_PendingResource_When_TaskFails_Then_CompletionCallbackFires()
        {
            // Arrange
            var resource = new FiberAsyncResource<int>(Array.Empty<object>());
            var source = new UniTaskCompletionSource<int>();
            var fired = false;
            resource.OnCompleted = () => fired = true;
            resource.Start(_ => source.Task);

            // Act
            source.TrySetException(new Exception("nope"));

            // Assert
            Assert.That(fired, Is.True, "The completion callback fires when an in-flight task fails");
        }

        #endregion

        #region Use hook integration

        [Test]
        public void Given_SyncCompletedFactory_When_Rendered_Then_ReturnsValue()
        {
            // Arrange
            s_useAsyncFactory = _ => UniTask.FromResult(42);

            // Act
            using var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));

            // Assert
            Assert.That(s_useAsyncLastValue, Is.EqualTo(42), "A sync-completed factory returns its value on the first render");
        }

        [Test]
        public void Given_PendingFactoryWithoutBoundary_When_Rendered_Then_SuspendsWithUnsetValue()
        {
            // Arrange — without a Suspense boundary the suspend goes to Debug.LogWarning and the render is aborted.
            var source = new UniTaskCompletionSource<int>();
            s_useAsyncFactory = _ => source.Task;
            LogAssert.Expect(LogType.Warning, new Regex("Suspense"));

            // Act
            using var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));

            // Assert
            Assert.That(s_useAsyncLastValue, Is.Null, "The suspended render leaves the value unset (no Suspense boundary)");
        }

        [Test]
        public void Given_FaultedFactory_When_Rendered_Then_LogsException()
        {
            // Arrange — a faulted factory surfaces its exception through the root path via Debug.LogException.
            s_useAsyncFactory = _ => UniTask.FromException<int>(new ArgumentException("boom"));
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException: boom"));

            // Act
            using var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_SameResourceKey_When_ReRendered_Then_SlotCountStaysOne()
        {
            // Arrange
            var resourceKey = new object();
            s_useAsyncFactory = _ => UniTask.FromResult(1);
            s_useAsyncResourceKey = resourceKey;
            using var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));
            Assume.That(s_useAsyncCapturedFiber.AsyncSlots, Has.Count.EqualTo(1), "Precondition: the first render allocated one slot");

            // Act — re-render with the same key instance
            s_useAsyncSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_useAsyncCapturedFiber.AsyncSlots, Has.Count.EqualTo(1), "The slot count stays at 1 across the re-render");
        }

        [Test]
        public void Given_SameResourceKey_When_ReRendered_Then_ReusesSameResourceInstance()
        {
            // Arrange
            var resourceKey = new object();
            s_useAsyncFactory = _ => UniTask.FromResult(1);
            s_useAsyncResourceKey = resourceKey;
            using var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));
            var firstSlot = s_useAsyncCapturedFiber.AsyncSlots[0];

            // Act
            s_useAsyncSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_useAsyncCapturedFiber.AsyncSlots[0], Is.SameAs(firstSlot),
                "An unchanged resource key reuses the same resource instance");
        }

        [Test]
        public void Given_ChangedResourceKey_When_ReRendered_Then_ReplacesResourceInstance()
        {
            // Arrange
            s_useAsyncFactory = _ => UniTask.FromResult(1);
            s_useAsyncResourceKey = new object();
            using var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));
            var firstSlot = s_useAsyncCapturedFiber.AsyncSlots[0];

            // Act — swap the resource key and re-render
            s_useAsyncResourceKey = new object();
            s_useAsyncSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_useAsyncCapturedFiber.AsyncSlots[0], Is.Not.SameAs(firstSlot),
                "A changed resource key allocates a new resource instance");
        }

        [Test]
        public void Given_PendingResourceWithoutBoundary_When_TaskCompletes_Then_ReRendersWithValue()
        {
            // Arrange — without a Suspense boundary the suspend is routed to Debug.LogWarning at Mount.
            LogAssert.Expect(LogType.Warning, new Regex("Suspense"));
            var source = new UniTaskCompletionSource<int>();
            s_useAsyncFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));
            Assume.That(s_useAsyncLastValue, Is.Null, "Precondition: the first render is suspended and the value is unset");

            // Act
            source.TrySetResult(99);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_useAsyncLastValue, Is.EqualTo(99), "Completing the task re-renders with the resolved value");
        }

        [Test]
        public void Given_PendingResource_When_ComponentUnmounted_Then_CancelsFactoryToken()
        {
            // Arrange — without a Suspense boundary the suspend is routed to Debug.LogWarning at Mount.
            LogAssert.Expect(LogType.Warning, new Regex("Suspense"));
            CancellationToken capturedToken = default;
            var source = new UniTaskCompletionSource<int>();
            s_useAsyncFactory = ct => { capturedToken = ct; return source.Task; };
            var mounted = V.Mount(_root, V.Component(UseAsyncRender, key: "async"));

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(capturedToken.IsCancellationRequested, Is.True, "Unmount cancels the pending resource's factory token");
        }

        #endregion

        #region Factory-identity footgun warning

        [Test]
        public void Given_NoResourceKey_When_InlineLambdaFactoryReRenders_Then_WarnsOnSecondRender()
        {
            // Arrange — the most common shape (inline lambda without resourceKey) restarts the resource every render
            // because the fresh delegate identity becomes the key. The first render mounts without a prior resource.
            s_inlineLambdaFactoryRender = 0;
            s_inlineLambdaSetTick = null;
            using var mounted = V.Mount(_root, V.Component(InlineLambdaFactoryRender, key: "footgun"));
            Assume.That(s_inlineLambdaFactoryRender, Is.EqualTo(1), "Precondition: only the mount render has happened (no warning yet)");

            // Act — re-render so the key (the render-1 lambda) differs from the new render-2 lambda
            LogAssert.Expect(LogType.Warning, new Regex("factory delegate identity changed"));
            s_inlineLambdaSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the footgun warning was emitted
        }

        [Test]
        public void Given_ExplicitResourceKey_When_InlineLambdaFactoryReRenders_Then_DoesNotWarn()
        {
            // Arrange — an explicit resourceKey is the documented escape hatch: it silences the footgun warning even
            // when the factory is a fresh inline lambda. No LogAssert.Expect — any leaked warning fails the test.
            s_explicitKeyFactoryRender = 0;
            s_explicitKeySetTick = null;
            s_explicitKeyValue = 42;
            using var mounted = V.Mount(_root, V.Component(ExplicitResourceKeyFactoryRender, key: "explicit"));

            // Act
            s_explicitKeySetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the absence of any logged warning is the assertion
        }

        private static int s_inlineLambdaFactoryRender;
        private static Action<int> s_inlineLambdaSetTick;

        [Component]
        private static VNode InlineLambdaFactoryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_inlineLambdaSetTick = setTick;
            s_inlineLambdaFactoryRender++;
            // Footgun: an inline lambda is a fresh delegate each render, and without an explicit resourceKey it
            // becomes the key — the resource restarts every render.
            _ = Hooks.Use<int>((CancellationToken _) => UniTask.FromResult(tick));
            return V.Label(text: tick.ToString());
        }

        private static int s_explicitKeyFactoryRender;
        private static Action<int> s_explicitKeySetTick;
        private static int s_explicitKeyValue;

        [Component]
        private static VNode ExplicitResourceKeyFactoryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_explicitKeySetTick = setTick;
            s_explicitKeyFactoryRender++;
            // The factory is an inline lambda (identity changes per render) but the explicit resourceKey is stable,
            // so the footgun warning must not fire.
            _ = Hooks.Use<int>((CancellationToken _) => UniTask.FromResult(s_explicitKeyValue), resourceKey: "stable-key");
            return V.Label(text: tick.ToString());
        }

        #endregion

        #region UseAsync component (Hooks.Use + UseState tick)

        private static Func<CancellationToken, UniTask<int>> s_useAsyncFactory;
        private static object s_useAsyncResourceKey;
        private static int? s_useAsyncLastValue;
        private static Action<int> s_useAsyncSetTick;
        private static ComponentFiber s_useAsyncCapturedFiber;

        private static void ResetUseAsync()
        {
            s_useAsyncFactory = null;
            s_useAsyncResourceKey = null;
            s_useAsyncLastValue = null;
            s_useAsyncSetTick = null;
            s_useAsyncCapturedFiber = null;
        }

        [Component]
        private static VNode UseAsyncRender()
        {
            // Capture the current fiber so tests can inspect AsyncSlots (internal accessor via InternalsVisibleTo).
            s_useAsyncCapturedFiber = FiberAmbientStack.Current;
            var (_, setTick) = Hooks.UseState(0);
            s_useAsyncSetTick = setTick;
            s_useAsyncLastValue = Hooks.Use(s_useAsyncFactory, s_useAsyncResourceKey);
            return V.Label(text: s_useAsyncLastValue?.ToString() ?? string.Empty);
        }

        #endregion
    }
}
