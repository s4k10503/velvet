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
    /// target element, so UI Toolkit bubbles their pointer / click / change events up the target's PHYSICAL
    /// ancestor chain — NOT up the logical ancestors of the V.Portal call site. This is a deliberate deviation:
    /// the ideal would be for portal events to bubble through the logical component tree; UI Toolkit computes every
    /// event's propagation path from the physical element tree and exposes no API to redirect it along a logical
    /// chain, so faithful logical bubbling is not reproducible without re-implementing the dispatcher. These tests
    /// are GREEN today and guard against an accidental future re-route that would silently change these expectations.
    /// </summary>
    /// <remarks>
    /// Mounted in a real <see cref="EditorWindow"/> panel because events only dispatch under a live panel: the
    /// logical ancestor and the portal target both hang off the same panel root so a synthetic
    /// <see cref="PointerDownEvent"/> on a portal child propagates through the real focus/event controller. The
    /// reconciler is driven directly (mirroring <see cref="PortalTests"/>) rather than the <c>V.Mount</c> path,
    /// because the contract under test is the physical reparenting the reconciler performs. The registry's static
    /// target table is cleared in <see cref="SetUp"/> and <see cref="TearDown"/> so registrations never leak.
    /// </remarks>
    [TestFixture]
    internal sealed class PortalEventBubblingTests
    {
        private EditorWindow _window;
        private Reconciler _reconciler;
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

        [Test]
        public void Given_PortalChildFiresPointerDown_When_HandlerOnLogicalAncestor_Then_HandlerNotInvoked()
        {
            // Arrange — the logical ancestor carries a Velvet PointerDownBinding (its own handler-wiring path); its
            // child is a Portal whose single leaf physically mounts under the target, off the ancestor's chain.
            var bubbledToLogical = false;
            var children = new VNode[]
            {
                V.Motion(
                    name: "logical",
                    events: new FiberEventBinding[]
                    {
                        new PointerDownBinding { Handler = _ => bubbledToLogical = true },
                    },
                    children: new VNode[]
                    {
                        V.Portal("evt-target", children: new VNode[] { V.Div(name: "pc") }),
                    }),
            };
            _reconciler.Reconcile(_logicalAncestor, Array.Empty<VNode>(), children);
            var portalChild = _portalTarget.Q<VisualElement>("pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act — fire a pointer-down on the portal child; it bubbles up the target's physical chain.
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(bubbledToLogical, Is.False,
                "Portal events do not bubble to the logical ancestor (physical-tree bubbling; documented React deviation)");
        }

        [Test]
        public void Given_PortalChildFiresPointerDown_When_HandlerOnPhysicalTargetAncestor_Then_HandlerInvoked()
        {
            // Arrange — the same mount, plus a raw callback on the target (the portal child's physical ancestor).
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
