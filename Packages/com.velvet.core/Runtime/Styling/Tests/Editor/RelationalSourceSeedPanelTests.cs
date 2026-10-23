using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a relational binding hooks its sources and what it reads off one as it does: a source already
    /// under the pointer, or already holding focus inside it, lights its consumer at once, as <c>.group:hover x</c>
    /// matches without waiting for the pointer to move, and a source that code outside a render puts beside a
    /// consumer is hooked without waiting for the next render.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalSourceSeedPanelTests : PanelTestBase
    {
        private static StateUpdater<bool> s_setShown;
        private static StateUpdater<bool> s_setReversed;

        public override void TearDown()
        {
            base.TearDown();
            s_setShown = default;
            s_setReversed = default;
        }

        [Component]
        private static VNode RenderLateHoverConsumer()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setShown = setShown;
            return V.Div(name: "source", className: "group", children: new VNode?[]
            {
                shown ? V.Div(name: "child", className: "group-hover:bg-on") : null,
            });
        }

        [Component]
        private static VNode RenderLateFocusConsumer()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setShown = setShown;
            return V.Div(name: "source", className: "group", children: new VNode?[]
            {
                V.Button(name: "field"),
                shown ? V.Div(name: "child", className: "group-focus:bg-on") : null,
            });
        }

        [Component]
        private static VNode RenderReorderedConsumer()
        {
            var (reversed, setReversed) = Hooks.UseState(false);
            s_setReversed = setReversed;
            var child = V.Div(key: "child", name: "child", className: "group-hover:bg-on");
            var first = V.Div(key: "first");
            var second = V.Div(key: "second");
            return V.Div(name: "source", className: "group",
                children: reversed ? new VNode?[] { first, second, child } : new VNode?[] { child, first, second });
        }

        private void Drain() => _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

        // UI Toolkit keeps :hover behind an internal setter; the test writes the state the engine's own pointer
        // bookkeeping would.
        private static void SetHoverPseudoState(VisualElement element)
        {
            var property = typeof(VisualElement).GetProperty("pseudoStates",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var current = Convert.ToInt32(property.GetValue(element));
            property.SetValue(element, Enum.ToObject(property.PropertyType, current | 0x2));
        }

        [Test]
        public void Given_APeerHoverConsumer_When_CodeOutsideARenderInsertsAPeerBeforeIt_Then_HoveringThatPeerAppliesThePayload()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "container", children: new VNode?[]
            {
                V.Div(name: "child", className: "peer-hover:bg-on"),
            }));
            var container = _window.rootVisualElement.Q<VisualElement>("container");
            var peer = new VisualElement { name = "peer" };
            peer.AddToClassList("peer");
            // A peer nothing hooked has no callback registry, and firing on it would throw before the assertion
            // could say the payload never arrived.
            peer.RegisterCallback<PointerOverEvent>(_ => { });

            // Act
            container.Insert(0, peer);
            EditorPanelTestHelpers.DriveSchedulerOnce(container.panel);
            using (var over = PointerOverEvent.GetPooled())
            {
                peer.SimulateEvent(over);
            }

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").ClassListContains("bg-on"), Is.True);
        }

        [Test]
        public void Given_AGroupUnderThePointer_When_AGroupHoverConsumerMounts_Then_ThePayloadIsApplied()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderLateHoverConsumer));
            SetHoverPseudoState(_window.rootVisualElement.Q<VisualElement>("source"));

            // Act
            s_setShown.Invoke(true);
            Drain();

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>("child")?.ClassListContains("bg-on"), Is.True);
        }

        [Test]
        public void Given_FocusInsideAGroup_When_AGroupFocusConsumerMounts_Then_ThePayloadIsApplied()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderLateFocusConsumer));
            var field = _window.rootVisualElement.Q<Button>("field");
            field.Focus();

            // Act
            s_setShown.Invoke(true);
            Drain();

            // Assert — the focused element is folded in, so a focus that never landed reads as a failure here
            // rather than as a payload missing for some other reason.
            Assert.That((field.panel.focusController.focusedElement == field,
                    _window.rootVisualElement.Q<VisualElement>("child")?.ClassListContains("bg-on")),
                Is.EqualTo((true, (bool?)true)));
        }

        [Test]
        public void Given_AConsumerLitByAHoveredGroup_When_AKeyedReorderMovesIt_Then_ThePayloadStaysApplied()
        {
            // Arrange — the source both holds the engine's hover state and has sent the PointerOver that lit the
            // consumer, as a real pointer resting on it does.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderReorderedConsumer));
            var source = _window.rootVisualElement.Q<VisualElement>("source");
            var child = _window.rootVisualElement.Q<VisualElement>("child");
            SetHoverPseudoState(source);
            using (var over = PointerOverEvent.GetPooled())
            {
                source.SimulateEvent(over);
            }
            var litBefore = child.ClassListContains("bg-on");
            var detaches = 0;
            child.RegisterCallback<DetachFromPanelEvent>(_ => detaches++);

            // Act
            s_setReversed.Invoke(true);
            Drain();

            // Assert — the detach is folded in: a reorder that moved the two siblings instead left the consumer's
            // binding hooked throughout, and its payload would stay applied with no seed at all.
            Assert.That((litBefore, detaches > 0, child.ClassListContains("bg-on")), Is.EqualTo((true, true, true)));
        }
    }
}
