using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural coverage for the responsive scope (the CSS container-query analog): an element marked
    /// <c>@container</c> becomes a "responsive root" so its descendants' <c>sm:</c>/<c>md:</c>… breakpoints
    /// evaluate against ITS width instead of the panel root's. These run in a real
    /// <see cref="UnityEditor.EditorWindow"/> panel (via <see cref="PanelTestBase"/>): the panel is sized wide,
    /// the scope a fixed narrower width, and the leaf carries a responsive variant — so the scope width, not the
    /// panel width, must decide whether the payload is on. Includes the no-scope regression guard (panel width
    /// still drives breakpoints), nested scopes (nearest wins), and stacked (<c>dark:md:</c>). GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class ResponsiveScopePanelTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;
        private const float PanelWidth = 1000f;   // ≥ md
        private const float NarrowScope = 500f;    // < md
        private const float WideScope = 900f;      // ≥ md

        protected override Rect WindowSize => new Rect(0, 0, PanelWidth, 600);

        // Forces a layout pass, then fires a GeometryChangedEvent on the given width source so the responsive
        // manipulator re-reads it. The source is the panel root (no scope) or a scope element.
        private void Resolve(VisualElement leaf, VisualElement widthSource)
        {
            ForcePanelUpdate(leaf.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            widthSource.SimulateEvent(evt);
        }

        // A panel-width-wide leaf with a responsive variant but NO scope ancestor: the regression guard that
        // unscoped trees keep evaluating against the panel root.
        [Test]
        public void Given_NoScope_When_PanelIsWiderThanMd_Then_PanelWidthStillDrivesTheBreakpoint()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "leaf", className: "md:bg-wide", text: "x"));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            Resolve(leaf, leaf.panel.visualTree);
            Assume.That(leaf.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: panel root resolved at least the md breakpoint wide");

            // Assert — without a scope the panel width (≥ md) drives the breakpoint on, exactly as before.
            Assert.IsTrue(leaf.ClassListContains("bg-wide"));
        }

        // A narrow scope around a leaf in a wide panel: the scope's width (< md), not the panel's (≥ md), decides.
        [Test]
        public void Given_ANarrowScope_When_PanelIsWide_Then_TheScopeWidthKeepsTheBreakpointOff()
        {
            // Arrange — @container scope fixed below md, leaf inside it, panel wide.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("@container w-[500px]", V.Label(name: "leaf", className: "md:bg-wide", text: "x")));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            var scope = leaf.parent;
            Resolve(leaf, scope);
            Assume.That(scope.resolvedStyle.width, Is.LessThan(MdBreakpoint), "Precondition: scope resolved below md");

            // Assert — the descendant's md: follows the narrow scope, not the wide panel.
            Assert.IsFalse(leaf.ClassListContains("bg-wide"));
        }

        // Widening the scope across the breakpoint flips the descendant's variant on.
        [Test]
        public void Given_ANarrowScope_When_TheScopeWidensPastMd_Then_TheDescendantBreakpointTurnsOn()
        {
            // Arrange — start with a sub-md scope (payload off).
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("@container w-[500px]", V.Label(name: "leaf", className: "md:bg-wide", text: "x")));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            var scope = leaf.parent;
            Resolve(leaf, scope);
            Assume.That(leaf.ClassListContains("bg-wide"), Is.False, "Precondition: payload off in the narrow scope");

            // Act — grow the scope past the md breakpoint and re-resolve against it.
            scope.style.width = WideScope;
            Resolve(leaf, scope);

            // Assert — the descendant's md: now follows the widened scope.
            Assert.IsTrue(leaf.ClassListContains("bg-wide"));
        }

        // Nested scopes: the NEAREST @container ancestor wins (the inner narrow scope, not the outer wide one).
        [Test]
        public void Given_NestedScopes_When_TheInnerIsNarrow_Then_TheNearestScopeWins()
        {
            // Arrange — wide outer @container (≥ md) wrapping a narrow inner @container (< md) wrapping the leaf.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("@container w-[900px]",
                    V.Div("@container w-[500px]", V.Label(name: "leaf", className: "md:bg-wide", text: "x"))));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            var innerScope = leaf.parent;
            Resolve(leaf, innerScope);
            Assume.That(innerScope.resolvedStyle.width, Is.LessThan(MdBreakpoint),
                "Precondition: the nearest (inner) scope resolved below md");

            // Assert — the nearest (narrow) scope decides; the wide outer scope does not leak through.
            Assert.IsFalse(leaf.ClassListContains("bg-wide"));
        }

        // Stacked variant (dark:md:): the responsive inner of a stack also respects the scope width.
        [Test]
        public void Given_AStackedDarkMdLeaf_When_DarkAndTheScopeIsNarrow_Then_TheScopeKeepsItOff()
        {
            // Arrange — dark on, but the md: inner is gated by the narrow scope width (< md).
            VelvetTheme.IsDark = true;
            try
            {
                _mounted = V.Mount(_window.rootVisualElement,
                    V.Div("@container w-[500px]", V.Label(name: "leaf", className: "dark:md:bg-wide", text: "x")));
                var leaf = _window.rootVisualElement.Q<Label>("leaf");
                var scope = leaf.parent;
                Resolve(leaf, scope);
                Assume.That(scope.resolvedStyle.width, Is.LessThan(MdBreakpoint), "Precondition: scope below md");

                // Assert — dark is satisfied, but the scope-driven md: inner gate stays closed, so nothing applies.
                Assert.IsFalse(leaf.ClassListContains("bg-wide"));
            }
            finally
            {
                VelvetTheme.IsDark = false;
            }
        }
    }
}
