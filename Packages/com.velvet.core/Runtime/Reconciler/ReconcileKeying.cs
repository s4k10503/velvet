#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    // Reconciliation identity, shared by both reconcile paths: which ChildKey a node participates under
    // (explicit VNode.Key, a Fragment-scoped key, or its positional index when unkeyed), the old-side
    // key→(index,node) map, and the CanPatch decision (whether two nodes share enough identity to patch in
    // place vs remove + recreate). The fast path (Indexed/Keyed diff in ChildReconciler) and the general path
    // (live-context walk in GeneralPathReconciler) both resolve identity through this single collaborator.
    //
    // A scoped key is computed for one emitted occurrence and travels beside it rather than being looked up
    // by node: one VNode can be emitted at several positions — the children a V.List's per-item copies of one
    // Fragment share — and each position is a row of its own.
    internal sealed class ReconcileKeying
    {
        internal bool HasAnyKey(VNode?[] nodes)
        {
            foreach (var node in nodes)
            {
                if (node?.Key != null)
                {
                    return true;
                }
            }
            return false;
        }

        // Whether any old leaf was emitted under an explicit key — its own, or one a keyed wrapper scoped.
        internal bool HasAnyKey(List<ChildKey> keys)
        {
            for (var i = 0; i < keys.Count; i++)
            {
                if (!keys[i].Equals(ChildKey.Positional(i))) return true;
            }
            return false;
        }

        // siblingIndex counts the flat list the diff matches; nodeIndex counts the array the leaf is written in,
        // which is what a scope composes an unkeyed leaf with.
        internal static ChildKey ScopedKey(VNode? node, string? scope, int nodeIndex, int siblingIndex)
        {
            var key = scope == null ? node?.Key : FiberKeying.LeafScope(scope, node?.Key, nodeIndex);
            return key != null ? ChildKey.Explicit(key) : ChildKey.Positional(siblingIndex);
        }

        // The key of a sibling in a child array holding no wrapper, so no scope can apply. An unkeyed node
        // reconciles by its full sibling index — keyed siblings occupy an index too — so the same array slot
        // patches across renders instead of being destroyed and recreated.
        internal ChildKey ReconcileKey(VNode? node, int siblingIndex)
        {
            var key = node?.Key;
            return key != null ? ChildKey.Explicit(key) : ChildKey.Positional(siblingIndex);
        }

        // Registers one old node under its reconcile key while building the keyed-diff old→(index,node)
        // map. On a duplicate key the later writer wins (the new-side lookup only ever resolves to the
        // last entry): the displaced earlier index is recorded as an orphan so the removal pass cleans
        // it up — it is not covered by the usedKeys removal test — and a warning is logged. Shared by
        // all three keyed-diff map-build sites (synchronous keyed, time-sliced Pass2BuildMap, general).
        internal static void RegisterOldKey(ChildKey key, VNode? node, int index,
            Dictionary<ChildKey, (int index, VNode? node)> map, HashSet<int>? orphaned)
        {
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
                    return oldElem.ElementType == newElem.ElementType
                        && (oldElem.WrapElement != null) == (newElem.WrapElement != null);
                case PortalNode oldPortal when newNode is PortalNode newPortal:
                    // (TargetId, Layer, TargetElement) is a one-of triple: portals with different kinds
                    // of target must never patch into each other, and two of a kind patch only on the
                    // same target — a mismatch remounts, releasing the old slot range on the old one.
                    // The element term is what moves an element-valued portal when its container
                    // changes: there is no patch that could, since the children are already parented
                    // under the old one.
                    return oldPortal.TargetId == newPortal.TargetId
                           && oldPortal.Layer == newPortal.Layer
                           && ReferenceEquals(oldPortal.TargetElement, newPortal.TargetElement);
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
                default:
                    return PatchesOnKindAlone(oldNode, newNode);
            }
        }

        // The kinds above carry a discriminator the pair can disagree on; these carry none, so matching kind
        // is the whole test. Every VNode kind is sealed and none derives from another, so splitting the
        // dispatch in two cannot reorder a match. Reaching this from an old node whose kind is listed above
        // means the new node's kind differs, which is the same remount the switch would have decided.
        private static bool PatchesOnKindAlone(VNode oldNode, VNode newNode) => oldNode switch
        {
            TextNode => newNode is TextNode,
            MemoNode => newNode is MemoNode,
            AnimatePresenceNode => newNode is AnimatePresenceNode,
            SuspenseNode => newNode is SuspenseNode,
            VirtualListNode => newNode is VirtualListNode,
            // Transform, size and children all patch in place on the live host.
            WorldSpaceNode => newNode is WorldSpaceNode,
            _ => false,
        };
    }
}
