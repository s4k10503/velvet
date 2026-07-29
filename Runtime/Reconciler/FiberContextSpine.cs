#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    // Reconstructs the live context cursor (ComponentContextStack) for an ISOLATED
    // re-render of an inline-mounted fiber, then unwinds it.
    // Context values are read from a live cursor (ComponentContextStack) representing the
    // stack of enclosing Providers. A re-render that walks from the root keeps the cursor correct because
    // it re-pushes each enclosing Provider while clean subtrees bail out. Velvet instead
    // re-renders a setState / context-invalidated fiber in isolation (FiberWorkLoop.FlushState)
    // rather than re-walking from the root, so the cursor is empty at that point. This type performs the
    // "spine-rewalk-with-bailout" lazily: it walks the spine from the root down to the target fiber and,
    // for each ancestor, structurally re-walks that ancestor's COMMITTED output
    // (ComponentFiber.PreviousTree) to re-push exactly the Providers that enclose the next
    // spine fiber — WITHOUT re-running any ancestor body (that is the bailout: clean ancestors contribute
    // their committed Provider values for free). The target then renders with a correct live cursor and
    // Hooks.UseContext<T> reads the top of the stack.
    // The provider-enclosure search mirrors GeneralPathReconciler.ExpandInlineRecursive: position keys
    // for unkeyed ComponentNodes are per-identity counters within one reconcile scope (a fiber body
    // output, or one element's children reconcile), Fragment/Provider continue the current scope, an
    // element node opens a fresh scope for its children, and a Memo resolves to its committed inner.
    // The match against the registry's (parentFiber, positionKey, identity) key is what lets the
    // reconstruction recognize the spine child without re-rendering. Both walkers derive every key
    // through FiberKeying so the two stay in lockstep by construction.
    internal readonly struct FiberContextSpine
    {
        private readonly ComponentContextStack _stack;
        private readonly List<ContextProviderNode> _pushed;
        // Keys raw-pushed from a Portal-top fiber's enclosing-context snapshot (no ContextProviderNode to
        // pop). Always pushed before any _pushed Provider (the Portal edge is the spine's first), so per
        // context key the raw base sits under the Providers and unwinds last.
        private readonly List<object>? _rawPushedKeys;

        private FiberContextSpine(
            ComponentContextStack stack, List<ContextProviderNode> pushed, List<object>? rawPushedKeys)
        {
            _stack = stack;
            _pushed = pushed;
            _rawPushedKeys = rawPushedKeys;
        }

        // Pushes the Provider values that enclose target onto the live cursor and
        // returns a handle whose Unwind pops them in reverse. A no-op (empty handle) when
        // the fiber has no reconciler / no parent (a root re-render needs no reconstruction — it pushes
        // its own Providers during its own reconcile).
        internal static FiberContextSpine Push(ComponentFiber target)
        {
            var ctx = target.Reconciler?.Context;
            if (ctx == null || target.Parent == null) return default;

            // Build the spine root -> target's parent. Each entry's committed tree contributes the
            // Providers enclosing the NEXT entry down (or the target at the deepest entry).
            List<ComponentFiber>? spine = null;
            for (var f = target.Parent; f != null; f = f.Parent)
            {
                (spine ??= new List<ComponentFiber>()).Add(f);
            }
            if (spine == null) return default;
            spine.Reverse();

            var stack = ctx.ComponentContextStack;
            var pushed = new List<ContextProviderNode>();
            List<object>? rawPushedKeys = null;
            var registry = ctx.ComponentRegistry;
            var memoCache = ctx.FiberMemoCache;

            // For each spine edge (ancestor -> child), re-push the Providers in the ancestor's committed
            // tree that enclose `child`. The deepest ancestor encloses `target` itself.
            //
            // INLINE-mounted spine children match the canonical wrapper-less Component path: a
            // ComponentNode in the ancestor's committed tree resolves to `child` via the registry's
            // tree-position keying. WRAPPER-mounted spine children (an Outlet route Component or a
            // V.List item) are hosted by a wrapper-emitting node (OutletNode / AnimatePresenceNode)
            // whose container VE is the child's MountPoint; the providers enclosing that container in
            // the ancestor's committed tree are the ones that enclose the wrapper-mounted child. For
            // an Outlet, the routing layer's wrapper-local context (Depth+1 / OutletContext) is pushed
            // on top after the spine providers so the isolated re-render of the route observes the
            // same context the top-down mount walked under.
            for (var i = 0; i < spine.Count; i++)
            {
                var ancestor = spine[i];
                var child = i + 1 < spine.Count ? spine[i + 1] : target;

                // Detached-mount top-level child (a Portal's drained children, or a VirtualList's
                // controller-rendered items): the subtree mounted outside the parent-walked reconcile, so
                // `ancestor`'s committed tree either does not contain this child (Portal: parented off the
                // reconcile root) or hides it behind a wrapper-emitting leaf the canonical descent skips
                // (VirtualList). Rebuild from the captured snapshot instead: push the context that enclosed
                // the detached mount as the base, then — when descendant VNodes were captured (Portal) — walk
                // them to recover any Provider placed directly inside the subtree above this child. Deeper
                // spine edges (this child -> target) then push the in-subtree Providers normally on top.
                // Unwind is balanced per context key regardless of push origin (Pop discards the top), so the
                // raw base pushes here coexist correctly with the Provider pushes the other edges add.
                var detached = child.DetachedMountContext;
                if (detached != null)
                {
                    var snapshot = detached.EnclosingSnapshot;
                    if (snapshot != null)
                    {
                        for (var s = 0; s < snapshot.Count; s++)
                        {
                            stack.PushRaw(snapshot[s].Key, snapshot[s].Value);
                            (rawPushedKeys ??= new List<object>()).Add(snapshot[s].Key);
                        }
                    }
                    if (detached.DescendantNodes is { Length: > 0 } && detached.Anchor != null)
                    {
                        var detachedCounters = new Dictionary<object, int>();
                        PushEnclosingProviders(detached.DescendantNodes, detached.Anchor, child, detachedCounters,
                            fragmentKeyScope: null, stack, pushed, registry, memoCache, isInlineSpineChild: true);
                    }
                    continue;
                }

                var tree = ancestor.PreviousTree;
                if (tree == null || tree.Length == 0) continue;

                var isInline = registry.TryGetInlineKey(child, out _, out _);
                if (!isInline && child.MountPoint == null) continue;

                var counters = new Dictionary<object, int>();
                PushEnclosingProviders(tree, ancestor, child, counters, fragmentKeyScope: null,
                    stack, pushed, registry, memoCache, isInline);
            }

            return new FiberContextSpine(stack, pushed, rawPushedKeys);
        }

        // Pops every Provider this handle pushed, restoring the cursor. Safe on a default handle.
        internal void Unwind()
        {
            if (_pushed != null)
            {
                for (var i = _pushed.Count - 1; i >= 0; i--)
                {
                    _pushed[i].PopContext(_stack);
                }
            }
            // Pop the Portal snapshot base last: it was raw-pushed before every Provider, so per context key
            // it sits at the bottom of the stack and must come off after the Providers layered above it.
            if (_rawPushedKeys != null)
            {
                for (var i = _rawPushedKeys.Count - 1; i >= 0; i--)
                {
                    _stack.PopRaw(_rawPushedKeys[i]);
                }
            }
        }

        // Pushes an OutletNode's wrapper-local routing context (Depth+1 / OutletContext) onto the live
        // cursor. The top-down mount walks <Outlet/> with the matched route's depth pushed
        // live around the wrapper-mounted route Component's mount; an isolated re-render of the route
        // bypasses the Outlet's mount path, so this layer is reconstructed here on top of the enclosing
        // spine Providers. The current Depth value on the live cursor (= the enclosing layout's depth
        // pushed by the spine walk so far) plus one matches what the Outlet pushed at mount time without
        // re-resolving the match.
        private static void PushOutletWrapperLocalContext(
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            OutletNode outlet)
        {
            var depth = stack.Get(RouterContext.Depth) + 1;
            var depthProvider = new ContextProviderNode<int>
            {
                Context = RouterContext.Depth,
                Value = depth,
                Children = System.Array.Empty<VNode>(),
            };
            var outletProvider = new ContextProviderNode<object>
            {
                Context = RouterContext.OutletContext,
                Value = outlet.OutletContextValue!,
                Children = System.Array.Empty<VNode>(),
            };
            depthProvider.PushContext(stack);
            pushed.Add(depthProvider);
            outletProvider.PushContext(stack);
            pushed.Add(outletProvider);
        }

        // Walks nodes (one reconcile scope of ancestor's committed
        // output) depth-first, pushing each Provider before descending its children. Returns true once the
        // node that hosts spineChild is reached — its enclosing Providers stay pushed
        // (recorded in pushed). The host node is a ComponentNode resolving to
        // spineChild for inline-mounted children, or an OutletNode / AnimatePresenceNode
        // whose dynamic wrapper VE matches spineChild's MountPoint for wrapper-mounted
        // children. For an Outlet hosting a wrapper-mounted route Component, the routing layer's wrapper-local
        // context (Depth+1 / OutletContext) is pushed on top of the spine Providers so the isolated re-render
        // observes the same context the top-down mount walked under. Providers on a subtree that does NOT
        // contain the spine child are popped on the way out so only the path to spineChild
        // remains on the cursor.
        private static bool PushEnclosingProviders(
            VNode?[] nodes,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            Dictionary<object, int> counters,
            string? fragmentKeyScope,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            ComponentRegistry registry,
            FiberMemoCache memoCache,
            bool isInlineSpineChild)
        {
            for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                if (PushEnclosingProvidersForNode(nodes[nodeIndex], nodeIndex, ancestor, spineChild, counters,
                        fragmentKeyScope, stack, pushed, registry, memoCache, isInlineSpineChild))
                {
                    return true;
                }
            }
            return false;
        }

        // MotionNode derives from BaseElementNode, so its arm must stay ahead of the BaseElementNode one or
        // the MotionContext push below is silently skipped and a descendant Motion re-rendering in isolation
        // reads the default label.
        private static bool PushEnclosingProvidersForNode(
            VNode? node,
            int nodeIndex,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            Dictionary<object, int> counters,
            string? fragmentKeyScope,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            ComponentRegistry registry,
            FiberMemoCache memoCache,
            bool isInlineSpineChild)
        {
            switch (node)
            {
                case null:
                    return false;

                case FragmentNode fragment:
                {
                    if (fragment.Children == null) return false;
                    var childScope = FiberKeying.FragmentChildScope(
                        fragmentKeyScope, fragment.Key, nodeIndex);
                    return PushEnclosingProviders(fragment.Children, ancestor, spineChild, counters,
                        childScope, stack, pushed, registry, memoCache, isInlineSpineChild);
                }

                case ContextProviderNode provider:
                    return PushProviderSubtree(provider, nodeIndex, ancestor, spineChild, counters,
                        fragmentKeyScope, stack, pushed, registry, memoCache, isInlineSpineChild);

                case ComponentNode component when isInlineSpineChild:
                    return MatchesInlineSpineChild(component, ancestor, spineChild, counters, registry);

                case OutletNode outlet when !isInlineSpineChild:
                    return PushOutletHost(outlet, ancestor, spineChild, stack, pushed);

                case MemoNode memo:
                    return PushMemoInner(memo, nodeIndex, ancestor, spineChild, fragmentKeyScope,
                        stack, pushed, registry, memoCache, isInlineSpineChild);

                case SuspenseNode suspense:
                    return PushSuspenseSubtree(suspense, nodeIndex, ancestor, spineChild, fragmentKeyScope,
                        stack, pushed, registry, memoCache, isInlineSpineChild);

                case MotionNode motion:
                    return PushMotionSubtree(motion, ancestor, spineChild, stack, pushed, registry,
                        memoCache, isInlineSpineChild);

                case BaseElementNode element:
                {
                    // An element opens a fresh reconcile scope for its children (the host reconciles them
                    // via ReconcileChildren with its own position counters); FiberStack.Current stays
                    // `ancestor`, so a Component among them is still a direct child fiber of `ancestor`.
                    if (element.Children is not { Length: > 0 }) return false;
                    var elementCounters = new Dictionary<object, int>();
                    return PushEnclosingProviders(element.Children, ancestor, spineChild, elementCounters,
                        fragmentKeyScope: null, stack, pushed, registry, memoCache, isInlineSpineChild);
                }

                case AnimatePresenceNode presence:
                    return PushPresenceChildren(presence, nodeIndex, ancestor, spineChild, fragmentKeyScope,
                        stack, pushed, registry, memoCache, isInlineSpineChild);

                default:
                    // Reaching a wrapper-emitting leaf (Portal / VirtualList) here means it sits in this
                    // ancestor's committed tree but does NOT host the spine child being searched for. A
                    // detached mount that DID host it (a Portal's drained children, a VirtualList's items)
                    // is handled before this walk — see the DetachedMountContext branch in Push, which
                    // rebuilds the enclosing context from the captured snapshot instead of descending the
                    // host's tree. So skip it: its own subtree cannot enclose a spine child not inside it.
                    return false;
            }
        }

        private static bool PushProviderSubtree(
            ContextProviderNode provider,
            int nodeIndex,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            Dictionary<object, int> counters,
            string? fragmentKeyScope,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            ComponentRegistry registry,
            FiberMemoCache memoCache,
            bool isInlineSpineChild)
        {
            provider.PushContext(stack);
            pushed.Add(provider);
            if (provider.Children != null)
            {
                var providerScope = FiberKeying.ProviderChildScope(
                    fragmentKeyScope, provider.Key, nodeIndex);
                if (PushEnclosingProviders(provider.Children, ancestor, spineChild, counters,
                        providerScope, stack, pushed, registry, memoCache, isInlineSpineChild))
                {
                    return true;
                }
            }
            // Not on the path to spineChild: undo this Provider's push.
            provider.PopContext(stack);
            pushed.RemoveAt(pushed.Count - 1);
            return false;
        }

        // Inline-mounted ComponentNodes register under (ancestor, slotKey, identity) with the SAME
        // position-key scheme used by ExpandInlineRecursive. A match means we have descended exactly to
        // the spine child — its enclosing Providers are pushed, so stop. A sibling component is never
        // descended into: its own subtree's Providers do not enclose spineChild (a direct child fiber of
        // `ancestor`). The counter bump happens for every unkeyed ComponentNode either way, so the keying
        // stays in lockstep with the expansion walk.
        private static bool MatchesInlineSpineChild(
            ComponentNode component,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            Dictionary<object, int> counters,
            ComponentRegistry registry)
        {
            var identity = component.ResolvedIdentity;
            var slotKey = component.Key ?? FiberKeying.ResolveInlinePositionKey(counters, identity, registry.InlinePositionKeyBoxes);
            var resolved = registry.TryGetFiberForInlineKey(ancestor, slotKey, identity);
            return ReferenceEquals(resolved, spineChild);
        }

        // Wrapper-mounted spine child: an OutletNode hosts a wrapper-mounted route Component whose
        // MountPoint is the Outlet's container VE. Before pushing the Outlet's wrapper-local context,
        // verify that spineChild is actually hosted by an Outlet (not by a sibling
        // AnimatePresence/Portal/VirtualList) by consulting ReconcilerContext.OutletContainers — a
        // structural identity register populated at FiberNodeFactory mount time, immune to USS class
        // manipulation. Without this check, a layout like V.Div(V.Outlet(), V.AnimatePresence(...)) would
        // walker-match the first Outlet and push a wrong Depth+1 context for any non-Outlet
        // wrapper-mounted sibling's isolated re-render.
        private static bool PushOutletHost(
            OutletNode outlet,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed)
        {
            var spineHost = spineChild.MountPoint;
            if (spineHost == null || !ancestor.Reconciler!.Context.OutletContainers.Contains(spineHost))
            {
                // spineChild is not Outlet-hosted; the Outlet here is a sibling — keep searching for the
                // actual wrapper host.
                return false;
            }
            PushOutletWrapperLocalContext(stack, pushed, outlet);
            return true;
        }

        private static bool PushMemoInner(
            MemoNode memo,
            int nodeIndex,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            string? fragmentKeyScope,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            ComponentRegistry registry,
            FiberMemoCache memoCache,
            bool isInlineSpineChild)
        {
            var memoScope = FiberKeying.MemoScope(fragmentKeyScope, nodeIndex);
            var cacheKey = FiberKeying.MemoCacheKey(memo.Key, memoScope);
            if (!memoCache.TryPeek(cacheKey, out var inner) || inner == null) return false;

            var innerCounters = new Dictionary<object, int>();
            return PushEnclosingProviders(new[] { inner }, ancestor, spineChild, innerCounters,
                memoScope, stack, pushed, registry, memoCache, isInlineSpineChild);
        }

        // Follow whichever subtree (children vs fallback) was committed last render so the
        // reconstruction matches the live DOM/fiber layout.
        private static bool PushSuspenseSubtree(
            SuspenseNode suspense,
            int nodeIndex,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            string? fragmentKeyScope,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            ComponentRegistry registry,
            FiberMemoCache memoCache,
            bool isInlineSpineChild)
        {
            var suspenseKey = FiberKeying.SuspenseKey(fragmentKeyScope, suspense.Key, nodeIndex);
            var wasFallback = ancestor.Reconciler != null
                && ancestor.Reconciler.Context.SuspenseFallbackShown.TryGetValue(
                    (ancestor, suspenseKey), out var shown) && shown;
            var sub = wasFallback
                ? (suspense.Fallback != null ? new[] { suspense.Fallback } : System.Array.Empty<VNode>())
                : (suspense.Children ?? System.Array.Empty<VNode>());
            if (sub.Length == 0) return false;

            var subScope = FiberKeying.SuspenseSubtreeScope(suspenseKey, wasFallback);
            var subCounters = new Dictionary<object, int>();
            return PushEnclosingProviders(sub, ancestor, spineChild, subCounters,
                subScope, stack, pushed, registry, memoCache, isInlineSpineChild);
        }

        // A MotionNode establishes MotionContext for its subtree, exactly as a Provider establishes its
        // context. Re-push the effective label (own Animate, else the inherited top) so a descendant Motion
        // re-rendering in isolation reads the ancestor's active label instead of dropping to the default.
        // Mirrors PushProviderSubtree (push, descend its fresh child scope, pop if the spine child is not
        // beneath it).
        private static bool PushMotionSubtree(
            MotionNode motion,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            ComponentRegistry registry,
            FiberMemoCache memoCache,
            bool isInlineSpineChild)
        {
            var motionLabel = motion.Animate ?? stack.Get(MotionContext.ActiveLabel);
            var motionProvider = new ContextProviderNode<string>
            {
                Context = MotionContext.ActiveLabel,
                Value = motionLabel,
                Children = System.Array.Empty<VNode>(),
            };
            motionProvider.PushContext(stack);
            pushed.Add(motionProvider);
            if (motion.Children is { Length: > 0 })
            {
                var motionCounters = new Dictionary<object, int>();
                if (PushEnclosingProviders(motion.Children, ancestor, spineChild, motionCounters,
                        fragmentKeyScope: null, stack, pushed, registry, memoCache, isInlineSpineChild))
                {
                    return true;
                }
            }
            motionProvider.PopContext(stack);
            pushed.RemoveAt(pushed.Count - 1);
            return false;
        }

        // AnimatePresence is DOM-less: GeneralPathReconciler.ExpandAnimatePresenceInline expands each
        // keyed child directly into the parent's slot range, each under its own PresenceChildScope
        // with a FRESH position counter (EmitPresenceChild rents one per child). A wrapper-hosted
        // spine child therefore registers under THIS ancestor by its own slotKey, exactly like an
        // inline child — the only reason the canonical descent misses it is that the walker never
        // steps THROUGH the AnimatePresenceNode. Descend into each current child (mirroring
        // BuildKeyedMapCopy's keying) so the ComponentNode arm matches the spine child and the
        // enclosing Providers / MotionContext stay pushed. Only currently-present children are
        // walked (not still-exiting ghosts): the covered case is an isolated re-render of a
        // persisting descendant. Mirrors the Suspense arm (descend the committed subtree).
        private static bool PushPresenceChildren(
            AnimatePresenceNode presence,
            int nodeIndex,
            ComponentFiber ancestor,
            ComponentFiber spineChild,
            string? fragmentKeyScope,
            ComponentContextStack stack,
            List<ContextProviderNode> pushed,
            ComponentRegistry registry,
            FiberMemoCache memoCache,
            bool isInlineSpineChild)
        {
            if (presence.Children == null) return false;

            var presenceScope = FiberKeying.PresenceKey(fragmentKeyScope, presence.Key, nodeIndex);
            var autoIndex = 0;
            foreach (var child in presence.Children)
            {
                if (child == null || child is FragmentNode) continue;
                var childKey = child.Key ?? FiberNodeFactory.AutoKeyPrefix + autoIndex++;
                var childScope = FiberKeying.PresenceChildScope(presenceScope, childKey);
                var childCounters = new Dictionary<object, int>();
                if (PushEnclosingProviders(new[] { child }, ancestor, spineChild, childCounters,
                        childScope, stack, pushed, registry, memoCache, isInlineSpineChild))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
