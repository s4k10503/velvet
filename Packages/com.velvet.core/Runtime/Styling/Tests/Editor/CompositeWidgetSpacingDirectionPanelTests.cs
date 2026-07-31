using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which element the inter-child spacing manipulators take their direction verdict from, on a
    /// composite widget that redirects its children into an inner box. The class string lands on the widget,
    /// so a reversed direction class reverses the widget and not the content: the boundary between two
    /// adjacent content children is still the axis's leading edge, and a plain <c>gap-*</c> takes its axis
    /// from the inner box rather than from the widget.
    /// <para>
    /// A real <see cref="UnityEditor.EditorWindow"/> panel with the bundled <c>StyleUtilities.uss</c> is
    /// required: <c>flex-row-reverse</c> is a USS-only rule, and an inner box's direction — which no Velvet
    /// class can reach — is readable only through <c>resolvedStyle</c>. The horizontally scrolling case is
    /// the one proving that branch runs at all; every other inner box here resolves to a column, which is
    /// also the off-panel default. GWT, one assert per case.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class CompositeWidgetSpacingDirectionPanelTests : PanelTestBase
    {
        // --space-4 == 16px (see _tokens.uss); a bare divide-x is a 1px border.
        private const float Space4 = 16f;
        private const float DivideWidth = 1f;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static VNode[] ThreeLabels() => new VNode[]
        {
            V.Label(name: "a", text: "a"),
            V.Label(name: "b", text: "b"),
            V.Label(name: "c", text: "c"),
        };

        // The geometry event is synthesized because the EditMode player loop delivers none, and the
        // manipulators only re-derive from one once resolvedStyle is valid.
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

        // V.Custom reaches the widgets with no first-class V factory; for a ScrollView it builds the same
        // node V.ScrollView does.
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

            // Assert — the content container is not reversed, so the boundary is a leading margin.
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

            // Assert — same boundary as the gap case, so a leading border.
            Assert.That(content[1].style.borderLeftWidth.value, Is.EqualTo(DivideWidth));
        }

        [Test]
        public void Given_ARowScrollView_When_APlainGapSpacesItsContent_Then_TheAxisFollowsTheContentContainer()
        {
            // Arrange / Act
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
            // Arrange / Act — a horizontal scroll mode resolves the inner box to a ROW, against both the
            // class on the widget and the off-panel default for an inner box, which are each a column.
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
            // Arrange / Act — the same mismatch on a widget that is not a ScrollView.
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
