using System;
using System.Collections.Generic;

namespace Velvet
{
    // What a render is handed by the walk that reaches the fiber, kept as a unit so that what a render nothing
    // commits wrote can be written back whole.
    internal readonly struct FiberInputs
    {
        internal IHookRefSetter? ExternalRef { get; init; }
        internal Func<VNode>? Body { get; init; }
        internal ComponentNode? SourceNode { get; init; }
        internal VNode?[]? SourceTree { get; init; }
        internal object? Props { get; init; }

        internal static FiberInputs Of(ComponentFiber fiber) => new()
        {
            ExternalRef = fiber.ExternalRef,
            Body = fiber.Body,
            SourceNode = fiber.SourceNode,
            SourceTree = fiber.SourceTree,
            Props = fiber.Props,
        };

        internal void WriteTo(ComponentFiber fiber)
        {
            fiber.ExternalRef = ExternalRef;
            fiber.Body = Body;
            fiber.SourceNode = SourceNode;
            fiber.SourceTree = SourceTree;
            fiber.Props = Props;
        }
    }

    // An inline fiber a pass rendered in place: what it held before the render, and the tree the render left as
    // its PreviousTree.
    internal readonly record struct SubsumedRender(
        ComponentFiber Fiber, FiberInputs Before, VNode?[]? TreeBefore, VNode?[]? TreeRendered);

    // The renders of the pass on the stack, which stay tentative until the pass ends. A suspend that nothing in the
    // pass catches leaves it abandoned, and Abandon takes back what those renders changed on the fibers: the inputs
    // each was handed, the tree each left as its patch baseline, and the hook state each staged. A pass that ends
    // any other way keeps them, and Close commits the hook state. React discards the work in progress of an
    // abandoned render the same way.
    //
    // The outermost RenderAndReconcile of a context opens the journal and is the only caller that closes it.
    internal sealed class FiberPassJournal
    {
        private readonly List<SuspenseRender> _hooks = new();
        private readonly List<SubsumedRender> _subsumed = new();
        private ComponentFiber? _owner;

        internal bool IsOpen => _owner != null;

        // Whether reconciler is the one the owner's render runs its own rows through. A pass another fiber starts
        // while the journal is open is not the owner's to abandon.
        internal bool IsOwnedBy(Reconciler reconciler) => _owner != null && ReferenceEquals(_owner.Reconciler, reconciler);

        // Returns whether this call opened the journal, which makes the caller its owner.
        internal bool TryOpen(ComponentFiber owner)
        {
            if (_owner != null) return false;
            _owner = owner;
            return true;
        }

        internal void RecordHooks(SuspenseRender render) => _hooks.Add(render);

        internal void RecordHooks(List<SuspenseRender> renders) => _hooks.AddRange(renders);

        // Called once the render has returned, so a render that threw is not in the journal: the catch that saw
        // it writes its inputs back itself.
        internal void RecordSubsumed(ComponentFiber fiber, in FiberInputs before, VNode?[]? treeBefore)
        {
            if (_owner == null) return;
            _subsumed.Add(new SubsumedRender(fiber, before, treeBefore, fiber.PreviousTree));
        }

        internal void Close()
        {
            try
            {
                for (var i = 0; i < _hooks.Count; i++)
                {
                    var fiber = _hooks[i].Fiber;
                    if (!fiber.IsDisposed && fiber.IsMounted) FiberHookCommit.CommitSuspenseRender(_hooks[i]);
                }
            }
            finally
            {
                _hooks.Clear();
                _subsumed.Clear();
                _owner = null;
            }
        }

        // Runs in the catch of the top-level reconcile, ahead of the finally that returns the old trees the inline
        // renders deferred: once a fiber holds its old tree again, that sweep spares it.
        internal void Abandon()
        {
            List<VNode?[]>? discarded = null;
            // Newest first, so a fiber rendered twice in the pass ends at what it held before the first.
            for (var i = _subsumed.Count - 1; i >= 0; i--)
            {
                var render = _subsumed[i];
                var fiber = render.Fiber;
                if (fiber.IsDisposed || !fiber.IsMounted) continue;
                render.Before.WriteTo(fiber);
                // A tree other than the one this render left was put there by something else, such as a boundary's
                // fallback, and is not this render's to take back.
                if (!ReferenceEquals(fiber.PreviousTree, render.TreeRendered)) continue;
                fiber.PreviousTree = render.TreeBefore;
                if (render.TreeRendered != null && !ReferenceEquals(render.TreeRendered, render.TreeBefore))
                {
                    (discarded ??= new()).Add(render.TreeRendered);
                }
            }
            for (var i = _hooks.Count - 1; i >= 0; i--)
            {
                var render = _hooks[i];
                if (render.Fiber.IsDisposed || !render.Fiber.IsMounted) continue;
                // The fiber the pass started from cut its own effects in the catch of its render, and the retry its
                // suspend asks for renders it. Marked dirty, the next flush to visit it would render it ahead of that
                // retry.
                if (ReferenceEquals(render.Fiber, _owner)) continue;
                FiberHookCommit.DiscardSuspenseRender(render);
            }
            _hooks.Clear();
            _subsumed.Clear();
            // Released rather than returned: a memoized fiber's slot can hand the same tree to the render that
            // retries, so nothing may recycle it.
            if (discarded == null) return;
            foreach (var tree in discarded) FiberTreeReturn.Release(tree);
        }
    }
}
