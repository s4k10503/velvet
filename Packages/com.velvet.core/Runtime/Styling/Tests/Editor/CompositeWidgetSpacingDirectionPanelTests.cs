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

        // GREEN_ON_BASE(characterization): the base already reads the content container's direction rather
        // than the widget's. What reddens it is the gap manipulator resolving its direction from the widget,
        // which puts the row gap on margin-bottom.
        [Test]
        public void Given_AReversedColumnScrollView_When_ARowGapSpacesItsContent_Then_TheMarginSitsOnTheLeadingEdge()
        {
            // Arrange / Act — gap-y-4 is row-gap, which spaces the column the content container stacks in.
            var scrollView = MountWidget<ScrollView>("flex flex-col-reverse gap-y-4");
            var content = scrollView.contentContainer;

            // Assert — the content container is not reversed, so the boundary is a leading margin; the widget's
            // own reversal rides along, since a panel that never resolved it would pass for either reason.
            Assert.That((scrollView.resolvedStyle.flexDirection, content[1].style.marginTop.value.value),
                Is.EqualTo((FlexDirection.ColumnReverse, Space4)));
        }

        [Test]
        public void Given_AGapRow_When_AMotionDriverWritesAPercentMarginOnAChild_Then_TheGapAddsToItInPixels()
        {
            // Arrange — the reference child resolves ml-[10%] the engine's way, in a row of the same width.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(className: "flex flex-row gap-x-4 w-[200px]", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px]"),
                }),
                V.Div(className: "flex flex-row w-[200px]", children: new VNode[]
                {
                    V.Div(name: "ref", className: "w-[20px] h-[20px] ml-[10%]"),
                }),
            }));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);

            // Act
            StyleArbitraryValueResolver.ApplyDriven(child,
                new ArbitraryStyle(ArbitraryProperty.MarginLeft, 10f, LengthUnit.Percent));
            ForcePanelUpdate(child.panel);

            // Assert — the reference's margin is non-zero, so a percentage left unresolved could not pass.
            var reference = _window.rootVisualElement.Q("ref").resolvedStyle.marginLeft;
            Assert.That((reference > 0f, child.resolvedStyle.marginLeft),
                Is.EqualTo((true, reference + Space4)));
        }

        [Test]
        public void Given_AScrollView_When_ADivideSeparatesItsContent_Then_TheBorderLandsOnTheContentChildren()
        {
            // Arrange / Act
            var scrollView = MountWidget<ScrollView>("flex divide-x divide-gray-200");
            var content = scrollView.contentContainer;

            // Assert — the divider lands on the content children, on the end edge its rule always uses; the
            // child count rides along, since the children have to have reconciled into the content container.
            Assert.That((content.childCount, content[1].style.borderRightWidth.value), Is.EqualTo((3, DivideWidth)));
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

        // GREEN_ON_BASE(characterization): the base already reads the Foldout's content container. What
        // reddens it is the gap manipulator resolving its direction from the Foldout's own box.
        [Test]
        public void Given_AReversedColumnFoldout_When_ARowGapSpacesItsContent_Then_TheMarginSitsOnTheLeadingEdge()
        {
            // Arrange / Act — the same mismatch on a widget that is not a ScrollView.
            var foldout = MountWidget<Foldout>("flex flex-col-reverse gap-y-4");
            var content = foldout.contentContainer;

            // Assert
            Assert.That((foldout.resolvedStyle.flexDirection, content[1].style.marginTop.value.value),
                Is.EqualTo((FlexDirection.ColumnReverse, Space4)));
        }

    }
}
