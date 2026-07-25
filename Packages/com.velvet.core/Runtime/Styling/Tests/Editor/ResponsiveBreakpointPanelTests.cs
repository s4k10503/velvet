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
