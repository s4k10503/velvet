using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pointer/focus event originating inside a <c>V.Portal(layer:)</c> host panel
    /// bubbles synthetically to the logical ancestor chain outside that panel — the component that
    /// called <c>V.Portal</c>, and beyond it, the physical ancestors of THAT component's own host
    /// element (FiberCrossPanelEventDispatcher). A host panel is a wholly separate UI Toolkit Panel
    /// from the declaring panel, so native bubbling structurally cannot cross the boundary on its own;
    /// FiberCrossPanelEventDispatcher.AttachBridge registers one BubbleUp listener per supported event
    /// type on the host panel's root, simulated here via
    /// <see cref="VisualElementTestExtensions.SimulateBubbledEvent{TEvent}"/> to model the arrival of a
    /// native event that already finished bubbling inside the host panel.
    /// </summary>
    [TestFixture]
    internal sealed class CrossPanelEventBubblingTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private static bool s_handlerFired;
        private static VisualElement s_handlerEventTarget;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_handlerFired = false;
            s_handlerEventTarget = null;
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
        private static VNode PortalHostRender()
        {
            return V.Portal(UILayer.Overlay, children: new VNode[]
            {
                // Wrapped in a component (rather than a bare V.Motion) so the portal drain
                // (ChildReconciler.DrainPendingPortalMounts) produces a top-level ComponentFiber to
                // stamp DetachedMountContext onto — see FiberCrossPanelEventDispatcher's own comment
                // on the bare-element limitation.
                V.Component(PortalChildRender),
            });
        }

        [Component]
        private static VNode PortalChildRender() => V.Motion(name: "portal-target");

        [Test]
        public void Given_APointerDownInsideALayerPortal_When_Simulated_Then_TheEnclosingComponentsHandlerFires()
        {
            // Arrange — the outer Motion (in the MAIN panel) owns the PointerDownBinding; its child
            // logically contains a V.Portal(layer:) whose own children physically live in a totally
            // separate host panel.
            var binding = new PointerDownBinding
            {
                Handler = evt =>
                {
                    s_handlerFired = true;
                    s_handlerEventTarget = evt.target as VisualElement;
                },
            };
            _mounted = V.Mount(_host.Root, V.Motion(
                name: "enclosing",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(PortalHostRender) }));

            var hostDoc = FindHostDocumentContaining("portal-target");
            Assume.That(hostDoc, Is.Not.Null, "Precondition: the layer host panel exists");
            var portalTarget = hostDoc.rootVisualElement.Q<VisualElement>("portal-target");
            Assume.That(portalTarget, Is.Not.Null, "Precondition: the portal's child mounted under the host");

            // Act — simulate a native PointerDownEvent that already bubbled to the host panel's root
            // (FiberCrossPanelEventDispatcher.AttachBridge's registration point) without crossing into
            // the main panel, since UI Toolkit's own dispatch cannot do that on its own.
            using var evt = PointerDownEvent.GetPooled();
            hostDoc.rootVisualElement.SimulateBubbledEvent(evt, portalTarget);

            // Assert
            Assert.That((s_handlerFired, s_handlerEventTarget == portalTarget), Is.EqualTo((true, true)));
        }

        [Component]
        private static VNode PortalHostWithPlainDivRender()
        {
            return V.Portal(UILayer.Overlay, children: new VNode[]
            {
                V.Component(PortalDivChildRender),
            });
        }

        [Component]
        private static VNode PortalDivChildRender() => V.Div(name: "portal-div-target");

        [Test]
        public void Given_APointerDownInsideALayerPortal_When_TheTargetIsAPlainDiv_Then_TheEnclosingComponentsHandlerFires()
        {
            // Arrange — same shape as the Motion case above, but the portal's child renders a plain
            // V.Div (ElementNode) rather than V.Motion (MotionNode) — both element-creation paths in
            // FiberNodeFactory.CreateElement independently stamp the userData reverse index.
            var binding = new PointerDownBinding
            {
                Handler = evt =>
                {
                    s_handlerFired = true;
                    s_handlerEventTarget = evt.target as VisualElement;
                },
            };
            _mounted = V.Mount(_host.Root, V.Motion(
                name: "enclosing",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(PortalHostWithPlainDivRender) }));

            var hostDoc = FindHostDocumentContaining("portal-div-target");
            Assume.That(hostDoc, Is.Not.Null, "Precondition: the layer host panel exists");
            var portalTarget = hostDoc.rootVisualElement.Q<VisualElement>("portal-div-target");
            Assume.That(portalTarget, Is.Not.Null, "Precondition: the portal's child mounted under the host");

            // Act
            using var evt = PointerDownEvent.GetPooled();
            hostDoc.rootVisualElement.SimulateBubbledEvent(evt, portalTarget);

            // Assert
            Assert.That((s_handlerFired, s_handlerEventTarget == portalTarget), Is.EqualTo((true, true)));
        }

        private UIDocument FindHostDocumentContaining(string childName)
        {
            foreach (var doc in UnityEngine.Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (doc.rootVisualElement != null && doc.rootVisualElement.Q<VisualElement>(childName) != null)
                {
                    return doc;
                }
            }
            return null;
        }
    }
}
