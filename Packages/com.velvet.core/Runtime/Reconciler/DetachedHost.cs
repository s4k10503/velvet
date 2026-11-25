#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // One occurrence of a node whose children mount outside the walk that reaches it: a V.VirtualList, whose
    // controller owns it, or a V.Portal / V.WorldSpace, found by its placeholder. Each walk that reaches the node
    // captures what the cursor holds around it within the frame (ComponentContextStack.EnterFrame) — the Providers
    // the owner's own output, or an enclosing host's children, put there — and never what lies above the owner,
    // which FiberContextSpine rebuilds from committed outputs. A render of a detached child reads the capture
    // Resolve picks, so what an earlier render of the owner put there reaches no child once a later one is
    // committed.
    internal sealed class DetachedHost
    {
        // A committed capture has to outlast the captures of the aborted or dropped renders taken after it.
        private const int KeptCaptures = 4;

        private static readonly ConditionalWeakTable<VisualElement, DetachedHost> s_ofPlaceholder = new();

        private readonly List<DetachedHostCapture> _captures = new(2);

        internal DetachedHost(ComponentFiber? owner) => Owner = owner;

        // FiberStack.Current where the walk reaches the node, which is the fiber the detached children parent under.
        internal ComponentFiber? Owner { get; }

        internal static DetachedHost OfPlaceholder(VisualElement placeholder, ComponentFiber? owner)
        {
            if (!s_ofPlaceholder.TryGetValue(placeholder, out var host))
            {
                host = new DetachedHost(owner);
                s_ofPlaceholder.Add(placeholder, host);
            }
            return host;
        }

        internal static DetachedHost? OfPlaceholder(VisualElement placeholder)
            => s_ofPlaceholder.TryGetValue(placeholder, out var host) ? host : null;

        // Taken with the cursor as it stands around the node, before anything below the node reconciles.
        internal void Capture(ReconcilerContext ctx, VNode?[]? children)
        {
            var stack = ctx.ComponentContextStack;
            var frame = stack.Frame;
            var pushed = new List<KeyValuePair<object, object>>();
            stack.CollectPushedInFrame(pushed);
            pushed.RemoveAll(MotionContext.TravelsOnlyInStamps);
            // A fallback swap reconciles the boundary's rows with no CurrentFiberTree of its own.
            var capture = new DetachedHostCapture(
                this,
                frame.EnclosingCapture == null ? Owner?.ReconcilingTree ?? ctx.CurrentFiberTree : null,
                frame.EnclosingCapture,
                pushed.ToArray(),
                MotionContext.OutlivingPass(stack.SnapshotTops())!,
                children);
            for (var i = _captures.Count - 1; i >= 0; i--)
            {
                if (capture.SameRenderAs(_captures[i])) _captures.RemoveAt(i);
            }
            if (_captures.Count == KeptCaptures) _captures.RemoveAt(0);
            _captures.Add(capture);
        }

        // The latest capture of a render that is committed now, else the latest there is.
        internal DetachedHostCapture? Resolve()
        {
            for (var i = _captures.Count - 1; i >= 0; i--)
            {
                if (IsCommitted(_captures[i])) return _captures[i];
            }
            return _captures.Count > 0 ? _captures[_captures.Count - 1] : null;
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
        internal readonly VNode?[]? Tree;
        internal readonly DetachedHostCapture? EnclosingCapture;
        // Each context's entries in the order they were pushed.
        internal readonly KeyValuePair<object, object>[] Local;
        // The top value of every context where the capture was taken, less the orchestration, which a range rendered
        // under this capture pushes under everything else, as FiberContextSpine pushes a stamp's.
        internal readonly List<KeyValuePair<object, object>> Tops;
        // A Portal's children in the render that reached it; null for a list.
        internal readonly VNode?[]? Children;

        internal DetachedHostCapture(
            DetachedHost host, VNode?[]? tree, DetachedHostCapture? enclosingCapture,
            KeyValuePair<object, object>[] local, List<KeyValuePair<object, object>> tops, VNode?[]? children)
        {
            Host = host;
            Tree = tree;
            EnclosingCapture = enclosingCapture;
            Local = local;
            Tops = tops;
            Children = children;
        }

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
            => ReferenceEquals(EnclosingCapture, other.EnclosingCapture) && ReferenceEquals(Tree, other.Tree);

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

    // The item node a VirtualList row was handed by each patch, against the list's capture current at that patch, so
    // the row's own render walks the node of the render its list resolves to.
    internal sealed class DetachedRow
    {
        private const int KeptNodes = 4;

        private readonly List<(DetachedHostCapture? Under, VNode?[] Nodes)> _nodes = new(1);

        internal void Note(DetachedHostCapture? under, VNode node)
        {
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_nodes[i].Under, under)) _nodes.RemoveAt(i);
            }
            if (_nodes.Count == KeptNodes) _nodes.RemoveAt(0);
            _nodes.Add((under, new[] { node }));
        }

        internal VNode?[]? NodesUnder(DetachedHostCapture? under)
        {
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_nodes[i].Under, under)) return _nodes[i].Nodes;
            }
            return _nodes.Count > 0 ? _nodes[_nodes.Count - 1].Nodes : null;
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
            => Row != null ? Row.NodesUnder(resolved) ?? DescendantNodes : resolved?.Children ?? DescendantNodes;
    }
}
