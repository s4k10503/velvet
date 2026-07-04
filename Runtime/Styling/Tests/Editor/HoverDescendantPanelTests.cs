using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for the core hover bug: a <c>hover:</c> variant on a parent must light up while the
    /// pointer is over a CHILD that fills the parent's interior (CSS <c>:hover</c> ancestor semantics), not only
    /// over the parent's own uncovered pixels. The manipulator drives this off the BUBBLING
    /// <see cref="PointerOverEvent"/>/<see cref="PointerOutEvent"/> pair, so a real event dispatched at the child
    /// reaches the parent. <see cref="PointerOutEvent"/> clears hover only once the pointer is outside the
    /// parent's bounds, so crossing between descendants keeps it. Real <see cref="UnityEditor.EditorWindow"/>
    /// panel (via <see cref="PanelTestBase"/>) + forced layout so bounds resolve; events go through real
    /// propagation via <c>SendEvent</c>. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class HoverDescendantPanelTests : PanelTestBase
    {
        // Parent carries hover:bg-on; the child fills the parent so the pointer is never over the parent's own pixels.
        private (VisualElement parent, VisualElement child) Mount()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "w-[400px] h-[200px] hover:bg-on",
                V.Label(name: "child", className: "w-[400px] h-[200px]", text: "x")));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            // "hover:bg-on" is consumed as a variant token (not added as a literal USS class), so query the
            // child by name and take its parent — that parent is the element carrying the hover manipulator.
            var child = _window.rootVisualElement.Q<Label>("child");
            var parent = child.parent;
            return (parent, child);
        }

        private static void Over(VisualElement on)
        {
            using var evt = PointerOverEvent.GetPooled();
            evt.target = on;
            on.SendEvent(evt);
        }

        private static void OutAt(VisualElement on, Vector2 pos)
        {
            using var evt = PointerOutEvent.GetPooled();
            typeof(PointerEventBase<PointerOutEvent>).GetProperty("position", BindingFlags.Public | BindingFlags.Instance)!
                .GetSetMethod(nonPublic: true)!.Invoke(evt, new object[] { (Vector3)pos });
            evt.target = on;
            on.SendEvent(evt);
        }

        [Test]
        public void Given_AHoverParent_When_ThePointerIsOverItsChild_Then_TheParentGetsTheHoverPayload()
        {
            // Arrange — a parent (hover:bg-on) whose child fills it, not hovered.
            var (parent, child) = Mount();
            Assume.That(parent.ClassListContains("bg-on"), Is.False, "Precondition: payload off before hover");

            // Act — a real (bubbling) pointer-over is dispatched at the child.
            Over(child);

            // Assert — the bubbling over reaches the parent and applies its hover payload.
            Assert.IsTrue(parent.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_AHoveredParent_When_ThePointerLeavesItsBounds_Then_TheHoverPayloadIsRemoved()
        {
            // Arrange — the parent hovered via its child.
            var (parent, child) = Mount();
            Over(child);
            Assume.That(parent.ClassListContains("bg-on"), Is.True, "Precondition: payload on while hovered");

            // Act — a pointer-out fires with the pointer well outside the parent's bounds.
            OutAt(child, parent.worldBound.center + new Vector2(10000f, 10000f));

            // Assert — leaving the bounds clears the hover payload.
            Assert.IsFalse(parent.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_AHoveredParent_When_ThePointerCrossesBetweenDescendants_Then_TheHoverPayloadIsKept()
        {
            // Arrange — the parent hovered via its child.
            var (parent, child) = Mount();
            Over(child);
            Assume.That(parent.ClassListContains("bg-on"), Is.True, "Precondition: payload on while hovered");

            // Act — a pointer-out fires but the pointer is still inside the parent's bounds (moving to a sibling child).
            OutAt(child, parent.worldBound.center);

            // Assert — hover persists while the pointer remains within bounds.
            Assert.IsTrue(parent.ClassListContains("bg-on"));
        }
    }
}
