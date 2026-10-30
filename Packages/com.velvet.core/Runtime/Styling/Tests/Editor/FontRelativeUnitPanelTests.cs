using NUnit.Framework;
using UnityEditor.UIElements.TestFramework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the font-relative units other than em on a simulated panel whose text is set in one font asset:
    /// ch against the text engine's own width of "0", ex and cap against the font's x-height and cap height, ic
    /// where the font has no "水", lh against the leading Velvet realises and the font's own line height, and the
    /// root-relative units against the panel root's font at the 16px a rem is taken at.
    /// </summary>
    internal sealed class FontRelativeUnitPanelTests
    {
        private readonly record struct ClassNameState(string ClassName);

        private sealed class ClassNameStore : Store<ClassNameState>
        {
            public ClassNameStore(string initial) : base(new ClassNameState(initial)) { }
            public void Set(string className) => SetState(_ => new ClassNameState(className));
            protected override void ResetCore() => SetState(_ => new ClassNameState(string.Empty));
        }

        private static ClassNameStore s_parentClass;
        private static string s_childClass;

        private EditorPanelSimulator _sim;
        private MountedTree _mounted;
        private FontAsset _font;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(400, 300) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _font = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            // The panel root, so the root-relative units read the same font as the elements.
            _sim.rootVisualElement.panel.visualTree.style.unityFontDefinition = FontDefinition.FromSDFFont(_font);
            s_parentClass = null;
            s_childClass = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _sim?.Dispose();
            _sim = null;
            s_parentClass?.Dispose();
            s_parentClass = null;
            Object.DestroyImmediate(_font);
            _font = null;
        }

        [Component]
        private static VNode Host()
            => V.Div(name: "frame", className: "w-[600px] h-[600px]", children: new VNode[]
            {
                V.Div(name: "parent", className: Hooks.UseStore(s_parentClass, s => s.ClassName), children: new VNode[]
                {
                    V.Div(name: "child", className: s_childClass),
                }),
            });

        private VisualElement Child => _sim.rootVisualElement.Q<VisualElement>("child");

        // Enough frames for the poll to measure a laid-out tree and for what it wrote to be laid out in turn.
        private void Settle()
        {
            _mounted?.FlushStateForTest();
            for (var i = 0; i < 3; i++)
            {
                _sim.FrameUpdateMs(16);
            }
        }

        private void Mount(string parentClass, string childClass)
        {
            s_parentClass = new ClassNameStore(parentClass);
            s_childClass = childClass;
            _mounted = V.Mount(_sim.rootVisualElement, V.Component(Host, key: "host"));
            Settle();
        }

        // The width Velvet wrote, which layout then rounds to the panel's device-pixel grid: a metric's product is
        // almost never on it, so the layout width is no reading of the unit.
        private float Written => Child.style.width.value.value;

        // A face metric of the font, in pixels at fontSize, as the text generator scales one.
        private float FacePixels(float metric, float fontSize) => metric * fontSize / _font.faceInfo.pointSize * _font.faceInfo.scale;

        [Test]
        public void Given_TenChUnderATwentyPixelFont_When_Settled_Then_ItIsTheEnginesWidthOfTenZeros()
        {
            // Arrange — measured once the frames have resolved the label's inherited font.
            var zeros = new Label("0000000000") { style = { fontSize = 20f } };
            _sim.rootVisualElement.panel.visualTree.Add(zeros);

            // Act
            Mount("text-[20px] w-[300px] h-[200px]", "w-[10ch] h-[10px]");

            // Assert
            var engineWidth = zeros.MeasureTextSize("0000000000", 0f, VisualElement.MeasureMode.Undefined, 0f,
                VisualElement.MeasureMode.Undefined).x;
            // The engine's width is the same quantity before it is rounded to a device pixel.
            Assert.That(Written, Is.EqualTo(engineWidth).Within(1f / Child.panel.scaledPixelsPerPoint));
        }

        [Test]
        public void Given_TenExUnderATwentyPixelFont_When_Settled_Then_ItIsTenXHeights()
        {
            // Act
            Mount("text-[20px] w-[300px] h-[200px]", "w-[10ex] h-[10px]");

            // Assert
            Assert.That(Written, Is.EqualTo(10f * FacePixels(_font.faceInfo.meanLine, 20f)).Within(1e-3f));
        }

        [Test]
        public void Given_TenCapUnderATwentyPixelFont_When_Settled_Then_ItIsTenCapHeights()
        {
            // Act
            Mount("text-[20px] w-[300px] h-[200px]", "w-[10cap] h-[10px]");

            // Assert
            Assert.That(Written, Is.EqualTo(10f * FacePixels(_font.faceInfo.capLine, 20f)).Within(1e-3f));
        }

        [Test]
        public void Given_TenIcInAFontWithoutTheIdeograph_When_Settled_Then_ItIsTenEms()
        {
            // Act
            Mount("text-[20px] w-[300px] h-[200px]", "w-[10ic] h-[10px]");

            // Assert — the font lacking the glyph is what makes the em CSS's stand-in.
            Assert.That(_font.HasCharacter('水', false, true) ? float.NaN : Child.layout.width, Is.EqualTo(200f));
        }

        [Test]
        public void Given_OneLhUnderAPixelLeading_When_Settled_Then_ItIsThatLeading()
        {
            // Act
            Mount("text-[12px] leading-[30px] w-[300px] h-[200px]", "w-[1lh] h-[10px]");

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void Given_OneLhUnderANumberLeading_When_Settled_Then_ItIsThatNumberOfTheElementsOwnFontSize()
        {
            // Act
            Mount("text-[12px] leading-loose w-[300px] h-[200px]", "text-[20px] w-[1lh] h-[10px]");

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(40f).Within(0.01f));
        }

        [Test]
        public void Given_OneLhUnderNoLeading_When_Settled_Then_ItIsTheFontsLineHeight()
        {
            // Act
            Mount("text-[20px] w-[300px] h-[200px]", "w-[1lh] h-[10px]");

            // Assert
            Assert.That(Written, Is.EqualTo(FacePixels(_font.faceInfo.lineHeight, 20f)).Within(1e-3f));
        }

        [Test]
        public void Given_AnLhWidth_When_TheParentsLeadingChanges_Then_ItIsMeasuredAgain()
        {
            // Arrange
            Mount("text-[12px] leading-[30px] w-[300px] h-[200px]", "w-[1lh] h-[10px]");
            var before = Child.layout.width;

            // Act
            s_parentClass.Set("text-[12px] leading-[40px] w-[300px] h-[200px]");
            Settle();

            // Assert
            Assert.That(before == 30f ? Child.layout.width : float.NaN, Is.EqualTo(40f));
        }

        [Test]
        public void Given_TenRexUnderATwentyPixelFont_When_Settled_Then_ItIsTenXHeightsAtSixteenPixels()
        {
            // Act
            Mount("text-[20px] w-[300px] h-[200px]", "w-[10rex] h-[10px]");

            // Assert
            Assert.That(Written, Is.EqualTo(10f * FacePixels(_font.faceInfo.meanLine, 16f)).Within(1e-3f));
        }

        [Test]
        public void Given_OneRlhUnderAPixelLeading_When_Settled_Then_ItIsTheRootFontsLineHeightAtSixteenPixels()
        {
            // Act
            Mount("text-[20px] leading-[30px] w-[300px] h-[200px]", "w-[1rlh] h-[10px]");

            // Assert
            Assert.That(Written, Is.EqualTo(FacePixels(_font.faceInfo.lineHeight, 16f)).Within(1e-3f));
        }
    }
}
