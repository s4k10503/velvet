using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which element the inter-child spacing manipulators take their direction verdict from when the two
    /// candidates differ. They differ on any composite widget that redirects its children into an inner box:
    /// a class string lands on the widget, whose own box lays out its chrome (a ScrollView's viewport and
    /// scrollers, a Foldout's toggle above its content), while the children being spaced are reconciled one
    /// level down. A reversed direction class therefore reverses the widget and not the content, so the
    /// boundary between two adjacent content children is still the axis's leading edge — reading the attached
    /// element instead moves the gap margin and the divider border to the trailing edge of content that is
    /// painted in source order. A plain <c>gap-*</c> likewise takes its AXIS from the inner box, so a
    /// <c>flex-row</c> on the widget does not make its column-stacked content space horizontally.
    /// <para>
    /// These cases need a real <see cref="UnityEditor.EditorWindow"/> panel with the bundled
    /// <c>StyleUtilities.uss</c> attached: <c>flex-row-reverse</c> is a USS-only rule, so nothing reports the
    /// widget as genuinely reversed without one, and an inner box's own direction — which no Velvet class can
    /// reach — is readable only through <c>resolvedStyle</c>. The horizontally scrolling case is the one that
    /// proves the <c>resolvedStyle</c> branch actually runs: every other inner box here resolves to a column,
    /// which is also the off-panel default, so it alone separates "resolved" from "defaulted and lucky".
    /// GWT, one assert per case.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class CompositeWidgetSpacingDirectionPanelTests : PanelTestBase
    {
        // --space-4 == 16px (see _tokens.uss); a bare divide-x is a 1px border.
        private const float Space4 = 16f;
        private const float DivideWidth = 1f;

        protected override void LoadStyleSheets() => _window.rootVisualElement.LoadBundledStyleUtilitiesForTest();

        private static VNode[] ThreeLabels() => new VNode[]
        {
            V.Label(name: "a", text: "a"),
            V.Label(name: "b", text: "b"),
            V.Label(name: "c", text: "c"),
        };

        // Mounts node and resolves the panel around its widget. The manipulators re-derive from a geometry
        // event on their own container once resolvedStyle is valid, which the EditMode player loop never
        // delivers on its own.
        private T MountAndResolve<T>(VNode node) where T : VisualElement
        {
            _mounted = V.Mount(_window.rootVisualElement, node);
            var widget = _window.rootVisualElement.Q<T>();
            ForcePanelUpdate(widget.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            widget.SimulateEvent(evt);
            ForcePanelUpdate(widget.panel);
            return widget;
        }

        // V.Custom is the mount path here so one helper covers every redirecting widget, including the ones
        // with no first-class V factory; for a ScrollView it builds the same node V.ScrollView does.
        private T MountWidget<T>(string className) where T : VisualElement
            => MountAndResolve<T>(V.Custom<T>(className, ThreeLabels()));

        [Test]
        public void Given_AReversedRowScrollView_When_AGapSpacesItsContent_Then_TheMarginSitsOnTheLeadingEdge()
        {
            // Arrange / Act
            var scrollView = MountWidget<ScrollView>("flex flex-row-reverse gap-x-4");
            var content = scrollView.contentContainer;
            Assume.That(scrollView.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse),
                "Precondition: the panel resolved flex-row-reverse on the ScrollView's own box");
            Assume.That(content.childCount, Is.EqualTo(3),
                "Precondition: the spaced children reconcile into the content container");

            // Assert — the content container is not reversed, so the gap between the first pair is the
            // second child's leading margin.
            Assert.That(content[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AReversedRowScrollView_When_ADivideSeparatesItsContent_Then_TheBorderSitsOnTheLeadingEdge()
        {
            // Arrange / Act
            var scrollView = MountWidget<ScrollView>("flex flex-row-reverse divide-x divide-gray-200");
            var content = scrollView.contentContainer;
            Assume.That(scrollView.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse),
                "Precondition: the panel resolved flex-row-reverse on the ScrollView's own box");
            Assume.That(content.childCount, Is.EqualTo(3),
                "Precondition: the divided children reconcile into the content container");

            // Assert — same boundary, same reasoning: the divider between the first pair is the second
            // child's leading border.
            Assert.That(content[1].style.borderLeftWidth.value, Is.EqualTo(DivideWidth));
        }

        [Test]
        public void Given_ARowScrollView_When_APlainGapSpacesItsContent_Then_TheAxisFollowsTheContentContainer()
        {
            // Arrange / Act — a plain gap-* takes its axis from the resolved direction, and the class fixes
            // the ScrollView's own direction to a row while its content container stays a column.
            var scrollView = MountWidget<ScrollView>("flex flex-row gap-4");
            var content = scrollView.contentContainer;
            Assume.That(scrollView.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row),
                "Precondition: the panel resolved flex-row on the ScrollView's own box");
            Assume.That(content.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Column),
                "Precondition: the content container lays its children out as a column");

            // Assert — the gap spaces the axis the content actually stacks on.
            Assert.That(content[1].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AHorizontallyScrollingScrollView_When_APlainGapSpacesItsContent_Then_TheAxisFollowsTheResolvedRow()
        {
            // Arrange / Act — a horizontal scroll mode is the case where the inner box resolves to a ROW,
            // against both the class on the widget (a column) and the off-panel default for an inner box
            // (also a column). Only reading the box's resolved style can land on the row's leading edge.
            var scrollView = MountAndResolve<ScrollView>(V.ScrollView(
                className: "flex flex-col gap-4",
                onCreated: el => ((ScrollView)el).mode = ScrollViewMode.Horizontal,
                children: ThreeLabels()));
            var content = scrollView.contentContainer;
            Assume.That(content.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row),
                "Precondition: the horizontal scroll mode lays the content container out as a row");
            Assume.That(scrollView.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Column),
                "Precondition: the class still resolves the ScrollView's own box to a column");

            // Assert — the row's leading edge, which neither the class nor the default would have chosen.
            Assert.That(content[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AReversedRowFoldout_When_AGapSpacesItsContent_Then_TheMarginSitsOnTheLeadingEdge()
        {
            // Arrange / Act — the same mismatch on a widget with no first-class V factory, reached through
            // V.Custom: a Foldout redirects its children into the box below its toggle.
            var foldout = MountWidget<Foldout>("flex flex-row-reverse gap-x-4");
            var content = foldout.contentContainer;
            Assume.That(foldout.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse),
                "Precondition: the panel resolved flex-row-reverse on the Foldout's own box");
            Assume.That(content.childCount, Is.EqualTo(3),
                "Precondition: the spaced children reconcile into the Foldout's inner container");

            // Assert
            Assert.That(content[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }
    }
}
