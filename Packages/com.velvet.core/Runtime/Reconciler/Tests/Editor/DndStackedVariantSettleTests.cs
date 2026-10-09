using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a drag's synthetic release reaches a stacked variant on an element the press reached: the
    /// session swallows the real PointerUp, so a <c>dark:active:</c> payload on the pressed grip clears only
    /// through the drag-end settle. Driven through the panel's own dispatcher, as <see cref="DndInteractionTests"/>
    /// drives its whileTap cases.
    /// </summary>
    [TestFixture]
    internal sealed class DndStackedVariantSettleTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private bool _darkBefore;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetTheme.IsDark = _darkBefore;
        }

        // A draggable whose whole face is a grip carrying dark:active:, so every press lands on the grip.
        [Component]
        private static VNode StackedGripScene()
        {
            return V.DndContext(
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]",
                        children: new VNode[]
                        {
                            V.Div(name: "grip", className: "w-[50px] h-[50px] dark:active:bg-on"),
                        }),
                });
        }

        // GREEN_ON_BASE(characterization): the base's whole-registry sweep already settled the stacked active.
        [Test]
        public void Given_ADragPressedOnAGripCarryingAStackedActive_When_TheDragEnds_Then_TheStackedPayloadIsSettled()
        {
            // Arrange — dark is on, so the stacked active manipulator exists; every position stays inside the
            // grip, so no pointer-out ends the press on its own.
            _mounted = V.Mount(_host.Root, V.Component(StackedGripScene, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            VelvetTheme.IsDark = true;
            var grip = _host.Root.Q<VisualElement>("grip");
            grip.SendPointerDownEvent(new Vector2(10, 10));
            grip.SendPointerMoveEvent(new Vector2(20, 10));
            var appliedDuringTheDrag = grip.ClassListContains("bg-on");

            // Act
            grip.SendPointerUpEvent(new Vector2(20, 10));

            // Assert
            Assert.That((appliedDuringTheDrag, grip.ClassListContains("bg-on")), Is.EqualTo((true, false)));
        }
    }
}
