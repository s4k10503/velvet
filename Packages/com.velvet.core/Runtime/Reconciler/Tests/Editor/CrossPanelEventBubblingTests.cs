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

        [Test]
        public void Given_AFocusInInsideALayerPortal_When_Simulated_Then_TheEnclosingComponentsHandlerFires()
        {
            // Arrange — same shape as the pointer-down cases, but for FocusInBinding (events: FocusIn
            // is one of the discrete bindings FiberEventBindingManager.TryInvokeSynthetic supports,
            // alongside PointerDown/Up, KeyDown/Up, FocusOut/Focus/Blur). Style-driven focus variants
            // (has-[:focus]:, group-focus-within:) are a SEPARATE mechanism
            // (StyleHasVariantManipulator/VariantSignalSource register their own native
            // RegisterCallback<FocusInEvent> directly, bypassing FiberEventBindingManager entirely) and
            // stay physical-tree-only per the existing portals.md contract — this test covers only the
            // events: FocusInBinding path this dispatcher actually bridges.
            var binding = new FocusInBinding
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

            // Act
            using var evt = FocusInEvent.GetPooled();
            hostDoc.rootVisualElement.SimulateBubbledEvent(evt, portalTarget);

            // Assert
            Assert.That((s_handlerFired, s_handlerEventTarget == portalTarget), Is.EqualTo((true, true)));
        }

        [Component]
        private static VNode StopPropagationPortalHostRender()
        {
            return V.Portal(UILayer.Overlay, children: new VNode[]
            {
                V.Component(StopPropagationPortalChildRender),
            });
        }

        [Component]
        private static VNode StopPropagationPortalChildRender() => V.Motion(name: "stop-portal-target");

        [Test]
        public void Given_AnInnerLogicalHandlerStopsPropagation_When_APointerDownInsideALayerPortal_Then_TheOuterLogicalHandlerDoesNotFire()
        {
            // Arrange — two logical ancestors stacked outward from the portal: "inner" stops
            // propagation, so "outer" — reached only by a LATER hop of the same outward synthetic
            // walk — must not fire. Pins the fix: Continue previously never checked
            // evt.isPropagationStopped between hops, so a synthetic StopPropagation() had no effect
            // on the rest of the walk.
            var outerFired = false;
            var innerFired = false;
            var outerBinding = new PointerDownBinding { Handler = _ => outerFired = true };
            var innerBinding = new PointerDownBinding
            {
                Handler = evt =>
                {
                    innerFired = true;
                    evt.StopPropagation();
                },
            };
            _mounted = V.Mount(_host.Root, V.Motion(
                name: "outer",
                events: new FiberEventBinding[] { outerBinding },
                children: new VNode[]
                {
                    V.Motion(
                        name: "inner",
                        events: new FiberEventBinding[] { innerBinding },
                        children: new VNode[] { V.Component(StopPropagationPortalHostRender) }),
                }));

            var hostDoc = FindHostDocumentContaining("stop-portal-target");
            Assume.That(hostDoc, Is.Not.Null, "Precondition: the layer host panel exists");
            var portalTarget = hostDoc.rootVisualElement.Q<VisualElement>("stop-portal-target");
            Assume.That(portalTarget, Is.Not.Null, "Precondition: the portal's child mounted under the host");

            // Act
            using var evt = PointerDownEvent.GetPooled();
            hostDoc.rootVisualElement.SimulateBubbledEvent(evt, portalTarget);

            // Assert
            Assert.That((innerFired, outerFired), Is.EqualTo((true, false)));
        }

        private static int s_ancestorRuns;
        private static StateUpdater<VisualElement> s_setLayerRoot;

        // A layer portal, and beside it a portal into the root of the layer's host panel once a render hands it
        // that root: the root is the host's bridge anchor and a portal target both.
        [Component]
        private static VNode PortalIntoTheLayerRootRender()
        {
            var (root, setRoot) = Hooks.UseState<VisualElement>(null);
            s_setLayerRoot = setRoot;
            return V.Fragment(
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Motion(name: "layer-child") }),
                root == null ? null : V.Portal(root, children: new VNode[] { V.Motion(name: "root-child") }));
        }

        private UIDocument MountPortalIntoTheLayerRoot()
        {
            var binding = new PointerDownBinding { Handler = _ => s_ancestorRuns++ };
            _mounted = V.Mount(_host.Root, V.Motion(
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(PortalIntoTheLayerRootRender) }));
            var hostDoc = FindHostDocumentContaining("layer-child");
            s_setLayerRoot.Invoke(hostDoc.rootVisualElement);
            _mounted.FlushStateForTest();
            return hostDoc;
        }

        [Test]
        public void Given_APortalIntoALayerHostsRoot_When_APointerDownBubblesFromTheLayersChild_Then_TheAncestorRunsOnce()
        {
            // Arrange
            s_ancestorRuns = 0;
            var hostDoc = MountPortalIntoTheLayerRoot();
            var root = hostDoc.rootVisualElement;

            // Act
            using var evt = PointerDownEvent.GetPooled();
            root.SimulateBubbledEvent(evt, root.Q<VisualElement>("layer-child"));

            // Assert — the second portal's child is folded in, since a portal that never mounted into the root
            // would satisfy the count on its own.
            Assert.That((root.Q<VisualElement>("root-child") != null, s_ancestorRuns), Is.EqualTo((true, 1)));
        }

        // GREEN_ON_BASE(characterization): the base attaches the host's listeners and the root portal's apart, so
        // the host's stay when that portal leaves; the hold count the change puts on the root must keep them.
        [Test]
        public void Given_APortalIntoALayerHostsRootThatLeaves_When_APointerDownBubblesFromTheLayersChild_Then_TheAncestorStillRuns()
        {
            // Arrange
            s_ancestorRuns = 0;
            var hostDoc = MountPortalIntoTheLayerRoot();
            var root = hostDoc.rootVisualElement;
            s_setLayerRoot.Invoke(null);
            _mounted.FlushStateForTest();

            // Act
            using var evt = PointerDownEvent.GetPooled();
            root.SimulateBubbledEvent(evt, root.Q<VisualElement>("layer-child"));

            // Assert — the second portal's child is folded in, since a portal that never left would satisfy the
            // count on its own.
            Assert.That((root.Q<VisualElement>("root-child") == null, s_ancestorRuns), Is.EqualTo((true, 1)));
        }

        [Test]
        public void Given_APortalIntoALayerHostsRoot_When_TheHostIsDestroyedTwice_Then_TheRootKeepsThePortalsBridge()
        {
            // Arrange
            var root = MountPortalIntoTheLayerRoot().rootVisualElement;
            var context = _mounted.Root.Reconciler!.Context;
            PanelHostRecord record = null;
            foreach (var host in context.LayerHosts.Values) record = host;

            // Act
            PanelHostFactory.Destroy(record);
            PanelHostFactory.Destroy(record);

            // Assert
            Assert.That(context.EventManager.IsBridgeAnchor(root), Is.True);
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
