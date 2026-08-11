using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Within the Reconciler, manages creation / reuse / disposal of ComponentNode.
    // The cache key is (VisualElement anchor, object slotKey, object identity). Two
    // mounting modes share this key:
    //   inline-mounted: anchor is the parent VE shared with sibling fibers,
    //   slotKey is the explicit ComponentNode.Key or a position counter
    //   computed per (parent, identity), and the fiber's rendered output occupies a
    //   sub-range of anchor.children;
    //   wrapper-mounted: anchor is a dedicated wrapper VE unique to the
    //   fiber, slotKey is null. Retained for Suspense and Outlet route mounts
    //   that require a single anchor for pending-fiber lookup.
    // Identity prefers ComponentNode.Identity, and falls back to
    // Body.Method (the function component's MethodInfo) when not specified.
    internal sealed class ComponentRegistry : IDisposable
    {
        // Inline-mounted fibers (function components emit no host node) anchor on their
        // parent ComponentFiber by tree position. The host VE does not exist when the fiber is matched
        // during begin-work, so identity is (parent fiber, position key, component identity) — never a
        // VisualElement. positionKey is the tree-position scope key, identity is ComponentNode.ResolvedIdentity.
        private readonly Dictionary<(ComponentFiber? parentFiber, object? positionKey, object identity), ComponentFiber> _inlineInstances = new();
        // O(1) reverse index inline fiber → its key, so disposal removes the entry without scanning.
        private readonly Dictionary<ComponentFiber, (ComponentFiber? parentFiber, object? positionKey, object identity)> _inlineFiberToKey = new();
        // O(1) forward index parent fiber → its inline child fibers, so subtree disposal locates them
        // without scanning _inlineInstances (and without the re-entrant dictionary-mutation hazard when
        // FiberRenderer.Dispose recursively triggers disposal during descendant cleanup).
        private readonly Dictionary<ComponentFiber, HashSet<ComponentFiber>> _parentToInlineFibers = new();
        // Interns the boxed (identity, index) position key that keys an unkeyed inline ComponentNode in
        // _inlineInstances. The walkers that derive these keys (ExpandInlineRecursive, the context
        // spine-rewalk) reuse one box per position instead of allocating a fresh box on every pass.
        // Sharing is equality-safe because position keys are only ever compared by content, never by
        // reference. Bounded by (distinct identities x sibling count); cleared in Dispose like the indexes.
        internal Dictionary<(object identity, int index), object> InlinePositionKeyBoxes { get; } = new();

        // Wrapper-mounted fibers (Velvet divergence: Outlet route mounts / V.List items own a dedicated
        // wrapper VE) anchor on that VisualElement. identity disambiguates an identity swap on the same
        // wrapper (see RemoveIfDifferentIdentity).
        private readonly Dictionary<VisualElement, ComponentFiber> _wrapperIndex = new();
        private readonly Dictionary<ComponentFiber, (VisualElement wrapper, object identity)> _wrapperFiberInfo = new();
        private readonly ReconcilerContext _ctx;

        internal ComponentRegistry(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Where one GetOrCreate call anchors its fiber and where that fiber's output lands. Anchor and
        // PositionKey are the identity half — what the registry looks the fiber up by — and MountPoint and
        // SlotStart the placement half; IsInline selects which spelling of both is in force, so the five are
        // never meaningful apart. Taken by `in`: this runs once per Component per reconcile.
        private readonly struct FiberMountSite
        {
            internal object? Anchor { get; init; }
            internal object? PositionKey { get; init; }
            internal VisualElement? MountPoint { get; init; }
            internal int SlotStart { get; init; }
            internal bool IsInline { get; init; }
        }

        // Wrapper-mounted GetOrCreate. The fiber owns the entire wrapper's
        // children; slotKey is null and the wrapper is registered in
        // _wrapperIndex for pending-fiber lookup. slotStart is unused on this path.
        public ComponentFiber GetOrCreate(
            ComponentNode node,
            VisualElement wrapper)
        {
            var site = new FiberMountSite
            {
                Anchor = wrapper,
                PositionKey = null,
                MountPoint = wrapper,
                SlotStart = 0,
                IsInline = false,
            };
            return GetOrCreateCore(node, in site);
        }

        // Fiber identity is anchored on its parent parentFiber
        // by tree position (positionKey) — independent of any VisualElement, so the same
        // fiber resolves regardless of which host container its output lands in (a child
        // fiber's identity is (parent fiber, key, position)). The fiber's rendered output occupies the
        // sub-range starting at slotStart of mountPoint.children.
        // On the existing-fiber path, the slot start is updated and the fiber is synchronously re-rendered
        // when props changed or it is dirty (setState / context invalidation) so the caller observes the
        // fresh ComponentFiber.PreviousTree; otherwise the previous tree is reused (the
        // bailout path).
        public ComponentFiber GetOrCreateInline(
            ComponentNode node,
            ComponentFiber? parentFiber,
            object positionKey,
            VisualElement? mountPoint,
            int slotStart)
        {
            if (positionKey == null) throw new ArgumentNullException(nameof(positionKey));
            var site = new FiberMountSite
            {
                Anchor = parentFiber,
                PositionKey = positionKey,
                MountPoint = mountPoint,
                SlotStart = slotStart,
                IsInline = true,
            };
            return GetOrCreateCore(node, in site);
        }

        private ComponentFiber GetOrCreateCore(ComponentNode node, in FiberMountSite site)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node.Body == null) throw new ArgumentException("ComponentNode.Body must not be null.", nameof(node));
            var identity = node.ResolvedIdentity;

            var existingFiber = site.IsInline
                ? LookupInline((ComponentFiber)site.Anchor!, site.PositionKey, identity)
                : LookupWrapper((VisualElement)site.Anchor!, identity);

            if (existingFiber != null)
            {
                return ReconcileExistingFiber(existingFiber, node, in site);
            }

            return CreateAndMountFiber(node, identity, in site);
        }

        private ComponentFiber ReconcileExistingFiber(
            ComponentFiber existingFiber,
            ComponentNode node,
            in FiberMountSite site)
        {
            // Context is read live from the cursor (ComponentContextStack) during
            // Render, so no per-fiber snapshot is pinned or merged here. The enclosing Providers
            // are already pushed on the live stack by the current expansion walk; an isolated
            // re-render reconstructs them via FiberContextSpine.
            // The forwarded ref is part of the component's identity, not its props: a memoized component
            // would otherwise bail on shallow-equal props and swallow a ref-instance change, so the new
            // ref never re-runs UseImperativeHandle. Capture the identity change before overwriting and
            // force a re-render below so RunImperativeHandleSlots adopts the new ref.
            var refChanged = !ReferenceEquals(existingFiber.ExternalRef, node.ExternalRef);
            existingFiber.ExternalRef = node.ExternalRef;
            // V.Component<TProps> overload allocates a fresh closure each render that captures
            // the latest props; sync Body so the next render sees the current values.
            existingFiber.Body = node.Body;
            // Only a memoized component bails a parent-driven re-render on
            // shallow-equal props. A plain component re-renders whenever its parent does, so the
            // props-equality bail is gated on opt-in memoization. Memoization is opted into either by
            // [Component(Memoize = true)] (node.Memoize) or by V.Memo (custom areEqual, node.AreEqual).
            // When NOT memoized, props are treated as changed so the component always re-renders.
            bool isMemoized = node.Memoize || node.AreEqual != null;
            bool propsChanged;
            if (isMemoized)
            {
                // Bail key: shallow per-property identity comparison of props (or a
                // custom areEqual predicate supplied via V.Memo). Record value-equality (Equals) is
                // intentionally NOT used — props are records whose deep structural equality is a
                // different memoization axis than the shallow per-key identity comparison.
                propsChanged = !PropsEqual(existingFiber.Props, node.Props, node.AreEqual);
            }
            else
            {
                // Non-memo component: a parent re-render always re-renders the child,
                // so the props bail never applies. Props are still synced below for the next render.
                propsChanged = true;
            }
            if (propsChanged) existingFiber.Props = node.Props;

            if (site.IsInline)
            {
                // The parent reconcile may have shifted this fiber's slot range; update before any
                // re-render. Synchronously re-render when props changed or the fiber is dirty
                // (setState / async resolve / context invalidation) so the caller's parent walk
                // observes the fresh PreviousTree. The shared ReconcilerContext keeps the registry /
                // FiberStack consistent across the root and descendant Reconcilers, so a synchronous
                // render here does not collide with a subsequent FlushState pass: clearing IsDirty
                // makes the later traversal short-circuit while the rendered output is already committed.
                existingFiber.MountSlotStart = site.SlotStart;
                // Rewritten on the carry as well as at creation, and before the re-render below: a component
                // can leave a Portal's children for a position outside them in the very render that removes
                // the Portal, and GeneralPathReconciler carries it there in the inline walk that precedes
                // the removals — so a stamp written only at creation would still name the departing Portal
                // when that Portal's teardown disposes by it.
                existingFiber.OwningPortalPlaceholder = _ctx.CurrentPortalPlaceholder;
                if (propsChanged || existingFiber.IsDirty || refChanged)
                {
                    // Subsumes the child into the parent's single batch pass: the re-render runs with the
                    // child's latest state, satisfying the lanes pending before it at once (lane
                    // coalescing), and the settle un-enrols those and drops the fiber from the batch
                    // scheduler so a later drain does not redundantly re-render it or silently drop a
                    // stranded higher-priority lane — merely clearing IsDirty would leave both behind.
                    FiberRenderer.SubsumeFiberIntoThisPass(existingFiber);
                }
            }
            else if (propsChanged || refChanged)
            {
                // Wrapper-mounted path keeps the legacy async scheduling.
                FiberWorkLoop.RequestRenderFromHook(existingFiber);
            }
            return existingFiber;
        }

        private ComponentFiber CreateAndMountFiber(ComponentNode node, object identity, in FiberMountSite site)
        {
            var anchor = site.Anchor;
            var positionKey = site.PositionKey;
            var isInline = site.IsInline;
            var fiber = FiberRenderer.CreateChild(node.Body, node.IsErrorBoundary);
            _ctx.FiberStack.Current?.AppendChild(fiber);
            fiber.ExternalRef = node.ExternalRef;
            fiber.Props = node.Props;

            try
            {
                if (isInline)
                {
                    FiberRenderer.MountInline(fiber, site.MountPoint, site.SlotStart, _ctx);
                }
                else
                {
                    FiberRenderer.Mount(fiber, site.MountPoint, _ctx);
                }
            }
            catch (FiberSuspendSignal)
            {
                RegisterFiber(anchor, positionKey, identity, fiber, isInline);
                throw;
            }
            // Dispose BEFORE any detach: Dispose's Unmount retires the committed tree with the parent
            // chain intact (the recycle sweep marks ancestor-held memoized roots through it) and then
            // detaches itself; detaching first would sever the chain and let the sweep recycle nodes
            // a surviving ancestor's slot still owes to a comeback render.
            catch { FiberRenderer.Dispose(fiber); throw; }
            RegisterFiber(anchor, positionKey, identity, fiber, isInline);
            return fiber;
        }

        // Props-bail comparison, reached only for memoized nodes (see ReconcileExistingFiber's isMemoized gate).
        // With a custom areEqual predicate (V.Memo(component, props, areEqual)) the predicate decides equality;
        // otherwise props are compared by shallow per-property identity. Both-null props (a memoized props-less
        // overload) are trivially equal, so such a component bails unless dirty. Non-memo components never reach
        // here: their props are unconditionally treated as changed so a parent re-render re-renders the child.
        private static bool PropsEqual(object? prev, object? next, Func<object?, object?, bool>? areEqual)
        {
            if (areEqual != null)
            {
                return areEqual(prev, next);
            }
            return ComponentPropsComparer.ShallowEquals(prev, next);
        }

        private ComponentFiber? LookupInline(ComponentFiber? parentFiber, object? positionKey, object identity)
            => _inlineInstances.TryGetValue((parentFiber, positionKey, identity), out var fiber) ? fiber : null;

        private ComponentFiber? LookupWrapper(VisualElement? wrapper, object identity)
        {
            if (wrapper == null) return null;
            if (!_wrapperIndex.TryGetValue(wrapper, out var fiber)) return null;
            return _wrapperFiberInfo.TryGetValue(fiber, out var info) && Equals(info.identity, identity) ? fiber : null;
        }

        private void RegisterFiber(
            object? anchor, object? positionKey, object identity,
            ComponentFiber fiber, bool isInline)
        {
            if (isInline)
            {
                var parentFiber = (ComponentFiber)anchor!;
                var key = (parentFiber, positionKey, identity);
                _inlineInstances[key] = fiber;
                _inlineFiberToKey[fiber] = key;
                // Written from the same source on both halves of GetOrCreate — see the carry half in
                // ReconcileExistingFiber for why one write cannot serve.
                fiber.OwningPortalPlaceholder = _ctx.CurrentPortalPlaceholder;
                // Top-level inline fibers (root components mounted with no enclosing component fiber)
                // have parentFiber == null. They are disposed via the reconcile orphan sweep and the
                // registry's full Dispose, not via parent-fiber cascade, so they are not grouped here.
                // _parentToInlineFibers is keyed by a non-null parent fiber for cascade disposal only.
                if (parentFiber != null)
                {
                    if (!_parentToInlineFibers.TryGetValue(parentFiber, out var siblings))
                    {
                        siblings = new HashSet<ComponentFiber>();
                        _parentToInlineFibers[parentFiber] = siblings;
                    }
                    siblings.Add(fiber);
                }
            }
            else
            {
                var wrapper = (VisualElement)anchor!;
                _wrapperIndex[wrapper] = fiber;
                _wrapperFiberInfo[fiber] = (wrapper, identity);
            }
        }

        private void UnregisterFiber(ComponentFiber fiber)
        {
            // A wrapper-less Suspense makes the fiber rendering it the boundary. When that fiber
            // unmounts while still suspended, prune its boundary state so no dangling fiber
            // reference survives in the context-level Suspense registries (they are keyed by the
            // boundary fiber).
            _ctx.PruneSuspenseBoundaryState(fiber);
            // Same rationale for DOM-less AnimatePresence: the rendering fiber is the boundary that keys
            // its presence state, so prune it when the fiber unmounts (e.g. while a child is exiting).
            _ctx.PrunePresenceBoundaryState(fiber);

            if (_inlineFiberToKey.TryGetValue(fiber, out var key))
            {
                _inlineFiberToKey.Remove(fiber);
                _inlineInstances.Remove(key);
                if (key.parentFiber != null && _parentToInlineFibers.TryGetValue(key.parentFiber, out var siblings))
                {
                    siblings.Remove(fiber);
                    if (siblings.Count == 0) _parentToInlineFibers.Remove(key.parentFiber);
                }
                return;
            }
            if (_wrapperFiberInfo.TryGetValue(fiber, out var info))
            {
                _wrapperFiberInfo.Remove(fiber);
                _wrapperIndex.Remove(info.wrapper);
            }
        }

        // Disposes an inline-mounted fiber identified by reference and removes its registry entry.
        // Called by GeneralPathReconciler.SweepOrphans when its per-call old/new fiber-set diff detects
        // an orphan (ComponentNode present on the old side but absent on the new side). The orphan
        // fiber's rendered VEs were already removed from anchor.children by the reconcile's
        // Remove ops; this method releases fiber-side resources (effects, ref cleanups, hook state).
        // The post-guard teardown triplet shared by the orphan-dispose paths. For inline-mounted orphans, the
        // parent reconcile has already settled the final state of anchor.children within this fiber's old slot
        // range — including reusing the orphan's emitted VEs in place when CanPatch=true (PatchNode rebound
        // them to the new owner). Letting Unmount run its own Reconcile(MountPoint, PreviousTree, []) would
        // then re-remove those VEs and pull the new owner's output out of the DOM, so the orphan's PreviousTree
        // is cleared first to suppress the DOM-side cleanup pass. Callers keep their own pre-guards (null /
        // already-disposed) — those differ per site and must stay outside this helper.
        private void DisposeFiberInternal(ComponentFiber fiber)
        {
            if (fiber.IsInlineMounted && fiber.PreviousTree != null)
            {
                // Retire the tree's pooled objects HERE: nulling PreviousTree suppresses Unmount's
                // DOM-side cleanup pass (correct — see above) but also its pool return, and no other
                // path ever reaches this baseline again. Sweeping while the fiber's slots and parent
                // chain are still intact keeps memo-shared nodes protected; the pass-scoped release
                // staging keeps the rest un-rentable while the enclosing reconcile still reads its
                // old-side captures of these nodes.
                var retired = fiber.PreviousTree;
                fiber.PreviousTree = null;
                FiberTreeReturn.ReturnRetiredTree(retired, fiber);
            }
            FiberRenderer.Dispose(fiber);
            UnregisterFiber(fiber);
        }

        internal void DisposeAndRemove(ComponentFiber fiber)
        {
            if (fiber == null) return;
            DisposeFiberInternal(fiber);
        }

        // Inline-mounted lookup by tree-position key (parent fiber, position key, identity). Used by
        // the old-side walk in GeneralPathReconciler to read the previously rendered
        // ComponentFiber.PreviousTree without triggering a re-render.
        internal ComponentFiber? TryGetFiberForInlineKey(ComponentFiber? parentFiber, object positionKey, object identity)
        {
            return _inlineInstances.TryGetValue((parentFiber, positionKey, identity), out var fiber) ? fiber : null;
        }

        // Returns the inline registration key (positionKey, identity) under which
        // fiber is registered, or false when it is not an inline-mounted
        // fiber (root / wrapper-mounted). Used by FiberContextSpine to recognize a spine
        // child while structurally walking an ancestor's committed tree, so the Providers that enclose
        // that child can be re-pushed onto the live cursor for an isolated re-render.
        internal bool TryGetInlineKey(ComponentFiber fiber, out object? positionKey, out object? identity)
        {
            if (fiber != null && _inlineFiberToKey.TryGetValue(fiber, out var key))
            {
                positionKey = key.positionKey;
                identity = key.identity;
                return true;
            }
            positionKey = null;
            identity = null;
            return false;
        }

        // Disposes every fiber — inline-mounted or wrapper-mounted — whose anchor VE sits inside one of
        // orphanRoots (the root counts as inside itself). For inline fibers the anchor
        // is ComponentFiber.MountPoint; for wrapper-mounted fibers it is the wrapper VE
        // recorded in _wrapperFiberInfo. Used by the Suspense primary rollback path: a created
        // orphan container is dropped (GC) but its CreateElement step reconciled the container's children,
        // which may have registered inline AND/OR wrapper-mounted (Outlet route, AnimatePresence child,
        // etc.) Component fibers under it. Those fibers' anchor VE becomes dangling once the orphan is
        // dropped: queued deferred layout effects would fire against a dead VE, and wrapper entries would
        // linger to confuse later identity checks. Disposing here releases registry entries, fires any
        // cleanup that has been registered (setups that never ran have none, since a suspended primary
        // subtree that never committed has no effects to clean up), and short-circuits the deferred layout-effect drain
        // via ComponentFiber.IsDisposed. Iterates snapshots because
        // FiberRenderer.Dispose mutates the registry through child cleanup cascades.
        internal void DisposeFibersUnder(HashSet<VisualElement> orphanRoots)
        {
            if (orphanRoots == null || orphanRoots.Count == 0) return;
            if (_inlineFiberToKey.Count == 0 && _wrapperFiberInfo.Count == 0) return;
            ComponentFiber[]? snapshot = null;
            var snapshotCount = 0;
            var capacity = _inlineFiberToKey.Count + _wrapperFiberInfo.Count;
            foreach (var entry in _inlineFiberToKey)
            {
                var fiber = entry.Key;
                if (fiber == null || fiber.IsDisposed) continue;
                if (!IsInsideAny(fiber.MountPoint, orphanRoots)) continue;
                snapshot ??= new ComponentFiber[capacity];
                snapshot[snapshotCount++] = fiber;
            }
            foreach (var entry in _wrapperFiberInfo)
            {
                var fiber = entry.Key;
                if (fiber == null || fiber.IsDisposed) continue;
                if (!IsInsideAny(entry.Value.wrapper, orphanRoots)) continue;
                snapshot ??= new ComponentFiber[capacity];
                snapshot[snapshotCount++] = fiber;
            }
            if (snapshot == null) return;
            for (var i = 0; i < snapshotCount; i++)
            {
                var fiber = snapshot[i];
                if (fiber.IsDisposed) continue;
                DisposeFiberInternal(fiber);
            }
        }

        // Single-root convenience over DisposeFibersUnder(HashSet). Element teardown
        // (FiberElementCleaner.CleanupElement) calls this when a subtree leaves the DOM: an inline-mounted
        // fiber nested under a host element anchors on its PARENT FIBER (not the host VE), so Remove(VE) —
        // which only knows wrapper-mounted fibers — never reaches it, and the host's owning reconcile emits
        // that host as an opaque leaf, so the orphan sweep does not collect it either. When the host is torn
        // down out-of-band (type-swap, Portal unmount, VirtualList recycle, presence drop) neither path runs,
        // leaving the fiber to be re-paired as a zombie on a same-key re-entry. Disposing by MountPoint
        // containment here closes that gap. The lazy set alloc is skipped when the registry holds no fibers.
        internal void DisposeFibersUnder(VisualElement root)
        {
            if (root == null) return;
            if (_inlineFiberToKey.Count == 0 && _wrapperFiberInfo.Count == 0) return;
            DisposeFibersUnder(new HashSet<VisualElement> { root });
        }

        // The containment form above cannot express this selection: the fiber is a sibling of the elements
        // a portal teardown removes rather than a descendant of any of them, so the parent-walk steps
        // straight past it (ComponentFiber.OwningPortalPlaceholder owns why the fiber is stamped rather
        // than located). Selecting by MountPoint alone would be wrong in the other direction — one target
        // carries the ranges of several Portals plus whatever rendered the target itself.
        // FiberElementCleaner.CleanupPortal owns the call.
        internal void DisposeInlineFibersOwnedByPortal(VisualElement placeholder)
        {
            if (placeholder == null || _inlineFiberToKey.Count == 0) return;
            List<ComponentFiber>? doomed = null;
            foreach (var entry in _inlineFiberToKey)
            {
                var fiber = entry.Key;
                if (fiber == null || fiber.IsDisposed) continue;
                if (!ReferenceEquals(fiber.OwningPortalPlaceholder, placeholder)) continue;
                (doomed ??= new List<ComponentFiber>()).Add(fiber);
            }
            if (doomed == null) return;
            // A fiber mounted by a stamped fiber's ISOLATED re-render carries no stamp: no Portal children
            // reconcile was in flight to name one. It is reached through the parent index rather than given
            // a stamp copied from its parent's, so nothing has to keep that copy current as the parent
            // moves — the registry key pins a fiber to one parent for its whole life, which is what makes
            // this select exactly the subtree that goes with each already-doomed fiber.
            var reached = new HashSet<ComponentFiber>(doomed);
            for (var i = 0; i < doomed.Count; i++)
            {
                if (!_parentToInlineFibers.TryGetValue(doomed[i], out var children)) continue;
                foreach (var child in children)
                {
                    if (child.IsDisposed || !reached.Add(child)) continue;
                    doomed.Add(child);
                }
            }
            // Snapshotted first: DisposeFiberInternal mutates the dictionary this walk reads, through
            // its own child cleanup cascades as well as its own unregister.
            foreach (var fiber in doomed)
            {
                if (fiber.IsDisposed) continue;
                DisposeFiberInternal(fiber);
            }
        }

        private static bool IsInsideAny(VisualElement? mountPoint, HashSet<VisualElement> roots)
        {
            for (var ve = mountPoint; ve != null; ve = ve.parent)
            {
                if (roots.Contains(ve)) return true;
            }
            return false;
        }

        // Disposes the wrapper-mounted fiber bound to wrapper when its identity
        // differs from newIdentity. Inline-mounted fibers anchor on their parent
        // fiber (not a VE) and are not eligible for identity-based eviction here.
        // Invariant: at most one fiber is bound per wrapper VE — _wrapperIndex[wrapper] is
        // overwritten on each register so the prior identity is evicted before a new one lands.
        public bool RemoveIfDifferentIdentity(VisualElement wrapper, object newIdentity)
        {
            if (!_wrapperIndex.TryGetValue(wrapper, out var fiber)) return false;
            if (!_wrapperFiberInfo.TryGetValue(fiber, out var info)) return false;
            if (Equals(info.identity, newIdentity)) return false;

            FiberRenderer.Dispose(fiber);
            UnregisterFiber(fiber);
            return true;
        }

        // Disposes the wrapper-mounted fiber bound to wrapper, if any. Inline-mounted
        // fibers anchor on their parent fiber (the host VE is not their anchor in the depth-first model),
        // so they are disposed by the reconcile orphan sweep (a ComponentNode present on the old side but
        // absent on the new side) via DisposeAndRemove, not by VE removal.
        // FiberRenderer.Dispose is idempotent.
        public void Remove(VisualElement wrapper)
        {
            if (_wrapperIndex.TryGetValue(wrapper, out var wrapperFiber))
            {
                FiberRenderer.Dispose(wrapperFiber);
                UnregisterFiber(wrapperFiber);
            }
        }

        public void Dispose()
        {
            var total = _inlineInstances.Count + _wrapperIndex.Count;
            if (total == 0) return;
            // Snapshot the fiber set before any Dispose runs. FiberRenderer.Dispose may reach back
            // into the registry via descendant cleanup, which mutates these indexes; iterating a live
            // dictionary here would throw InvalidOperationException on the next MoveNext. Re-entrant
            // cleanup may have disposed some fibers already by the time the snapshot loop reaches them;
            // FiberRenderer.Dispose is idempotent (IsDisposed early-return), so the loop is safe
            // without an explicit ContainsKey guard.
            var snapshot = new ComponentFiber[total];
            _inlineInstances.Values.CopyTo(snapshot, 0);
            _wrapperIndex.Values.CopyTo(snapshot, _inlineInstances.Count);
            foreach (var fiber in snapshot)
            {
                FiberRenderer.Dispose(fiber);
            }
            // Bulk clear is intentional: any nested Remove during the snapshot loop has already
            // partially mutated the indexes, but the registry is going away wholesale so the
            // remaining entries (if any) are unreachable. Contract: every index defined on this
            // class must be cleared here. Adding a new index without updating this block leaks.
            _inlineInstances.Clear();
            _inlineFiberToKey.Clear();
            _parentToInlineFibers.Clear();
            _wrapperIndex.Clear();
            _wrapperFiberInfo.Clear();
            InlinePositionKeyBoxes.Clear();
        }
    }
}
