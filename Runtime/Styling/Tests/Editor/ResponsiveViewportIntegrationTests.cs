using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// End-to-end coverage for the contract a preview Viewport switcher relies on: a mount canvas marked with the
    /// public <see cref="VelvetResponsive.ContainerClass"/> at a simulated viewport width becomes a responsive
    /// scope, so a story mounted under it has its <c>md:</c> breakpoint driven by that width — not the panel's.
    /// This is what makes a simulated narrow viewport in a wide editor window flip a descendant's responsive
    /// variant. Runs in a real panel (via <see cref="PanelTestBase"/>) wide enough that, without the scope, the
    /// breakpoint would be on. GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class ResponsiveViewportIntegrationTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;
        private const float PanelWidth = 1200f;   // ≥ md, so the panel alone would turn md: on
        private const float MobileWidth = 375f;    // < md, a simulated mobile viewport
        private const float DesktopWidth = 1000f;  // ≥ md, a simulated desktop viewport

        protected override Rect WindowSize => new Rect(0, 0, PanelWidth, 700);

        // Mounts a canvas sized to a simulated viewport width and marked as a responsive scope (what the preview
        // window's viewport switcher does), with an md: leaf inside, then resolves layout and re-reads the scope.
        private Label MountInViewport(float viewportWidth)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "canvas", className: VelvetResponsive.ContainerClass + " w-[" + (int)viewportWidth + "px]",
                    children: new VNode[] { V.Label(name: "leaf", className: "md:bg-wide", text: "x") }));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            var canvas = _window.rootVisualElement.Q<VisualElement>("canvas");
            ForcePanelUpdate(leaf.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            canvas.SimulateEvent(evt);
            return leaf;
        }

        [Test]
        public void Given_AMobileViewportScope_When_ThePanelIsWide_Then_TheDescendantMdStaysOff()
        {
            // Arrange / Act — a 375px scope inside a 1200px panel.
            var leaf = MountInViewport(MobileWidth);
            Assume.That(_window.rootVisualElement.Q<VisualElement>("canvas").resolvedStyle.width,
                Is.LessThan(MdBreakpoint), "Precondition: the simulated viewport resolved below md");

            // Assert — the descendant's md: follows the narrow simulated viewport, not the wide panel.
            Assert.IsFalse(leaf.ClassListContains("bg-wide"));
        }

        [Test]
        public void Given_ADesktopViewportScope_When_Mounted_Then_TheDescendantMdTurnsOn()
        {
            // Arrange / Act — a 1000px scope (≥ md).
            var leaf = MountInViewport(DesktopWidth);
            Assume.That(_window.rootVisualElement.Q<VisualElement>("canvas").resolvedStyle.width,
                Is.GreaterThanOrEqualTo(MdBreakpoint), "Precondition: the simulated viewport resolved at/above md");

            // Assert — the wide simulated viewport drives the descendant's md: on.
            Assert.IsTrue(leaf.ClassListContains("bg-wide"));
        }
    }
}
