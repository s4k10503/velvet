#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    // Reconciliation identity, shared by both reconcile paths: which ChildKey a node participates under
    // (explicit VNode.Key, a Fragment-scoped override, or its positional index when unkeyed), the scoped-key
    // overrides for keyed-Fragment children, the old-side key→(index,node) map, and the CanPatch decision
    // (whether two nodes share enough identity to patch in place vs remove + recreate). The fast path
    // (Indexed/Keyed diff in ChildReconciler) and the general path (live-context walk in
    // GeneralPathReconciler) both resolve identity through this single collaborator, so their semantics
    // cannot drift. Key-override state lives on ReconcilerContext.EffectiveKeys (scoped to one reconcile
    // pass); the underlying VNode is never mutated.
    internal sealed class ReconcileKeying
    {
        private readonly ReconcilerContext _ctx;

        public ReconcileKeying(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        internal bool HasAnyKey(VNode?[] nodes)
        {
            foreach (var node in nodes)
            {
                if (EffectiveKey(node) != null)
                {
                    return true;
                }
            }
            return false;
        }

        // Records the effective key under which a non-wrapper VNode (leaf) participates in the keyed
        // reconciler's identity map. When fragmentKeyScope is null, no override is
        // published — the node falls through to its own VNode.Key as before (unkeyed
        // behavior preserved). When non-null, the scope is composed with the node's own key (or its
        // positional index when unkeyed) so siblings under the same keyed Fragment do not collide
        // with siblings under a Fragment carrying a different key. The override lives in
        // ReconcilerContext.EffectiveKeys for this reconcile pass; the underlying VNode
        // is never mutated.
        internal void RegisterScopedKey(VNode? node, string? fragmentKeyScope, int nodeIndex)
        {
            if (node == null || fragmentKeyScope == null) return;
            var contribution = node.Key ?? FiberKeying.Index(nodeIndex);
            _ctx.EffectiveKeys[node] = FiberKeying.ComposeFragmentScope(fragmentKeyScope, contribution);
        }

        // Returns the effective key used by the keyed reconciler for node: the
        // override published by RegisterScopedKey if present, otherwise the node's
        // own VNode.Key. Null-safe — returns null for a null node.
        internal string? EffectiveKey(VNode? node)
            => node != null && _ctx.EffectiveKeys.TryGetValue(node, out var k) ? k : node?.Key;

        // Resolves the identity key for one sibling in the keyed reconcile path. A node carrying an
        // explicit key (its own or a Fragment-scoped override) reconciles by that key. An unkeyed
        // node reconciles by its full sibling index siblingIndex — keyed siblings
        // occupy an index too — so the same array slot patches across renders instead of being
        // destroyed and recreated. An unkeyed child is thus matched between renders by its array
        // index as an implicit key. Explicit and implicit keys never collide
        // because ChildKey distinguishes them by kind.
        internal ChildKey ReconcileKey(VNode? node, int siblingIndex)
        {
            var key = EffectiveKey(node);
            return key != null ? ChildKey.Explicit(key) : ChildKey.Positional(siblingIndex);
        }

        // Registers one old node under its reconcile key while building the keyed-diff old→(index,node)
        // map. On a duplicate key the later writer wins (the new-side lookup only ever resolves to the
        // last entry): the displaced earlier index is recorded as an orphan so the removal pass cleans
        // it up — it is not covered by the usedKeys removal test — and a warning is logged. Shared by
        // all three keyed-diff map-build sites (synchronous keyed, time-sliced Pass2BuildMap, general).
        internal void RegisterOldKey(VNode? node, int index,
            Dictionary<ChildKey, (int index, VNode? node)> map, HashSet<int>? orphaned)
        {
            var key = ReconcileKey(node, index);
            if (map.TryAdd(key, (index, node))) return;

            FiberLogger.LogWarning("ReconcileKeying",
                $"Duplicate key detected in keyed reconciliation: {key}. " +
                "Later element overwrites earlier one, causing unnecessary destroy/recreate.");
            orphaned!.Add(map[key].index);
            map[key] = (index, node);
        }

        // CanPatch decision — whether an old and new node share enough identity to patch the existing
        // element in place rather than remove + recreate. Shared by the keyed/indexed fast path and the
        // general-path CommitLeaf.
        internal static bool CanPatch(VNode? oldNode, VNode? newNode)
        {
            if (oldNode == null || newNode == null)
            {
                return false;
            }

            switch (oldNode)
            {
                case ElementNode oldElem when newNode is ElementNode newElem:
                {
                    if (oldElem.ElementType != newElem.ElementType)
                    {
                        return false;
                    }
                    return (oldElem.WrapElement != null) == (newElem.WrapElement != null);
                }
                case TextNode when newNode is TextNode:
                case MemoNode when newNode is MemoNode:
                case AnimatePresenceNode when newNode is AnimatePresenceNode:
                case SuspenseNode when newNode is SuspenseNode:
                case VirtualListNode when newNode is VirtualListNode:
                    return true;
                case PortalNode oldPortal when newNode is PortalNode newPortal:
                    // (TargetId, Layer) is a one-of pair: a registry portal and a layer portal must
                    // never patch into each other, and two layer portals patch only on the same
                    // layer — a mismatch remounts, releasing the old slot range on the old target.
                    return oldPortal.TargetId == newPortal.TargetId && oldPortal.Layer == newPortal.Layer;
                case WorldSpaceNode when newNode is WorldSpaceNode:
                    // Transform, size and children all patch in place on the live host.
                    return true;
                case MotionNode oldMotion when newNode is MotionNode newMotion:
                    return oldMotion.ElementType == newMotion.ElementType;
                case ContextProviderNode oldProvider when newNode is ContextProviderNode newProvider:
                    return ReferenceEquals(oldProvider.ContextKey, newProvider.ContextKey);
                case ComponentNode oldComp when newNode is ComponentNode newComp:
                    // Every [Component] function compiles to the same CLR type, so type alone cannot tell two
                    // distinct components apart. Compare component identity (Body.Method) too: a
                    // different component at the same position must remount rather than have A's element patched as B.
                    return oldComp.GetType() == newComp.GetType()
                        && Equals(oldComp.ResolvedIdentity, newComp.ResolvedIdentity);
                case OutletNode when newNode is OutletNode:
                    return true;
                default:
                    return false;
            }
        }
    }
}
