using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The inspection overlay's measure pass: the element it measures is dropped when the pointer leaves the canvas,
    /// and the label sits at the left edge of the measured element's margin box.
    /// </summary>
    internal sealed class PreviewInspectOverlayTests : PanelTestBase
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int CanvasWidth = 100;
        private const int CanvasHeight = 60;
        private const float MarginLeft = 10f;
        private const int FramesBeforeReadback = 8;

        private VelvetPreviewPanel _panel;
        private VisualElement _canvas;

        public override void SetUp()
        {
            base.SetUp();
            _canvas = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    width = CanvasWidth,
                    height = CanvasHeight,
                },
            };
            _window.rootVisualElement.Add(_canvas);
        }

        public override void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
            base.TearDown();
        }

        // GREEN_ON_BASE(characterization): the base already drops the measured element when the pointer leaves the
        // canvas.
        [Test]
        public void Given_AMeasuredElement_When_ThePointerLeavesTheCanvas_Then_NothingIsMeasured()
        {
            // Arrange
            var overlay = new PreviewInspectOverlay(_canvas, new PreviewPanelBridge(_canvas, () => null));
            _window.rootVisualElement.Add(overlay);
            SetMeasured(overlay, new VisualElement());
            using var leave = PointerLeaveEvent.GetPooled();

            // Act
            _canvas.SimulateEvent(leave);

            // Assert
            Assert.That(typeof(PreviewInspectOverlay).GetField("_measured", Private)?.GetValue(overlay), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already puts the label at the left edge of the margin box, which
        // is the measured element's left edge less its left margin.
        [Test]
        public void Given_AMeasuredElementWithALeftMargin_When_TheOverlayDraws_Then_TheLabelSitsAtTheMarginBoxsLeftEdge()
        {
            // Arrange
            _panel = new VelvetPreviewPanel(null, CanvasWidth, CanvasHeight);
            var bridge = new PreviewPanelBridge(_canvas, () => _panel);
            var overlay = new PreviewInspectOverlay(_canvas, bridge) { MeasureEnabled = true };
            var measured = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 20,
                    top = 20,
                    width = 40,
                    height = 10,
                    marginLeft = MarginLeft,
                },
            };
            _window.rootVisualElement.Add(overlay);
            _window.rootVisualElement.Add(measured);
            SetMeasured(overlay, measured);

            // Act
            // The label is written by Refresh from the laid-out elements, so the layout pass comes first; the second
            // pass runs the repaint Refresh asks for, which draws the measure without changing the label.
            ForcePanelUpdate(_window.rootVisualElement.panel);
            overlay.Refresh();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            var ratio = bridge.ToPanel(Vector2.one).x;
            var expected = (measured.worldBound.xMin - MarginLeft) / ratio
                + _canvas.worldBound.xMin - overlay.worldBound.xMin;
            Assert.That(overlay.Q<Label>().style.left.value.value, Is.EqualTo(expected).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base already leaves the measured element alone once the overlay has
        // left its panel, so the canvas it listened to no longer moves it.
        [Test]
        public void Given_ADetachedOverlay_When_ThePointerLeavesTheCanvas_Then_ItsMeasuredElementIsKept()
        {
            // Arrange
            var overlay = new PreviewInspectOverlay(_canvas, new PreviewPanelBridge(_canvas, () => null));
            _window.rootVisualElement.Add(overlay);
            var kept = new VisualElement();
            SetMeasured(overlay, kept);
            overlay.RemoveFromHierarchy();
            using var leave = PointerLeaveEvent.GetPooled();

            // Act
            _canvas.SimulateEvent(leave);

            // Assert
            Assert.That(Measured(overlay), Is.SameAs(kept));
        }

        // GREEN_ON_BASE(characterization): the base already stops following the pointer over the canvas once the
        // overlay has left its panel. With no panel to pick on, a pointer move the overlay still followed would
        // replace the measured element with none.
        [Test]
        public void Given_ADetachedOverlay_When_ThePointerMovesOverTheCanvas_Then_ItsMeasuredElementIsKept()
        {
            // Arrange
            var overlay = new PreviewInspectOverlay(_canvas, new PreviewPanelBridge(_canvas, () => null))
            {
                MeasureEnabled = true,
            };
            _window.rootVisualElement.Add(overlay);
            var kept = new VisualElement();
            SetMeasured(overlay, kept);
            overlay.RemoveFromHierarchy();
            using var move = PointerMoveEvent.GetPooled();

            // Act
            _canvas.SimulateEvent(move);

            // Assert
            Assert.That(Measured(overlay), Is.SameAs(kept));
        }

        // GREEN_ON_BASE(characterization): the base already counts only an element below the story's root as part of
        // the story.
        [Test]
        public void Given_AnElementOutsideTheStory_When_ItIsAskedWhetherItIsInTheStory_Then_ItIsNot()
        {
            // Arrange
            var root = new VisualElement();
            var outside = new VisualElement();

            // Act
            var inStory = typeof(PreviewInspectOverlay).GetMethod("IsInStory", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { root, outside });

            // Assert
            Assert.That(inStory, Is.EqualTo(false));
        }

        // GREEN_ON_BASE(characterization): the base already writes the measured element's content size and its
        // padding and margins into the label. The content box is the border box less the padding on each side.
        [Test]
        public void Given_AMeasuredElementWithPadding_When_TheOverlayDraws_Then_TheLabelNamesTheContentSizeAndTheBoxes()
        {
            // Arrange
            _panel = new VelvetPreviewPanel(null, CanvasWidth, CanvasHeight);
            var overlay = new PreviewInspectOverlay(_canvas, new PreviewPanelBridge(_canvas, () => _panel))
            {
                MeasureEnabled = true,
            };
            var measured = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 20,
                    top = 20,
                    width = 40,
                    height = 30,
                    paddingLeft = 4,
                    paddingRight = 2,
                    paddingTop = 6,
                    paddingBottom = 3,
                    marginLeft = 10,
                    marginRight = 6,
                    marginTop = 5,
                    marginBottom = 7,
                },
            };
            _window.rootVisualElement.Add(overlay);
            _window.rootVisualElement.Add(measured);
            SetMeasured(overlay, measured);

            // Act
            // Laid out first, then refreshed, as in the left-margin case above.
            ForcePanelUpdate(_window.rootVisualElement.panel);
            overlay.Refresh();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(overlay.Q<Label>().text, Is.EqualTo("34×21   p 6 2 3 4   m 5 6 7 10"));
        }

        // GREEN_ON_BASE(characterization): the base already draws the margin band to the right of the measured
        // element. The overlay sits in the panel it measures, over a canvas the size of the panel, and the texture
        // is read where only the right margin band reaches.
        [UnityTest]
        public IEnumerator Given_AMeasuredElementWithARightMargin_When_TheOverlayDraws_Then_TheMarginBandIsPaintedRightOfIt()
        {
            // Arrange
            _panel = new VelvetPreviewPanel(null, CanvasWidth, CanvasHeight);
            var canvas = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    width = CanvasWidth,
                    height = CanvasHeight,
                },
            };
            var measured = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 20,
                    top = 20,
                    width = 40,
                    height = 20,
                    marginRight = 8,
                },
            };
            var overlay = new PreviewInspectOverlay(canvas, new PreviewPanelBridge(canvas, () => _panel))
            {
                MeasureEnabled = true,
            };
            _panel.Root.Add(canvas);
            _panel.Root.Add(measured);
            _panel.Root.Add(overlay);
            SetMeasured(overlay, measured);
            yield return Frames();
            overlay.Refresh();
            yield return Frames();

            // Act
            var pixels = RenderTexturePixelReader.ReadPixels(
                _panel.Texture, new RectInt(0, 0, _panel.Texture.width, _panel.Texture.height));

            // Assert
            // Row 30 is the middle of the texture, so it holds the element's middle whichever way rows run.
            Assert.That(pixels[30 * CanvasWidth + 64].a, Is.GreaterThan(0));
        }

        private static IEnumerator Frames()
        {
            for (var frame = 0; frame < FramesBeforeReadback; frame++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
        }

        private static VisualElement Measured(PreviewInspectOverlay overlay) =>
            (VisualElement)typeof(PreviewInspectOverlay).GetField("_measured", Private)?.GetValue(overlay);

        private static void SetMeasured(PreviewInspectOverlay overlay, VisualElement element) =>
            typeof(PreviewInspectOverlay).GetField("_measured", Private)?.SetValue(overlay, element);
    }
}
