using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the three facts about UI Toolkit's hit testing that the pointer-events utilities are built on:
    /// <see cref="PickingMode.Ignore"/> excludes the element it is set on and none of its descendants, an ignored
    /// element in front lets the element behind it take the pick, and the pick descends through an element's raw
    /// hierarchy rather than only through its content container. Elements are built by hand, so nothing of
    /// Velvet's takes part.
    /// </summary>
    /// <remarks>
    /// Picked with <see cref="IPanel.PickAll"/>, which picks afresh, rather than <see cref="IPanel.Pick"/>, which
    /// can hand back the element it last found under the pointer.
    /// </remarks>
    internal sealed class PickingModeEngineTests : PanelTestBase
    {
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

        private static VisualElement Picked(VisualElement anyMounted, Vector2 point)
            => anyMounted.panel.PickAll(point, null);

        // GREEN_ON_BASE(characterization): an engine fact the base already has, and the reason a scope walks.
        // The pointer-events scope takes a hold on every descendant because the engine excludes only the
        // element itself.
        [Test]
        public void Given_AnIgnoredParentWithAPositionChild_When_PickedOverEach_Then_OnlyTheChildIsATarget()
        {
            // Arrange
            var parent = Box(100f, 100f, 200f, 200f);
            parent.pickingMode = PickingMode.Ignore;
            var child = Box(20f, 20f, 60f, 60f);
            parent.Add(child);
            _window.rootVisualElement.Add(parent);
            ForcePanelUpdate(parent.panel);
            ForcePanelUpdate(parent.panel);

            // Act — one point on the child, one on the parent's own area clear of the child.
            var onChild = Picked(parent, child.worldBound.center);
            var onParentOnly = Picked(parent, new Vector2(parent.worldBound.xMax - 10f, parent.worldBound.yMax - 10f));

            // Assert
            Assert.That((onChild == child, onParentOnly == parent), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): an engine fact the base already has, and what makes an overlay
        // click-through once the pointer-events scope ignores it.
        [Test]
        public void Given_AnIgnoredLeafInFrontOfASibling_When_PickedWhereTheyOverlap_Then_TheSiblingBehindIsPicked()
        {
            // Arrange — the later sibling is the one in front.
            var behind = Box(100f, 100f, 100f, 100f);
            var front = Box(100f, 100f, 100f, 100f);
            front.pickingMode = PickingMode.Ignore;
            _window.rootVisualElement.Add(behind);
            _window.rootVisualElement.Add(front);
            ForcePanelUpdate(front.panel);
            ForcePanelUpdate(front.panel);
            var point = behind.worldBound.center;

            // Act
            var picked = Picked(front, point);

            // Assert — that the front leaf covers the point rides along, since two leaves laid out apart would
            // also pick the one behind.
            Assert.That((front.worldBound.Contains(point), picked == behind), Is.EqualTo((true, true)));
        }

        private sealed class RedirectingBox : VisualElement
        {
            public readonly VisualElement Content = new();

            public RedirectingBox()
            {
                hierarchy.Add(Content);
            }

            public override VisualElement contentContainer => Content;
        }

        // GREEN_ON_BASE(characterization): an engine fact the base already has, and why the walk is raw.
        // The pointer-events scope follows the raw hierarchy rather than the content container because of it.
        [Test]
        public void Given_AnIgnoredElementWithAPartOutsideItsContentContainer_When_PickedOverThePart_Then_ThePartIsPicked()
        {
            // Arrange — the part sits in the raw hierarchy only, the way a control's internal elements do.
            var box = new RedirectingBox();
            box.style.position = Position.Absolute;
            box.style.left = 100f;
            box.style.top = 100f;
            box.style.width = 200f;
            box.style.height = 200f;
            box.pickingMode = PickingMode.Ignore;
            var part = Box(20f, 20f, 60f, 60f);
            box.hierarchy.Add(part);
            _window.rootVisualElement.Add(box);
            ForcePanelUpdate(box.panel);
            ForcePanelUpdate(box.panel);

            // Act
            var picked = Picked(box, part.worldBound.center);

            // Assert — that the part is outside the content container rides along, since a part reachable
            // through the content container would be picked either way.
            Assert.That((box.contentContainer.Contains(part), picked == part), Is.EqualTo((false, true)));
        }
    }
}
