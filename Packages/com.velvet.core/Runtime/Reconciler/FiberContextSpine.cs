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
            internal bool IsInlineSpineChild { get; init; }
            // The Portal scope and container members of SpineChild's own registration key, read back
            // from the registry rather than derived here — ComponentRegistry.TryGetInlineKey owns why.
            // Both are therefore constants of one edge rather than readings of where the descent has
            // got to, so the key resolves SpineChild at every container holding the same position and
            // cannot say which of them the walk is in. MatchesInlineSpineChild separates them by the
            // node instead, where OwnOutput says that node can be compared. Two occurrences a
            // declaring body builds from one shared node instance are one node, so those stay
            // unseparated and the walk still takes the first. A walk by reference sets the two itself: PortalScope to
            // the Portal whose children it is in, which is what a memo or a Suspense there was written under, and
            // Container to the element its target placed (a Portal's placeholder or a list's scroll view), which a
            // Suspense's container, a Portal's target and a list's row must hold for the walk to go through them.
            internal VisualElement? PortalScope { get; init; }
            internal VisualElement? Container { get; init; }

            // The array being walked where it is Ancestor's own output: its ReconcilingTree where it has one, else
            // its PreviousTree. Null for a detached mount's captured DescendantNodes, which is
            // stamped onto a fiber only on the drain that created it — so after the declaring body renders again
            // that array still holds the nodes of the render that captured it, which ComponentFiber.SourceNode no
            // longer names.
            internal VNode?[]? OwnOutput { get; init; }
            // One per Push, cleared at each edge. An element's children are reconciled by a new walk,
            // so PushScope starts their occurrence count over where that walk's claim does.
            internal InlineKeyClaims Claims { get; init; }
            // The node of a Portal, a world-space panel or a VirtualList that a walk finds by reference instead of a
            // spine child by registry key. Such a walk also goes through the Portals and lists on its way, which a
            // keyed walk skips, since the node can sit inside one of them.
            internal VNode? Target { get; init; }
            internal Dictionary<VisualElement, PortalSlotInfo> Portals { get; init; }
            internal Dictionary<VisualElement, FiberVirtualListController> Controllers { get; init; }
        }

        // Occurrence of an explicit component key while one spine edge is walked.
        private sealed class InlineKeyClaims
        {
            private readonly Dictionary<(int scope, long slotPath, string key, object identity), int> _counts = new();
            private readonly Stack<int> _scopes = new();
            private int _scope;
            private int _nextScope = 1;

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
        internal static FiberContextSpine Push(ComponentFiber target) => Push(target, around: null);

        // Pushes anchor's spine and then the Providers that anchor's output puts around list, where that output holds
        // the list's node.
        internal static FiberContextSpine PushAround(ComponentFiber anchor, FiberVirtualListController list)
            => Push(anchor, list);

        private static FiberContextSpine Push(ComponentFiber target, FiberVirtualListController? around)
        {
            var ctx = target.Reconciler?.Context;
            if (ctx == null || target.Parent == null) return default;

            // Build the spine root -> target's parent. Each entry contributes the
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
            var claims = new InlineKeyClaims();
            var shared = Shared(ctx, pushed, claims);

            // For each spine edge (ancestor -> child), re-push the Providers in the ancestor's output
            // that enclose `child`. The deepest ancestor encloses `target` itself.
            //
            // INLINE-mounted spine children match the canonical wrapper-less Component path: a
            // ComponentNode in the ancestor's output resolves to `child` via the registry's
            // tree-position keying. A WRAPPER-mounted spine child is hosted by a wrapper-emitting node
            // whose container VE is the child's MountPoint; the providers enclosing that container in the
            // ancestor's output are the ones that enclose it.
            for (var i = 0; i < spine.Count; i++)
            {
                claims.Clear();
                var ancestor = spine[i];
                var child = i + 1 < spine.Count ? spine[i + 1] : target;

                // Detached-mount top-level child (a Portal's drained children, or a VirtualList's
                // controller-rendered items): the subtree mounted outside the parent-walked reconcile, so
                // `ancestor`'s output hides this child behind a wrapper-emitting leaf the keyed
                // descent skips (see the default arm of PushEnclosingProvidersForNode, which is where both
                // kinds of host land). PushAroundDetachedHost walks to the host instead. Where `ancestor`'s output
                // holds no node of the host, the context captured where the detached mount ran is pushed as the
                // base, then — when descendant VNodes were captured — those are walked to recover any Provider
                // placed directly inside the subtree above this child. Deeper spine edges (this child -> target)
                // then push the in-subtree Providers normally on top.
                // Unwind is balanced per context key regardless of push origin (Pop discards the top), so the
                // raw base pushes here coexist correctly with the Provider pushes the other edges add.
                var detached = child.DetachedMountContext;
                if (detached != null)
                {
                    if (PushAroundDetachedHost(ancestor, child, detached, in shared)) continue;
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
                        registry.TryGetInlineKey(child, out _, out _, out var detachedScope, out var detachedContainer);
                        var detachedWalk = new SpineWalk
                        {
                            Ancestor = detached.Anchor,
                            SpineChild = child,
                            Stack = stack,
                            Pushed = pushed,
                            Registry = registry,
                            MemoCache = memoCache,
                            IsInlineSpineChild = true,
                            PortalScope = detachedScope,
                            Container = detachedContainer,
                            OwnOutput = null,
                            Claims = claims,
                        };
                        // A VirtualList item is one rendered node rather than a reconciled child array.
                        PushEnclosingProviders(
                            detached.RootProviderChildrenStartAtWalkRoot
                                ? detached.DescendantNodes
                                : FiberKeying.UnwrapLoneFragment(detached.DescendantNodes!),
                            FiberKeying.WalkRoot, in detachedWalk,
                            detached.RootProviderChildrenStartAtWalkRoot);
                    }
                    continue;
                }

                var tree = OwnOutput(ancestor, child);
                if (tree == null || tree.Length == 0) continue;

                var isInline = registry.TryGetInlineKey(child, out _, out _, out var childScope, out var childContainer);
                if (!isInline && child.MountPoint == null) continue;

                var walk = new SpineWalk
                {
                    Ancestor = ancestor,
                    SpineChild = child,
                    Stack = stack,
                    Pushed = pushed,
                    Registry = registry,
                    MemoCache = memoCache,
                    IsInlineSpineChild = isInline,
                    PortalScope = childScope,
                    Container = childContainer,
                    OwnOutput = tree,
                    Claims = claims,
                };
                PushEnclosingProviders(tree, FiberKeying.WalkRoot, in walk);
            }

            if (around != null) PushAroundList(target, around, in shared);
            return new FiberContextSpine(stack, pushed, rawPushedKeys);
        }

        // The members a walk takes from the spine rather than from the edge it walks.
        private static SpineWalk Shared(ReconcilerContext ctx, List<ContextProviderNode> pushed, InlineKeyClaims claims)
            => new()
            {
                Stack = ctx.ComponentContextStack,
                Pushed = pushed,
                Registry = ctx.ComponentRegistry,
                MemoCache = ctx.FiberMemoCache,
                Claims = claims,
                Portals = ctx.PortalState.Stored,
                Controllers = ctx.VirtualListControllers,
            };

        // The output of ancestor's that a keyed walk to child reads. Where child was last reached from the baseline of
        // ancestor's parked pass, that pass has not reached child yet, and child's rows are still that baseline's;
        // otherwise it is the output ancestor is reconciling, or else the one it kept last.
        private static VNode?[]? OwnOutput(ComponentFiber ancestor, ComponentFiber child)
        {
            var parked = ancestor.PendingOldTree;
            return parked != null && ReferenceEquals(child.SourceTree, parked)
                ? parked
                : ancestor.ReconcilingTree ?? ancestor.PreviousTree;
        }

        // A detached child is among the current children of its Portal or in one of its list's current rows. The walk
        // goes to that host's node in ancestor's output by reference, then by registry key to the child in what the
        // host holds now, as the walk over a mount's captured nodes does. False, with nothing pushed, where ancestor's
        // output holds neither the host's node nor the one that node replaced.
        private static bool PushAroundDetachedHost(
            ComponentFiber ancestor, ComponentFiber child, DetachedMountContext detached, in SpineWalk shared)
        {
            if (detached.List is { } list)
            {
                if (!PushAroundList(ancestor, list, in shared)) return false;
                var withinRow = KeyedWithinHost(ancestor, child, in shared);
                // A row is one rendered node rather than a reconciled child array.
                PushEnclosingProvidersForNode(
                    list.RowHolding(child.MountPoint), 0, FiberKeying.WalkRoot, in withinRow,
                    rootProviderChildrenStartAtWalkRoot: true);
                return true;
            }
            // A detached child's stamp names its list or its Portal.
            var placeholder = detached.Placeholder!;
            shared.Portals.TryGetValue(placeholder, out var portal);
            var host = PushAroundHostNode(ancestor, portal.Node, placeholder, in shared) ? portal.Node
                : PushAroundHostNode(ancestor, portal.PreviousNode, placeholder, in shared) ? portal.PreviousNode
                : null;
            if (host == null) return false;
            var withinPortal = KeyedWithinHost(ancestor, child, in shared);
            PushEnclosingProviders(
                FiberKeying.UnwrapLoneFragment(ChildrenOf(host)), FiberKeying.WalkRoot, in withinPortal);
            return true;
        }

        private static VNode?[] ChildrenOf(VNode host)
            => (host as PortalNode)?.Children ?? (host as WorldSpaceNode)?.Children ?? System.Array.Empty<VNode?>();

        private static bool PushAroundList(ComponentFiber ancestor, FiberVirtualListController list, in SpineWalk shared)
            => PushAroundHostNode(ancestor, list.Node, list.ScrollView, in shared)
                || PushAroundHostNode(ancestor, list.PreviousNode, list.ScrollView, in shared);

        private static SpineWalk KeyedWithinHost(ComponentFiber ancestor, ComponentFiber child, in SpineWalk shared)
        {
            shared.Registry.TryGetInlineKey(child, out _, out _, out var scope, out var container);
            return shared with
            {
                Ancestor = ancestor,
                SpineChild = child,
                IsInlineSpineChild = true,
                PortalScope = scope,
                Container = container,
            };
        }

        // Pushes the Providers ancestor's output puts around node, found by reference, and leaves them pushed where it
        // is found; element is what node placed. The baseline of ancestor's parked pass is searched first: the parked
        // output holds a node of the baseline's only where it reuses the node, and the baseline's Providers are the
        // committed ones.
        private static bool PushAroundHostNode(
            ComponentFiber ancestor, VNode? node, VisualElement element, in SpineWalk shared)
        {
            var walk = shared with { Ancestor = ancestor, Container = element, Target = node };
            return PushAroundHostNodeIn(ancestor.PendingOldTree, in walk)
                || PushAroundHostNodeIn(ancestor.ReconcilingTree ?? ancestor.PreviousTree, in walk);
        }

        private static bool PushAroundHostNodeIn(VNode?[]? tree, in SpineWalk walk)
            => tree != null && PushEnclosingProviders(tree, FiberKeying.WalkRoot, in walk);

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

        // Walks nodes (one reconcile scope of ancestor's
        // output) depth-first, pushing each Provider before descending its children. Returns true once the
        // node that hosts spineChild is reached — its enclosing Providers stay pushed
        // (recorded in pushed). The host node is a ComponentNode resolving to
        // spineChild for inline-mounted children, or an AnimatePresenceNode
        // whose dynamic wrapper VE matches spineChild's MountPoint for wrapper-mounted
        // children. Providers on a subtree that does NOT
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
            if (walk.Target != null && ReferenceEquals(node, walk.Target)) return true;
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

                case ComponentNode component when walk.IsInlineSpineChild:
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
                    // A keyed walk skips a wrapper-emitting leaf (Portal / VirtualList). A spine child mounted inside
                    // one is stamped with a DetachedMountContext by the reconcile that mounted it, and Push walks to
                    // the leaf by reference instead.
                    return walk.Target != null && PushThroughDetachedHost(node, in walk);
            }
        }

        // A walk by reference goes through a Portal or a world-space panel whose node, or the node it replaced, a pass
        // last mounted or patched, under that Portal's scope, and through the row that holds the element its target
        // placed of a list whose node, or the node it replaced, a pass last created or patched, under no Portal scope
        // (ReconcilerContext.PortalChildKeyScope says why a list's item scope drops it).
        private static bool PushThroughDetachedHost(VNode node, in SpineWalk walk)
        {
            switch (node)
            {
                case PortalNode:
                case WorldSpaceNode:
                    var placeholder = PlaceholderOf(node, walk.Portals);
                    return placeholder != null && PushInFreshScope(
                        FiberKeying.UnwrapLoneFragment(ChildrenOf(node)), FiberKeying.WalkRoot,
                        walk with { PortalScope = placeholder });
                case VirtualListNode list:
                    var row = RowOf(list, walk.Controllers, walk.Container);
                    return row != null && PushEnclosingProvidersForNode(
                        row, 0, FiberKeying.WalkRoot, walk with { PortalScope = null },
                        rootProviderChildrenStartAtWalkRoot: true);
                default:
                    return false;
            }
        }

        private static VisualElement? PlaceholderOf(VNode node, Dictionary<VisualElement, PortalSlotInfo> portals)
        {
            foreach (var entry in portals)
            {
                if (ReferenceEquals(entry.Value.Node, node) || ReferenceEquals(entry.Value.PreviousNode, node)) return entry.Key;
            }
            return null;
        }

        private static VNode? RowOf(
            VirtualListNode list, Dictionary<VisualElement, FiberVirtualListController> controllers, VisualElement? element)
        {
            foreach (var controller in controllers.Values)
            {
                if (ReferenceEquals(controller.Node, list) || ReferenceEquals(controller.PreviousNode, list))
                {
                    return controller.RowHolding(element);
                }
            }
            return null;
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
            // written with Ancestor current and under the spine child's PortalScope, or in a walk by reference, the
            // Portal scope of where the walk is. One position can hold an entry for each of several containers, and
            // nothing here says which container this is.
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
