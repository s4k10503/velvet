using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the USS-only parity presets that have no C# parse path: the reverse
    /// flex utilities (<c>_layout.uss</c>), the larger font sizes (<c>_typography.uss</c>), the
    /// <c>size-*</c> width+height shorthand and <c>basis-*</c> flex-basis presets (<c>_sizing.uss</c> /
    /// <c>_layout.uss</c>), and the <c>origin-*</c> transform-origin utilities (<c>_transforms.uss</c>).
    /// Each mounts a leaf in a real <see cref="UnityEditor.EditorWindow"/> panel (via <see cref="PanelTestBase"/>)
    /// with the bundled <c>StyleUtilities.uss</c>, forces a layout pass, then reads <c>resolvedStyle</c>.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SizingFlexUssTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override Rect WindowSize => new Rect(0, 0, 600, 600);

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        [Test]
        public void Given_FlexRowReverseClass_When_Resolved_Then_SetsRowReverseDirection()
        {
            // Arrange/Act
            var leaf = MountAndResolve("flex flex-row-reverse");

            // Assert
            Assert.That(leaf.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse));
        }

        [Test]
        public void Given_FlexColReverseClass_When_Resolved_Then_SetsColumnReverseDirection()
        {
            // Arrange/Act
            var leaf = MountAndResolve("flex flex-col-reverse");

            // Assert
            Assert.That(leaf.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.ColumnReverse));
        }

        [Test]
        public void Given_FlexWrapReverseClass_When_Resolved_Then_SetsWrapReverse()
        {
            // Arrange/Act
            var leaf = MountAndResolve("flex flex-wrap-reverse");

            // Assert
            Assert.That(leaf.resolvedStyle.flexWrap, Is.EqualTo(Wrap.WrapReverse));
        }

        [Test]
        public void Given_Text5xlClass_When_Resolved_Then_FontSizeIs48()
        {
            // Arrange/Act — Velvet's token (48px), already referenced by sample screens.
            var leaf = MountAndResolve("text-5xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(48f));
        }

        [Test]
        public void Given_Text7xlClass_When_Resolved_Then_FontSizeIs72()
        {
            // Arrange/Act
            var leaf = MountAndResolve("text-7xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(72f));
        }

        [Test]
        public void Given_Size8Class_When_Resolved_Then_WidthAndHeightAre32()
        {
            // Arrange/Act — --space-8 == 32px; size-* writes both axes.
            var leaf = MountAndResolve("size-8");

            // Assert
            Assert.That((leaf.resolvedStyle.width, leaf.resolvedStyle.height), Is.EqualTo((32f, 32f)));
        }

        [Test]
        public void Given_Basis24Class_When_Resolved_Then_FlexBasisIs96()
        {
            // Arrange/Act — --space-24 == 96px.
            var leaf = MountAndResolve("basis-24");

            // Assert
            Assert.That(leaf.resolvedStyle.flexBasis.value, Is.EqualTo(96f));
        }

        [Test]
        public void Given_BasisPxClass_When_Resolved_Then_FlexBasisIs1px()
        {
            // Arrange/Act — --space-px == 1px (off the larger Velvet token curve).
            var leaf = MountAndResolve("basis-px");

            // Assert
            Assert.That(leaf.resolvedStyle.flexBasis.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_SizeHalfFractionClass_When_Resolved_Then_WidthIsHalfTheParent()
        {
            // Arrange — a 200px parent so the 50% fraction resolves to a stable 100px.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "w-[200px] h-[200px]",
                    V.Div(name: "leaf", className: "size-1/2")));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);

            // Assert — size-1/2 sets both axes to 50% (= 100px of the 200px parent).
            Assert.That((leaf.resolvedStyle.width, leaf.resolvedStyle.height), Is.EqualTo((100f, 100f)));
        }

        [Test]
        public void Given_HeightTwoThirdsFractionClass_When_Resolved_Then_HeightIsTwoThirdsOfParent()
        {
            // Arrange — a 300px-tall parent so the single-axis h-2/3 fraction resolves to a stable 200px.
            // (size-1/2 above proves the Size fan-out path; this pins the standalone Height setter + Percent.)
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "w-[300px] h-[300px]",
                    V.Div(name: "leaf", className: "h-2/3")));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);

            // Assert — h-2/3 == 66.667% of 300px == 200px.
            Assert.That(leaf.resolvedStyle.height, Is.EqualTo(200f).Within(0.5f));
        }

        [Test]
        public void Given_OriginTopRightClass_When_Resolved_Then_TransformOriginIsAtTheTopEdge()
        {
            // Arrange/Act — origin-top-right -> `right top`; the y component resolves to the top edge (0),
            // which is height-independent and reliably readable.
            var leaf = MountAndResolve("size-8 origin-top-right");

            // Assert
            Assert.That(leaf.resolvedStyle.transformOrigin.y, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AspectVideoClass_When_Resolved_Then_AspectRatioIs16By9()
        {
            // Arrange/Act — the USS-only aspect-video preset (16/9, stored as the reduced decimal).
            var leaf = MountAndResolve("aspect-video");

            // Assert
            Assert.That(leaf.resolvedStyle.aspectRatio.value, Is.EqualTo(16f / 9f).Within(0.001f));
        }
    }
}
