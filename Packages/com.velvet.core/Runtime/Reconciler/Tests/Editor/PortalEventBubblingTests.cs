using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the event-bubbling contract of <see cref="V.Portal"/>, the axis orthogonal to the slot bookkeeping
    /// covered by <see cref="PortalTests"/>. A Portal physically reparents its children under the registered
    /// target element, so UI Toolkit's OWN native dispatch bubbles their pointer / click / change events up the
    /// target's PHYSICAL ancestor chain. Velvet separately bridges pointer / key / focus events (not click / value-
    /// change, which have no underlying bubbling event object) synthetically to the LOGICAL ancestor chain of the
    /// <c>V.Portal</c> call site too — the same mechanism <c>V.Portal(layer:)</c>/<c>V.WorldSpace</c> already had for
    /// their separate host panels, extended to this same-panel form (see
    /// <see cref="SamePanelPortalBubblingTests"/> for the fuller synthetic-bubbling contract: truncation against a
    /// shared ancestor, nested portals, <c>StopPropagation</c>). This file stays scoped to what changes at the
    /// Portal-specific boundary: physical bubbling keeps working exactly as before, and a logical-ancestor handler
    /// now ALSO fires.
    /// </summary>
    /// <remarks>
    /// Mounted in a real <see cref="EditorWindow"/> panel because events only dispatch under a live panel: the
    /// logical ancestor and the portal target both hang off the same panel root so a synthetic
    /// <see cref="PointerDownEvent"/> on a portal child propagates through the real focus/event controller. The
    /// physical-bubbling test drives the reconciler directly (mirroring <see cref="PortalTests"/>), because the
    /// contract under test there is only the physical reparenting the reconciler performs and needs no fiber
    /// machinery. The logical-bubbling test goes through <c>V.Mount</c> instead: the synthetic bridge resolves the
    /// logical ancestor from <c>DetachedMountContext</c>, which is stamped only while a real root fiber stays
    /// current on <c>FiberStack</c> through the post-reconcile drain — a bare <c>Reconciler.Reconcile</c> call
    /// never establishes one. The registry's static target table is cleared in <see cref="SetUp"/> and
    /// <see cref="TearDown"/> so registrations never leak.
    /// </remarks>
    [TestFixture]
    internal sealed class PortalEventBubblingTests
    {
        private EditorWindow _window;
        private Reconciler _reconciler;
        private MountedTree _mounted;
        private VisualElement _logicalAncestor;
        private VisualElement _portalTarget;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");

            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.Show();

            _reconciler = new Reconciler();
            FiberPortalRegistry.Clear();

            // Both hosts share the live panel: the logical ancestor holds the V.Portal call site, the target is
            // where the children physically mount. A portal child's event bubbles up the target's chain only.
            _logicalAncestor = new VisualElement();
            _portalTarget = new VisualElement();
            _window.rootVisualElement.Add(_logicalAncestor);
            _window.rootVisualElement.Add(_portalTarget);
            FiberPortalRegistry.Register("evt-target", _portalTarget);
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _reconciler.Dispose();
            FiberPortalRegistry.Clear();
            if (_window != null)
            {
                _window.Close();
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        // Pooled-event dispatch is the canonical EditMode way to fire a real bubbling event without a player loop.
        private static void DispatchPointerDown(VisualElement el)
        {
            using var evt = PointerDownEvent.GetPooled();
            evt.target = el;
            el.SendEvent(evt);
        }

        [Component]
        private static VNode LogicalAncestorPortalHostRender()
        {
            return V.Portal("evt-target", children: new VNode[] { V.Component(LogicalAncestorPortalChildRender) });
        }

        [Component]
        private static VNode LogicalAncestorPortalChildRender() => V.Div(name: "pc");

        [Test]
        public void Given_PortalChildFiresPointerDown_When_HandlerOnLogicalAncestor_Then_HandlerInvoked()
        {
            // Arrange — the logical ancestor carries a Velvet PointerDownBinding; its child is a registry Portal
            // whose content physically mounts under the target, off the ancestor's physical chain. Both the
            // Portal call site and its content are wrapped in components (rather than bare elements) so the
            // portal drain (ChildReconciler.DrainPendingPortalMounts) has top-level ComponentFibers to stamp
            // DetachedMountContext onto — see FiberCrossPanelEventDispatcher's own comment on the bare-element
            // limitation this mirrors from the layer/world-space case.
            var bubbledToLogical = false;
            var binding = new PointerDownBinding { Handler = _ => bubbledToLogical = true };
            _mounted = V.Mount(_logicalAncestor, V.Motion(
                name: "logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(LogicalAncestorPortalHostRender) }));
            var portalChild = _portalTarget.Q<VisualElement>("pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act — fire a pointer-down on the portal child; it bubbles up the target's physical chain and,
            // separately, synthetically to the logical ancestor chain outside the call site.
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(bubbledToLogical, Is.True,
                "V.Portal(targetId:) events now bubble synthetically to the logical ancestor chain, mirroring V.Portal(layer:)/V.WorldSpace");
        }

        [Test]
        public void Given_PortalChildFiresPointerDown_When_HandlerOnPhysicalTargetAncestor_Then_HandlerInvoked()
        {
            // Arrange — the same mount, plus a raw callback on the target (the portal child's physical ancestor).
            // The portal content here is a BARE V.Div (no enclosing V.Component) and the mount goes through the
            // bare reconciler (no root fiber), so the same-panel synthetic bridge that auto-attaches to
            // _portalTarget on every registry-portal mount (see ReconcilerContext.SamePanelPortalBridges) finds
            // no DetachedMountContext to resolve and is a no-op here — this test is unaffected and still pins
            // ordinary native physical bubbling in isolation from the synthetic path.
            var bubbledToPhysical = false;
            EventCallback<PointerDownEvent> onTargetPointerDown = _ => bubbledToPhysical = true;
            _portalTarget.RegisterCallback(onTargetPointerDown);
            var children = new VNode[]
            {
                V.Div(name: "logical", children: new VNode[]
                {
                    V.Portal("evt-target", children: new VNode[] { V.Div(name: "pc") }),
                }),
            };
            _reconciler.Reconcile(_logicalAncestor, Array.Empty<VNode>(), children);
            var portalChild = _portalTarget.Q<VisualElement>("pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act
            DispatchPointerDown(portalChild);
            _portalTarget.UnregisterCallback(onTargetPointerDown);

            // Assert
            Assert.That(bubbledToPhysical, Is.True,
                "Portal child events bubble up the physical target chain");
        }

        /// <summary>Minimal EditorWindow host that supplies a real panel with an event controller.</summary>
        private sealed class TestHostWindow : EditorWindow { }
    }
}
