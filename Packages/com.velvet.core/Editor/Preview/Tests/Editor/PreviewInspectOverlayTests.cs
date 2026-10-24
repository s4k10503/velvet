using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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
            using var leave = PointerLeaveEvent.GetPooled(new Event
            {
                type = EventType.MouseMove,
                mousePosition = new Vector2(10f, 10f),
                button = 0,
            });

            // Act
            _canvas.SendEvent(leave);

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
            // A second pass: the first one lays the elements out and resolves their styles, and the repaint it
            // takes up may read them before they are.
            ForcePanelUpdate(_window.rootVisualElement.panel);
            overlay.Refresh();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            var ratio = bridge.ToPanel(Vector2.one).x;
            var expected = (measured.worldBound.xMin - MarginLeft) / ratio
                + _canvas.worldBound.xMin - overlay.worldBound.xMin;
            Assert.That(overlay.Q<Label>().style.left.value.value, Is.EqualTo(expected).Within(0.01f));
        }

        private static void SetMeasured(PreviewInspectOverlay overlay, VisualElement element) =>
            typeof(PreviewInspectOverlay).GetField("_measured", Private)?.SetValue(overlay, element);
    }
}
