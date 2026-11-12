#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // The walk's part of patching one node: FiberNodePatcher writes the element itself in two halves, and between
    // them this reconciles the node's children under the scopes and context the node hands them, beside the
    // bookkeeping those reconciles keep in the walk.
    internal sealed class NodePatchWalk
    {
        private readonly ReconcilerContext _ctx;
        private readonly FiberNodePatcher _patcher;
        private IReconcilerHost _host = null!;

        public NodePatchWalk(ReconcilerContext ctx, FiberNodePatcher patcher)
        {
            _ctx = ctx;
            _patcher = patcher;
        }

        internal FiberNodePatcher Patcher => _patcher;

        internal void SetHost(IReconcilerHost host)
        {
            if (_host != null)
            {
                throw new System.InvalidOperationException("[NodePatchWalk] SetHost called twice");
            }
            _host = host;
        }

        // Applies the oldNode → newNode diff to the existing element.
        // Contract (important): a patch must not modify the childCount or child order of element's parent.
        // Reconciling the child container (children of element itself) is not subject to this constraint, but
        // removing/inserting the element from its parent is forbidden. The diff inserts new leaves by slot, so a
        // patch that moved the slots would have a later insert land in the wrong place. New VNode types must preserve
        // this contract.
        // Returns what occupies the element's slot once the patch is done (FiberNodePatcher.PatchSelfPost).
        internal VisualElement PatchNode(VisualElement element, VNode? oldNode, VNode? newNode)
        {
            var patch = new FiberNodePatcher.SelfPatch(element, oldNode, newNode);
            _patcher.PatchSelfPre(ref patch);
            ReconcileChildrenOf(ref patch);
            return _patcher.PatchSelfPost(ref patch);
        }

        private void ReconcileChildrenOf(ref FiberNodePatcher.SelfPatch patch)
        {
            switch (patch.Kind)
            {
                case FiberNodePatcher.SelfPatchKind.Element:
                    ReconcileOwnRows(patch);
                    break;
                case FiberNodePatcher.SelfPatchKind.Motion:
                    ReconcileUnderMotion(patch);
                    break;
                case FiberNodePatcher.SelfPatchKind.Portal:
                    ReconcilePortalRange(ref patch);
                    break;
                case FiberNodePatcher.SelfPatchKind.Provider:
                    ReconcileUnderProvider(patch);
                    break;
                case FiberNodePatcher.SelfPatchKind.Component:
                    _patcher.HandleComponentMount(patch.Element, (ComponentNode)patch.NewNode!);
                    break;
                case FiberNodePatcher.SelfPatchKind.VirtualList:
                    patch.VirtualList!.Update((VirtualListNode)patch.NewNode!);
                    break;
            }
        }

        // BaseElementNode children are inline-expanded by both the initial CreateElement chain (which routes through
        // ReconcileChildren) and this patch path. Keeping both passes on the same expansion strategy means
        // ComponentNode siblings under an ElementNode appear as direct VE children — never wrapped in the container
        // the wrapper-mount path uses, which would put an element between this container and each Component's
        // output.
        // The ref is synced by the walk rather than by PatchSelfPost, so a mode deferring the halves' writes leaves it
        // here: SyncRefCallback queues the setup under the fiber and pass current at the call and sends a dropped ref's
        // cleanup failure to that fiber's boundary, which a call deferred past the walk would read as whatever is
        // current then.
        private void ReconcileOwnRows(in FiberNodePatcher.SelfPatch patch)
        {
            var container = patch.Container!;
            FiberCommitWork.OpenOwnRows(_ctx, container);
            int? rowsBehindRanges;
            using var host = _ctx.OpenChildVariantHost(patch.HostsChildVariant);
            try
            {
                _host.ReconcileChildren(container, patch.OldChildren, patch.NewChildren);
            }
            finally
            {
                rowsBehindRanges = FiberCommitWork.PopOwnRows(_ctx, container);
            }
            if (rowsBehindRanges != null)
            {
                _ctx.HostCommit.FollowOwnRows(container, rowsBehindRanges.Value);
            }
            _ctx.SyncRefCallback(patch.Self, ((BaseElementNode)patch.NewNode!).RefCallback);
        }

        private void ReconcileUnderMotion(in FiberNodePatcher.SelfPatch patch)
        {
            var node = (MotionNode)patch.NewNode!;
            var motion = patch.Motion;
            // Mirror the create path's anchor-element recording: a presence keyed child that REUSES its
            // element (a ghost's old-side reproduction, a cancelled exit's re-entry) reaches its Motion
            // through this patch, and the expansion still needs the Motion's own element to dispatch
            // variant enter/exit against. Written by the walk rather than by PatchSelfPre, so a mode deferring the
            // halves' writes still has it written when the expansion reads it, as soon as the emission holding this
            // patch returns (GeneralPathReconciler.EmitPresenceChildAsAnchor).
            if (ReferenceEquals(node, _ctx.PresenceAnchorMotion))
            {
                _ctx.PresenceAnchorMotionElement = patch.Self;
            }
            _patcher.EnterMotionChildren(node, motion.ChildLabel, motion.InitialLabel, motion.ChildOrchestration,
                motion.Playback);
            try
            {
                using var inside = new LayoutIdMotionScope(_ctx, node.LayoutId != null);
                ReconcileOwnRows(patch);
            }
            finally
            {
                _patcher.ExitMotionChildren(node, motion.ChildLabel);
            }
        }

        private void ReconcilePortalRange(ref FiberNodePatcher.SelfPatch patch)
        {
            var placeholder = patch.Element;
            var target = patch.Container!;
            // Restored rather than cleared: a Portal declared inside another Portal's children patches from
            // within this call, and what the outer one mounts after that returns is still the outer one's.
            var enclosingPortal = _ctx.CurrentPortalPlaceholder;
            _ctx.CurrentPortalPlaceholder = placeholder;
            // The mount half sets the same scope for the same reason — DrainPendingPortalMounts owns it —
            // and the two have to agree or a patch looks this Portal's children up under a key the mount
            // did not register them by. No push here: this runs from the reconcile of the tree the
            // declaring component returned, so its fiber is already current.
            var enclosingChildScope = _ctx.EnterPortalChildKeyScope(placeholder);
            _patcher.OpenPortalRange(ref patch);
            var mounted = false;
            try
            {
                PointerEventsScope.NoteReconciledInto(_ctx, target);
                _host.ReconcileChildren(target, patch.OldChildren, patch.NewChildren, slotStart: patch.SlotStart);
                mounted = true;
            }
            finally
            {
                _ctx.ExitPortalChildKeyScope(enclosingChildScope);
                _ctx.CurrentPortalPlaceholder = enclosingPortal;
                _patcher.ClosePortalRange(ref patch, mounted);
            }
        }

        // A Provider reached as a node-typed keyed entry (e.g. inside an AnimatePresence subtree, or as a MemoNode's
        // resolved inner), on a path where GeneralPathReconciler.ExpandInlineRecursive's inline Provider expansion
        // does not apply: the new value is pushed, and a changed one propagated, before the children reconcile
        // under it.
        private void ReconcileUnderProvider(in FiberNodePatcher.SelfPatch patch)
        {
            var oldNode = (ContextProviderNode)patch.OldNode!;
            var newNode = (ContextProviderNode)patch.NewNode!;
            newNode.PushContext(_ctx.ComponentContextStack);
            try
            {
                if (newNode.HasValueChanged(oldNode))
                {
                    _host.NotifyContextValueChange(newNode);
                    // A replaced VNode-valued provider value has no other retirement point: the sweep
                    // deliberately never returns provider values (a consumer may hold the CURRENT one),
                    // but a superseded value is out of distribution for good — every subscribed consumer
                    // re-renders off the change notification above and commits the NEW value's nodes,
                    // and the pass-scoped release staging keeps the old parts un-rentable until those
                    // re-renders have run. A consumer that committed the old node retires it through its
                    // own old tree too; pool returns are idempotent, so the overlap is harmless.
                    var oldValueRoot = oldNode.BoxedValueForRecycleMark;
                    if (oldValueRoot != null && !ReferenceEquals(oldValueRoot, newNode.BoxedValueForRecycleMark))
                    {
                        switch (oldValueRoot)
                        {
                            case VNode oldValueNode:
                                FiberTreeReturn.ReturnRetiredTree(
                                    FiberTreeReturn.NormalizeToArray(oldValueNode), _ctx.FiberStack.Current);
                                break;
                            case VNode?[] oldValueTree:
                                FiberTreeReturn.ReturnRetiredTree(oldValueTree, _ctx.FiberStack.Current);
                                break;
                        }
                    }
                }
                _host.ReconcileChildren(patch.Container!, patch.OldChildren, patch.NewChildren);
            }
            finally
            {
                newNode.PopContext(_ctx.ComponentContextStack);
            }
        }
    }
}
