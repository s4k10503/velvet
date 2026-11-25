#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // One occurrence of a node whose children mount outside the walk that reaches it: a V.VirtualList, whose
    // controller owns it, or a V.Portal / V.WorldSpace, found by its placeholder. A walk that creates or patches the
    // node captures what the cursor holds around it within the frame (ComponentContextStack.EnterFrame) — the Providers
    // the owner's own output, or an enclosing host's children, put there — which goes on over what FiberContextSpine
    // rebuilds from committed outputs; for a list it also takes the top of every context, which goes under that for
    // the rows a range renders outside a pass. A render of a detached child reads the capture Resolve picks.
    internal sealed class DetachedHost
    {
        // Bounds the captures held beside the committed one, which _committed keeps whatever this evicts.
        private const int KeptCaptures = 4;

        private static readonly ConditionalWeakTable<VisualElement, DetachedHost> s_ofPlaceholder = new();

        // A capture names its render's output weakly, so a record holds neither its owner's output tree nor a
        // Portal's children: a capture whose tree has been collected is dropped at the next capture or resolve,
        // except the latest.
        private readonly List<DetachedHostCapture> _captures = new(2);

        // The latest capture whose tree was its owner's committed output, which Resolve reads where no capture is of a
        // render that is the owner's now: a render committed since that did not reach the node took none, and a later
        // one taken by a render that was not committed is passed over.
        private DetachedHostCapture? _committed;

        // Tops is read only where a list's rows render outside a pass (Reconciler.BeginDetachedItemScope), so a Portal's
        // capture takes none.
        private readonly bool _takesTops;

        private static readonly List<KeyValuePair<object, object>> s_pushed = new();
        private static readonly Predicate<KeyValuePair<object, object>> s_travelsOnlyInStamps =
            MotionContext.TravelsOnlyInStamps;

        internal DetachedHost(ComponentFiber? owner, bool takesTops)
        {
            Owner = owner;
            _takesTops = takesTops;
        }

        // FiberStack.Current where the walk reaches the node: the fiber the detached children parent under while it is
        // mounted.
        internal ComponentFiber? Owner { get; }

        internal static DetachedHost OfPlaceholder(VisualElement placeholder, ComponentFiber? owner)
        {
            if (!s_ofPlaceholder.TryGetValue(placeholder, out var host))
            {
                host = new DetachedHost(owner, takesTops: false);
                s_ofPlaceholder.Add(placeholder, host);
            }
            return host;
        }

        // Whether a walk of node reaches a Portal or a list in the frame it walks node in: a component's output is
        // walked in a frame of its own, and a memo's inner, not being in the tree, counts as holding one.
        internal static bool HeldIn(VNode? node) => node switch
        {
            PortalNode or WorldSpaceNode or VirtualListNode or MemoNode => true,
            BaseElementNode element => AnyHeldIn(element.Children),
            FragmentNode fragment => AnyHeldIn(fragment.Children),
            ContextProviderNode provider => AnyHeldIn(provider.Children),
            SuspenseNode suspense => AnyHeldIn(suspense.Children) || HeldIn(suspense.Fallback),
            AnimatePresenceNode presence => AnyHeldIn(presence.Children),
            _ => false,
        };

        private static bool AnyHeldIn(VNode?[]? nodes)
        {
            if (nodes == null) return false;
            foreach (var node in nodes)
            {
                if (HeldIn(node)) return true;
            }
            return false;
        }

        // Taken with the cursor as it stands around the node, before anything below the node reconciles.
        internal void Capture(ReconcilerContext ctx, VNode?[]? children)
        {
            var stack = ctx.ComponentContextStack;
            var frame = stack.Frame;
            // A fallback swap reconciles the boundary's rows with no CurrentFiberTree of its own.
            var tree = frame.EnclosingCapture == null ? Owner?.ReconcilingTree ?? ctx.CurrentFiberTree : null;
            // A new capture each render rather than the latest one renamed where it holds the same: a row's node is noted
            // against the capture its list's render took (DetachedRow), and a capture taken inside a Portal's children
            // is committed while the Portal resolves to the one it was taken under, so a render that is not committed
            // would pass its rows' nodes and those captures off as the committed render's.
            s_pushed.Clear();
            stack.CollectPushedInFrame(s_pushed);
            s_pushed.RemoveAll(s_travelsOnlyInStamps);
            var capture = new DetachedHostCapture(
                this, tree, frame.EnclosingCapture,
                s_pushed.Count == 0 ? Array.Empty<KeyValuePair<object, object>>() : s_pushed.ToArray(),
                _takesTops ? MotionContext.OutlivingPass(stack.SnapshotTops()) : null,
                children);
            s_pushed.Clear();
            for (var i = _captures.Count - 1; i >= 0; i--)
            {
                if (capture.SameRenderAs(_captures[i])) _captures.RemoveAt(i);
            }
            _captures.Add(capture);
            DropDeadCaptures();
            if (_captures.Count > KeptCaptures) _captures.RemoveAt(0);
            if (capture.EnclosingCapture != null || Owner == null) return;
            // An owner rendered inline in a walk holds the output that walk expands before it reaches the node.
            if (ReferenceEquals(capture.Tree, Owner.PreviousTree)) _committed = capture;
            else (Owner.CapturesAwaitingCommit ??= new List<DetachedHostCapture>(1)).Add(capture);
        }

        // Where FiberRenderer makes tree fiber's output: the latest capture of each host that names it becomes that
        // host's committed one. A render whose output is not kept leaves its captures to that next commit, which names
        // another tree.
        internal static void CommitCapturesOf(ComponentFiber fiber, VNode?[] tree)
        {
            if (fiber.CapturesAwaitingCommit is not { Count: > 0 } captures) return;
            foreach (var capture in captures)
            {
                if (ReferenceEquals(capture.Tree, tree)) capture.Host._committed = capture;
            }
            captures.Clear();
        }

        // The latest capture of a render that is the owner's now, else the latest committed one, else the latest.
        internal DetachedHostCapture? Resolve()
        {
            DropDeadCaptures();
            for (var i = _captures.Count - 1; i >= 0; i--)
            {
                if (IsCommitted(_captures[i])) return _captures[i];
            }
            return _committed ?? (_captures.Count > 0 ? _captures[_captures.Count - 1] : null);
        }

        internal bool Holds(DetachedHostCapture capture)
            => ReferenceEquals(capture, _committed) || _captures.Contains(capture);

        // A capture whose render can no longer be committed: its tree collected, or the capture it was taken under
        // dropped by its own host. The latest is kept whatever it names.
        private void DropDeadCaptures()
        {
            for (var i = _captures.Count - 2; i >= 0; i--)
            {
                var capture = _captures[i];
                var dead = capture.EnclosingCapture is { } enclosing
                    ? !enclosing.Host.Holds(enclosing)
                    : capture.Tree == null;
                if (dead) _captures.RemoveAt(i);
            }
        }

        // The trees OwnOutput in FiberContextSpine reads an owner's output from. A capture taken inside an enclosing
        // host's drain or range is committed while that host still resolves to the capture it was taken under.
        private bool IsCommitted(DetachedHostCapture capture)
        {
            if (capture.EnclosingCapture is { } enclosing)
            {
                return ReferenceEquals(enclosing.Host.Resolve(), enclosing);
            }
            if (Owner == null) return true;
            var tree = capture.Tree;
            return tree != null
                && (ReferenceEquals(tree, Owner.ReconcilingTree)
                    || ReferenceEquals(tree, Owner.PreviousTree)
                    || ReferenceEquals(tree, Owner.PendingOldTree));
        }
    }

    internal sealed class DetachedHostCapture
    {
        internal readonly DetachedHost Host;
        private readonly WeakReference<VNode?[]>? _tree;
        private readonly WeakReference<VNode?[]>? _children;
        internal readonly DetachedHostCapture? EnclosingCapture;
        // Each context's entries in the order they were pushed.
        internal readonly KeyValuePair<object, object>[] Local;
        // The top value of every context where the capture was taken, less the orchestration, which a range rendered
        // under this capture pushes under everything else, as FiberContextSpine pushes a stamp's. Null for a Portal's.
        internal readonly List<KeyValuePair<object, object>>? Tops;

        internal DetachedHostCapture(
            DetachedHost host, VNode?[]? tree, DetachedHostCapture? enclosingCapture,
            KeyValuePair<object, object>[] local, List<KeyValuePair<object, object>>? tops, VNode?[]? children)
        {
            Host = host;
            _tree = tree != null ? new WeakReference<VNode?[]>(tree) : null;
            EnclosingCapture = enclosingCapture;
            Local = local;
            Tops = tops;
            _children = children != null ? new WeakReference<VNode?[]>(children) : null;
        }

        // The owner output the walk was expanding; null once it has been collected, or for a capture taken under an
        // enclosing host's.
        internal VNode?[]? Tree => _tree != null && _tree.TryGetTarget(out var tree) ? tree : null;

        // A Portal's children in the render that reached it; null once collected, and for a list.
        internal VNode?[]? Children => _children != null && _children.TryGetTarget(out var children) ? children : null;

        // Each key pushed goes into pushedKeys for the pop.
        internal static void PushSnapshot(
            ComponentContextStack stack, List<KeyValuePair<object, object>> snapshot, List<object> pushedKeys)
        {
            for (var i = 0; i < snapshot.Count; i++)
            {
                stack.PushRaw(snapshot[i].Key, snapshot[i].Value);
                pushedKeys.Add(snapshot[i].Key);
            }
        }

        internal bool SameRenderAs(DetachedHostCapture other)
            => ReferenceEquals(EnclosingCapture, other.EnclosingCapture)
                && (EnclosingCapture != null || Tree is { } tree && ReferenceEquals(tree, other.Tree));

        // Outermost first, so a nearer Provider of a context ends on top. The enclosing host is asked again rather than
        // taken as it was here: a walk of it that did not reach this host still decides what lies around it. Each key
        // pushed goes into pushedKeys for the pop.
        internal void PushChain(ComponentContextStack stack, List<object> pushedKeys)
        {
            EnclosingCapture?.Host.Resolve()?.PushChain(stack, pushedKeys);
            for (var i = 0; i < Local.Length; i++)
            {
                stack.PushRaw(Local[i].Key, Local[i].Value);
                pushedKeys.Add(Local[i].Key);
            }
        }
    }

    // The item node a VirtualList row was created or patched with, against the list's capture current then, so the
    // row's own render walks the node of the render its list resolves to. A node noted against a capture the list no
    // longer holds is dropped at the row's next note or read, the latest kept whatever it was noted against.
    internal sealed class DetachedRow
    {
        private readonly List<(DetachedHostCapture? Under, VNode?[] Nodes)> _nodes = new(1);

        internal void Note(DetachedHost list, DetachedHostCapture? under, VNode node)
        {
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_nodes[i].Under, under)) _nodes.RemoveAt(i);
            }
            _nodes.Add((under, new[] { node }));
            DropNodesOfDeadCaptures(list);
        }

        internal VNode?[]? NodesUnder(DetachedHost list, DetachedHostCapture? under)
        {
            DropNodesOfDeadCaptures(list);
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_nodes[i].Under, under)) return _nodes[i].Nodes;
            }
            return _nodes.Count > 0 ? _nodes[_nodes.Count - 1].Nodes : null;
        }

        private void DropNodesOfDeadCaptures(DetachedHost list)
        {
            for (var i = _nodes.Count - 2; i >= 0; i--)
            {
                if (_nodes[i].Under is not { } under || !list.Holds(under)) _nodes.RemoveAt(i);
            }
        }
    }

    // Set on the top-level child fiber of a detached mount, whose children reconcile outside the walk that reached
    // their host, so FiberContextSpine cannot find them in their parent's output. Host is the occurrence that placed
    // them. DescendantNodes is what held the fiber when it was created — the Portal's children, or the row's item
    // node — which the spine still walks where the current ones no longer hold it (a child exiting an
    // AnimatePresence). Anchor is the fiber the children parent under, the registry parent that walk matches
    // against.
    internal sealed class DetachedMountContext
    {
        internal readonly DetachedHost Host;
        // Null for a Portal's children, whose current nodes are the Portal's capture's.
        internal readonly DetachedRow? Row;
        internal readonly VNode?[]? DescendantNodes;
        // The top value of every context where the fiber was created, less the orchestration
        // (MotionContext.OutlivingPass), which FiberContextSpine pushes under everything its edges rebuild.
        internal readonly List<KeyValuePair<object, object>> EnclosingSnapshot;
        internal readonly ComponentFiber Anchor;
        internal readonly bool RootProviderChildrenStartAtWalkRoot;

        internal DetachedMountContext(
            DetachedHost host, DetachedRow? row, VNode?[]? descendantNodes, ComponentContextStack stack,
            ComponentFiber anchor, bool rootProviderChildrenStartAtWalkRoot = false)
        {
            Host = host;
            Row = row;
            DescendantNodes = descendantNodes;
            EnclosingSnapshot = MotionContext.OutlivingPass(stack.SnapshotTops())!;
            Anchor = anchor;
            RootProviderChildrenStartAtWalkRoot = rootProviderChildrenStartAtWalkRoot;
        }

        internal VNode?[]? CurrentNodes(DetachedHostCapture? resolved)
            => Row != null ? Row.NodesUnder(Host, resolved) ?? DescendantNodes : resolved?.Children ?? DescendantNodes;
    }
}
