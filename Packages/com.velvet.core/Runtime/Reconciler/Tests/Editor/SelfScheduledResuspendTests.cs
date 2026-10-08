using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a component under a revealed Suspense whose own update, drained from the immediate tier, suspends
    /// again: the boundary shows its fallback and the tier still registers a drain for an unrelated update, and a
    /// resource that then faults reaches the error boundary above.
    /// </summary>
    [TestFixture]
    internal sealed class SelfScheduledResuspendTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_source = null;
            s_setOwn = default;
            s_setOther = default;
        }

        // GREEN_ON_BASE(characterization): the base already catches this suspend in FiberWorkLoop.FlushState.
        // What this pins is the fallback, and a scheduler that keeps registering drains after it.
        [Test]
        public void Given_ARevealedBoundaryWhoseChildsOwnUpdateSuspends_When_AnUnrelatedComponentUpdatesAfterTheDrain_Then_TheFallbackShowsAndADrainIsRegistered()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_setOwn.Invoke(1);
            scheduler.DrainImmediateForTest();
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            s_setOther.Invoke(1);

            // Assert
            Assert.That((Texts(), scheduler.ScheduledCallbackCount - callbacksBefore), Is.EqualTo(("other:0|loading", 1)),
                "React shows the fallback again, and the suspend reaches neither the host scheduler nor its next drain");
        }

        // GREEN_ON_BASE(characterization): the base already routes this faulted read to the error boundary.
        [Test]
        public void Given_AChildWhoseOwnUpdateSuspendedARevealedBoundary_When_ItsResourceFaults_Then_TheErrorBoundaryShowsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(
                _root, V.Component(ErrorBoundaryRender, key: "error-boundary"), new MountOptions((_, _) => { }));
            var scheduler = mounted.GetSchedulerForTest();
            s_setOwn.Invoke(1);
            scheduler.DrainImmediateForTest();

            // Act
            s_source.TrySetException(new InvalidOperationException("boom"));
            mounted.FlushStateForTest();
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("error"),
                "React throws the rejected read to the error boundary above the Suspense");
        }

        private string Texts() => string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));

        private static VelvetTaskCompletionSource<int> s_source;
        private static StateUpdater<int> s_setOwn;
        private static StateUpdater<int> s_setOther;

        private static VNode HostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(OtherRender, key: "other"),
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(ReaderRender, key: "reader") }),
            });

        [Component(IsErrorBoundary = true)]
        private static VNode ErrorBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "error"));
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(ReaderRender, key: "reader") });
        }

        private static VNode ReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(text: "reader:" + value);
        }

        private static VNode OtherRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setOther = setValue;
            return V.Label(text: "other:" + value);
        }
    }
}
