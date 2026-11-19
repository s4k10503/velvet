using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a component under a revealed Suspense whose own update, drained from the immediate tier, suspends
    /// again: the boundary shows its fallback and an unrelated update still commits in the next drain, and a
    /// resource that then faults reaches the error boundary above. A component in a Suspense's fallback whose own
    /// update suspends reaches the Suspense above that one, which shows its fallback without the component rendering
    /// the inner Suspense being asked to render.
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

        // GREEN_ON_BASE(characterization): the base removes the children the boundary had shown. What this pins
        // is the fallback, and an unrelated update committing beside it in the next drain. Read as every label
        // rather than the displayed ones, this reddens on SetPrimaryHidden keeping the reader's label in the tree,
        // hidden. Whether a drain is registered after an exception is DrainExceptionRecoveryTests' question; this
        // suspend throws nothing out of the drain.
        [Test]
        public void Given_ARevealedBoundaryWhoseChildsOwnUpdateSuspends_When_AnUnrelatedComponentUpdatesInTheNextDrain_Then_ItCommitsBesideTheFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_setOwn.Invoke(1);
            scheduler.DrainImmediateForTest();

            // Act
            s_setOther.Invoke(1);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("other:1|loading"),
                "React shows the fallback again, and an unrelated update still commits in the next drain");
        }

        // GREEN_ON_BASE(refactor): only how this case spells its MountOptions changed.
        // OnCaughtError is now set as the init property the options take, which the base's record accepts too.
        [Test]
        public void Given_AChildWhoseOwnUpdateSuspendedARevealedBoundary_When_ItsResourceFaults_Then_TheErrorBoundaryShowsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(
                _root, V.Component(ErrorBoundaryRender, key: "error-boundary"), new MountOptions { OnCaughtError = (_, _) => { } });
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

        [Test]
        public void Given_AFallbackWhoseOwnUpdateSuspends_When_TheImmediateTierDrains_Then_TheSuspenseAboveShowsItsFallbackWithoutTheFallbacksOwnerRendering()
        {
            // Arrange — the owner is memoized, so only a render asked of it renders it
            s_pendingSource = new VelvetTaskCompletionSource<int>();
            using var mounted = V.Mount(_root, V.Component(OuterSuspenseRender, key: "outer"));
            s_fallbackOwnerRenders = 0;

            // Act
            s_setFallbackKey.Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That($"{Texts()} | {s_fallbackOwnerRenders}", Is.EqualTo("outer loading | 0"),
                "React takes a suspend in a fallback to the Suspense above the one showing it");
        }

        // The labels a user sees: a Suspense keeps the children it hid mounted under an inline display: none.
        private string Texts() => string.Join("|", _root.Query<Label>().ToList().Where(IsDisplayed).Select(label => label.text));

        private static bool IsDisplayed(VisualElement element)
        {
            for (var ve = element; ve != null; ve = ve.parent)
            {
                if (ve.style.display == DisplayStyle.None) return false;
            }
            return true;
        }

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

        private static VelvetTaskCompletionSource<int> s_pendingSource;
        private static StateUpdater<int> s_setFallbackKey;
        private static int s_fallbackOwnerRenders;

        private static VNode OuterSuspenseRender()
            => V.Suspense(
                fallback: V.Label(text: "outer loading"),
                children: new VNode[] { V.Component(FallbackOwnerRender, key: "owner") });

        [Component(Compiler = false, Memoize = true)]
        private static VNode FallbackOwnerRender()
        {
            s_fallbackOwnerRenders++;
            return V.Suspense(
                fallback: V.Component(FallbackReaderRender, key: "fallback-reader"),
                children: new VNode[] { V.Component(PendingReaderRender, key: "pending-reader") });
        }

        private static VNode PendingReaderRender()
            => V.Label(text: "primary:" + Hooks.Use<int>(_ => s_pendingSource.Task, "pending"));

        private static VNode FallbackReaderRender()
        {
            var (key, setKey) = Hooks.UseState(0);
            s_setFallbackKey = setKey;
            var value = Hooks.Use<int>(
                _ => key == 0 ? VelvetTask.FromResult(0) : new VelvetTaskCompletionSource<int>().Task, key);
            return V.Label(text: "fallback:" + value);
        }

        private static VNode OtherRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setOther = setValue;
            return V.Label(text: "other:" + value);
        }
    }
}
