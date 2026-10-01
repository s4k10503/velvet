using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a click and a field value change raised inside a <c>V.Portal(layer:)</c> reach a
    /// logical ancestor's <c>onClick</c> / <c>onValueChanged</c>, the way React's <c>onClick</c> and
    /// <c>onChange</c> bubble out of a portal to its React ancestors. The native events are simulated at
    /// the host panel's root, where the bridge listens, as <see cref="CrossPanelEventBubblingTests"/> does
    /// for the pointer and focus events.
    /// </summary>
    [TestFixture]
    internal sealed class PortalClickAndChangeBubblingTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode LayerPortalRender()
        {
            return V.Portal(UILayer.Overlay, children: new VNode[]
            {
                V.Component(LayerPortalChildRender),
            });
        }

        [Component]
        private static VNode LayerPortalChildRender() => V.Div(name: "portal-child");

        [Test]
        public void Given_AButtonAroundALayerPortal_When_AClickBubblesToTheHostRoot_Then_ItsOnClickFires()
        {
            // Arrange
            var clicks = 0;
            _mounted = V.Mount(_host.Root, V.Button(
                onClick: () => clicks++,
                children: new VNode[] { V.Component(LayerPortalRender) }));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ClickEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(clicks, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base bridges no click, so no Button's onClick fires there.
        // This pins that a disabled Button refuses the bridged click, as Clickable and a disabled DOM button do.
        [Test]
        public void Given_ADisabledButtonAroundALayerPortal_When_AClickBubblesToTheHostRoot_Then_ItsOnClickDoesNotFire()
        {
            // Arrange
            var clicks = 0;
            _mounted = V.Mount(_host.Root, V.Button(
                enabled: false,
                onClick: () => clicks++,
                children: new VNode[] { V.Component(LayerPortalRender) }));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ClickEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(clicks, Is.EqualTo(0));
        }

        [Test]
        public void Given_AButtonAroundALayerPortalOfABareElement_When_AClickBubblesToTheHostRoot_Then_ItsOnClickFires()
        {
            // Arrange — the portal's child is an element with no component around it.
            var clicks = 0;
            _mounted = V.Mount(_host.Root, V.Button(
                onClick: () => clicks++,
                children: new VNode[] { V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "bare-child") }) }));
            var (hostRoot, child) = PortalChild("bare-child");

            // Act
            using var evt = ClickEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(clicks, Is.EqualTo(1));
        }

        [Test]
        public void Given_APointerDownBindingAroundALayerPortalOfABareElement_When_APointerDownBubblesToTheHostRoot_Then_ItFires()
        {
            // Arrange
            var presses = 0;
            _mounted = V.Mount(_host.Root, V.Motion(
                events: new FiberEventBinding[] { new PointerDownBinding { Handler = _ => presses++ } },
                children: new VNode[] { V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "bare-child") }) }));
            var (hostRoot, child) = PortalChild("bare-child");

            // Act
            using var evt = PointerDownEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(presses, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): a click binding on a non-Button binds nothing physically either.
        // This pins that the bridge keeps the same gate.
        [Test]
        public void Given_AClickBindingOnANonButtonAroundALayerPortal_When_AClickBubblesToTheHostRoot_Then_NothingFires()
        {
            // Arrange
            var clicks = 0;
            _mounted = V.Mount(_host.Root, V.Motion(
                events: new FiberEventBinding[] { new ClickedBinding { Handler = () => clicks++ } },
                children: new VNode[] { V.Component(LayerPortalRender) }));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ClickEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(clicks, Is.EqualTo(0));
        }

        [Test]
        public void Given_AToggleAroundALayerPortal_When_ABoolChangeBubblesToTheHostRoot_Then_ItsHandlerGetsTheNewValue()
        {
            // Arrange
            bool? received = null;
            MountField<Toggle, bool>(v => received = v);
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ChangeEvent<bool>.GetPooled(false, true);
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(received, Is.EqualTo(true));
        }

        [Test]
        public void Given_ASliderAroundALayerPortal_When_AFloatChangeBubblesToTheHostRoot_Then_ItsHandlerGetsTheNewValue()
        {
            // Arrange
            float? received = null;
            MountField<Slider, float>(v => received = v);
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ChangeEvent<float>.GetPooled(0f, 0.25f);
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(received, Is.EqualTo(0.25f));
        }

        [Test]
        public void Given_ATextFieldAroundALayerPortal_When_AStringChangeBubblesToTheHostRoot_Then_ItsHandlerGetsTheNewValue()
        {
            // Arrange
            string received = null;
            MountField<TextField, string>(v => received = v);
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ChangeEvent<string>.GetPooled("", "typed");
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(received, Is.EqualTo("typed"));
        }

        [Test]
        public void Given_AnIntegerSliderAroundALayerPortal_When_AnIntChangeBubblesToTheHostRoot_Then_ItsHandlerGetsTheNewValue()
        {
            // Arrange
            int? received = null;
            MountField<SliderInt, int>(v => received = v);
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ChangeEvent<int>.GetPooled(0, 7);
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(received, Is.EqualTo(7));
        }

        // GREEN_ON_BASE(characterization): a value-change binding on a non-field binds nothing physically either.
        // This pins that the bridge keeps the same gate.
        [Test]
        public void Given_ABoolChangeBindingOnANonFieldAroundALayerPortal_When_ABoolChangeBubblesToTheHostRoot_Then_NothingFires()
        {
            // Arrange
            bool? received = null;
            _mounted = V.Mount(_host.Root, V.Motion(
                events: new FiberEventBinding[] { new ChangeEventBinding<bool> { Handler = v => received = v } },
                children: new VNode[] { V.Component(LayerPortalRender) }));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ChangeEvent<bool>.GetPooled(false, true);
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(received, Is.Null);
        }

        // GREEN_ON_BASE(characterization): a pointer press is not a click, which the base's bridge agrees with.
        // This pins that a Button's click binding answers only the click the bridge carries.
        [Test]
        public void Given_AButtonAroundALayerPortal_When_APointerDownBubblesToTheHostRoot_Then_ItsOnClickDoesNotFire()
        {
            // Arrange
            var clicks = 0;
            _mounted = V.Mount(_host.Root, V.Button(
                onClick: () => clicks++,
                children: new VNode[] { V.Component(LayerPortalRender) }));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = PointerDownEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(clicks, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base's bridge already carries a pointer move to an ancestor.
        // This pins that the continuous kinds still reach their handler beside the click and change pass.
        [Test]
        public void Given_APointerMoveBindingAroundALayerPortal_When_APointerMoveBubblesToTheHostRoot_Then_ItFires()
        {
            // Arrange
            var moves = 0;
            _mounted = V.Mount(_host.Root, V.Motion(
                events: new FiberEventBinding[] { new PointerMoveBinding { Handler = _ => moves++ } },
                children: new VNode[] { V.Component(LayerPortalRender) }));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = PointerMoveEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(moves, Is.EqualTo(1));
        }

        [Test]
        public void Given_APortalIntoAContainer_When_ItUnmounts_Then_TheContainerKeepsNoBridgeListener()
        {
            // Arrange
            var reconciler = new Reconciler();
            var root = new VisualElement();
            var container = new VisualElement();
            var listenersBefore = BubbleUpListenerCount(container);
            var tree = new VNode[] { V.Portal(container, children: new VNode?[] { V.Div(name: "portal-child") }) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), tree);
            var listenersWhileMounted = BubbleUpListenerCount(container);

            // Act
            reconciler.Reconcile(root, tree, Array.Empty<VNode>());
            var listenersAfter = BubbleUpListenerCount(container);
            reconciler.Dispose();

            // Assert — the mounted count is folded in, since a bridge that never attached leaves nothing either.
            Assert.That(
                (listenersWhileMounted > listenersBefore, listenersAfter == listenersBefore),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base's dispose releases a mounted portal's bridge as well.
        // This pins that disposing with the portal still mounted removes every listener the bridge added.
        [Test]
        public void Given_APortalIntoAContainer_When_TheReconcilerIsDisposedWithItMounted_Then_TheContainerKeepsNoBridgeListener()
        {
            // Arrange
            var reconciler = new Reconciler();
            var container = new VisualElement();
            var listenersBefore = BubbleUpListenerCount(container);
            reconciler.Reconcile(new VisualElement(), Array.Empty<VNode>(),
                new VNode[] { V.Portal(container, children: new VNode?[] { V.Div(name: "portal-child") }) });
            var listenersWhileMounted = BubbleUpListenerCount(container);

            // Act
            reconciler.Dispose();

            // Assert — the mounted count is folded in, since a bridge that never attached leaves nothing either.
            Assert.That(
                (listenersWhileMounted > listenersBefore, BubbleUpListenerCount(container) == listenersBefore),
                Is.EqualTo((true, true)));
        }

        private static int BubbleUpListenerCount(VisualElement element)
        {
            var registry = typeof(CallbackEventHandler)
                .GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(element);
            if (registry == null)
            {
                return 0;
            }
            var bubbleUp = registry.GetType()
                .GetField("m_BubbleUpCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(registry);
            return (int)bubbleUp.GetType().GetProperty("Count")!.GetValue(bubbleUp);
        }

        private void MountField<TField, TValue>(Action<TValue> handler) where TField : VisualElement
        {
            _mounted = V.Mount(_host.Root, V.Motion(
                elementType: typeof(TField),
                events: new FiberEventBinding[] { new ChangeEventBinding<TValue> { Handler = handler } },
                children: new VNode[] { V.Component(LayerPortalRender) }));
        }

        private static (VisualElement HostRoot, VisualElement Child) PortalChild(string name = "portal-child")
        {
            foreach (var doc in UnityEngine.Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                var child = doc.rootVisualElement?.Q<VisualElement>(name);
                if (child != null)
                {
                    return (doc.rootVisualElement, child);
                }
            }
            throw new InvalidOperationException("The layer portal's child mounted under no host document.");
        }
    }
}
