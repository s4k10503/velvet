using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what an error boundary whose fallback fails or suspends as it is swapped in commits, as one
    /// <see cref="EventLog"/> string per case, each what react-reconciler 19.1 commits for the same tree: an error
    /// raised while a boundary renders its fallback goes to the boundary above (<c>throwException</c> passes over a
    /// boundary that has already captured in this render), whose fallback replaces both, and a boundary reports what
    /// it caught in the layout phase of the commit that shows its fallback, after its children's layout effects.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class ErrorBoundaryFallbackEventLogTests
    {
        private readonly EventLogRenderer _renderer;

        public ErrorBoundaryFallbackEventLogTests(EventLogRenderer renderer) => _renderer = renderer;

        private sealed record Named(string Name);

        private sealed class Gate
        {
            internal VelvetTaskCompletionSource<int>? Source;
        }

        private sealed class Dropping
        {
            internal bool Drop;
        }

        [Component]
        private static VNode Probe(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback<VisualElement>(props.Name));
        }

        // Registers no insertion effect: the boundary takes these rows down before its fallback suspends, so their
        // insertion cleanup runs at the hide here, where React keeps them mounted under the hidden primary until the
        // reveal deletes them.
        [Component]
        private static VNode ThrowsOnTick(int tick)
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:X");
                return () => log.Add("lay-x:X");
            }, Array.Empty<object>());
            var refX = EventLog.UseRefCallback<VisualElement>("X");
            return tick > 0 ? throw new InvalidOperationException("thrown by X") : V.Div(refCallback: refX);
        }

        [Component]
        private static VNode SuspendingContent(Gate gate)
        {
            Hooks.Use(() => (gate.Source ??= new VelvetTaskCompletionSource<int>()).Task, "gate");
            return V.Component(Probe, new Named("IF"));
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Inner")]
        private static VNode SuspendingFallbackBoundary(Gate gate)
        {
            var (tick, _) = EventLog.UseState("Tick", 0);
            Hooks.UseFallback(_ => V.Component(SuspendingContent, gate));
            return V.Component(ThrowsOnTick, tick);
        }

        [Component]
        private static VNode SuspenseAroundTheBoundary(Gate gate)
            => V.Suspense(
                fallback: V.Component(Probe, new Named("SF"), key: "sf"),
                children: new VNode[] { V.Component(SuspendingFallbackBoundary, gate, key: "inner") });

        [Component]
        private static VNode BrokenContent() => throw new InvalidOperationException("thrown by the fallback");

        private static readonly Func<VisualElement, Action> s_throwingCleanup =
            _ => () => throw new InvalidOperationException("thrown by the ref cleanup");

        [Component(Compiler = false)]
        private static VNode DropsARef(Dropping dropping)
            => V.Div(refCallback: dropping.Drop ? null : s_throwingCleanup);

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Inner")]
        private static VNode BrokenFallbackBoundary(Dropping dropping)
        {
            Hooks.UseFallback(_ => V.Component(BrokenContent));
            return V.Component(DropsARef, dropping);
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Outer")]
        private static VNode RenderingOuter(Dropping dropping)
        {
            EventLog.UseState("Outer", 0);
            Hooks.UseFallback(_ => V.Component(Probe, new Named("OF")));
            return V.Component(BrokenFallbackBoundary, dropping);
        }

        // GREEN_ON_BASE(characterization): the base sends a suspend in a boundary's fallback to the Suspense above it.
        // Have ShowFallbackTree return false where it catches a FiberSuspendSignal and the log reads
        // `lay-x:X ref-x:X uncaught:thrown by X |`.
        [Test]
        public void Given_ABoundaryWhoseFallbackSuspendsUnderASuspense_When_ItsOwnRenderCatchesAndTheReadResolves_Then_TheSuspenseFallbackShowsUntilTheBoundaryShowsItsFallbackAndReportsTheCatch()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = _renderer.Mount(log.Provide(V.Component(SuspenseAroundTheBoundary, gate)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Tick").Invoke(1);
            mounted.DrainForTest();
            log.Add("|");
            gate.Source?.TrySetResult(5);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:X ref-x:X ins:SF ref:SF lay:SF | ins-x:SF lay-x:SF ref-x:SF ins:IF ref:IF lay:IF caught:Inner:thrown by X"));
        }

        // GREEN_ON_BASE(characterization): the base lets the outer boundary's catch in its walk unwind the inner one's swap.
        // Have ShowFallbackTree return false where it catches a BoundaryCaughtSignal and the outer reports
        // `thrown by the ref cleanup` instead.
        [Test]
        public void Given_ABoundaryWhoseFallbackThrowsInsideAnOuterBoundarysRender_When_ARefCleanupInsideItThrows_Then_TheOuterShowsItsFallbackAndReportsTheFallbacksError()
        {
            // Arrange
            var log = new EventLog();
            var dropping = new Dropping();
            using var mounted = _renderer.Mount(log.Provide(V.Component(RenderingOuter, dropping)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Clear();

            // Act
            dropping.Drop = true;
            log.Setter<int>("Outer").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:OF ref:OF lay:OF caught:Outer:thrown by the fallback"));
        }

        private sealed class ConstructionRefusingElement : VisualElement
        {
            public ConstructionRefusingElement() => throw new InvalidOperationException("thrown by the constructor");
        }

        [Component(IsErrorBoundary = true, DisplayName = "Inner")]
        private static VNode RefusingFallbackBoundary()
        {
            Hooks.UseFallback(_ => V.Custom<ConstructionRefusingElement>());
            return V.Component(ThrowsOnOwnTick);
        }

        [Component]
        private static VNode ThrowsOnOwnTick()
        {
            var (tick, _) = EventLog.UseState("Tick", 0);
            EventLog.UseInsertionAndLayoutEffects("X", Array.Empty<object>());
            var refX = EventLog.UseRefCallback<VisualElement>("X");
            return tick > 0 ? throw new InvalidOperationException("thrown by X") : V.Div(refCallback: refX);
        }

        [Component(IsErrorBoundary = true, DisplayName = "Outer")]
        private static VNode OuterOfARefusingFallback()
        {
            Hooks.UseFallback(_ => V.Component(Probe, new Named("OF")));
            return V.Component(RefusingFallbackBoundary);
        }

        [TestFixture]
        internal sealed class UIToolkitOnlyFallbacks
        {
            [Test]
            public void Given_ABoundaryWhoseFallbackElementCannotBeCreated_When_ItsChildsOwnRenderThrows_Then_TheBoundaryAboveShowsItsFallback()
            {
                // Arrange
                var log = new EventLog();
                using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(OuterOfARefusingFallback)),
                    log.ReportingErrors());
                mounted.DrainForTest();
                log.Clear();
                LogAssert.Expect(LogType.Error, new Regex("reconciling the fallback UI threw"));
                LogAssert.Expect(LogType.Error, new Regex(@"\[ErrorBoundary\] An exception occurred"));
                LogAssert.Expect(LogType.Exception, new Regex("thrown by the constructor"));

                // Act
                log.Setter<int>("Tick").Invoke(1);
                mounted.DrainForTest();

                // Assert
                Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ref-x:X ins:OF ref:OF lay:OF caught:Outer:thrown by the constructor"));
            }
        }

        [Component]
        private static VNode ThrowsAtOnce() => throw new InvalidOperationException("thrown by the child");

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Inner")]
        private static VNode InnerWithABrokenFallback()
        {
            Hooks.UseFallback(_ => V.Component(BrokenContent));
            return V.Component(ThrowsAtOnce);
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "Outer")]
        private static VNode OuterShowingTheBrokenInner()
        {
            Hooks.UseFallback(_ => V.Component(InnerWithABrokenFallback));
            return V.Component(ThrowsAtOnce);
        }

        // GREEN_ON_BASE(characterization): the base has the outer, which is showing its fallback when the inner
        // boundary's fallback throws, report no catch.
        [Test]
        public void Given_ABoundaryWhoseFallbackHoldsABoundaryWithABrokenFallback_When_ItMountsAfterItsOwnChildThrew_Then_TheOuterReportsNothing()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = _renderer.Mount(log.Provide(V.Component(OuterShowingTheBrokenInner)), log.ReportingErrors());
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString().Contains("caught:Outer:"), Is.False);
        }
    }
}
