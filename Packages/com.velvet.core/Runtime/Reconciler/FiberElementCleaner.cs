using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Element-exit teardown: every path that removes an element from the DOM funnels through here so
    // the ~25 per-element resource categories are released exactly once (see CleanupElementResources).
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
            if (_ctx.RefCallbacks.TryGetValue(element, out var installedRef))
            {
                _ctx.RefCallbacks.Remove(element);
                installedRef.Cleanup?.Invoke();
            }
            _ctx.StyleAnimationScheduler.CancelEnter(element);
            // Teardown-flavored: this element is being released for good (pool return / disposal), not merely
            // interrupted, so an ordinary CancelExit's reversal hand-off (which assumes the element keeps
            // living) must not run even though it can still be attached at this point — DOM detachment is the
            // caller's own job, performed after this cleanup.
            _ctx.StyleAnimationScheduler.CancelExitForTeardown(element);
            // Must run BEFORE ClearElementSideTables below: it reads ElementToLayoutId (one of the
            // pure side-tables that call clears) to find this element's layoutId, if any, and cancels
            // its in-flight tick / drops its LayoutIdRegistry entry so a departing element's tween
            // never keeps ticking against it.
            MotionLayoutIdDriver.CancelForTeardown(element, _ctx);
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
            DetachManipulator(element, _ctx.TextBalanceManipulators);
            DetachManipulator(element, _ctx.ChildVariantManipulators);
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
            if (_ctx.BorderStyleBindings.TryGetValue(element, out var borderStyleBinding))
            {
                // The dashed-outline paint is a generateVisualContent delegate, not a style property, so the
                // pool reset cannot scrub it — detach unhooks the paint/stash callbacks and releases the border
                // color suppression so a pooled element cannot ghost a dashed outline onto its next consumer.
                BorderStyleSilhouette.Detach(element, borderStyleBinding);
                _ctx.BorderStyleBindings.Remove(element);
            }
            if (_ctx.DivideDashBindings.TryGetValue(element, out var divideDashBinding))
            {
                // Keyed by the divided CHILD, so a keyed-list reorder recycling one child independently of its
                // divide container is still caught here (the container's own manipulator may never re-run for
                // it). Detach unhooks the divider paint delegate the pool reset cannot scrub.
                DivideDashPainter.Detach(element, divideDashBinding);
                _ctx.DivideDashBindings.Remove(element);
            }
            if (_ctx.TextOverlineBindings.TryGetValue(element, out var overlineBinding))
            {
                // The overline rule is a generateVisualContent delegate, not a style property, so the pool
                // reset cannot scrub it — detach unhooks the paint callback so a pooled element cannot ghost
                // a painted rule onto its next consumer.
                TextOverlineSilhouette.Detach(element, overlineBinding);
                _ctx.TextOverlineBindings.Remove(element);
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
            if (_ctx.FilterTransitionBindings.TryGetValue(element, out var filterTransitionBinding))
            {
                // Pause the one-shot tick and unregister so a pooled element does not keep ticking a mid-flight
                // filter tween. The inline filter itself is scrubbed by FiberElementPoolReset before reuse; the
                // ClearAll above only drops the arbitrary-value layer map, never style.filter.
                StyleFilterTransitionDriver.Detach(element, filterTransitionBinding);
                _ctx.FilterTransitionBindings.Remove(element);
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
            if (_ctx.AnchoredBindings.TryGetValue(element, out var anchoredBinding))
            {
                // Pause the recurring projection tick so a pooled element does not keep repositioning
                // itself after it is reused for something unrelated.
                AnchoredDriver.Detach(element, anchoredBinding);
                _ctx.AnchoredBindings.Remove(element);
            }
            if (_ctx.FocusScopeBindings.Remove(element, out var focusScopeBinding))
            {
                // The registry entry is dropped BEFORE the restore focus fires: the restore's own FocusIn
                // must not see the dying scope as a live contain scope, or the snap-back would revert the
                // restore straight back into the detaching subtree. RestoreFocus itself must still run
                // BEFORE this element physically leaves the tree (UI Toolkit clears the focused element
                // the moment it detaches — the same ordering RescueFocusFromWorldSpaceHost exists for).
                if (focusScopeBinding.Settings.RestoreFocus
                    && element.panel?.focusController?.focusedElement is VisualElement held
                    && element.Contains(held))
                {
                    var restore = focusScopeBinding.RestoreTarget;
                    if (restore != null && restore.panel != null && restore.canGrabFocus)
                    {
                        restore.Focus();
                    }
                }
                FocusScopeDriver.Detach(element, focusScopeBinding);
            }
            // A departing element must not linger as any scope's remembered member or restore target:
            // pooled primitives get recycled into unrelated roles, and the liveness checks at use time
            // (panel / canGrabFocus / Contains) all pass again for a recycled element mounted elsewhere,
            // so focus would land on a widget in a different logical role. Dropping the reference here
            // makes those paths fall back (ring-first entry / skipped restore) instead.
            if (_ctx.FocusScopeBindings.Count > 0)
            {
                foreach (var scopeBinding in _ctx.FocusScopeBindings.Values)
                {
                    if (ReferenceEquals(scopeBinding.LastFocusedMember, element)) scopeBinding.LastFocusedMember = null;
                    if (ReferenceEquals(scopeBinding.RestoreTarget, element)) scopeBinding.RestoreTarget = null;
                }
            }
            // A chained portal placeholder unmounting releases both chained registries; if it owned the
            // host's ring-edge escape, ownership is handed to a surviving chained placeholder of the same
            // host (removal from ChainedPlaceholders first, so the departing one cannot re-elect itself).
            if (_ctx.ChainedPlaceholders.Remove(element, out var chainedRecord))
            {
                var chainedHostRoot = chainedRecord.Document != null ? chainedRecord.Document.rootVisualElement : null;
                FiberFocusNavigator.ReleaseChainedOwnership(element, chainedHostRoot, _ctx);
            }
            // A deferred navigator attach hook still pending on this element unregisters with it — a
            // pooled element must not carry a live hook into its next role.
            if (_ctx.NavigatorPendingAttachHooks.Count > 0)
            {
                FiberFocusNavigator.ReleasePendingAttachHooks(element, _ctx);
            }
            // Drag-and-drop bindings: the draggable's pointer-down armer unregisters, and an element that
            // is the active session's source or scope cancels that session teardown-flavored (synchronous
            // scrub so the element reaches the pool clean, deferred user callback — an ARBITRARY user
            // callback run mid-flush could read a half-mutated tree or re-enter the reconciler, so it
            // waits for the flush like effect callbacks do; see DndActiveDrag. Plain state writes from
            // mid-flush are safe — they schedule a follow-up render.)
            if (_ctx.DndScopeBindings.ContainsKey(element))
            {
                DndScopeDriver.Detach(element, _ctx);
                _ctx.DndScopeBindings.Remove(element);
            }
            if (_ctx.DraggableBindings.TryGetValue(element, out var draggableBinding))
            {
                DndDraggableDriver.Detach(element, draggableBinding, _ctx);
                _ctx.DraggableBindings.Remove(element);
            }
            if (_ctx.DroppableBindings.ContainsKey(element))
            {
                DndDroppableDriver.Detach(element, _ctx);
                _ctx.DroppableBindings.Remove(element);
            }
            if (_ctx.DragOverlayBindings.ContainsKey(element))
            {
                DndOverlayDriver.Detach(element, _ctx);
                _ctx.DragOverlayBindings.Remove(element);
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
        // Deliberately does NOT touch ReconcilerContext.SamePanelPortalBridges: a same-panel target's
        // synthetic-bubbling bridge (if this was a registry portal) stays attached even once this is
        // the last Portal on that target — see that field's own comment for why leaving it is both
        // correct (a harmless no-op) and simpler than reference-counting per-target attaches.
        private void CleanupPortal(VisualElement element)
        {
            if (!_ctx.PortalState.TryGetValue(element, out var portalInfo))
            {
                return;
            }

            _ctx.PortalState.Remove(element);
            // The RESOLVED target recorded at mount: the same removal works for registry, layer and
            // world-space portals, and it stays correct even if a registry id was re-registered to
            // a different element since (the children live where they were mounted). Null only for
            // the never-mounted missing-registry-target path (SlotLength 0 — nothing to remove).
            var target = portalInfo.Target;
            if (target == null)
            {
                return;
            }

            // PortalState.SlotStart is stored LOGICAL (ChildReconciler.DrainPendingPortalMounts /
            // FiberNodePatcher.PatchPortalChildren both keep it in that basis so Reconcile's own leading-offset
            // entry gate folds it exactly once) — this walk indexes target's children PHYSICALLY and never goes
            // through that entry gate, so it converts once here instead, re-deriving the live offset rather than
            // trusting a stale copy (see FiberZLayerCoordinator.LeadingOffset's own doc on why it must always be
            // read fresh).
            var physicalSlotStart = portalInfo.SlotStart + FiberZLayerCoordinator.LeadingOffset(target);
            var slotEnd = physicalSlotStart + portalInfo.SlotLength;
            if (slotEnd > target.childCount) slotEnd = target.childCount;
            for (var i = slotEnd - 1; i >= physicalSlotStart; i--)
            {
                var child = target.ElementAt(i);
                var poolable = PoolableOccupantOf(child);
                CleanupElement(child);
                target.RemoveAt(i);
                ReturnOccupantToPool(child, poolable);
            }

            // Surviving Portals on the same target whose slot starts after the removed range
            // collapse left by SlotLength so their next patch addresses the right DOM positions.
            PortalSlotTracker.ShiftSlotStartsAfter(_ctx.PortalState, target, portalInfo.SlotStart, -portalInfo.SlotLength);
        }

        // Destroys the framework-owned world-space host bound to a departing placeholder. Runs
        // AFTER CleanupPortal, which already removed and released the children living inside the
        // host's root — what remains is the host GameObject and its runtime-created panel assets.
        // Layer hosts are deliberately NOT torn down here: they are shared per layer and persist
        // until reconciler disposal.
        private void CleanupWorldSpaceHost(VisualElement element)
        {
            if (!_ctx.WorldSpaceBindings.TryGetValue(element, out var record))
            {
                return;
            }

            _ctx.WorldSpaceBindings.Remove(element);
            // Focus, if the host held any, was already rescued by RescueFocusFromWorldSpaceHost
            // before CleanupPortal removed this panel's content (see CleanupElementCore) — by this
            // point the host's FocusController.focusedElement is already null regardless, since UI
            // Toolkit clears it as soon as the focused element leaves the panel's visual tree.
            PanelHostFactory.Destroy(record);
        }

        // Tears down the real element owned by a departing z-layer placeholder — mirrors CleanupPortal: the
        // real element is not a DOM descendant of the placeholder (it lives in a layer container elsewhere
        // under the same stacking parent), so the ordinary CleanupDescendants walk below would never reach
        // it on its own. A no-op when element is not (or no longer) a z-layer placeholder.
        private void CleanupZLayerPlaceholder(VisualElement element)
        {
            var real = FiberZLayerCoordinator.TakeReal(_ctx, element);
            if (real == null)
            {
                return;
            }
            var poolable = PoolableOccupantOf(real);
            CleanupElement(real);
            // TakeReal already detached `real` from its layer container; nothing left to remove from the DOM.
            ReturnOccupantToPool(real, poolable);
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

                // A z-layer container's own members are z-managed REAL elements, each with a placeholder
                // sitting elsewhere in this SAME child list (both are direct children of `element` whenever
                // it is a stacking-context parent being torn down) — CleanupZLayerPlaceholder already reaches
                // and fully cleans up every member via its placeholder in this very loop. Descending into the
                // container here too would run the full CleanupElementResources + DisposeFibersUnder sequence
                // on each member a second time.
                if (FiberZLayerCoordinator.IsLayerContainer(child))
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
            // Must run BEFORE CleanupPortal: UI Toolkit clears FocusController.focusedElement the
            // moment the focused element leaves its panel's visual tree, which CleanupPortal's own
            // target.RemoveAt(i) triggers below — checking for a focused element afterward (as
            // CleanupWorldSpaceHost used to) always observes null by then.
            RescueFocusFromWorldSpaceHost(element);
            CleanupPortal(element);
            CleanupWorldSpaceHost(element);
            CleanupZLayerPlaceholder(element);
            CleanupDescendants(element, innerElement);
        }

        // If element is a world-space placeholder whose host panel currently holds focus, hands focus
        // back to the main panel before that panel's content (and soon its whole GameObject) is torn
        // down — otherwise focus would either dangle on an about-to-be-destroyed FocusController or
        // simply vanish, leaving keyboard input going nowhere until the app author notices.
        private void RescueFocusFromWorldSpaceHost(VisualElement element)
        {
            if (!_ctx.WorldSpaceBindings.TryGetValue(element, out var record)) return;
            var hostFocusController = record.Document != null ? record.Document.rootVisualElement?.panel?.focusController : null;
            if (hostFocusController?.focusedElement != null)
            {
                _ctx.MainPanelRoot?.Focus();
            }
        }
    }
}
