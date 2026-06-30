using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural regression coverage for responsive breakpoint variants (<c>sm:</c>/<c>md:</c>/<c>lg:</c>…), which
    /// the conditional manipulator drives off the panel root's resolved width. These run inside a real
    /// <see cref="UnityEditor.EditorWindow"/> panel (via <see cref="PanelTestBase"/>) sized PER TEST to a known
    /// width, force the panel's layout pass so <c>resolvedStyle.width</c> resolves, then deliver a
    /// <see cref="GeometryChangedEvent"/> so the manipulator re-evaluates: the <c>md:</c> payload must be present
    /// only while the root is at least the md breakpoint (768px) wide, and must toggle when the panel is resized
    /// across that boundary. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ResponsiveBreakpointPanelTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;

        // Sets the panel width, forces the layout pass (resolvedStyle.width only resolves once the panel updates),
        // then fires a GeometryChangedEvent on the panel root so the responsive manipulator re-reads the width.
        private Label MountAndResolveAt(float width, string className)
        {
            _window.position = new Rect(0, 0, width, 600);
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "leaf", className: className, text: "x"));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            ResolveAt(width, leaf);
            return leaf;
        }

        private void ResolveAt(float width, VisualElement leaf)
        {
            _window.position = new Rect(0, 0, width, 600);
            ForcePanelUpdate(leaf.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            leaf.panel.visualTree.SimulateEvent(evt);
        }

        [Test]
        public void Given_AnMdVariantLeaf_When_TheRootIsWiderThanTheMdBreakpoint_Then_ThePayloadIsApplied()
        {
            // Arrange/Act — an md:bg-wide leaf in a 1000px-wide panel (≥ md 768).
            var leaf = MountAndResolveAt(1000f, "md:bg-wide");
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert — the md payload is applied above the breakpoint.
            Assert.IsTrue(leaf.ClassListContains("bg-wide"));
        }

        [Test]
        public void Given_AnMdVariantLeaf_When_TheRootIsNarrowerThanTheMdBreakpoint_Then_ThePayloadIsNotApplied()
        {
            // Arrange/Act — an md:bg-wide leaf in a 500px-wide panel (< md 768).
            var leaf = MountAndResolveAt(500f, "md:bg-wide");
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.LessThan(MdBreakpoint),
                "Precondition: the panel root resolved below the md breakpoint");

            // Assert — the md payload stays off below the breakpoint.
            Assert.IsFalse(leaf.ClassListContains("bg-wide"));
        }

        [Test]
        public void Given_AnMdPayloadActiveWide_When_ThePanelShrinksBelowTheBreakpoint_Then_ThePayloadIsRemoved()
        {
            // Arrange — an md:bg-wide leaf applied while the panel is wide.
            var leaf = MountAndResolveAt(1000f, "md:bg-wide");
            Assume.That(leaf.ClassListContains("bg-wide"), Is.True, "Precondition: payload on while wide");

            // Act — the panel shrinks below the md breakpoint and re-resolves.
            ResolveAt(500f, leaf);

            // Assert — the responsive payload toggles back off.
            Assert.IsFalse(leaf.ClassListContains("bg-wide"));
        }
    }
}
