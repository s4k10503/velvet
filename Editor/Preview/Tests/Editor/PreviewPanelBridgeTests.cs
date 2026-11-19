using System.Collections;
using System.Collections.Generic;
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
        private readonly List<(EventType Type, Vector2 At)> _pointerMoves = new();
        private readonly List<Vector2> _movesAnywhere = new();
        private readonly List<KeyCode> _keysDown = new();
        private readonly List<KeyCode> _keysUp = new();
        private int _submits;
        private int _cancels;

        private VelvetPreviewPanel _panel;
        private VisualElement _canvas;
        private PreviewPanelBridge _bridge;

        public override void SetUp()
        {
            base.SetUp();
            _wheels.Clear();
            _moves.Clear();
            _pointerMoves.Clear();
            _movesAnywhere.Clear();
            _keysDown.Clear();
            _keysUp.Clear();
            _submits = 0;
            _cancels = 0;
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
        [Test]
        public void Given_ANavigationMoveOnTheCanvas_When_TheBridgeHandlesIt_Then_ItStopsAtTheCanvas()
        {
            // Arrange
            using var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right);

            // Act
            _canvas.SimulateEvent(move);

            // Assert
            Assert.That(move.isPropagationStopped, Is.True);
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

        // GREEN_ON_BASE(characterization): the base already hands a navigation cancel on the canvas to the panel it
        // shows.
        [UnityTest]
        public IEnumerator Given_ANavigationCancelOnTheCanvas_When_ItIsSent_Then_ThePanelReceivesIt()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(NavigationCancelEvent.GetPooled());

            // Assert
            Assert.That(_cancels, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base already hands a key press on the canvas to the panel it shows.
        [UnityTest]
        public IEnumerator Given_AKeyPressOnTheCanvas_When_ItIsSent_Then_ThePanelReceivesTheKey()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(KeyDownEvent.GetPooled('a', KeyCode.A, EventModifiers.None));

            // Assert
            Assert.That(_keysDown, Is.EqualTo(new[] { KeyCode.A }));
        }

        // GREEN_ON_BASE(characterization): the base already hands a key release on the canvas to the panel it shows.
        [UnityTest]
        public IEnumerator Given_AKeyReleaseOnTheCanvas_When_ItIsSent_Then_ThePanelReceivesTheKey()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(KeyUpEvent.GetPooled('a', KeyCode.A, EventModifiers.None));

            // Assert
            Assert.That(_keysUp, Is.EqualTo(new[] { KeyCode.A }));
        }

        // GREEN_ON_BASE(characterization): the base already hands a pointer move with no button down on to the panel
        // as a mouse move rather than a drag.
        [UnityTest]
        public IEnumerator Given_APointerMoveOnTheCanvasWithNoButtonDown_When_ItIsSent_Then_ThePanelReceivesAMouseMove()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(PointerMoveEvent.GetPooled());

            // Assert
            Assert.That(_pointerMoves.ConvertAll(move => move.Type), Is.EqualTo(new[] { EventType.MouseMove }));
        }

        // GREEN_ON_BASE(characterization): the base already moves the story's pointer off its panel when the canvas's
        // pointer leaves and the canvas holds no capture.
        [UnityTest]
        public IEnumerator Given_ThePointerLeavingTheCanvas_When_ItIsSent_Then_ThePanelsPointerIsMovedOffIt()
        {
            // Arrange
            yield return Listen();

            // Act
            SendToCanvas(PointerLeaveEvent.GetPooled());

            // Assert
            Assert.That(
                _movesAnywhere.ConvertAll(at => at.x < -1e5f && at.y < -1e5f),
                Is.EqualTo(new[] { true }));
        }

        // GREEN_ON_BASE(characterization): the base already makes the canvas focusable, which a key to the story
        // needs.
        [Test]
        public void Given_ABridge_When_ItIsBuilt_Then_TheCanvasCanTakeFocus()
        {
            // Arrange / Act: SetUp built the bridge.

            // Assert
            Assert.That(_canvas.focusable, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already takes focus on a pointer down over the canvas. The handler
        // is invoked directly so only the bridge's own focus is read.
        [Test]
        public void Given_APointerDownOnTheCanvas_When_TheBridgeHandlesIt_Then_TheCanvasHoldsFocus()
        {
            // Arrange
            using var down = PointerDownEvent.GetPooled(new Event
            {
                type = EventType.MouseDown,
                mousePosition = new Vector2(10f, 10f),
                button = 0,
            });

            // Act
            typeof(PreviewPanelBridge).GetMethod("OnPointerDown", Private)?.Invoke(_bridge, new object[] { down });

            // Assert
            Assert.That(_canvas.focusController.focusedElement, Is.SameAs(_canvas));
        }

        // GREEN_ON_BASE(characterization): the base already captures the pointer on a pointer down over the canvas.
        // The handler is invoked directly for the reason given above.
        [Test]
        public void Given_APointerDownOnTheCanvas_When_TheBridgeHandlesIt_Then_TheCanvasHoldsThePointer()
        {
            // Arrange
            using var down = PointerDownEvent.GetPooled(new Event
            {
                type = EventType.MouseDown,
                mousePosition = new Vector2(10f, 10f),
                button = 0,
            });

            // Act
            typeof(PreviewPanelBridge).GetMethod("OnPointerDown", Private)?.Invoke(_bridge, new object[] { down });

            // Assert
            Assert.That(_canvas.HasPointerCapture(PointerId.mousePointerId), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already maps at a ratio of one while the canvas has no size,
        // whatever size the panel has. The panel's size is part of the assertion, since a panel that has none maps at
        // one for that reason instead.
        [UnityTest]
        public IEnumerator Given_ACollapsedCanvas_When_APositionIsMapped_Then_ItIsUnchanged()
        {
            // Arrange
            var collapsed = new VisualElement
            {
                style = { position = Position.Absolute, left = 0, top = 0, width = 0, height = 0 },
            };
            _window.rootVisualElement.Add(collapsed);
            var bridge = new PreviewPanelBridge(collapsed, () => _panel);
            for (var frame = 0; frame < FramesBeforePanel * 2; frame++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }

            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            var mapped = bridge.ToPanel(new Vector2(10f, 20f));

            // Assert
            var panelSize = _panel.Root.panel.visualTree.layout.size;
            Assert.That(
                (panelSize.x > 0f && panelSize.y > 0f, collapsed.layout.size, mapped),
                Is.EqualTo((true, Vector2.zero, new Vector2(10f, 20f))));
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

        // The recorder covers the whole panel and holds its focus, so a pointer or wheel at any position picks it and
        // a key or navigation event, which goes to the focused element, reaches it. The callbacks sit on the panel's
        // root, which the event bubbles up to.
        private IEnumerator Listen()
        {
            var recorder = new VisualElement
            {
                focusable = true,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };
            _panel.Root.Add(recorder);
            for (var frame = 0; frame < FramesBeforePanel; frame++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }

            recorder.Focus();
            var root = _panel.Root;
            root.RegisterCallback<WheelEvent>(evt => _wheels.Add(evt.delta));
            root.RegisterCallback<NavigationMoveEvent>(evt => _moves.Add(evt.direction));
            root.RegisterCallback<NavigationSubmitEvent>(_ => _submits++);
            root.RegisterCallback<NavigationCancelEvent>(_ => _cancels++);
            root.RegisterCallback<KeyDownEvent>(evt => _keysDown.Add(evt.keyCode));
            root.RegisterCallback<KeyUpEvent>(evt => _keysUp.Add(evt.keyCode));
            // A position off the panel picks nothing, so that move is targeted at the panel itself, above the root.
            root.panel.visualTree.RegisterCallback<PointerMoveEvent>(
                evt => _movesAnywhere.Add(evt.imguiEvent.mousePosition), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(
                evt => _pointerMoves.Add((evt.imguiEvent.type, evt.imguiEvent.mousePosition)));
        }

        // SendEvent is not routed to an element's callbacks here, so the event is handed to them the way the
        // repository's other fixtures do.
        private void SendToCanvas<TEvent>(TEvent evt)
            where TEvent : EventBase<TEvent>, new()
        {
            using (evt)
            {
                _canvas.SimulateEvent(evt);
            }
        }
    }
}
