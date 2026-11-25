#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

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
    // for each ancestor, structurally re-walks that ancestor's output (SpineWalk.OwnOutput says which
    // array, and when a detached mount's captured nodes stand in for it) to re-push exactly the Providers
    // that enclose the next spine fiber — WITHOUT re-running any ancestor body (that is the bailout). The
    // target then renders with a correct live cursor and
    // Hooks.UseContext<T> reads the top of the stack.
    // The provider-enclosure search mirrors GeneralPathReconciler.ExpandInlineRecursive: an unkeyed
    // ComponentNode's position key is its slot in the child array it sits in under that array's
    // WalkPosition, Fragment/Provider extend the current position, and an element node opens a fresh walk
    // for its children.
    // The registry's key narrows the reconstruction to the positions this fiber could occupy; it does
    // not on its own name which one, since it answers at every container holding that position.
    // MatchesInlineSpineChild owns what separates them. Both walkers derive every key through
    // FiberKeying so the two stay in lockstep by construction.
    internal readonly struct FiberContextSpine
    {
        private readonly ComponentContextStack _stack;
        private readonly List<ContextProviderNode> _pushed;
        // Keys raw-pushed from a stamp's snapshot and at a detached edge (no ContextProviderNode to pop). They can sit
        // between Providers of the same context pushed by other edges; Unwind pops every one of them, and a pop
        // discards a top whoever pushed it, so the cursor comes back to where it was.
        private readonly List<object>? _rawPushedKeys;
        private readonly ReconcilerBufferPool _bufferPool;

        private FiberContextSpine(
            ComponentContextStack stack, List<ContextProviderNode> pushed, List<object>? rawPushedKeys,
            ReconcilerBufferPool bufferPool)
        {
            _stack = stack;
            _pushed = pushed;
            _rawPushedKeys = rawPushedKeys;
            _bufferPool = bufferPool;
        }

        // What every step of one spine edge's walk threads unchanged: the edge being searched, the cursor and
        // the undo log it writes to, and the two registries the keying reads. A struct passed by `in` so the
        // recursion carries only what varies from step to step without putting an object on the heap — this
        // runs on every isolated re-render, which is every setState.
        private readonly struct SpineWalk
        {
            internal ComponentFiber Ancestor { get; init; }
            internal ComponentFiber SpineChild { get; init; }
            internal ComponentContextStack Stack { get; init; }
            internal List<ContextProviderNode> Pushed { get; init; }
            internal ComponentRegistry Registry { get; init; }
            internal FiberMemoCache MemoCache { get; init; }
            // The Portal scope and container members of SpineChild's own registration key, read back
            // from the registry rather than derived here — ComponentRegistry.TryGetInlineKey owns why.
            // Both are therefore constants of one edge rather than readings of where the descent has
            // got to, so the key resolves SpineChild at every container holding the same position and
            // cannot say which of them the walk is in. MatchesInlineSpineChild separates them by the
            // node instead, where OwnOutput says that node can be compared. Two occurrences a
            // declaring body builds from one shared node instance are one node, so those stay
            // unseparated and the walk still takes the first.
            internal VisualElement? PortalScope { get; init; }
            internal VisualElement? Container { get; init; }

            // The array being walked where it is Ancestor's own output, as FiberContextSpine.OwnOutput picks it for
            // SpineChild. Null at a detached edge, whose nodes are a Portal's children or a row's item node rather
            // than the array a fiber's ComponentFiber.SourceTree is stamped from (ReconcilerContext.CurrentFiberTree).
            internal VNode?[]? OwnOutput { get; init; }
            // One instance shared by every Push, cleared at each edge. An element's children are reconciled by a
            // new walk, so PushScope starts their occurrence count over where that walk's claim does.
            internal InlineKeyClaims Claims { get; init; }
        }

        // Occurrence of an explicit component key while one spine edge is walked.
        private sealed class InlineKeyClaims
        {
            private readonly Dictionary<(int scope, long slotPath, string key, object identity), int> _counts = new();
            private readonly Stack<int> _scopes = new();
            private int _scope;
            private int _nextScope = 1;

            // Puts back what a new instance holds. One instance serves every Push, since none starts while
            // another walks.
            internal void Reset()
            {
                // MUTANT_SURVIVES(equivalent, line removed): Claim is reached only from the walk inside Push, whose edge loop calls
                // Claims.Clear() before each edge, so the counts Reset leaves are dropped before anything reads them, and no result differs; the
                // clear here only decides when the identities those counts are keyed on are released.
                _counts.Clear();
                // MUTANT_SURVIVES(equivalent, line removed): PushScope has one caller, PushInFreshScope, whose finally pairs it with
                // PopScope, so the stack is empty whenever Reset runs.
                _scopes.Clear();
                _scope = 0;
                _nextScope = 1;
            }

            internal int Claim(long slotPath, string key, object identity)
            {
                var id = (_scope, slotPath, key, identity);
                _counts.TryGetValue(id, out var seen);
                _counts[id] = seen + 1;
                return seen;
            }

            internal void PushScope()
            {
                _scopes.Push(_scope);
                _scope = _nextScope++;
            }

            internal void PopScope() => _scope = _scopes.Pop();

            internal void Clear() => _counts.Clear();
        }

        // Pushes the Provider values that enclose target onto the live cursor and
        // returns a handle whose Unwind pops them in reverse. A no-op (empty handle) when
        // the fiber has no reconciler / no parent (a root re-render needs no reconstruction — it pushes
        // its own Providers during its own reconcile).
        internal static FiberContextSpine Push(ComponentFiber target)
        {
            var ctx = target.Reconciler?.Context;
            if (ctx == null || target.Parent == null) return default;

            // Build the spine root -> target's parent. Each entry contributes the
            // Providers enclosing the NEXT entry down (or the target at the deepest entry).
            var bufferPool = ctx.BufferPool;
            var spine = bufferPool.RentFiberList();
            for (var f = target.Parent; f != null; f = f.Parent)
            {
                spine.Add(f);
            }
            spine.Reverse();

            var stack = ctx.ComponentContextStack;
            var pushed = bufferPool.RentProviderNodeList();
            var registry = ctx.ComponentRegistry;
            var memoCache = ctx.FiberMemoCache;
            var claims = s_claims;
            claims.Reset();
            var rawPushedKeys = PushNearestStampUnderneath(target, stack);

            // For each spine edge (ancestor -> child), re-push the Providers in the ancestor's output
            // that enclose `child`. The deepest ancestor encloses `target` itself: a ComponentNode in the
            // ancestor's output resolves to `child` via the registry's tree-position keying.
            for (var i = 0; i < spine.Count; i++)
            {
                claims.Clear();
                var ancestor = spine[i];
                var child = i + 1 < spine.Count ? spine[i + 1] : target;

                // Detached-mount top-level child (a Portal's drained children, or a VirtualList's
                // controller-rendered items): the subtree mounted outside the parent-walked reconcile, so
                // `ancestor`'s output hides this child behind a wrapper-emitting leaf the canonical
                // descent skips (see the default arm of PushEnclosingProvidersForNode, which is where both
                // kinds of host land). The edges above have pushed what encloses `ancestor`; on top go what the
                // host's capture holds around the host, then the Providers inside the subtree above this child.
                var detached = child.DetachedMountContext;
                if (detached != null)
                {
                    registry.TryGetInlineKey(child, out _, out _, out var detachedScope, out var detachedContainer);
                    var detachedWalk = new SpineWalk
                    {
                        Ancestor = detached.Anchor,
                        SpineChild = child,
                        Stack = stack,
                        Pushed = pushed,
                        Registry = registry,
                        MemoCache = memoCache,
                        PortalScope = detachedScope,
                        Container = detachedContainer,
                        OwnOutput = null,
                        Claims = claims,
                    };
                    PushDetachedEdge(detached, in detachedWalk, rawPushedKeys ??= new List<object>());
                    continue;
                }

                var tree = OwnOutput(ancestor, child);
                if (tree == null || tree.Length == 0) continue;
                if (PushesNoContext(ancestor, tree)) continue;

                // A child mounted into a wrapper of its own is a VirtualList row's top
                // (FiberNodeFactory.CreateForComponentNode), whose edge is the detached one above once the item loop
                // has stamped it, and no node of an ancestor's output resolves to it.
                if (!registry.TryGetInlineKey(child, out _, out _, out var childScope, out var childContainer)) continue;

                var walk = new SpineWalk
                {
                    Ancestor = ancestor,
                    SpineChild = child,
                    Stack = stack,
                    Pushed = pushed,
                    Registry = registry,
                    MemoCache = memoCache,
                    PortalScope = childScope,
                    Container = childContainer,
                    OwnOutput = tree,
                    Claims = claims,
                };
                PushEnclosingProviders(tree, FiberKeying.WalkRoot, in walk);
            }

            bufferPool.ReturnFiberList(spine);
            claims.Reset();
            return new FiberContextSpine(stack, pushed, rawPushedKeys, bufferPool);
        }

        private static readonly InlineKeyClaims s_claims = new();

        // Goes under everything the edges push, so what an edge rebuilds from committed outputs reads as it is now and
        // the snapshot answers for a context no edge pushes: the enter flags, which nothing rebuilds, and what lies
        // above a fiber an edge finds no node for, such as a presence's exiting child, since PushPresenceChildren walks
        // the children the presence is given. The nearest stamp's is the one taken last on the way down.
        private static List<object>? PushNearestStampUnderneath(ComponentFiber target, ComponentContextStack stack)
        {
            for (var f = target; f != null; f = f.Parent)
            {
                if (f.DetachedMountContext is not { } detached) continue;
                var pushedKeys = new List<object>();
                DetachedHostCapture.PushSnapshot(stack, detached.EnclosingSnapshot, pushedKeys);
                return pushedKeys;
            }
            return null;
        }

        private static void PushDetachedEdge(DetachedMountContext detached, in SpineWalk walk, List<object> pushedKeys)
        {
            var resolved = detached.Host.Resolve();
            resolved?.PushChain(walk.Stack, pushedKeys);
            var current = detached.CurrentNodes(resolved);
            if (PushDetachedDescent(current, detached, in walk) || ReferenceEquals(current, detached.DescendantNodes))
            {
                return;
            }
            walk.Claims.Clear();
            PushDetachedDescent(detached.DescendantNodes, detached, in walk);
        }

        // Whether nodes hold the walk's spine child, with the Providers above it left pushed where they do.
        private static bool PushDetachedDescent(VNode?[]? nodes, DetachedMountContext detached, in SpineWalk walk)
        {
            if (nodes is not { Length: > 0 }) return false;
            // A VirtualList item is one rendered node rather than a reconciled child array.
            return PushEnclosingProviders(
                detached.RootProviderChildrenStartAtWalkRoot ? nodes : FiberKeying.UnwrapLoneFragment(nodes),
                FiberKeying.WalkRoot, in walk,
                detached.RootProviderChildrenStartAtWalkRoot);
        }

        // Whether nothing in tree can push onto the cursor, so the walk of it would push nothing whichever child it
        // looked for. A walk visits every sibling before the one it wants, so skipping it keeps an isolated render's
        // cost off the length of the list its output holds. Memo inner nodes are not in the tree, so a MemoNode
        // counts as a pusher.
        private static bool PushesNoContext(ComponentFiber ancestor, VNode?[] tree)
        {
            if (!ReferenceEquals(ancestor.ContextFreeOutput, tree))
            {
                ancestor.ContextFreeOutputAnswer = HoldsNoPusher(tree);
                ancestor.ContextFreeOutput = tree;
            }
            return ancestor.ContextFreeOutputAnswer;
        }

        private static bool HoldsNoPusher(VNode?[]? nodes)
        {
            if (nodes == null) return true;
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case ContextProviderNode or MotionNode or MemoNode:
                        return false;
                    case FragmentNode fragment:
                        if (!HoldsNoPusher(fragment.Children)) return false;
                        break;
                    case BaseElementNode element:
                        if (!HoldsNoPusher(element.Children)) return false;
                        break;
                    case SuspenseNode suspense:
                        if (!HoldsNoPusher(suspense.Children)) return false;
                        if (suspense.Fallback != null && !HoldsNoPusher(new[] { suspense.Fallback })) return false;
                        break;
                    case AnimatePresenceNode presence:
                        if (!HoldsNoPusher(presence.Children)) return false;
                        break;
                }
            }
            return true;
        }

        // The output of ancestor's that a keyed walk to child reads. Where child was last reached from the baseline of
        // ancestor's parked pass, that pass has not reached child yet, and the Providers around child are still the
        // baseline's; otherwise it is the output ancestor is reconciling, or else the one it kept last.
        private static VNode?[]? OwnOutput(ComponentFiber ancestor, ComponentFiber child)
        {
            var parked = ancestor.PendingOldTree;
            return parked != null && ReferenceEquals(child.SourceTree, parked)
                ? parked
                : ancestor.ReconcilingTree ?? ancestor.PreviousTree;
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
                _bufferPool.ReturnProviderNodeList(_pushed);
            }
            if (_rawPushedKeys != null)
            {
                for (var i = _rawPushedKeys.Count - 1; i >= 0; i--)
                {
                    _stack.PopRaw(_rawPushedKeys[i]);
                }
            }
        }

        // Walks nodes (one reconcile scope of ancestor's
        // output) depth-first, pushing each Provider before descending its children. Returns true once the
        // node that hosts spineChild is reached — its enclosing Providers stay pushed
        // (recorded in pushed). The host node is the ComponentNode resolving to
        // spineChild. Providers on a subtree that does NOT
        // contain the spine child are popped on the way out so only the path to spineChild
        // remains on the cursor.
        private static bool PushEnclosingProviders(
            VNode?[] nodes,
            WalkPosition position,
            in SpineWalk walk,
            bool rootProviderChildrenStartAtWalkRoot = false)
        {
            for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                if (PushEnclosingProvidersForNode(
                        nodes[nodeIndex], nodeIndex, position, in walk,
                        rootProviderChildrenStartAtWalkRoot))
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
            WalkPosition position,
            in SpineWalk walk,
            bool rootProviderChildrenStartAtWalkRoot)
        {
            switch (node)
            {
                case null:
                    return false;

                case FragmentNode fragment:
                {
                    if (fragment.Children == null) return false;
                    var childPosition = FiberKeying.FragmentChild(position, fragment.Key, nodeIndex);
                    return PushEnclosingProviders(fragment.Children, childPosition, in walk);
                }

                case ContextProviderNode provider:
                    return PushProviderSubtree(
                        provider, nodeIndex, position, in walk,
                        rootProviderChildrenStartAtWalkRoot);

                case ComponentNode component:
                    return MatchesInlineSpineChild(component, position, nodeIndex, in walk);

                case MemoNode memo:
                    return PushMemoInner(memo, nodeIndex, position, in walk);

                case SuspenseNode suspense:
                    return PushSuspenseSubtree(suspense, nodeIndex, position, in walk);

                case MotionNode motion:
                    return PushMotionSubtree(motion, in walk);

                case BaseElementNode element:
                {
                    // An element opens a fresh reconcile scope for its children (the host reconciles them
                    // via ReconcileChildren, which starts its own walk at FiberKeying.WalkRoot);
                    // FiberStack.Current stays `ancestor`, so a Component among them is still a direct
                    // child fiber of `ancestor`.
                    if (element.Children is not { Length: > 0 }) return false;
                    return PushInFreshScope(
                        FiberKeying.UnwrapLoneFragment(element.Children), FiberKeying.WalkRoot, in walk);
                }

                case AnimatePresenceNode presence:
                    return PushPresenceChildren(presence, nodeIndex, position, in walk);

                default:
                    // Reaching a wrapper-emitting leaf (Portal / VirtualList) here means it sits in this
                    // ancestor's output but does NOT host the spine child being searched for. A
                    // detached mount that DID host it (a Portal's drained children, a VirtualList's items)
                    // is handled before this walk — see the DetachedMountContext branch in Push, which
                    // rebuilds the enclosing context from the host's capture instead of descending the
                    // host's tree. So skip it: its own subtree cannot enclose a spine child not inside it.
                    return false;
            }
        }

        private static bool PushProviderSubtree(
            ContextProviderNode provider,
            int nodeIndex,
            WalkPosition position,
            in SpineWalk walk,
            bool childrenStartAtWalkRoot)
        {
            provider.PushContext(walk.Stack);
            walk.Pushed.Add(provider);
            if (provider.Children != null)
            {
                // Children starting at the walk root are the ones a Provider reconciled as an element reconciles.
                var found = childrenStartAtWalkRoot
                    ? PushInFreshScope(
                        FiberKeying.UnwrapLoneFragment(provider.Children), FiberKeying.WalkRoot, in walk)
                    : PushEnclosingProviders(
                        provider.Children,
                        FiberKeying.ProviderChild(position, provider.Key, nodeIndex),
                        in walk);
                if (found)
                {
                    return true;
                }
            }
            // Not on the path to spineChild: undo this Provider's push.
            provider.PopContext(walk.Stack);
            walk.Pushed.RemoveAt(walk.Pushed.Count - 1);
            return false;
        }

        // Inline-mounted ComponentNodes register under the SAME position-key scheme used by
        // ExpandInlineRecursive. A match means we have descended exactly to
        // the spine child — its enclosing Providers are pushed, so stop. A sibling component is never
        // descended into: its own subtree's Providers do not enclose spineChild (a direct child fiber of
        // `ancestor`).
        private static bool MatchesInlineSpineChild(
            ComponentNode component,
            WalkPosition position,
            int nodeIndex,
            in SpineWalk walk)
        {
            var registry = walk.Registry;
            var identity = component.ResolvedIdentity;
            // Claimed before the source-node return: an earlier sibling that is not the spine child still
            // takes its occurrence, which is the count ExpandComponentInline recorded for this one.
            var occurrence = component.Key is { } explicitKey
                ? walk.Claims.Claim(position.SlotPath, explicitKey, identity)
                : 0;
            if (walk.OwnOutput != null
                && ReferenceEquals(walk.SpineChild.SourceTree, walk.OwnOutput)
                && !ReferenceEquals(component, walk.SpineChild.SourceNode))
            {
                return false;
            }
            var slotKey = FiberKeying.ResolveInlineComponentPosition(
                occurrence, position, component.Key, nodeIndex,
                registry.InlinePositionKeyBoxes,
                registry.InlineExplicitPositionKeyBoxes);
            var resolved = registry.TryGetFiberForInlineKey(
                walk.Ancestor, slotKey, identity, walk.PortalScope, walk.Container);
            return ReferenceEquals(resolved, walk.SpineChild);
        }

        private static bool PushMemoInner(
            MemoNode memo,
            int nodeIndex,
            WalkPosition position,
            in SpineWalk walk)
        {
            var innerPosition = FiberKeying.MemoInner(position, memo.Key, nodeIndex);
            // A memo this walk has to find encloses the spine child at the spine child's own level, so it was
            // written with Ancestor current and under the spine child's PortalScope. One position can hold an
            // entry for each of several containers, and nothing here says which container this is.
            var memoPosition = FiberKeying.MemoAt(walk.Ancestor, walk.PortalScope, memo.Key, position, innerPosition);
            for (var candidate = 0; walk.MemoCache.PeekInner(memoPosition, memo, candidate) is { } inner; candidate++)
            {
                if (PushEnclosingProviders(new[] { inner }, innerPosition, in walk)) return true;
            }
            return false;
        }

        private static bool PushSuspenseSubtree(
            SuspenseNode suspense,
            int nodeIndex,
            WalkPosition position,
            in SpineWalk walk)
        {
            var ancestor = walk.Ancestor;
            var suspenseKey = FiberKeying.SuspenseKey(position.Scope, suspense.Key, nodeIndex);
            var wasFallback = ancestor.Reconciler != null
                && ancestor.Reconciler.Context.IsSuspenseFallbackShownOnSpine(
                    ancestor, walk.Container, walk.PortalScope, FiberKeying.SuspenseAt(position, suspense.Key, nodeIndex),
                    suspense);
            var sub = wasFallback
                ? (suspense.Fallback != null ? new[] { suspense.Fallback } : System.Array.Empty<VNode>())
                : (suspense.Children ?? System.Array.Empty<VNode>());
            if (sub.Length == 0) return false;

            var subPosition = FiberKeying.SuspenseSubtree(
                position, suspenseKey, suspense.Key, nodeIndex, wasFallback);
            return PushEnclosingProviders(sub, subPosition, in walk);
        }

        // A MotionNode establishes MotionContext for its subtree, exactly as a Provider establishes its
        // context. Re-push the effective label (own Animate, else the inherited top) so a descendant Motion
        // re-rendering in isolation reads the ancestor's active label instead of dropping to the default.
        // Mirrors PushProviderSubtree (push, descend its fresh child scope, pop if the spine child is not
        // beneath it).
        private static bool PushMotionSubtree(MotionNode motion, in SpineWalk walk)
        {
            var stack = walk.Stack;
            var motionLabel = MotionVariantResolver.LabelForChildren(motion, stack.Get(MotionContext.ActiveLabel));
            var motionProvider = new ContextProviderNode<string>
            {
                Context = MotionContext.ActiveLabel,
                Value = motionLabel,
                Children = System.Array.Empty<VNode>(),
            };
            motionProvider.PushContext(stack);
            walk.Pushed.Add(motionProvider);
            var initialProvider = new ContextProviderNode<string>
            {
                Context = MotionContext.InitialLabel,
                Value = MotionVariantResolver.InitialLabel(motion, stack.Get(MotionContext.InitialLabel)),
                Children = System.Array.Empty<VNode>(),
            };
            initialProvider.PushContext(stack);
            walk.Pushed.Add(initialProvider);
            var playbackProvider = new ContextProviderNode<MotionPlayback>
            {
                Context = MotionContext.Playback,
                Value = MotionVariantResolver.PlaybackForChildren(motion, stack.Get(MotionContext.Playback)),
                Children = System.Array.Empty<VNode>(),
            };
            playbackProvider.PushContext(stack);
            walk.Pushed.Add(playbackProvider);
            if (motion.Children is { Length: > 0 })
            {
                // A Motion is an element: its children reconcile in their own walk, so the occurrence count
                // starts over with them.
                if (PushInFreshScope(
                        FiberKeying.UnwrapLoneFragment(motion.Children), FiberKeying.WalkRoot, in walk))
                {
                    return true;
                }
            }
            playbackProvider.PopContext(stack);
            initialProvider.PopContext(stack);
            motionProvider.PopContext(stack);
            walk.Pushed.RemoveRange(walk.Pushed.Count - 3, 3);
            return false;
        }

        // AnimatePresence is DOM-less: GeneralPathReconciler.ExpandAnimatePresenceInline expands each
        // keyed child directly into the parent's slot range, each under its own PresenceChild position. A
        // wrapper-hosted spine child therefore registers under THIS ancestor by its own slotKey, exactly
        // like an inline child — the only reason the canonical descent misses it is that the walker never
        // steps THROUGH the AnimatePresenceNode. Descend into each current child under the map key
        // FiberNodeFactory.PresenceChildMapKey assigns, so the ComponentNode arm matches the spine child
        // and the enclosing Providers / MotionContext stay pushed. Only currently-present children are
        // walked (not still-exiting ghosts): the covered case is an isolated re-render of a
        // persisting descendant. Mirrors the Suspense arm.
        private static bool PushPresenceChildren(
            AnimatePresenceNode presence,
            int nodeIndex,
            WalkPosition position,
            in SpineWalk walk)
        {
            if (presence.Children == null) return false;

            var presencePosition = FiberKeying.Presence(position, presence.Key, nodeIndex);
            var taken = new Dictionary<string, int>();
            var autoIndex = 0;
            foreach (var child in presence.Children)
            {
                if (child == null) continue;
                var childKey = FiberNodeFactory.PresenceChildMapKey(child.Key, ref autoIndex, taken, out _);
                var childPosition = FiberKeying.PresenceChild(presencePosition, childKey);
                if (PushEnclosingProviders(new[] { child }, childPosition, in walk))
                {
                    return true;
                }
            }

            return false;
        }

        // Element children, and a Provider reconciled as an element, open a walk of their own.
        // Their occurrence count starts at zero there; this scope does the same.
        private static bool PushInFreshScope(VNode?[] nodes, WalkPosition position, in SpineWalk walk)
        {
            walk.Claims.PushScope();
            try
            {
                return PushEnclosingProviders(nodes, position, in walk);
            }
            finally
            {
                walk.Claims.PopScope();
            }
        }
    }
}
