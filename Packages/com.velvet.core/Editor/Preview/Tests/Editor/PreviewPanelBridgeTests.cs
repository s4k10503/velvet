using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What the canvas hands on to the panel it shows: the wheel and the navigation events arrive there, a
    /// navigation move stops at the canvas, a pointer released over the canvas is no longer held by it, and a
    /// position maps to the panel's coordinates at a ratio of one wherever either size is not yet known.
    /// </summary>
    internal sealed class PreviewPanelBridgeTests : PanelTestBase
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int CanvasWidth = 100;
        private const int CanvasHeight = 60;
        private const int FramesBeforePanel = 4;

        private readonly List<Vector2> _wheels = new();
        private readonly List<NavigationMoveEvent.Direction> _moves = new();
        private int _submits;
        private int _movesReachingTheWindow;

        private VelvetPreviewPanel _panel;
        private VisualElement _canvas;
        private PreviewPanelBridge _bridge;

        public override void SetUp()
        {
            base.SetUp();
            _wheels.Clear();
            _moves.Clear();
            _submits = 0;
            _movesReachingTheWindow = 0;
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
            _panel = new VelvetPreviewPanel(null, CanvasWidth, CanvasHeight);
            _bridge = new PreviewPanelBridge(_canvas, () => _panel);
        }

        public override void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
            base.TearDown();
        }

        // GREEN_ON_BASE(characterization): the base already hands a wheel on the canvas to the panel it shows.
        [UnityTest]
        public IEnumerator Given_AWheelEventOnTheCanvas_When_ItIsSent_Then_ThePanelReceivesTheSameScroll()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(WheelEvent.GetPooled(new Event
            {
                type = EventType.ScrollWheel,
                mousePosition = new Vector2(10f, 10f),
                delta = new Vector2(0f, 3f),
            }));

            // Assert
            Assert.That(_wheels, Is.EqualTo(new[] { new Vector2(0f, 3f) }));
        }

        // GREEN_ON_BASE(characterization): the base already hands a navigation move on the canvas to the panel it
        // shows.
        [UnityTest]
        public IEnumerator Given_ANavigationMoveOnTheCanvas_When_ItIsSent_Then_ThePanelReceivesItsDirection()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right));

            // Assert
            Assert.That(_moves, Is.EqualTo(new[] { NavigationMoveEvent.Direction.Right }));
        }

        // GREEN_ON_BASE(characterization): the base already hands a submit on the canvas to the panel it shows.
        [UnityTest]
        public IEnumerator Given_ANavigationSubmitOnTheCanvas_When_ItIsSent_Then_ThePanelReceivesIt()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(NavigationSubmitEvent.GetPooled());

            // Assert
            Assert.That(_submits, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base already stops a navigation move at the canvas, so the window
        // around it does not also move its own focus.
        [UnityTest]
        public IEnumerator Given_ANavigationMoveOnTheCanvas_When_ItIsSent_Then_TheWindowAroundItDoesNotReceiveIt()
        {
            // Arrange
            yield return Listen();
            _window.rootVisualElement.RegisterCallback<NavigationMoveEvent>(_ => _movesReachingTheWindow++);

            // Act
            SendToCanvas(NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right));

            // Assert
            Assert.That(_movesReachingTheWindow, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base already lets go of the pointer when it is released over the
        // canvas. The handler is invoked directly: whether the dispatcher releases a captured mouse pointer on
        // its own after a pointer up is the engine's, and this case is about the bridge's own release.
        [Test]
        public void Given_ACanvasHoldingThePointer_When_ThePointerIsReleased_Then_TheCanvasNoLongerHoldsIt()
        {
            // Arrange
            _canvas.CapturePointer(PointerId.mousePointerId);
            var heldBefore = _canvas.HasPointerCapture(PointerId.mousePointerId);
            using var up = PointerUpEvent.GetPooled(new Event
            {
                type = EventType.MouseUp,
                mousePosition = new Vector2(10f, 10f),
                button = 0,
            });

            // Act
            typeof(PreviewPanelBridge).GetMethod("OnPointerUp", Private)?.Invoke(_bridge, new object[] { up });

            // Assert
            Assert.That((heldBefore, _canvas.HasPointerCapture(PointerId.mousePointerId)), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base already maps at a ratio of one while there is no panel, whose
        // size is then zero. The canvas is laid out first so its own size is known.
        [Test]
        public void Given_NoPanel_When_APositionIsMapped_Then_ItIsUnchanged()
        {
            // Arrange
            var withoutPanel = new PreviewPanelBridge(_canvas, () => null);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            var mapped = withoutPanel.ToPanel(new Vector2(10f, 20f));

            // Assert
            Assert.That(
                (_canvas.layout.size, mapped),
                Is.EqualTo((new Vector2(CanvasWidth, CanvasHeight), new Vector2(10f, 20f))));
        }

        // The panel needs a few frames before it has a panel of its own to take events.
        private IEnumerator Listen()
        {
            for (var frame = 0; frame < FramesBeforePanel; frame++) yield return null;
            var tree = _panel.Root.panel.visualTree;
            tree.RegisterCallback<WheelEvent>(evt => _wheels.Add(evt.delta), TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationMoveEvent>(evt => _moves.Add(evt.direction), TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationSubmitEvent>(_ => _submits++, TrickleDown.TrickleDown);
        }

        private void SendToCanvas(EventBase evt)
        {
            using (evt)
            {
                _canvas.SendEvent(evt);
            }
        }
    }
}
