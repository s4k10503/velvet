using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A PopLayout exit pins the keyed child's top-level element, which for a clipped child is the clip wrapper,
    /// and cancelling the exit clears that pin again. The clip layer's own geometry sync has to leave a pin in
    /// place and restore what the cancel cleared. Both cases call the reconciler's own pin and restore on the
    /// wrapper directly, because the AnimatePresence shapes tried here did not keep a clipped wrapper as a
    /// working exit anchor. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathWrapperPopLayoutPanelTests : PanelTestBase
    {
        private const string Triangle = "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement Named(string name) => _window.rootVisualElement.Q<VisualElement>(name);

        private static void Invoke(string method, params object[] args)
            => typeof(GeneralPathReconciler)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, args);

        // The host's border puts the pin's left/top a border's width from where the wrapper sat, so the pinned
        // wrapper moves and raises a geometry event of its own.
        private VisualElement MountCardInBorderedHost(string cardClass)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "host",
                className: "relative border-[2px] w-[200px] h-[200px]",
                children: new VNode[] { V.Div(name: "card", className: cardClass + " " + Triangle) }));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return Named("card");
        }

        // GREEN_ON_BASE(characterization): the base writes the wrapper's position once, so a pin there stays.
        // A geometry sync that rewrites the position on every pass is what reddens this.
        [Test]
        public void Given_AnInFlowClippedElement_When_ItsWrapperIsPinnedAndLaidOut_Then_TheWrapperStaysOutOfFlow()
        {
            // Arrange
            var wrapper = MountCardInBorderedHost("w-[60px] h-[24px]").parent;

            // Act
            Invoke("PinExitingChildOutOfFlow", wrapper);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                (wrapper.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass),
                    wrapper.resolvedStyle.position),
                Is.EqualTo((true, Position.Absolute)));
        }

        [Test]
        public void Given_APinnedAbsoluteClippedElement_When_ThePinIsCleared_Then_ItKeepsTheBoxItsOffsetsDeclare()
        {
            // Arrange
            var card = MountCardInBorderedHost("absolute left-[10px] top-[10px] right-[10px] bottom-[10px]");
            var wrapper = card.parent;
            Invoke("PinExitingChildOutOfFlow", wrapper);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            Invoke("RestorePopLayoutChildToFlow", wrapper, null);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert: the host is 200x200 including a 2px border, so the offsets leave a 176x176 box at (12, 12).
            var host = Named("host").worldBound;
            var box = card.worldBound;
            Assert.That(
                (wrapper.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass),
                    new Rect(box.x - host.x, box.y - host.y, box.width, box.height)),
                Is.EqualTo((true, new Rect(12f, 12f, 176f, 176f))));
        }
    }
}
