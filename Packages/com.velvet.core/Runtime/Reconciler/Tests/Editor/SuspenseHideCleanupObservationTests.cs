using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a component's layout cleanup finds of its own element when a revealed <c>V.Suspense</c> suspends
    /// again: React 19 runs <c>disappearLayoutEffects</c> ahead of <c>hideOrUnhideAllChildren</c>, so the element
    /// is still displayed, and still focused, when the cleanup runs. Mounted in a real
    /// <see cref="UnityEditor.EditorWindow"/> panel so a focus controller exists.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHideCleanupObservationTests : PanelTestBase
    {
        private sealed class Probe
        {
            internal Func<VisualElement, string> Read;
            internal VisualElement? Element;
            internal string? AtCleanup;
            internal StateUpdater<int> SetBlocked;
        }

        [Component]
        private static VNode Watched(Probe probe)
        {
            var refCallback = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                probe.Element = element;
                return null;
            }, Array.Empty<object>());
            Hooks.UseLayoutEffect(() => (Action)(() => probe.AtCleanup = probe.Read(probe.Element!)), Array.Empty<object>());
            return V.Button(name: "watched", refCallback: refCallback);
        }

        // Pending for good once blocked, so the boundary keeps its fallback for the rest of the case.
        [Component]
        private static VNode Reader(int blocked)
        {
            var value = Hooks.Use(
                () => blocked > 0 ? new VelvetTaskCompletionSource<int>().Task : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode Host(Probe probe)
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            probe.SetBlocked = setBlocked;
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Component(Watched, probe, key: "watched"),
                    V.Component(Reader, blocked, key: "reader"),
                });
        }

        private void Suspend(Probe probe)
        {
            probe.SetBlocked.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private bool DriveFocus(VisualElement target)
        {
            _window.Focus();
            target.Focus();
            ForcePanelUpdate(target.panel);
            return ReferenceEquals(target.focusController?.focusedElement, target);
        }

        [Test]
        public void Given_AShownComponentWhoseElementTheBoundaryHides_When_TheBoundarySuspendsAgain_Then_ItsLayoutCleanupFindsTheElementStillDisplayed()
        {
            // Arrange
            var probe = new Probe { Read = element => element.style.display.value == DisplayStyle.None ? "hidden" : "displayed" };
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Host, probe));

            // Act
            Suspend(probe);

            // Assert
            Assert.That(probe.AtCleanup, Is.EqualTo("displayed"));
        }

        [Test]
        public void Given_AFocusedButtonWhoseComponentTheBoundaryHides_When_TheBoundarySuspendsAgain_Then_ItsLayoutCleanupFindsTheButtonStillFocused()
        {
            // Arrange
            var probe = new Probe
            {
                Read = element => ReferenceEquals(element.focusController?.focusedElement, element) ? "focused" : "blurred",
            };
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Host, probe));
            var granted = DriveFocus(probe.Element!);

            // Act
            Suspend(probe);

            // Assert — a button the focus never reached fails here rather than reading as one the cleanup found focused
            Assert.That(granted ? probe.AtCleanup : "never focused", Is.EqualTo("focused"));
        }
    }
}
