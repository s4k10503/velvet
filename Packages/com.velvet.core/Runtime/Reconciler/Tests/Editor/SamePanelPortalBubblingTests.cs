using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the fuller synthetic-bubbling contract of <c>V.Portal(targetId:)</c>
    /// (<see cref="FiberCrossPanelEventDispatcher"/>, attached per resolved target from
    /// <c>ChildReconciler</c>'s registry-portal drain branch — see
    /// <c>ReconcilerContext.SamePanelPortalBridges</c>): the truncation guard against a handler that is
    /// both a physical AND a logical ancestor firing twice, <c>StopPropagation</c> mid-chain, nested
    /// same-panel portals, and the <c>events:</c>-only scoping that also governs
    /// <c>V.Portal(layer:)</c>/<c>V.WorldSpace</c>. The BASIC case (a plain logical-ancestor handler
    /// firing at all) is pinned in <see cref="PortalEventBubblingTests"/> alongside the physical-bubbling
    /// contract it shares a file with; this fixture is scoped to what is genuinely NEW about the
    /// same-panel form specifically.
    /// </summary>
    /// <remarks>
    /// Uses a real, live-dispatched <see cref="PointerDownEvent"/> (<c>VisualElement.SendEvent</c> under a
    /// real <see cref="EditorWindow"/> panel) rather than <c>CrossPanelEventBubblingTests</c>'
    /// <c>SimulateBubbledEvent</c> reflection helper: a same-panel target's remaining physical ancestors
    /// genuinely keep bubbling in the SAME live dispatch once this bridge's BubbleUp callback returns (see
    /// <c>FiberCrossPanelEventDispatcher.Continue</c>'s own comment), so exercising that interaction for
    /// real — rather than asserting against a single simulated hop — is what makes the truncation-guard
    /// tests below trustworthy. This is unlike the cross-panel case, where two genuinely bubbling-connected
    /// Panels cannot be constructed at all and simulation is the only option. Every mount goes through
    /// <c>V.Mount</c> (not a bare <c>Reconciler.Reconcile</c> call): the synthetic bridge resolves the
    /// logical ancestor from <c>DetachedMountContext</c>, which is stamped only while a real root fiber
    /// stays current on <c>FiberStack</c> through the post-reconcile drain.
    /// </remarks>
    [TestFixture]
    internal sealed class SamePanelPortalBubblingTests
    {
        private EditorWindow _window;
        private MountedTree _mounted;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");

            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.Show();
            FiberPortalRegistry.Clear();

            _root = new VisualElement();
            _window.rootVisualElement.Add(_root);

            // Static fixture fields hold the latest StateUpdater from whichever test last rendered their
            // owning component; a stale reference left over from a previous test would invoke a setter
            // against an already-disposed fiber tree (CrossPanelEventBubblingTests' own SetUp convention).
            s_showRegressionPortal = default;
            s_bumpLateHeal = default;
            s_showRemountPortal = default;
            s_showGrowthChildren = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            FiberPortalRegistry.Clear();
            if (_window != null)
            {
                _window.Close();
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        // Pooled-event dispatch is the canonical EditMode way to fire a real bubbling event without a
        // player loop (mirrors PortalEventBubblingTests.DispatchPointerDown).
        private static void DispatchPointerDown(VisualElement el)
        {
            using var evt = PointerDownEvent.GetPooled();
            evt.target = el;
            el.SendEvent(evt);
        }

        #region Basic

        [Component]
        private static VNode BasicPortalHostRender()
            => V.Portal("same-panel-basic-target", children: new VNode[] { V.Component(BasicPortalChildRender) });

        [Component]
        private static VNode BasicPortalChildRender() => V.Div(name: "basic-pc");

        [Test]
        public void Given_PointerDownOnRegistryPortalContent_When_HandlerOnLogicalAncestor_Then_HandlerInvoked()
        {
            // Arrange — the target is registered before mount (the registry portal resolves it
            // synchronously at CreateElement time) and lives fully outside the mounted tree, so only
            // the synthetic bridge — not any physical relationship — can reach the logical ancestor.
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-basic-target", target);
            var bubbled = false;
            var binding = new PointerDownBinding { Handler = _ => bubbled = true };
            _mounted = V.Mount(_root, V.Motion(
                name: "basic-logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(BasicPortalHostRender) }));
            var portalChild = target.Q<VisualElement>("basic-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(bubbled, Is.True);
        }

        #endregion

        #region Truncation (the load-bearing regression)

        private static StateUpdater<bool> s_showRegressionPortal;

        [Component]
        private static VNode RegressionCallerRender()
        {
            var (showPortal, setShowPortal) = Hooks.UseState(false);
            s_showRegressionPortal = setShowPortal;
            return V.When(showPortal, () => V.Portal("same-panel-regression-target",
                children: new VNode[] { V.Component(RegressionPortalChildRender) }));
        }

        [Component]
        private static VNode RegressionPortalChildRender() => V.Div(name: "regression-pc");

        [Test]
        public void Given_HandlerOnElementBothPhysicalAndLogicalAncestor_When_PortalChildFiresPointerDown_Then_HandlerFiresExactlyOnce()
        {
            // Arrange — "shared" is BOTH a physical ancestor of the resolved target (via "slot", an
            // always-empty V.Div Velvet never diffs children into, so a manually added child is safe
            // from ever being touched by a later reconcile) AND the logical ancestor of the Portal call
            // site (its caller mounts as "shared"'s own direct inline child). The target is registered,
            // and physically nested under "slot", BEFORE the Portal itself ever renders — revealed only
            // by a later setState (V.Mount cannot express "mount already showing this" for something
            // that also needs a not-yet-registered target) — so the registry always resolves a real
            // element by the time the Portal node is created.
            var fireCount = 0;
            var binding = new PointerDownBinding { Handler = _ => fireCount++ };
            _mounted = V.Mount(_root, V.Motion(
                name: "shared",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Div(name: "slot"), V.Component(RegressionCallerRender) }));
            var slot = _root.Q<VisualElement>("slot");
            Assume.That(slot, Is.Not.Null, "Precondition: the static slot mounted");
            var target = new VisualElement();
            slot.Add(target);
            FiberPortalRegistry.Register("same-panel-regression-target", target);
            s_showRegressionPortal.Invoke(true);
            _mounted.FlushStateForTest();
            var portalChild = target.Q<VisualElement>("regression-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act — the SAME pointer-down bubbles up the target's physical chain (through "slot" to
            // "shared", natively) and would ALSO reach "shared" via the synthetic logical walk if that
            // walk were not truncated.
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(fireCount, Is.EqualTo(1));
        }

        #endregion

        #region StopPropagation

        [Component]
        private static VNode StopPropagationCallerRender()
            => V.Portal("same-panel-stop-target", children: new VNode[] { V.Component(StopPropagationPortalChildRender) });

        [Component]
        private static VNode StopPropagationPortalChildRender() => V.Div(name: "stop-pc");

        [Test]
        public void Given_InnerLogicalHandlerStopsPropagation_When_PortalChildFiresPointerDown_Then_OuterLogicalHandlerDoesNotFire()
        {
            // Arrange — two logical ancestors stacked outward from the Portal call site. The inner one
            // stops propagation; the outer one is reached only by a LATER hop of the same outward
            // synthetic walk, pinning the fix: Continue previously never checked
            // evt.isPropagationStopped between hops, so a synthetic StopPropagation() had no effect.
            var innerFired = false;
            var outerFired = false;
            var innerBinding = new PointerDownBinding { Handler = evt => { innerFired = true; evt.StopPropagation(); } };
            var outerBinding = new PointerDownBinding { Handler = _ => outerFired = true };
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-stop-target", target);
            _mounted = V.Mount(_root, V.Motion(
                name: "outer",
                events: new FiberEventBinding[] { outerBinding },
                children: new VNode[]
                {
                    V.Motion(
                        name: "inner",
                        events: new FiberEventBinding[] { innerBinding },
                        children: new VNode[] { V.Component(StopPropagationCallerRender) }),
                }));
            var portalChild = target.Q<VisualElement>("stop-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That((innerFired, outerFired), Is.EqualTo((true, false)));
        }

        #endregion

        #region Nested same-panel portals

        [Component]
        private static VNode NestedOuterHostRender()
            => V.Portal("same-panel-nested-a", children: new VNode[] { V.Component(NestedInnerCallerRender) });

        [Component]
        private static VNode NestedInnerCallerRender()
            => V.Portal("same-panel-nested-b", children: new VNode[] { V.Component(NestedInnerPortalChildRender) });

        [Component]
        private static VNode NestedInnerPortalChildRender() => V.Div(name: "nested-pc");

        [Test]
        public void Given_PortalNestedInsideAnotherPortalsContent_When_InnerContentFiresPointerDown_Then_OutermostLogicalAncestorHandlerFires()
        {
            // Arrange — Portal A's content is a component that itself calls Portal B: B's own
            // LogicalParent fiber is ITSELF a detached-mount top-level child of A (not an ordinarily-
            // mounted fiber), so reaching the outermost logical ancestor requires escaping BOTH
            // boundaries, not just the innermost one.
            var bubbled = false;
            var binding = new PointerDownBinding { Handler = _ => bubbled = true };
            var targetA = new VisualElement();
            var targetB = new VisualElement();
            _window.rootVisualElement.Add(targetA);
            _window.rootVisualElement.Add(targetB);
            FiberPortalRegistry.Register("same-panel-nested-a", targetA);
            FiberPortalRegistry.Register("same-panel-nested-b", targetB);
            _mounted = V.Mount(_root, V.Motion(
                name: "nested-logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(NestedOuterHostRender) }));
            var portalChild = targetB.Q<VisualElement>("nested-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the innermost portal child mounted physically under target B");

            // Act
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(bubbled, Is.True);
        }

        #endregion

        #region Scoping

        [Component]
        private static VNode ScopingPortalHostRender()
            => V.Portal("same-panel-scoping-target", children: new VNode[] { V.Component(ScopingPortalChildRender) });

        [Component]
        private static VNode ScopingPortalChildRender() => V.Div(name: "scoping-pc");

        [Test]
        public void Given_RawRegisterCallbackOnLogicalAncestor_When_PortalChildFiresPointerDown_Then_HandlerNotInvoked()
        {
            // Arrange — the logical ancestor's handler is registered via the raw UI Toolkit API, not
            // Velvet's events: prop, so FiberEventBindingManager never learns about it:
            // TryInvokeSynthetic resolves exclusively from its own _bindingsByElement table (populated
            // only by Bind), never a native RegisterCallback registration regardless of who made it.
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-scoping-target", target);
            _mounted = V.Mount(_root, V.Motion(
                name: "scoping-logical",
                children: new VNode[] { V.Component(ScopingPortalHostRender) }));
            var logicalAncestor = _root.Q<VisualElement>("scoping-logical");
            Assume.That(logicalAncestor, Is.Not.Null, "Precondition: the logical ancestor mounted");
            var rawFired = false;
            EventCallback<PointerDownEvent> onPointerDown = _ => rawFired = true;
            logicalAncestor.RegisterCallback(onPointerDown);
            var portalChild = target.Q<VisualElement>("scoping-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act
            DispatchPointerDown(portalChild);
            logicalAncestor.UnregisterCallback(onPointerDown);

            // Assert
            Assert.That(rawFired, Is.False);
        }

        #endregion

        #region Late registration healing (Fix 1 regression)

        private static StateUpdater<int> s_bumpLateHeal;

        [Component]
        private static VNode LateHealCallerRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_bumpLateHeal = bump;
            return V.Portal("same-panel-late-heal-target",
                children: new VNode[] { V.Component(LateHealPortalChildRender) });
        }

        [Component]
        private static VNode LateHealPortalChildRender() => V.Div(name: "late-heal-pc");

        [Test]
        public void Given_TargetRegisteredAfterMount_When_PatchedAfterRegistration_Then_LogicalAncestorHandlerFires()
        {
            // Arrange — the target does not exist at mount time: CreateElement's registry-portal branch
            // warns and returns a hidden placeholder without enqueuing a DrainPendingPortalMounts entry,
            // so the mount-time attach (ChildReconciler's drain branch) never runs for this target. The
            // only remaining place the bridge can attach is PatchPortal's healing branch, on the first
            // patch after the id registers.
            LogAssert.Expect(LogType.Warning,
                "[Portal] Target \"same-panel-late-heal-target\" is not registered. Children will not be rendered.");
            var bubbled = false;
            var binding = new PointerDownBinding { Handler = _ => bubbled = true };
            _mounted = V.Mount(_root, V.Motion(
                name: "late-heal-logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(LateHealCallerRender) }));
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-late-heal-target", target);

            // Act — an unrelated state bump re-renders the caller and patches the Portal, which heals:
            // the now-registered target resolves and its children mount into it for the first time.
            s_bumpLateHeal.Invoke(v => v + 1);
            _mounted.FlushStateForTest();
            var portalChild = target.Q<VisualElement>("late-heal-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the heal mounted the portal child physically under the target");
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(bubbled, Is.True);
        }

        #endregion

        #region Growth after the initial mount (already-resolved patch)

        private static StateUpdater<bool> s_showGrowthChildren;

        [Component]
        private static VNode GrowthCallerRender()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_showGrowthChildren = setShow;
            return V.Portal("same-panel-growth-target",
                children: show ? new VNode[] { V.Component(GrowthPortalChildRender) } : Array.Empty<VNode>());
        }

        [Component]
        private static VNode GrowthPortalChildRender() => V.Div(name: "growth-pc");

        [Test]
        public void Given_RegistryPortalMountedWithNoChildren_When_ALaterPatchAddsAChild_Then_LogicalAncestorHandlerFires()
        {
            // Arrange — unlike the late-registration-heal case above, the target IS registered before
            // mount, so the Portal resolves and drains through DrainPendingPortalMounts on the very
            // first render — with ZERO children, since `show` starts false. That one-time drain stamp
            // finds nothing new to mark, and PatchPortalChildren's own bookkeeping already records this
            // placeholder's target as resolved (PortalState[placeholder].Target != null), so the
            // steady-state (already-resolved) branch of PatchPortal — not the healing branch pinned
            // above — is what this test exercises once the child actually mounts.
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-growth-target", target);
            var bubbled = false;
            var binding = new PointerDownBinding { Handler = _ => bubbled = true };
            _mounted = V.Mount(_root, V.Motion(
                name: "growth-logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(GrowthCallerRender) }));
            Assume.That(target.childCount, Is.EqualTo(0), "Precondition: the portal drained with no children");

            // Act — flipping `show` patches the ALREADY-RESOLVED Portal and mounts its first top-level
            // child, long after the initial (empty) drain's own stamp already ran with nothing to mark.
            s_showGrowthChildren.Invoke(true);
            _mounted.FlushStateForTest();
            var portalChild = target.Q<VisualElement>("growth-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the later patch mounted the portal child physically under the target");
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(bubbled, Is.True);
        }

        #endregion

        #region Remount to the same target

        private static StateUpdater<bool> s_showRemountPortal;

        [Component]
        private static VNode RemountCallerRender()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_showRemountPortal = setShow;
            return V.When(show, () => V.Portal("same-panel-remount-target",
                children: new VNode[] { V.Component(RemountPortalChildRender) }));
        }

        [Component]
        private static VNode RemountPortalChildRender() => V.Div(name: "remount-pc");

        [Test]
        public void Given_PortalUnmountedThenRemountedToTheSameTarget_When_PortalChildFiresPointerDown_Then_HandlerStillFires()
        {
            // Arrange — the target is registered once and never re-registered; only the Portal above it
            // unmounts and remounts. SamePanelPortalBridges is deliberately never cleared when a Portal's
            // own children unmount (see that field's own comment on ReconcilerContext), so the remount
            // must reuse the already-attached bridge rather than needing, or getting, a second attach.
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-remount-target", target);
            var bubbled = false;
            var binding = new PointerDownBinding { Handler = _ => bubbled = true };
            _mounted = V.Mount(_root, V.Motion(
                name: "remount-logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(RemountCallerRender) }));
            Assume.That(target.childCount, Is.EqualTo(1), "Precondition: the portal mounted its child under the target");

            // Act — hide (the portal's own slot range on the target clears) then show again (a fresh
            // placeholder resolves the SAME registered target and remounts into it).
            s_showRemountPortal.Invoke(false);
            _mounted.FlushStateForTest();
            Assume.That(target.childCount, Is.EqualTo(0), "Precondition: hiding unmounted the portal's children");
            s_showRemountPortal.Invoke(true);
            _mounted.FlushStateForTest();
            var portalChild = target.Q<VisualElement>("remount-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the remount mounted the portal child physically under the target again");
            DispatchPointerDown(portalChild);

            // Assert
            Assert.That(bubbled, Is.True);
        }

        #endregion

        #region Non-pointer events

        [Component]
        private static VNode KeyDownPortalHostRender()
            => V.Portal("same-panel-keydown-target", children: new VNode[] { V.Component(KeyDownPortalChildRender) });

        [Component]
        private static VNode KeyDownPortalChildRender() => V.Div(name: "keydown-pc");

        // Mirrors DispatchPointerDown for a discrete non-pointer event: DndInteractionTests dispatches
        // Escape the same way (evt.target set explicitly, so delivery does not depend on panel focus).
        private static void DispatchKeyDown(VisualElement el)
        {
            using var evt = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None);
            evt.target = el;
            el.SendEvent(evt);
        }

        [Test]
        public void Given_KeyDownOnRegistryPortalContent_When_HandlerOnLogicalAncestor_Then_HandlerInvoked()
        {
            // Arrange — same shape as the pointer-down Basic case, but for a KeyDownEvent: AttachBridge
            // registers a KeyDownEvent listener alongside the pointer ones, so a non-pointer discrete
            // event must cross the same-panel boundary too, not just pointer input.
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-keydown-target", target);
            var bubbled = false;
            var binding = new KeyDownBinding { Handler = _ => bubbled = true };
            _mounted = V.Mount(_root, V.Motion(
                name: "keydown-logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(KeyDownPortalHostRender) }));
            var portalChild = target.Q<VisualElement>("keydown-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act
            DispatchKeyDown(portalChild);

            // Assert
            Assert.That(bubbled, Is.True);
        }

        #endregion

        #region Target element itself

        [Component]
        private static VNode TargetSelfPortalHostRender()
            => V.Portal("same-panel-target-self-target", children: new VNode[] { V.Component(TargetSelfPortalChildRender) });

        [Component]
        private static VNode TargetSelfPortalChildRender() => V.Div(name: "target-self-pc");

        [Test]
        public void Given_PointerDownOnTheRegistryTargetItself_When_Dispatched_Then_LogicalAncestorHandlerDoesNotFire()
        {
            // Arrange — dispatch directly on the registry TARGET container, not on the Portal's own
            // content. Continue's walk looks for a DetachedMountContext starting AT the native event
            // target: that marker lives only on the Portal's own top-level children, never on the
            // pre-existing target container itself, so the walk finds nothing and the bridge is a no-op.
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("same-panel-target-self-target", target);
            var bubbled = false;
            var binding = new PointerDownBinding { Handler = _ => bubbled = true };
            _mounted = V.Mount(_root, V.Motion(
                name: "target-self-logical",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(TargetSelfPortalHostRender) }));
            var portalChild = target.Q<VisualElement>("target-self-pc");
            Assume.That(portalChild, Is.Not.Null, "Precondition: the portal child mounted physically under the target");

            // Act
            DispatchPointerDown(target);

            // Assert
            Assert.That(bubbled, Is.False);
        }

        #endregion

        /// <summary>Minimal EditorWindow host that supplies a real panel with an event controller.</summary>
        private sealed class TestHostWindow : EditorWindow { }
    }
}
