using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Responsible for removing DOM elements and releasing their resources.
    // Recursively cleans up event bindings, components, animations, and descendants.
    internal sealed class FiberElementCleaner
    {
        private readonly ReconcilerContext _ctx;
        private IReconcilerHost _host = null!;

        public FiberElementCleaner(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        internal void SetHost(IReconcilerHost host)
        {
            if (_host != null)
            {
                throw new System.InvalidOperationException("[FiberElementCleaner] SetHost called twice");
            }
            _host = host;
        }

        public void RemoveElement(VisualElement parent, int index)
        {
            if (index < 0 || index >= parent.childCount)
            {
                return;
            }

            var element = parent.ElementAt(index);
            var poolable = PoolableOccupantOf(element);
            CleanupElement(element);
            parent.RemoveAt(index);
            ReturnOccupantToPool(element, poolable);
        }

        // Removes an element from its parent directly (by element reference, not index).
        public void RemoveElementDirect(VisualElement parent, VisualElement element)
        {
            if (element?.parent != parent)
            {
                return;
            }

            var poolable = PoolableOccupantOf(element);
            CleanupElement(element);
            element.RemoveFromHierarchy();
            ReturnOccupantToPool(element, poolable);
        }

        // Resolves the poolable occupant of a slot: a ring-*/clip-path-* leaf sits inside a
        // structural wrapper, and the wrapper — not the widget — is the slot's element. Must run
        // BEFORE CleanupElement, which consumes the wrapper map entry.
        private VisualElement PoolableOccupantOf(VisualElement element)
            => _ctx.WrapperToInnerMap.TryGetValue(element, out var inner) ? inner : element;

        // Reclaims the slot's poolable occupant after cleanup. A wrapped inner is detached from its
        // wrapper first so a pooled widget never carries the dead wrapper as its parent — mirroring
        // the rollback-orphan path, which already unwraps before reclaiming.
        private static void ReturnOccupantToPool(VisualElement removed, VisualElement poolable)
        {
            if (!ReferenceEquals(poolable, removed))
            {
                poolable.RemoveFromHierarchy();
            }
            ReturnToPool(poolable);
        }

        // Returns a created-but-never-placed orphan element to the pool. Used by the speculative
        // Suspense-primary commit rollback: a primary that suspends discards the elements it created
        // before the suspend, and those orphans were never inserted into the DOM so
        // RemoveElement / the general-commit finalize never reach them. EVERY orphan — leaf or
        // container — gets its element-keyed resources released here, or the ReconcilerContext
        // side-tables (variant manipulators, the structural/has/data/aria/supports rules,
        // arbitrary-value layers, shadow/clip/gradient bindings) would retain the discarded element
        // for the context's whole lifetime. A genuine poolable leaf is also reclaimed to the pool:
        // Label / Toggle / Slider / TextField never carry inline-mounted Velvet descendants (their
        // DSL hardcodes empty children), and a Button is reclaimed only when childless. A container
        // orphan — including a Button declared with children, whose children CreateElement
        // inline-expands into the element itself — has its subtree's resources released recursively
        // via CleanupElementCore, which deliberately does NOT dispose the subtree's fibers / Outlet
        // scopes (that is CleanupElement's job): those anchor on the fiber tree and may be re-paired
        // when the suspended primary later resolves. The element was never in the hierarchy, so there
        // is no DOM detach.
        public void ReturnRolledBackOrphan(VisualElement? element)
        {
            if (element == null) return;
            // A clip-path-* / ring / wrapElement orphan arrives as its structural WRAPPER (CreateElement
            // returns the wrapper). Detach the inner from the wrapper, then reclaim it by the rules below —
            // they release its binding-tracked resources (clip VectorImage, the wrapper-less skew + shadow
            // paint bindings, geometry callbacks, the binding dictionary entries) and either pool it or recurse.
            if (_ctx.WrapperToInnerMap.TryGetValue(element, out var innerOfWrapper))
            {
                _ctx.WrapperToInnerMap.Remove(element);
                innerOfWrapper.RemoveFromHierarchy();
                ReturnRolledBackOrphan(innerOfWrapper);
                return;
            }

            var exactType = element.GetType();
            if (exactType == typeof(Label) || exactType == typeof(Toggle)
                || exactType == typeof(Slider) || exactType == typeof(TextField)
                || (exactType == typeof(Button) && ((Button)element).childCount == 0))
            {
                CleanupElementResources(element);
                ReturnToPool(element);
            }
            else
            {
                // A plain container orphan (Div), a Button declared with children, or a user
                // subclass of a poolable primitive (never pooled — see ReturnToPool). Release the
                // orphan subtree's element-keyed resources without disposing its fibers and
                // without pooling the non-poolable container.
                CleanupElementCore(element);
            }
        }

        // Returns the element to the VNodePool when the type supports pooling.
        // Called after the element has been detached from the DOM hierarchy and Velvet-managed
        // resources have been released. Dispatches on the EXACT runtime type, mirroring the
        // factory's exact-type rent checks: a user subclass of a poolable primitive (mounted via
        // V.Custom<T>) must never enter the shared pool — its own fields and constructor-registered
        // callbacks survive the base-type reset, so a later plain rent would resurrect them on an
        // unrelated mount.
        private static void ReturnToPool(VisualElement element)
        {
            var type = element.GetType();
            if (type == typeof(TextField))
            {
                VNodePool.ReturnTextField((TextField)element);
            }
            else if (type == typeof(Toggle))
            {
                VNodePool.ReturnToggle((Toggle)element);
            }
            else if (type == typeof(Slider))
            {
                VNodePool.ReturnSlider((Slider)element);
            }
            else if (type == typeof(Button))
            {
                VNodePool.ReturnButton((Button)element);
            }
            else if (type == typeof(Label))
            {
                VNodePool.ReturnLabel((Label)element);
            }
        }

        // Recursively cleans up the element's events, components, animations, and descendants.
        // DOM operations (RemoveAt / RemoveFromHierarchy) are performed by the caller.
        public void CleanupElement(VisualElement element)
        {
            // Same wrapper → resources → portal → descendants teardown as the recursive descendant pass.
            CleanupElementCore(element);

            // Wrapper-mounted fibers under this subtree were disposed by the recursive Remove(VE) above, but
            // an inline-mounted fiber nested under a host element anchors on its PARENT FIBER, not a VE: when a
            // host element is torn down out-of-band (type-swap, Portal unmount, VirtualList recycle, presence
            // drop) its owning reconcile never walks into the host (it is an opaque leaf), so the orphan sweep
            // does not reach the fiber and it would survive to be re-paired as a zombie on a same-key re-entry.
            // Dispose by subtree containment here so its effect cleanups fire and a re-entry mounts fresh. This
            // set is disjoint from the orphan sweep's (top-level inline fibers anchor on the parent, outside any
            // removed child), so the disposal does not overlap; FiberRenderer.Dispose is idempotent regardless.
            _ctx.ComponentRegistry.DisposeFibersUnder(element);
        }

        // Detaches an element-keyed manipulator: removes it from the element and drops its side-table entry.
        // One helper so a new manipulator side-table is torn down by adding a call here, not by re-deriving the
        // TryGetValue / RemoveManipulator / Remove triple by hand (the ghost-leak / pool-reuse bug class).
        private static void DetachManipulator<T>(VisualElement element, Dictionary<VisualElement, T> table)
            where T : class, IManipulator
        {
            if (table.TryGetValue(element, out var manipulator))
            {
                element.RemoveManipulator(manipulator);
                table.Remove(element);
            }
        }

        // Releases resources for a single element (animations, events, components, gestures, VirtualList).
        // Performs no DOM operations. Shared logic between CleanupElement and
        // CleanupDescendants.
        private void CleanupElementResources(VisualElement element)
        {
            if (_ctx.RefCleanups.TryGetValue(element, out var refCleanup))
            {
                _ctx.RefCleanups.Remove(element);
                refCleanup?.Invoke();
            }
            _ctx.StyleAnimationScheduler.CancelEnter(element);
            // Teardown-flavored: this element is being released for good (pool return / disposal), not merely
            // interrupted, so an ordinary CancelExit's reversal hand-off (which assumes the element keeps
            // living) must not run even though it can still be attached at this point — DOM detachment is the
            // caller's own job, performed after this cleanup.
            _ctx.StyleAnimationScheduler.CancelExitForTeardown(element);
            _ctx.EventManager.UnbindAll(element);
            _ctx.ComponentRegistry.Remove(element);
            DetachManipulator(element, _ctx.GestureManipulators);
            DetachManipulator(element, _ctx.VariantManipulators);
            DetachManipulator(element, _ctx.ConditionalVariantManipulators);
            DetachManipulator(element, _ctx.RelationalVariantManipulators);
            // has-[:checked]: / has-[:focus]: own an event manipulator (descendant-event-driven); detach it.
            DetachManipulator(element, _ctx.HasVariantManipulators);
            // The pure side-tables (structural / has-[.class]: / data-/aria- rules + their attribute store /
            // supports- / Motion applied-classes) carry no manipulator and no disposable resource — their
            // applied payloads die with the element — so dropping the element's entry is the whole teardown.
            // Cleared through one mechanism so a new such side-table is wired by enrolling it in
            // ReconcilerContext, not by adding a line here. Dropping these also prevents a pooled widget from
            // ghosting a prior consumer's attributes / propagated-variant classes into its next mount.
            _ctx.ClearElementSideTables(element);
            // Stacked-variant manipulators are keyed by a tuple (not the element), so collect this element's
            // entries and detach them — releasing their inner-variant subscriptions (incl. a stacked dark:'s
            // process-wide VelvetTheme.DarkModeChanged) so they do not leak past unmount.
            if (_ctx.StackedVariantManipulators.Count > 0)
            {
                List<(VisualElement, object, int, StyleVariantKind, string, string?)>? stale = null;
                foreach (var kv in _ctx.StackedVariantManipulators)
                {
                    if (kv.Key.target == element)
                    {
                        (stale ??= new List<(VisualElement, object, int, StyleVariantKind, string, string?)>()).Add(kv.Key);
                    }
                }
                if (stale != null)
                {
                    foreach (var key in stale)
                    {
                        element.RemoveManipulator(_ctx.StackedVariantManipulators[key]);
                        _ctx.StackedVariantManipulators.Remove(key);
                    }
                }
            }
            DetachManipulator(element, _ctx.GapManipulators);
            DetachManipulator(element, _ctx.DivideManipulators);
            DetachManipulator(element, _ctx.GridManipulators);
            // Drop the arbitrary-value layer stack so a pooled widget does not inherit a prior consumer's
            // base/variant layers (state ghosting across pool reuse).
            StyleArbitraryValueResolver.ClearAll(element);
            if (_ctx.ShadowBindings.TryGetValue(element, out var shadowBinding))
            {
                // Detach unhooks the paint + re-bake callbacks so a pooled element cannot ghost a prior
                // consumer's drop shadow. The baked shadow textures are cached process-wide (DropShadowBaker)
                // and are NOT destroyed here — only this element's paint binding is dropped.
                DropShadowSilhouette.Detach(element, shadowBinding);
                _ctx.ShadowBindings.Remove(element);
            }
            if (_ctx.ClipPathBindings.TryGetValue(element, out var clipPathBinding))
            {
                // Keyed by the INNER element (the wrapper carries the baked VectorImage background).
                // Destroy the image explicitly: off-panel teardown (EditMode) has no
                // DetachFromPanelEvent path that would otherwise release it.
                clipPathBinding.DisposeImage();
                if (clipPathBinding.OnGeometry != null)
                {
                    element.UnregisterCallback(clipPathBinding.OnGeometry);
                }
                _ctx.ClipPathBindings.Remove(element);
            }
            if (_ctx.RingBindings.TryGetValue(element, out var ringBinding))
            {
                // Keyed by the INNER element. The ring overlay is a plain native-border VisualElement (no GPU
                // resource), so teardown only unregisters the geometry callback and drops the entry — the
                // wrapper + overlay leave with the element's subtree.
                if (ringBinding.OnGeometry != null)
                {
                    element.UnregisterCallback(ringBinding.OnGeometry);
                }
                _ctx.RingBindings.Remove(element);
            }
            if (_ctx.SkewBindings.TryGetValue(element, out var skewBinding))
            {
                // Detach unhooks the paint/stash callbacks and releases the inline color
                // suppression so a pooled element cannot ghost a sheared face or a sentinel
                // background onto its next consumer.
                SkewSilhouette.Detach(element, skewBinding);
                _ctx.SkewBindings.Remove(element);
            }
            if (_ctx.GradientBackgrounds.ContainsKey(element))
            {
                // Clear the baked gradient background-image so a pooled element cannot ghost a prior
                // gradient onto its next consumer. The texture itself is shared/cached, so it is NOT
                // destroyed here — only this element's reference is dropped.
                GradientBackground.Clear(element);
                _ctx.GradientBackgrounds.Remove(element);
            }
            if (_ctx.AnimationBindings.TryGetValue(element, out var animationBinding))
            {
                // Pause the recurring tick and restore the styles the motion drove so a pooled element does
                // not keep ticking or ghost a panned position / hue filter onto its next consumer.
                StyleAnimateDriver.Detach(element, animationBinding);
                _ctx.AnimationBindings.Remove(element);
            }
            if (_ctx.SceneViewBindings.TryGetValue(element, out var sceneViewBinding))
            {
                // Release both ends of the camera-output pair: the geometry callback, the camera's target
                // (only when it still points at the framework texture — a user reassignment after mount is
                // left intact), the texture itself, and the background image showing it.
                SceneViewDriver.Detach(element, sceneViewBinding);
                _ctx.SceneViewBindings.Remove(element);
            }
            if (_ctx.ParticlesBindings.TryGetValue(element, out var particlesBinding))
            {
                // Unhook the particle painter, pause the repaint tick, and destroy the hidden simulation
                // host so no orphaned GameObject keeps simulating after the element leaves.
                ParticlesDriver.Detach(element, particlesBinding);
                _ctx.ParticlesBindings.Remove(element);
            }
            if (_ctx.VirtualListControllers.TryGetValue(element, out var virtualListController))
            {
                virtualListController.Dispose();
                _ctx.VirtualListControllers.Remove(element);
            }
            if (_ctx.OutletScopes.TryGetValue(element, out var routeScope))
            {
                routeScope.Dispose();
                _ctx.OutletScopes.Remove(element);
            }
            // Identity-side registration added on every Outlet mount; without this per-element
            // removal the set would pin every unmounted Outlet's dead container element until the
            // whole reconciler disposes. No-op for non-Outlet elements.
            _ctx.OutletContainers.Remove(element);
        }

        // Removes only this Portal's slot range from the target's children
        // (slotStart .. slotStart + slotLength), preserving children placed by other
        // Portals sharing the same target. After removal, downstream Portals on the same target
        // have their slotStart shifted by -slotLength so subsequent patches stay
        // correctly addressed. Entry removal happens before target mutation to prevent double
        // processing via CleanupDescendants recursion.
        private void CleanupPortal(VisualElement element)
        {
            if (!_ctx.PortalState.TryGetValue(element, out var portalInfo))
            {
                return;
            }

            _ctx.PortalState.Remove(element);
            var target = FiberPortalRegistry.Get(portalInfo.TargetId);
            if (target == null)
            {
                return;
            }

            var slotEnd = portalInfo.SlotStart + portalInfo.SlotLength;
            if (slotEnd > target.childCount) slotEnd = target.childCount;
            for (var i = slotEnd - 1; i >= portalInfo.SlotStart; i--)
            {
                var child = target.ElementAt(i);
                var poolable = PoolableOccupantOf(child);
                CleanupElement(child);
                target.RemoveAt(i);
                ReturnOccupantToPool(child, poolable);
            }

            // Surviving Portals on the same target whose slot starts after the removed range
            // collapse left by SlotLength so their next patch addresses the right DOM positions.
            PortalSlotTracker.ShiftSlotStartsAfter(_ctx.PortalState, portalInfo.TargetId, portalInfo.SlotStart, -portalInfo.SlotLength);
        }

        // Recursively cleans up the descendants of an element. DOM operations are the caller's
        // responsibility. Applies the same processing order as CleanupElement to each
        // child, preventing silent bugs caused by asymmetric wrapper handling.
        // element: Root element whose descendants are cleaned up.
        // excluded:
        // Child element to skip. Specified to prevent double cleanup of the inner element when the
        // wrapper pattern places the inner as a direct child of element. When null,
        // all children are processed.
        private void CleanupDescendants(VisualElement element, VisualElement? excluded = null)
        {
            for (var i = 0; i < element.childCount; i++)
            {
                var child = element.ElementAt(i);
                if (child == excluded)
                {
                    continue;
                }

                CleanupElementCore(child);
            }
        }

        // The single element-teardown core: wrapper → resources → portal → descendants recursion, with no
        // DOM operations. Shared by CleanupElement (the public entry, which additionally disposes inline
        // fibers under the subtree) and by CleanupDescendants (the recursive loop body), so the wrapper
        // handling lives in exactly one place — an asymmetry between the two used to be a silent-bug source.
        //
        // When a wrapper → inner link exists the inner element is a DOM child of the wrapper: it is fully
        // cleaned here and excluded from the element's CleanupDescendants so it is not processed twice (when
        // not a wrapper, innerElement is null and nothing is excluded).
        //
        // Descendant elements are NOT returned to VNodePool here because the descendant DOM remove is bulk
        // (parent.RemoveAt) and individual children are not surfaced. Pool return is limited to the top-level
        // element of RemoveElement, RemoveElementDirect, and CleanupPortal.
        private void CleanupElementCore(VisualElement element)
        {
            var isWrapper = _ctx.WrapperToInnerMap.TryGetValue(element, out var innerElement);
            if (isWrapper)
            {
                CleanupElementResources(innerElement);
                CleanupDescendants(innerElement);
                _ctx.WrapperToInnerMap.Remove(element);
            }

            CleanupElementResources(element);
            CleanupPortal(element);
            CleanupDescendants(element, innerElement);
        }
    }
}
