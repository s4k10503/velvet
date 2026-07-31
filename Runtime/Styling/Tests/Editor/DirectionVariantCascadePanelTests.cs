using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the flex-direction utilities' USS cascade under a responsive variant.
    /// A variant is realized by adding the BARE utility to the live class list, so
    /// <c>"flex flex-col md:flex-row"</c> carries BOTH <c>.flex-col</c> and <c>.flex-row</c> above the
    /// breakpoint; both selectors are single-class, so specificity ties and the later-declared rule decides
    /// the direction. Asserting which classes are present therefore proves nothing — these cases run in a
    /// real <see cref="UnityEditor.EditorWindow"/> panel with the bundled <c>StyleUtilities.uss</c>
    /// attached, resize it across the <c>md</c> breakpoint, and read
    /// <c>resolvedStyle.flexDirection</c> (plus, for the gap polyfill, the inline margin edge it wrote).
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DirectionVariantCascadePanelTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;
        private const float WideWidth = 1000f;
        private const float NarrowWidth = 500f;
        private const float Space4 = 16f;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // Mounts a container at the given panel width and resolves it: the panel needs a forced layout pass
        // before resolvedStyle.width exists, and the responsive manipulator re-reads that width off a
        // GeometryChangedEvent on the width source (the panel root here), which the EditMode player loop
        // never delivers on its own.
        private VisualElement MountAt(float width, string className, params VNode[] children)
        {
            _window.position = new Rect(0, 0, width, 600);
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: className, children: children));
            var box = _window.rootVisualElement.Q<VisualElement>("box");
            ResolveAt(width, box);
            return box;
        }

        private void ResolveAt(float width, VisualElement box)
        {
            _window.position = new Rect(0, 0, width, 600);
            ForcePanelUpdate(box.panel);
            using var rootEvt = EventBase<GeometryChangedEvent>.GetPooled();
            box.panel.visualTree.SimulateEvent(rootEvt);
            // The variant has just rewritten the class list; the gap polyfill re-derives its axis from a
            // geometry event on its own container, which a real resize delivers and EditMode does not.
            using var boxEvt = EventBase<GeometryChangedEvent>.GetPooled();
            box.SimulateEvent(boxEvt);
            ForcePanelUpdate(box.panel);
        }

        [Test]
        public void Given_AColumnBaseWithAnMdRowVariant_When_TheRootIsNarrowerThanMd_Then_TheBaseColumnLaysOut()
        {
            // Arrange / Act — the documented responsive idiom below its breakpoint.
            var box = MountAt(NarrowWidth, "flex flex-col md:flex-row");
            Assume.That(box.panel.visualTree.resolvedStyle.width, Is.LessThan(MdBreakpoint),
                "Precondition: the panel root resolved below the md breakpoint");

            // Assert — only the base direction is on the element, so it lays out as a column.
            Assert.That(box.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Column));
        }

        [Test]
        public void Given_AColumnBaseWithAnMdRowVariant_When_TheRootIsWiderThanMd_Then_TheVariantRowWins()
        {
            // Arrange / Act — the same idiom above its breakpoint: both .flex-col and .flex-row now match.
            var box = MountAt(WideWidth, "flex flex-col md:flex-row");
            Assume.That(box.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert — the variant-applied direction beats the base one.
            Assert.That(box.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
        }

        [Test]
        public void Given_AColumnBaseWithAnMdRowVariant_When_ThePanelGrowsPastTheBreakpoint_Then_TheDirectionFlipsToRow()
        {
            // Arrange — mounted narrow, laying out as a column.
            var box = MountAt(NarrowWidth, "flex flex-col md:flex-row");
            Assume.That(box.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Column),
                "Precondition: the stack starts as a column below the breakpoint");

            // Act — the panel grows past md.
            ResolveAt(WideWidth, box);

            // Assert — the stack becomes a row.
            Assert.That(box.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
        }

        [Test]
        public void Given_AnMdRowVariantActiveWide_When_ThePanelShrinksBelowTheBreakpoint_Then_TheDirectionFallsBackToColumn()
        {
            // Arrange — mounted wide, so the md payload is on the element. The precondition reads the class
            // list rather than the resolved direction: it must hold independently of the cascade order this
            // fixture is pinning, so a broken order fails the assertion instead of skipping the case.
            var box = MountAt(WideWidth, "flex flex-col md:flex-row");
            Assume.That(box.ClassListContains("flex-row"), Is.True,
                "Precondition: the md payload is applied above the breakpoint");

            // Act — the panel shrinks below md.
            ResolveAt(NarrowWidth, box);

            // Assert — removing the variant class restores the base column.
            Assert.That(box.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Column));
        }

        [Test]
        public void Given_AColumnBaseWithAnMdColReverseVariant_When_TheRootIsWiderThanMd_Then_TheVariantReverseWins()
        {
            // Arrange / Act — the same-axis override: the plain form is declared before the reversed one, so
            // a variant can reverse a base direction without leaving the axis.
            var box = MountAt(WideWidth, "flex flex-col md:flex-col-reverse");
            Assume.That(box.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert
            Assert.That(box.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.ColumnReverse));
        }

        [Test]
        public void Given_AGapColumnWithAnMdRowVariant_When_TheRootIsWiderThanMd_Then_TheGapSpacesTheRowAxis()
        {
            // Arrange / Act — the gap polyfill picks its margin edge from the same class precedence the USS
            // cascade uses, so it must agree with the direction the element actually renders: horizontal,
            // leading edge.
            var box = MountAt(WideWidth, "flex flex-col md:flex-row gap-4",
                V.Label(name: "first", text: "a"), V.Label(name: "second", text: "b"));
            var second = box.Q<Label>("second");
            Assume.That(box.panel.visualTree.resolvedStyle.width, Is.GreaterThanOrEqualTo(MdBreakpoint),
                "Precondition: the panel root resolved at least the md breakpoint wide");

            // Assert — the inter-child margin sits on the row axis, not the column one.
            Assert.That(second.style.marginLeft.value.value, Is.EqualTo(Space4));
        }
    }
}
