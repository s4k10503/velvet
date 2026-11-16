using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a tree commits when the fallback of an error boundary suspends after the boundary's swap has already
    /// disposed the child that threw, as one <see cref="EventLog"/> string per case, each what react-reconciler 19.1
    /// commits for the same tree: the boundary shows its fallback and reports the catch once, in the commit after the
    /// read resolves (with no Suspense above, <c>throwException</c> leaves a concurrent root suspended until then). The
    /// cases leave out what comes before that commit, and register no effect on the child that threw, since Velvet runs
    /// its cleanups at the swap where React runs them in that commit.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class FallbackSuspendEventLogTests
    {
        private readonly EventLogRenderer _renderer;

        public FallbackSuspendEventLogTests(EventLogRenderer renderer) => _renderer = renderer;

        private sealed class Gate
        {
            internal VelvetTaskCompletionSource<int>? Source;
        }

        [Component]
        private static VNode SuspendingContent(Gate gate)
        {
            Hooks.Use(() => (gate.Source ??= new VelvetTaskCompletionSource<int>()).Task, "gate");
            EventLog.UseInsertionAndLayoutEffects("IF", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback<VisualElement>("IF"));
        }

        [Component]
        private static VNode ThrowsOnItsOwnTick()
        {
            var (tick, _) = EventLog.UseState("Tick", 0);
            return tick > 0 ? throw new InvalidOperationException("thrown by X") : V.Div();
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Inner")]
        private static VNode SuspendingFallbackBoundary(Gate gate)
        {
            Hooks.UseFallback(_ => V.Component(SuspendingContent, gate));
            return V.Component(ThrowsOnItsOwnTick);
        }

        [Test]
        public void Given_AnErrorBoundaryWhoseFallbackSuspends_When_AChildsOwnRenderThrowsAndTheReadResolves_Then_TheBoundaryShowsItsFallbackAndReportsTheCatchOnce()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = _renderer.Mount(log.Provide(V.Component(SuspendingFallbackBoundary, gate)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Tick").Invoke(1);
            mounted.DrainForTest();
            log.Add("|");
            gate.Source?.TrySetResult(5);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("| ins:IF ref:IF lay:IF caught:Inner:thrown by X"));
        }

        [Component]
        private static VNode ThrowsInItsLayoutEffect()
        {
            Hooks.UseLayoutEffect(
                (Func<Action?>)(() => throw new InvalidOperationException("thrown by the layout effect")),
                Array.Empty<object>());
            return V.Div();
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Inner")]
        private static VNode SuspendingFallbackOverALayoutEffect(Gate gate)
        {
            Hooks.UseFallback(_ => V.Component(SuspendingContent, gate));
            return V.Component(ThrowsInItsLayoutEffect);
        }

        [Test]
        public void Given_AnErrorBoundaryWhoseFallbackSuspends_When_ALayoutEffectThrowsAtMount_Then_TheMountReturnsAndTheBoundaryShowsItsFallbackAndReportsTheCatchOnce()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();

            // Act
            using var mounted = _renderer.Mount(log.Provide(V.Component(SuspendingFallbackOverALayoutEffect, gate)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Add("|");
            gate.Source?.TrySetResult(5);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("| ins:IF ref:IF lay:IF caught:Inner:thrown by the layout effect"));
        }

        [Component]
        private static VNode SuspenseAroundTheCatchingBoundary(Gate gate)
            => V.Suspense(
                fallback: V.Div(),
                children: new VNode[] { V.Component(SuspendingFallbackBoundary, gate, key: "inner") });

        [Test]
        public void Given_ASuspenseAboveABoundaryWhoseFallbackSuspends_When_AChildsOwnRenderThrowsAndTheReadResolves_Then_TheBoundaryShowsItsFallbackAndReportsTheCatchOnce()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = _renderer.Mount(log.Provide(V.Component(SuspenseAroundTheCatchingBoundary, gate)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Tick").Invoke(1);
            mounted.DrainForTest();
            log.Clear();
            gate.Source?.TrySetResult(5);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:IF ref:IF lay:IF caught:Inner:thrown by X"));
        }

        [Component]
        private static VNode RerenderingParent(Gate gate)
        {
            EventLog.UseState("Parent", 0);
            return V.Component(SuspendingFallbackBoundary, gate, key: "inner");
        }

        [Test]
        public void Given_ABoundaryWhoseFallbackSuspends_When_ItsParentRendersAgainBeforeTheReadResolves_Then_TheCatchIsReportedOnlyAfterTheReadResolves()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = _renderer.Mount(log.Provide(V.Component(RerenderingParent, gate)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Setter<int>("Tick").Invoke(1);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Parent").Invoke(1);
            mounted.DrainForTest();
            log.Add("|");
            gate.Source?.TrySetResult(5);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("| ins:IF ref:IF lay:IF caught:Inner:thrown by X"));
        }

        [Component]
        private static VNode ThrowsOnItsTickOverAThrowingCleanup()
        {
            var (tick, _) = EventLog.UseState("Tick", 0);
            Hooks.UseLayoutEffect(
                (Func<Action?>)(() => () => throw new InvalidOperationException("cleanup threw")),
                Array.Empty<object>());
            return tick > 0 ? throw new InvalidOperationException("thrown by X") : V.Div();
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Inner")]
        private static VNode SuspendingFallbackOverAThrowingCleanup(Gate gate)
        {
            Hooks.UseFallback(_ => V.Component(SuspendingContent, gate));
            return V.Component(ThrowsOnItsTickOverAThrowingCleanup);
        }

        // GREEN_ON_BASE(characterization): the base drops what the swap took when the fallback it swapped in
        // suspends. Drop `DropTheSwapsErrors(fiber);` in the catch of FiberSuspendSignal in TryCatch and the
        // cleanup's error stays queued and is reported with the catch.
        [Test]
        public void Given_AnErrorBoundaryWhoseFallbackSuspends_When_TheFailedChildsCleanupThrowsAtTheSwap_Then_OnlyTheCatchIsReported()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = _renderer.Mount(log.Provide(V.Component(SuspendingFallbackOverAThrowingCleanup, gate)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Tick").Invoke(1);
            mounted.DrainForTest();
            log.Add("|");
            gate.Source?.TrySetResult(5);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("| ins:IF ref:IF lay:IF caught:Inner:thrown by X"));
        }
    }
}
