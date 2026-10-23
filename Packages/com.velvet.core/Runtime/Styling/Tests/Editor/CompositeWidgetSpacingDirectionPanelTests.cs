using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how the inter-child spacing manipulators space a composite widget that redirects its children into
    /// an inner box. A direction class written on the widget lays out the content, as a CSS flex utility lays
    /// out the element's own children, so a reversed one puts the gap on the trailing edge between two content
    /// children; with no direction class the inner box keeps the direction the widget's own stylesheet gives it.
    /// <para>
    /// A real <see cref="UnityEditor.EditorWindow"/> panel with the bundled <c>StyleUtilities.uss</c> is
    /// required: the direction utilities are USS rules, and a widget's own stylesheet reaches its inner box
    /// only on a panel. The horizontally scrolling ScrollView and the horizontal TwoPaneSplitView are the rows
    /// the off-panel default mirrors (StyleFlexDirectionResolver.ResolveWithoutClasses). GWT, one assert per case.
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
        public void Given_AReversedColumnScrollView_When_ARowGapSpacesItsContent_Then_TheMarginSitsOnTheTrailingEdge()
        {
            // Arrange / Act — gap-y-4 is row-gap, which spaces the column the content container stacks in.
            var scrollView = MountWidget<ScrollView>("flex flex-col-reverse gap-y-4");
            var content = scrollView.contentContainer;

            // Assert — the content's resolved reversal rides along: a gap reading the widget's class without the
            // class reaching the content would put the margin on the same edge.
            Assert.That((content.resolvedStyle.flexDirection, content[1].style.marginBottom.value.value),
                Is.EqualTo((FlexDirection.ColumnReverse, Space4)));
        }

        [Test]
        public void Given_AGapRow_When_APlayDrivesAPercentMarginOnAChild_Then_TheGapAddsToItInPixels()
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
            var reference = _window.rootVisualElement.Q("ref").resolvedStyle.marginLeft;
            Assume.That(reference, Is.GreaterThan(0f), "Precondition: a percentage left unresolved could not pass");
            var plan = MotionSpringClassParser.Resolve(new[] { "ml-[10%]" }, new[] { "ml-[20%]" });
            var state = BezierTweenDriver.Create(plan, 0f, 0f, 1f, 1f, durationSec: 1f)!;

            // Act
            BezierTweenDriver.ApplyCurrentValues(child, state);
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(child.resolvedStyle.marginLeft, Is.EqualTo(reference + Space4));
        }

        [Test]
        public void Given_AGridWithAColumnGap_When_APlayDrivesTheSecondColumnsMargin_Then_ItMovesInsideItsColumn()
        {
            // Arrange — a grid item's margin sits inside its area, past the column gap, and its neighbours stay put.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "grid", className: "grid grid-cols-2 gap-4 w-[300px]",
                children: new VNode[] { V.Div(className: "h-[20px]"), V.Div(className: "h-[20px]") }));
            var container = _window.rootVisualElement.Q("grid");
            ForcePanelUpdate(container.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                container.SimulateEvent(evt);
            }
            var plan = MotionSpringClassParser.Resolve(new[] { "ml-[8px]" }, new[] { "ml-[16px]" });
            var state = BezierTweenDriver.Create(plan, 0f, 0f, 1f, 1f, durationSec: 1f)!;

            // Act
            BezierTweenDriver.ApplyCurrentValues(container[1], state);
            ForcePanelUpdate(container.panel);

            // Assert — a pixel either way separates this from the gap replaced (16px short) and from a wrap onto a
            // second row, which starts the child back at the row's left edge.
            Assert.That(container[1].layout.x, Is.EqualTo(container[0].layout.xMax + Space4 + 8f).Within(1f));
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
        public void Given_ARowScrollView_When_APlainGapSpacesItsContent_Then_TheContentIsSpacedAsARow()
        {
            // Arrange / Act
            var scrollView = MountWidget<ScrollView>("flex flex-row gap-4");
            var content = scrollView.contentContainer;

            // Assert — the content's resolved row rides along, for the reason the reversed column gives.
            Assert.That((content.resolvedStyle.flexDirection, content[1].style.marginLeft.value.value),
                Is.EqualTo((FlexDirection.Row, Space4)));
        }

        // GREEN_ON_BASE(characterization): the base resolves this row on a panel as well. It pins the engine fact
        // the off-panel default for a horizontally scrolling ScrollView mirrors.
        [Test]
        public void Given_AHorizontallyScrollingScrollView_When_ItResolvesOnAPanel_Then_ItsContentIsARow()
        {
            // Arrange / Act — no direction class, so the widget's own stylesheet lays the inner box out.
            var scrollView = MountAndResolve<ScrollView>(V.ScrollView(
                onCreated: el => ((ScrollView)el).mode = ScrollViewMode.Horizontal,
                children: ThreeLabels()));

            // Assert
            Assert.That(scrollView.contentContainer.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
        }

        // GREEN_ON_BASE(characterization): the base resolves this row on a panel as well. It pins the engine fact
        // the off-panel default for a horizontal TwoPaneSplitView mirrors.
        [Test]
        public void Given_AHorizontalTwoPaneSplitView_When_ItResolvesOnAPanel_Then_ItsContentIsARow()
        {
            // Arrange / Act
            var split = MountAndResolve<TwoPaneSplitView>(V.Custom<TwoPaneSplitView>(children: new VNode[]
            {
                V.Label(name: "a", text: "a"),
                V.Label(name: "b", text: "b"),
            }));

            // Assert
            Assert.That(split.contentContainer.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
        }

        [Test]
        public void Given_AReversedColumnFoldout_When_ARowGapSpacesItsContent_Then_TheMarginSitsOnTheTrailingEdge()
        {
            // Arrange / Act — the same on a widget that is not a ScrollView.
            var foldout = MountWidget<Foldout>("flex flex-col-reverse gap-y-4");
            var content = foldout.contentContainer;

            // Assert — as above.
            Assert.That((content.resolvedStyle.flexDirection, content[1].style.marginBottom.value.value),
                Is.EqualTo((FlexDirection.ColumnReverse, Space4)));
        }

        [Test]
        public void Given_AGapRow_When_AChildDeclaresAPercentMargin_Then_TheGapAddsToItOnceTheRowIsLaidOut()
        {
            // Arrange — the reference child resolves ml-[10%] the engine's way, in a row of the same width.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(name: "row", className: "flex flex-row gap-x-4 w-[200px]", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px] ml-[10%]"),
                }),
                V.Div(className: "flex flex-row w-[200px]", children: new VNode[]
                {
                    V.Div(name: "ref", className: "w-[20px] h-[20px] ml-[10%]"),
                }),
            }));
            var child = _window.rootVisualElement.Q("b");

            // Act — the row's geometry event, once laid out, is what finds its width.
            ForcePanelUpdate(child.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                _window.rootVisualElement.Q("row").SimulateEvent(evt);
            }
            ForcePanelUpdate(child.panel);

            // Assert — whether the reference resolved rides along, since an unresolved one reads zero.
            var reference = _window.rootVisualElement.Q("ref").resolvedStyle.marginLeft;
            Assert.That((reference > 0f, child.resolvedStyle.marginLeft - reference), Is.EqualTo((true, Space4)));
        }

    }
}
