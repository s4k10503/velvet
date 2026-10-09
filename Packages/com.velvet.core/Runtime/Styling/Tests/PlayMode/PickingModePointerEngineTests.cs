using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how UI Toolkit delivers pointer events around an element whose <see cref="PickingMode"/> is
    /// <see cref="PickingMode.Ignore"/>, through a runtime panel's own dispatcher: an ignored ancestor of the
    /// element under the pointer still receives the enter and the bubbling over and press and takes the hover
    /// pseudo-state, which is what CSS gives an ancestor of a hovered element whatever its own
    /// <c>pointer-events</c>, and an ignored element in front receives none of them. Elements are built by hand,
    /// so nothing of Velvet's takes part.
    /// </summary>
    /// <remarks>
    /// Events are sent to the panel's visual tree with no target, so the panel picks the target itself, and are
    /// built from an IMGUI event, the constructor that asks the panel to recompute the element under the pointer.
    /// Each case reads its outcome as soon as the send returns, before a frame can deliver anything else.
    /// </remarks>
    internal sealed class PickingModePointerEngineTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("PickingModePanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private VisualElement Root => _panelGo.GetComponent<UIDocument>().rootVisualElement;

        private static VisualElement Box(float left, float top, float width, float height)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = left;
            box.style.top = top;
            box.style.width = width;
            box.style.height = height;
            return box;
        }

        private void MoveTo(Vector2 position)
        {
            using var evt = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position });
            Root.panel.visualTree.SendEvent(evt);
        }

        private void Press(Vector2 position)
        {
            using (var down = PointerDownEvent.GetPooled(new Event
                   {
                       type = EventType.MouseDown, mousePosition = position, button = 0, clickCount = 1,
                   }))
            {
                Root.panel.visualTree.SendEvent(down);
            }
            using var up = PointerUpEvent.GetPooled(new Event
            {
                type = EventType.MouseUp, mousePosition = position, button = 0, clickCount = 1,
            });
            Root.panel.visualTree.SendEvent(up);
        }

        // GREEN_ON_BASE(characterization): an engine fact the base already has, and why auto needs no gate.
        // A pointer-events-auto element inside an ignored subtree lights its ancestors' hover payloads the way
        // CSS lights an ancestor's :hover.
        [UnityTest]
        public IEnumerator Given_AnIgnoredParentWithAPositionChild_When_ThePointerMovesOntoTheChild_Then_TheParentIsEnteredAndHovered()
        {
            // Arrange
            var parent = Box(0f, 0f, 200f, 200f);
            parent.pickingMode = PickingMode.Ignore;
            var child = Box(20f, 20f, 60f, 60f);
            parent.Add(child);
            Root.Add(parent);
            var entered = false;
            var overReached = false;
            parent.RegisterCallback<PointerEnterEvent>(_ => entered = true);
            parent.RegisterCallback<PointerOverEvent>(_ => overReached = true);
            yield return null;
            yield return null;

            // Act
            MoveTo(new Vector2(50f, 50f));

            // Assert
            Assert.That((entered, overReached, parent.hasHoverPseudoState), Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): an engine fact the base already has, and why a scope needs no gate.
        // No hit test lands on an element the pointer-events scope ignores, so no hover or press payload of its
        // own is lit while the pointer is over the element behind it.
        [UnityTest]
        public IEnumerator Given_AnIgnoredLeafInFrontOfASibling_When_ThePointerMovesOntoTheOverlap_Then_OnlyTheSiblingIsEnteredAndHovered()
        {
            // Arrange — the later sibling is the one in front.
            var behind = Box(0f, 0f, 100f, 100f);
            var front = Box(0f, 0f, 100f, 100f);
            front.pickingMode = PickingMode.Ignore;
            Root.Add(behind);
            Root.Add(front);
            var frontReached = false;
            front.RegisterCallback<PointerEnterEvent>(_ => frontReached = true);
            front.RegisterCallback<PointerOverEvent>(_ => frontReached = true);
            yield return null;
            yield return null;

            // Act
            MoveTo(new Vector2(50f, 50f));

            // Assert
            Assert.That((frontReached, front.hasHoverPseudoState, behind.hasHoverPseudoState),
                Is.EqualTo((false, false, true)));
        }

        // GREEN_ON_BASE(characterization): an engine fact the base already has, and why bubbling is untouched.
        // A press on a pointer-events-auto descendant still bubbles through its ignored ancestors to a handler
        // registered on one of them.
        [UnityTest]
        public IEnumerator Given_AnIgnoredParentWithAPositionChild_When_TheChildIsPressed_Then_ThePressBubblesThroughTheParent()
        {
            // Arrange
            var parent = Box(0f, 0f, 200f, 200f);
            parent.pickingMode = PickingMode.Ignore;
            var child = Box(20f, 20f, 60f, 60f);
            parent.Add(child);
            Root.Add(parent);
            IEventHandler target = null;
            parent.RegisterCallback<PointerDownEvent>(evt => target = evt.target);
            yield return null;
            yield return null;

            // Act
            Press(new Vector2(50f, 50f));

            // Assert
            Assert.That(ReferenceEquals(target, child), Is.True);
        }
    }
}
