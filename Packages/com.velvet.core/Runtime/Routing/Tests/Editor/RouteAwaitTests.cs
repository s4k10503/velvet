// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.Await</c> over a <see cref="Deferred{T}"/>: React Router's <c>&lt;Await&gt;</c> over an
    /// unawaited promise in loader data.
    /// <list type="bullet">
    /// <item>A resolved value renders through the children; a pending one suspends to the nearest
    /// <c>V.Suspense</c>, whose fallback gives way to the value once it arrives.</item>
    /// <item>One deferred value can be read by more than one <c>V.Await</c>.</item>
    /// <item>A failed task, or a throw while rendering the value, renders the errorElement, beneath which
    /// <c>UseAsyncError</c> reads the exception; without an errorElement a failed task propagates to the
    /// nearest error boundary.</item>
    /// <item>After a throw while rendering the value, the errorElement stays for a deferred handed to the same
    /// <c>V.Await</c> afterwards, as React Router's AwaitErrorBoundary keeps it until it remounts; an Await
    /// remounted after a failed task renders the new deferred's value.</item>
    /// <item>Element children read the value through <c>UseAsyncValue</c>.</item>
    /// <item>A loader returning a deferred value commits its navigation before the value arrives.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteAwaitTests
    {
        private VisualElement _root = null!;

        private static Deferred<string>? s_deferred;
        private static Func<string, VNode?> s_render = value => V.Label(text: "value-" + value);
        private static VNode? s_errorElement;
        private static Exception? s_asyncError;
        private static Exception? s_boundaryError;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_deferred = null;
            s_render = value => V.Label(text: "value-" + value);
            s_errorElement = null;
            s_asyncError = null;
            s_boundaryError = null;
            s_setAwaitGeneration = null;
            s_setAwaitTick = null;
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        #region Render targets

        [Component]
        private static VNode SuspendedAwait()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Await(s_deferred!, s_render, s_errorElement, key: "await") });

        [Component]
        private static VNode TwoAwaits()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Await(s_deferred!, value => V.Label(text: "first-" + value), key: "first"),
                    V.Await(s_deferred!, value => V.Label(text: "second-" + value), key: "second"),
                });

        [Component]
        private static VNode ElementChildrenAwait()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Await(s_deferred!, V.Component(AsyncValueReader, key: "reader")) });

        private static Action<int>? s_setAwaitGeneration;
        private static Action<int>? s_setAwaitTick;

        // Keys the Await by a generation, so a new generation remounts it; the tick renders it again as it is.
        [Component(Compiler = false)]
        private static VNode KeyedAwaitHost()
        {
            var (generation, setGeneration) = Hooks.UseState(0);
            var (_, setTick) = Hooks.UseState(0);
            s_setAwaitGeneration = setGeneration;
            s_setAwaitTick = setTick;
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Await(s_deferred!, s_render, s_errorElement, key: "await-" + generation) });
        }

        [Component]
        private static VNode AsyncValueReader() => V.Label(text: "read-" + Hooks.UseAsyncValue<string>());

        [Component]
        private static VNode AsyncErrorReader()
        {
            s_asyncError = Hooks.UseAsyncError();
            return V.Label(text: "await-error");
        }

        private static VNode UnderBoundary(VNode child)
            => V.ErrorBoundary(
                error =>
                {
                    s_boundaryError = error;
                    return V.Label(text: "boundary-fallback");
                },
                new VNode?[] { child });

        private static bool HasLabel(VisualElement root, string text) => root.FindLabelByText(text) != null;

        #endregion

        #region Arguments

        [Test]
        public void Given_ANullDeferred_When_AwaitingWithARenderFunction_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => V.Await<string>(null!, value => V.Label(text: value)));
        }

        [Test]
        public void Given_ANullRenderFunction_When_Awaiting_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.Await(new Deferred<string>(VelvetTask.FromResult("ready")), (Func<string, VNode?>)null!));
        }

        [Test]
        public void Given_ANullDeferred_When_AwaitingWithElementChildren_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => V.Await<string>(null!, V.Label(text: "child")));
        }

        [Test]
        public void Given_NullElementChildren_When_Awaiting_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.Await(new Deferred<string>(VelvetTask.FromResult("ready")), (VNode)null!));
        }

        #endregion

        #region Resolving

        [Test]
        public void Given_ADeferredAlreadyResolved_When_Awaited_Then_TheChildrenRenderTheValue()
        {
            // Arrange
            s_deferred = new Deferred<string>(VelvetTask.FromResult("ready"));

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspendedAwait, key: "host"));

            // Assert
            Assert.That(HasLabel(_root, "value-ready"), Is.True);
        }

        [Test]
        public void Given_ADeferredStillPending_When_AwaitedBeneathASuspense_Then_TheFallbackShowsInsteadOfTheValue()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<string>();
            s_deferred = new Deferred<string>(source.Task);

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspendedAwait, key: "host"));

            // Assert
            Assert.That((HasLabel(_root, "loading"), HasLabel(_root, "value-ready")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APendingDeferred_When_ItResolves_Then_TheValueReplacesTheFallback()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<string>();
            s_deferred = new Deferred<string>(source.Task);
            using var mounted = V.Mount(_root, V.Component(SuspendedAwait, key: "host"));

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            Assert.That((HasLabel(_root, "loading"), HasLabel(_root, "value-ready")), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_TwoAwaitsOfOneDeferred_When_ItResolves_Then_BothRenderTheValue()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<string>();
            s_deferred = new Deferred<string>(source.Task);
            using var mounted = V.Mount(_root, V.Component(TwoAwaits, key: "host"));

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            Assert.That((HasLabel(_root, "first-ready"), HasLabel(_root, "second-ready")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ElementChildren_When_TheDeferredResolves_Then_UseAsyncValueReadsTheValue()
        {
            // Arrange
            s_deferred = new Deferred<string>(VelvetTask.FromResult("ready"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ElementChildrenAwait, key: "host"));

            // Assert
            Assert.That(HasLabel(_root, "read-ready"), Is.True);
        }

        #endregion

        #region Failing

        [Test]
        public void Given_ADeferredWhoseTaskFails_When_AwaitedWithAnErrorElement_Then_UseAsyncErrorReadsTheException()
        {
            // Arrange
            s_deferred = new Deferred<string>(VelvetTask.FromException<string>(new InvalidOperationException("deferred-boom")));
            s_errorElement = V.Component(AsyncErrorReader, key: "error");

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspendedAwait, key: "host"));

            // Assert
            Assert.That(s_asyncError?.Message, Is.EqualTo("deferred-boom"));
        }

        // GREEN_ON_BASE(characterization): the merge base renders a remounted Await's new value after a failed task.
        // React Router's AwaitErrorBoundary resets on a remount alone, and a remount doing so is what this pins.
        [Test]
        public void Given_AnAwaitWhoseDeferredFailed_When_ItRemountsWithADeferredThatResolves_Then_ItRendersTheValue()
        {
            // Arrange
            s_deferred = new Deferred<string>(VelvetTask.FromException<string>(new InvalidOperationException("deferred-boom")));
            s_errorElement = V.Component(AsyncErrorReader, key: "error");
            using var mounted = V.Mount(_root, V.Component(KeyedAwaitHost, key: "host"));
            var failed = HasLabel(_root, "await-error");
            s_deferred = new Deferred<string>(VelvetTask.FromResult("ready"));

            // Act
            s_setAwaitGeneration!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((failed, HasLabel(_root, "value-ready")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnAwaitThatCaughtAThrowRenderingItsValue_When_ItIsHandedADeferredThatResolves_Then_ItKeepsItsErrorElement()
        {
            // Arrange
            s_deferred = new Deferred<string>(VelvetTask.FromResult("first"));
            s_render = _ => throw new InvalidOperationException("render-boom");
            s_errorElement = V.Component(AsyncErrorReader, key: "error");
            using var mounted = V.Mount(_root, V.Component(KeyedAwaitHost, key: "host"), CaughtErrors.Unlogged);
            s_render = value => V.Label(text: "value-" + value);
            s_deferred = new Deferred<string>(VelvetTask.FromResult("ready"));

            // Act
            s_setAwaitTick!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((HasLabel(_root, "await-error"), s_asyncError?.Message), Is.EqualTo((true, "render-boom")));
        }

        [Test]
        public void Given_ADeferredWhoseTaskFails_When_AwaitedWithoutAnErrorElement_Then_TheNearestErrorBoundaryCatchesIt()
        {
            // Arrange
            s_deferred = new Deferred<string>(VelvetTask.FromException<string>(new InvalidOperationException("deferred-boom")));

            // Act
            using var mounted = V.Mount(_root, UnderBoundary(V.Component(SuspendedAwait, key: "host")),
                CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_boundaryError?.Message, Is.EqualTo("deferred-boom"));
        }

        [Test]
        public void Given_ARenderFunctionThatThrows_When_AwaitedWithAnErrorElement_Then_UseAsyncErrorReadsThatThrow()
        {
            // Arrange
            s_deferred = new Deferred<string>(VelvetTask.FromResult("ready"));
            s_render = _ => throw new InvalidOperationException("render-boom");
            s_errorElement = V.Component(AsyncErrorReader, key: "error");

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspendedAwait, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_asyncError?.Message, Is.EqualTo("render-boom"));
        }

        #endregion

        #region From a loader

        private sealed record DashboardData(Deferred<string> Reviews);

        [Component]
        private static VNode Dashboard()
        {
            var data = Hooks.UseLoaderData<DashboardData>();
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Await(data!.Reviews, reviews => V.Label(text: "value-" + reviews)) });
        }

        [Test]
        public void Given_ALoaderReturningADeferredValue_When_Navigating_Then_TheRouteCommitsAndShowsTheFallbackBeforeTheValueArrives()
        {
            // Arrange
            var reviews = new VelvetTaskCompletionSource<string>();
            var router = BuildRouter("/home", Route("home"),
                Route("dashboard", element: V.Component(Dashboard, key: "dashboard"),
                    loader: (_, _) => VelvetTask.FromResult<object>(new DashboardData(new Deferred<string>(reviews.Task)))));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Act
            router.NavigateSync("/dashboard");
            mounted.FlushStateForTest();

            // Assert
            Assert.That((router.CurrentLocation.Path, HasLabel(_root, "loading")), Is.EqualTo(("/dashboard", true)));
        }

        [Component]
        private static VNode DashboardWithErrorElement()
        {
            var data = Hooks.UseLoaderData<DashboardData>();
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Await(data!.Reviews, reviews => V.Label(text: "value-" + reviews),
                        V.Label(text: "reviews-failed")),
                });
        }

        [Test]
        public void Given_ADeferredValueCancelledWhenItsRouteWasLeft_When_SteppingBackOntoTheRoute_Then_ANewLoaderRunIsAwaited()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"),
                Route("dashboard", element: V.Component(DashboardWithErrorElement, key: "dashboard"),
                    loader: (_, ct) =>
                    {
                        var reviews = new VelvetTaskCompletionSource<string>();
                        ct.Register(() => reviews.TrySetCanceled());
                        return VelvetTask.FromResult<object>(new DashboardData(new Deferred<string>(reviews.Task)));
                    }));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();
            router.NavigateSync("/dashboard");
            mounted.FlushStateForTest();
            router.NavigateSync("/home");
            mounted.FlushStateForTest();

            // Act
            router.GoBackSync();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((router.CurrentLocation.Path, HasLabel(_root, "loading")), Is.EqualTo(("/dashboard", true)));
        }

        #endregion
    }
}
