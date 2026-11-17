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

    // What a fiber held in its previous and pending old trees and its Portal stamp.
    internal readonly record struct FiberTrees(
        VNode?[]? Previous, VNode?[]? PendingOld, UnityEngine.UIElements.VisualElement? OwningPortal);

    // What a fiber held in the Suspense and offscreen marks, the fallback flags, the output a reconcile is writing,
    // the presence it sits in, the discarded-mount flag and the marks of work waiting. Kept as a unit, as FiberInputs
    // is, so that a write to any one of them records all.
    internal readonly record struct FiberBoundaryState
    {
        internal bool IsOffscreen { get; init; }
        internal UnityEngine.UIElements.VisualElement? HiddenUnder { get; init; }
        internal ComponentFiber? OffscreenUnder { get; init; }
        internal ComponentFiber? SuspenseFallbackOf { get; init; }
        internal bool LayoutEffectsHidden { get; init; }
        internal bool IsShowingFallback { get; init; }
        internal bool FallbackReplacedPreviousTree { get; init; }
        internal VNode?[]? ReconcilingTree { get; init; }
        internal ReconcilerContext.PresenceBoundaryState? EnclosingPresence { get; init; }
        internal string? EnclosingPresenceKey { get; init; }
        internal bool MountDiscarded { get; init; }
        internal bool ParkedPassSuspended { get; init; }
        internal bool PendingWorkBelow { get; init; }
        internal bool SuspendedInTransition { get; init; }

        internal static FiberBoundaryState Of(ComponentFiber fiber) => new()
        {
            IsOffscreen = fiber.IsOffscreen,
            HiddenUnder = fiber.HiddenUnder,
            OffscreenUnder = fiber.OffscreenUnder,
            SuspenseFallbackOf = fiber.SuspenseFallbackOf,
            LayoutEffectsHidden = fiber.LayoutEffectsHidden,
            IsShowingFallback = fiber.IsShowingFallback,
            FallbackReplacedPreviousTree = fiber.FallbackReplacedPreviousTree,
            ReconcilingTree = fiber.ReconcilingTree,
            EnclosingPresence = fiber.EnclosingPresence,
            EnclosingPresenceKey = fiber.EnclosingPresenceKey,
            MountDiscarded = fiber.MountDiscarded,
            ParkedPassSuspended = fiber.ParkedPassSuspended,
            PendingWorkBelow = fiber.PendingWorkBelow,
            SuspendedInTransition = fiber.SuspendedInTransition,
        };

        internal void WriteTo(ComponentFiber fiber)
        {
            fiber.IsOffscreen = IsOffscreen;
            fiber.HiddenUnder = HiddenUnder;
            fiber.OffscreenUnder = OffscreenUnder;
            fiber.SuspenseFallbackOf = SuspenseFallbackOf;
            fiber.LayoutEffectsHidden = LayoutEffectsHidden;
            fiber.IsShowingFallback = IsShowingFallback;
            fiber.FallbackReplacedPreviousTree = FallbackReplacedPreviousTree;
            fiber.ReconcilingTree = ReconcilingTree;
            fiber.EnclosingPresence = EnclosingPresence;
            fiber.EnclosingPresenceKey = EnclosingPresenceKey;
            fiber.MountDiscarded = MountDiscarded;
            fiber.ParkedPassSuspended = ParkedPassSuspended;
            fiber.PendingWorkBelow = PendingWorkBelow;
            fiber.SuspendedInTransition = SuspendedInTransition;
        }
    }

    // An inline fiber a pass rendered in place, with what it held before the render.
    internal readonly record struct SubsumedRender(ComponentFiber Fiber, FiberInputs Before);

    // The renders of the pass on the stack, which stay tentative until the pass ends. A suspend that nothing in the
    // pass catches leaves it abandoned, and Abandon takes back what those renders changed on the fibers: the inputs
    // each was handed and the hook state each staged. A pass that ends any other way keeps them, and Close commits
    // the hook state. React discards the work in progress of an abandoned render the same way.
    //
    // The context's ReconcilePass owns the journal: the render that opens it completes the pass, which closes it, or
    // drops the pass, which abandons it. A wave holds it open instead (FiberWave): a render that would open it opens a
    // segment of it, which that render ends or abandons, and the wave closes the journal once its pass has applied.
    internal sealed class FiberPassJournal
    {
        private readonly List<SuspenseRender> _hooks = new();
        private readonly List<SubsumedRender> _subsumed = new();
        // What each fiber and tenancy held before the pass first wrote it. The write sites are the setters of
        // ComponentFiber.MountSlotStart, MountSlotCount, PreviousTree, PendingOldTree, OwningPortalPlaceholder and
        // the members of FiberBoundaryState, and of InlineTenancy.ShiftedRows, so a new write site of one of those
        // members is journaled with the rest; a new member is not. Rollback reads them, Abandon does not, and Close
        // discards them.
        private readonly Dictionary<ComponentFiber, (int Start, int Count)> _slots = new();
        private readonly Dictionary<InlineTenancy, int> _shiftedRows = new();
        // The trees and the Portal stamp of a fiber are kept together: a write to any of the three records all of them.
        private readonly Dictionary<ComponentFiber, FiberTrees> _trees = new();
        private readonly Dictionary<ComponentFiber, FiberBoundaryState> _boundaries = new();
        private ComponentFiber? _owner;
        private bool _held;
        // Where the open segment's renders start in _hooks and _subsumed, which Abandon takes back to.
        private int _hooksAtSegment;
        private int _subsumedAtSegment;

        internal bool IsOpen => _owner != null;

        internal bool IsHeld => _held;

        // Returns whether this call opened the journal, or a segment of a held one, which makes the caller its owner.
        internal bool TryOpen(ComponentFiber owner)
        {
            if (_owner != null) return false;
            _owner = owner;
            _hooksAtSegment = _hooks.Count;
            _subsumedAtSegment = _subsumed.Count;
            return true;
        }

        // A journal something already owns is left to it.
        internal void Hold()
        {
            if (_owner == null) _held = true;
        }

        // A held journal keeps what the segment recorded for Close.
        internal void EndSegment() => _owner = null;

        internal void RecordHooks(SuspenseRender render) => _hooks.Add(render);

        internal void RecordHooks(List<SuspenseRender> renders) => _hooks.AddRange(renders);

        // Called once the render has returned, so a render that threw is not in the journal: the catch that saw
        // it writes its inputs back itself.
        internal void RecordSubsumed(ComponentFiber fiber, in FiberInputs before)
        {
            if (_owner == null) return;
            _subsumed.Add(new SubsumedRender(fiber, before));
        }

        // Called by the setters before the first write of a pass, so the entry is the value the pass found.
        internal void RecordSlots(ComponentFiber fiber)
        {
            if (_owner == null) return;
            if (!_slots.ContainsKey(fiber)) _slots.Add(fiber, (fiber.MountSlotStart, fiber.MountSlotCount));
        }

        internal void RecordTrees(ComponentFiber fiber)
        {
            if (_owner == null) return;
            if (!_trees.ContainsKey(fiber)) _trees.Add(fiber, new FiberTrees(fiber.PreviousTree, fiber.PendingOldTree, fiber.OwningPortalPlaceholder));
        }

        internal void RecordBoundary(ComponentFiber fiber)
        {
            if (_owner == null) return;
            if (!_boundaries.ContainsKey(fiber)) _boundaries.Add(fiber, FiberBoundaryState.Of(fiber));
        }

        internal void RecordShiftedRows(InlineTenancy tenancy)
        {
            if (_owner == null) return;
            _shiftedRows.TryAdd(tenancy, tenancy.ShiftedRows);
        }

        // Puts back what the pass wrote on the fiber side. Private until the drop that reads it lands; the pass-drop
        // harness reaches it by reflection.
        private void Rollback()
        {
            var slots = new List<KeyValuePair<ComponentFiber, (int Start, int Count)>>(_slots);
            var shifted = new List<KeyValuePair<InlineTenancy, int>>(_shiftedRows);
            var trees = new List<KeyValuePair<ComponentFiber, FiberTrees>>(_trees);
            var boundaries = new List<KeyValuePair<ComponentFiber, FiberBoundaryState>>(_boundaries);
            foreach (var (fiber, before) in slots)
            {
                fiber.MountSlotStart = before.Start;
                fiber.MountSlotCount = before.Count;
            }
            foreach (var (tenancy, before) in shifted) tenancy.ShiftedRows = before;
            foreach (var (fiber, before) in trees)
            {
                fiber.PreviousTree = before.Previous;
                fiber.PendingOldTree = before.PendingOld;
                fiber.OwningPortalPlaceholder = before.OwningPortal;
            }
            foreach (var (fiber, before) in boundaries) before.WriteTo(fiber);
            _slots.Clear();
            _shiftedRows.Clear();
            _trees.Clear();
            _boundaries.Clear();
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
                _slots.Clear();
                _shiftedRows.Clear();
                _trees.Clear();
                _boundaries.Clear();
                _owner = null;
                _held = false;
            }
        }

        // Takes back the open segment's renders, which outside a held journal are all of them.
        internal void Abandon()
        {
            // Newest first, so a fiber rendered twice in the pass ends at what it held before the first.
            for (var i = _subsumed.Count - 1; i >= _subsumedAtSegment; i--)
            {
                var render = _subsumed[i];
                if (render.Fiber.IsDisposed || !render.Fiber.IsMounted) continue;
                render.Before.WriteTo(render.Fiber);
            }
            // MUTANT_SURVIVES(equivalent, boundary): the first entry of a segment is its owner's own render, recorded as its
            // body returns and ahead of every render its walk makes, and the loop passes over the owner's.
            for (var i = _hooks.Count - 1; i >= _hooksAtSegment; i--)
            {
                var render = _hooks[i];
                // MUTANT_SURVIVES(unreachable, clause removed): a journaled render had the Reconciler FiberRenderer.Mount assigns, and FiberRenderer.Unmount's one production caller is Dispose, which sets IsDisposed first, so no entry point leaves a journaled fiber unmounted and undisposed.
                if (render.Fiber.IsDisposed || !render.Fiber.IsMounted) continue;
                // The fiber the pass started from cut its own effects in the catch of its render, and the retry its
                // suspend asks for renders it, so it is not owed a render here.
                if (ReferenceEquals(render.Fiber, _owner)) continue;
                FiberHookCommit.DiscardRenderedWork(render);
                render.Fiber.RenderOwed = true;
                // The stranded layout commit that follows an abandoned pass runs a handle factory the render staged,
                // and the render that retries stages its own.
                DropStagedHandles(render.Fiber);
            }
            _hooks.RemoveRange(_hooksAtSegment, _hooks.Count - _hooksAtSegment);
            _subsumed.RemoveRange(_subsumedAtSegment, _subsumed.Count - _subsumedAtSegment);
        }

        private static void DropStagedHandles(ComponentFiber fiber)
        {
            var slots = fiber.ImperativeHandleSlots;
            if (slots == null) return;
            for (var i = 0; i < slots.Count; i++) slots[i].NextFactory = null;
        }
    }
}
