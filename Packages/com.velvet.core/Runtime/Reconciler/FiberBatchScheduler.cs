using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Coalesces re-render requests from every fiber sharing one ReconcilerContext into a
    // single frame-boundary flush. A single event handler that calls
    // setState on N different fibers commits in one reconcile pass with no intermediate render between
    // the updates, instead of scheduling N independent IVisualElementScheduler callbacks.
    // Two tiers exist because the Deferred / Transition lanes intentionally defer their flush by
    // DeferredDelayMs while Normal / Urgent flush on the next frame. Each tier registers exactly
    // one schedule.Execute callback per pending batch (guarded by a flag) and drains its whole
    // set in one pass. The per-fiber lane queue is still popped one lane per FiberWorkLoop.FlushState
    // call inside the drain, so lane priority ordering, starvation promotion, and the Deferred delay are
    // preserved — only the scheduler-callback count collapses to one per tier.
    internal sealed class FiberBatchScheduler
    {
        // Cap on the drain-until-quiet passes one DrainImmediate performs — React's
        // "Maximum update depth exceeded" limit (one initial pass plus this many nested ones):
        // each pass exists for commit-phase writes (a callback ref or an event dispatched during
        // a commit re-enqueues its fiber mid-drain), and a component whose commit writes a NEW
        // value every pass would otherwise spin the drain forever inside one frame callback.
        // Overflow DROPS the runaway update (see DrainImmediate) rather than deferring it — a
        // deferred runaway re-arms every frame and burns the full cap forever.
        private const int NestedUpdateLimit = 50;

        // Insertion-ordered pending queues: the List preserves enqueue order so the drain matches the
        // pre-batching schedule.Execute registration order (a parent dirtied before its child flushes
        // first, so the parent's reconcile does not redundantly rebuild the child's subtree), and the
        // HashSet dedups so a fiber dirtied multiple times within one batch is flushed once.
        private readonly List<ComponentFiber> _immediateOrder = new();
        private readonly HashSet<ComponentFiber> _immediateSet = new();
        private readonly List<ComponentFiber> _delayedOrder = new();
        private readonly HashSet<ComponentFiber> _delayedSet = new();
        private bool _immediateScheduled;
        private bool _delayedScheduled;

        // Guards against re-entering a drain. FlushImmediate runs at the end of a discrete event handler; when
        // such an event is dispatched SYNCHRONOUSLY while a drain is already in progress (e.g. focus loss when a
        // focused element is removed during a commit), re-entering would (1) re-enter the reconciler while an
        // outer reconcile is still on the stack — a nested reconcile at depth > 0 skips the top-level reset of
        // abort / context-snapshot state and can corrupt the in-flight pass — and (2) clobber the shared
        // _drainBuffer mid-iteration. While set, FlushImmediate is a no-op and the update stays queued; the
        // next-frame callback ScheduleImmediate already registered still commits it. This deliberately applies
        // across BOTH tiers: a discrete flush must not run while either an immediate or a delayed drain is live,
        // because the danger is the reconcile-on-stack, not the tier.
        private bool _draining;

        // Reports whether a reconcile pass is currently on the stack (SharedReconcileDepth > 0), registered by
        // the owning Reconciler. _draining alone does not cover a time-sliced resume (ContinueReconcile): a
        // resume is scheduled via schedule.Execute, not through Drain, so _draining is false while it runs even
        // though a reconcile is on the stack. FlushImmediate also blocks on this probe so a discrete event
        // dispatched synchronously inside a slice cannot re-enter the reconciler. The hazard is the
        // reconcile-on-stack, not which path put it there.
        private Func<bool>? _reconcileActiveProbe;

        // UseStore cross-tier tearing-guard hooks (ReconcilerContext.BeginStoreSnapshotWave /
        // EndStoreSnapshotWave). A "wave" spans the immediate drain and the delayed drain that follows it in
        // the same frame: the immediate drain opens the wave with reset=true (drop the prior wave's pins) so its
        // first UseStore read pins the now-current snapshot; the delayed drain opens with reset=false so it
        // REUSES that pin, keeping an immediate-tier ancestor and a delayed-tier descendant on the same
        // snapshot. Pinning is active only inside a drain — renders outside a drain (mount, a synchronous
        // whole-tree flush) run in a single pass with no tier separation and read the live store snapshot.
        private Action<bool>? _onDrainBegin;
        private Action? _onDrainEnd;

        // True when an immediate drain has just established snapshot pins that a pending delayed drain should
        // reuse (the delayed drain continues the immediate drain's wave). Set by DrainImmediate, consumed by
        // DrainDelayed; a solo delayed drain finds it false and opens a fresh wave.
        private bool _delayedContinuesWave;

        // Set by ScheduleImmediate whenever intake happens while a drain is live; the delayed drain
        // resets it before draining and reads it after, to decide whether its commits spawned
        // immediate-tier work owed the setState-in-commit boundary pass (see DrainDelayed).
        private bool _immediateWorkArrivedMidDrain;

        // Tree-stable element the drain callbacks are registered on (the root mount element). A
        // descendant's MountPoint can detach from the panel before the next frame, which stops a
        // scheduled item registered on it and would strand still-mounted fibers that joined the same
        // batch; the root mount element outlives every descendant in the tree.
        private VisualElement? _anchor;

        // Number of frame-boundary drain callbacks registered with the scheduler since construction.
        // In a fully batched flush this increments by exactly one per tier per batch regardless of how
        // many fibers were enqueued. Exposed for tests that assert coalescing; not used by production.
        internal int ScheduledCallbackCount { get; private set; }

        // Count of fibers awaiting the next-frame (Normal / Urgent) batch drain.
        internal int ImmediatePendingCount => _immediateOrder.Count;

        // Count of fibers awaiting the delayed (Deferred / Transition) batch drain.
        internal int DelayedPendingCount => _delayedOrder.Count;

        // Reused per drain so a fiber re-dirtying itself during flush (re-enqueue) does not mutate the
        // queue being iterated. A single buffer suffices because the _draining guard ensures only one drain
        // (immediate or delayed) is ever live at a time. Cleared after each drain to release fiber refs for GC.
        private readonly List<ComponentFiber> _drainBuffer = new();

        // Records the tree-stable element on which drain callbacks are registered. Called once when the
        // root fiber that owns this context mounts.
        internal void SetAnchor(VisualElement anchor) => _anchor = anchor;

        // Registers a probe reporting whether a reconcile pass is currently on the stack. Called once by the
        // owning Reconciler so FlushImmediate can block a synchronous discrete-event flush during
        // a time-sliced resume, which runs outside the batch Drain (so _draining is false)
        // yet still has a reconcile on the stack.
        internal void SetReconcileActiveProbe(Func<bool> probe) => _reconcileActiveProbe = probe;

        // Registers the callbacks bracketing each drain that drive the UseStore tearing-guard wave (see
        // _onDrainBegin). Called once by the owning Reconciler.
        internal void SetStoreSnapshotWaveCallbacks(Action<bool> onDrainBegin, Action onDrainEnd)
        {
            _onDrainBegin = onDrainBegin;
            _onDrainEnd = onDrainEnd;
        }

        // Enqueues fiber for a batched flush at the next frame boundary
        // (Normal / Urgent priority) and registers the single shared drain callback if not already pending.
        internal void ScheduleImmediate(ComponentFiber fiber)
        {
            if (fiber?.MountPoint == null) return;
            // Monotonic mid-drain intake marker for the delayed drain's boundary pass: a net count
            // comparison would be masked when the drain's own commits also REMOVED entries (an
            // unmount, a subsume) or when the write dedups onto a fiber already queued.
            if (_draining) _immediateWorkArrivedMidDrain = true;
            if (_immediateSet.Add(fiber)) _immediateOrder.Add(fiber);
            if (_immediateScheduled || _anchor == null) return;
            _immediateScheduled = true;
            ScheduledCallbackCount++;
            _anchor.schedule.Execute(DrainImmediate);
        }

        // Enqueues fiber for a batched flush deferred by delayMs
        // (Deferred / Transition priority) and registers the single shared delayed drain callback if not
        // already pending.
        // Intentional: this is fixed-delay deferral — each scheduled delayed drain fires
        // delayMs after it was scheduled. A fiber that re-defers itself from inside a
        // delayed drain therefore pushes its next cycle a further delayMs out; this is the
        // expected behaviour of repeated deferral and affects only when the work runs (latency), never
        // correctness. Multiple fibers within one pending window coalesce into a single drain via
        // _delayedScheduled and do NOT accumulate. Anchoring to a fixed base time instead would change
        // the deferral semantics, so the simple per-schedule delay is kept.
        internal void ScheduleDelayed(ComponentFiber fiber, int delayMs)
        {
            if (fiber?.MountPoint == null) return;
            if (_delayedSet.Add(fiber)) _delayedOrder.Add(fiber);
            if (_delayedScheduled || _anchor == null) return;
            _delayedScheduled = true;
            ScheduledCallbackCount++;
            _anchor.schedule.Execute(DrainDelayed).ExecuteLater(delayMs);
        }

        private void DrainImmediate()
        {
            // Commit-phase state writes (a callback ref invoked during the patch, an event
            // dispatched from a detach) re-enqueue their fiber mid-drain. Keep draining until the
            // queue is quiet so the follow-up render commits before this frame callback yields —
            // React's "setState during the commit schedules a follow-up pass before paint". Each
            // pass opens a fresh tearing-guard wave (a commit write moved state forward, so its
            // first UseStore read re-pins the now-current snapshot). _immediateScheduled stays SET
            // for the whole loop: the loop itself consumes every mid-drain enqueue, so registering
            // another frame callback for one would only fire an empty drain next frame (dead
            // scheduler churn that also skews the coalescing counter).
            var openedWave = false;
            var totalPasses = 0;
            while (_immediateOrder.Count > 0)
            {
                if (totalPasses > NestedUpdateLimit)
                {
                    // Runaway commit-phase loop (a component writing a NEW value on every pass).
                    // React throws here; a throw from the frame callback cannot reach an error
                    // boundary and would only re-arm next frame, burning the full cap every frame
                    // forever — so the runaway update is DROPPED instead and the UI keeps the last
                    // committed state. Only the IMMEDIATE-tier lanes are dropped: a fiber can be
                    // queued this deep with an unrelated Transition/Deferred lane still pending (the
                    // runaway's commits may write bystanders each pass), and wiping that lane would
                    // strand its delayed-tier work (a StartTransition left isPending forever).
                    FiberLogger.LogError("Scheduler",
                        "Maximum update depth exceeded. A component repeatedly schedules state"
                        + " updates from its commit phase (a callback ref or an effect writing a"
                        + " new value on every pass). The runaway update was dropped.");
                    _drainBuffer.Clear();
                    _drainBuffer.AddRange(_immediateOrder);
                    _immediateOrder.Clear();
                    _immediateSet.Clear();
                    for (var i = 0; i < _drainBuffer.Count; i++)
                    {
                        var dropped = _drainBuffer[i];
                        dropped.LaneQueue?.Remove(FiberUpdatePriority.Urgent);
                        dropped.LaneQueue?.Remove(FiberUpdatePriority.Normal);
                        if (dropped.LaneQueue == null || dropped.LaneQueue.Count == 0)
                        {
                            dropped.IsDirty = false;
                            dropped.ClearAllTransitionPending();
                        }
                        // else: a delayed-tier lane survives — the fiber stays dirty and enrolled on
                        // that tier, so its pending Transition/Deferred work still commits there.
                    }
                    _drainBuffer.Clear();
                    break;
                }
                totalPasses++;
                openedWave = true;
                Drain(_immediateOrder, _immediateSet, resetWave: true);
            }
            _immediateScheduled = false;
            // A delayed drain already pending after this immediate drain CONTINUES this wave (it should reuse the
            // pins this drain just established). A delayed drain that arrives later, with no immediate drain to pin
            // first, is a separate wave and must open fresh. Only hand off when this drain actually opened a wave —
            // and an EMPTY run (a callback whose work an earlier synchronous drain already consumed) must not
            // clobber a hand-off that earlier drain established.
            if (openedWave)
            {
                _delayedContinuesWave = _delayedScheduled;
            }
        }

        private void DrainDelayed()
        {
            _delayedScheduled = false;
            // Reuse the immediate drain's pins ONLY when continuing its wave; a SOLO delayed drain (no immediate
            // drain pinned a snapshot this cycle) opens a fresh wave so it reads the current store value rather
            // than a stale pin retained from a prior wave.
            var continuesImmediateWave = _delayedContinuesWave;
            _delayedContinuesWave = false;
            // Immediate-tier work that PRE-DATES this drain belongs to the next frame callback (its own
            // wave); only work this drain's commits spawn is owed the setState-in-commit guarantee, so
            // the boundary pass below is gated on the monotonic intake marker (never on a net count,
            // which the drain's own removals or a dedup onto an already-queued fiber would mask).
            _immediateWorkArrivedMidDrain = false;
            Drain(_delayedOrder, _delayedSet, resetWave: !continuesImmediateWave);
            // A commit-phase write during a DELAYED-tier commit enqueues on the immediate tier; the
            // setState-in-commit guarantee (the follow-up render commits before this frame callback
            // yields) is tier-agnostic, so drain it now rather than leaving a one-frame slot/UI
            // desync for the next immediate callback to converge. (When new and pre-dated work mixed
            // in the window, the boundary pass carries both — the write's immediacy outranks holding
            // unrelated work back for one frame.)
            if (_immediateWorkArrivedMidDrain && _immediateOrder.Count > 0)
            {
                DrainImmediate();
            }
        }

        private void Drain(List<ComponentFiber> order, HashSet<ComponentFiber> set, bool resetWave)
        {
            if (order.Count == 0) return;
            // Activate UseStore snapshot pinning for the span of this drain. Bracketed only on the outer drain
            // (a re-entrant call is blocked by _draining before reaching here) so a nested entry cannot reset
            // the wave mid-flush.
            var openedWave = !_draining;
            if (openedWave) _onDrainBegin?.Invoke(resetWave);
            _draining = true;
            try
            {
                _drainBuffer.Clear();
                _drainBuffer.AddRange(order);
                order.Clear();
                set.Clear();
                StableSortByTreeDepth(_drainBuffer);
                for (var i = 0; i < _drainBuffer.Count; i++)
                {
                    // A fiber whose ancestor flushed earlier in this same pass may have been subsumed by that
                    // ancestor's inline re-expansion (RenderInlineForExpansion → SettleSubsumedFiber), which
                    // clears IsDirty and removes the fiber from the pending set. FlushState early-returns on a
                    // non-dirty fiber, so the subsumed entry is skipped rather than re-rendered a second time.
                    FiberWorkLoop.FlushState(_drainBuffer[i]);
                }
                _drainBuffer.Clear();
            }
            finally
            {
                // _onDrainEnd flushes the drain's deferred layout effects. Run it while _draining is still
                // true so a re-entrant FlushImmediate raised from one of those effects still no-ops (defers)
                // rather than re-entering the reconciler. The inner finally guarantees _draining is cleared even
                // if a deferred effect throws — otherwise a single user exception would wedge the scheduler.
                try
                {
                    if (openedWave) _onDrainEnd?.Invoke();
                }
                finally
                {
                    _draining = false;
                }
            }
        }

        // Orders the drain ancestors-before-descendants so a parent's flush (which re-expands its inline
        // children via ComponentRegistry → RenderInlineForExpansion) subsumes a child into the same pass
        // BEFORE the child's own enqueued entry is reached — matching the single top-down pass where
        // each component renders at most once regardless of setter call order. A child dirtied before its
        // parent in one handler would otherwise re-render its slot in isolation, then a second time when the
        // parent re-expands it. Insertion sort by tree depth (Parent-hop count): stable, so same-depth fibers
        // (siblings, unrelated subtrees) keep their enqueue order, and cheap for the small batches drained here.
        private static void StableSortByTreeDepth(List<ComponentFiber> buffer)
        {
            var count = buffer.Count;
            if (count < 2) return;

            // Cache each fiber's depth once. TreeDepth walks the parent chain to the root, so recomputing the
            // comparand's depth inside the inner loop would cost O(count^2 * depth) parent-hops per drain;
            // precomputing makes it O(count * depth) hops + O(count^2) int comparisons. The parent chain does
            // not change during the sort, so the cached keys yield the same stable ordering as the recompute.
            var depths = new int[count];
            for (var i = 0; i < count; i++) depths[i] = TreeDepth(buffer[i]);

            for (var i = 1; i < count; i++)
            {
                var item = buffer[i];
                var depth = depths[i];
                var j = i - 1;
                while (j >= 0 && depths[j] > depth)
                {
                    buffer[j + 1] = buffer[j];
                    depths[j + 1] = depths[j];
                    j--;
                }
                buffer[j + 1] = item;
                depths[j + 1] = depth;
            }
        }

        private static int TreeDepth(ComponentFiber fiber)
        {
            var depth = 0;
            for (var p = fiber.Parent; p != null; p = p.Parent) depth++;
            return depth;
        }

        // Synchronously drains the next-frame (Normal / Urgent) batch now instead of waiting for the scheduled
        // frame-boundary callback. Called at the end of a discrete user-input event handler so Urgent-lane
        // updates scheduled during the handler commit synchronously, so the UI reflects them before the next frame. A frame-boundary
        // callback registered earlier in the same batch still fires later but finds the queue already drained.
        // No-op while ANY drain (immediate or delayed) is already running OR a reconcile pass is otherwise on
        // the stack: a discrete event dispatched synchronously during a commit (e.g. focus loss when a focused
        // element is removed during reconcile) would otherwise re-enter the reconciler while an outer reconcile
        // is still on the stack — a nested reconcile at depth > 0 skips the top-level reset of abort /
        // context-snapshot state and can corrupt the in-flight pass — and would also re-enter Drain
        // and clobber the shared drain buffer mid-iteration. The block spans both tiers on purpose: the hazard
        // is the reconcile already on the stack, not which tier is draining. A time-sliced resume
        // (Reconciler.ContinueReconcile) runs via schedule.Execute rather than through
        // Drain, so _draining is false during it; the reconcile-active probe
        // (SetReconcileActiveProbe) catches that case via SharedReconcileDepth > 0. The
        // update is not lost — it stays queued and commits on the next-frame callback that
        // ScheduleImmediate already registered.
        internal void FlushImmediate()
        {
            if (_draining || (_reconcileActiveProbe?.Invoke() ?? false)) return;
            DrainImmediate();
        }

        // Drops fiber from both pending queues. Called on Unmount / Dispose so a
        // torn-down fiber is not flushed by a still-pending drain callback.
        internal void Remove(ComponentFiber fiber)
        {
            if (_immediateSet.Remove(fiber)) _immediateOrder.Remove(fiber);
            if (_delayedSet.Remove(fiber)) _delayedOrder.Remove(fiber);
        }

        // Drops every pending fiber and resets the scheduled flags and anchor. Called from the owning
        // Reconciler's Dispose so a still-registered drain callback does not run against a disposed context.
        internal void Clear()
        {
            _immediateOrder.Clear();
            _immediateSet.Clear();
            _delayedOrder.Clear();
            _delayedSet.Clear();
            _delayedContinuesWave = false;
            _immediateWorkArrivedMidDrain = false;
            _immediateScheduled = false;
            _delayedScheduled = false;
            _anchor = null;
            _onDrainBegin = null;
            _onDrainEnd = null;
        }

        // Runs the next-frame (Normal / Urgent) drain synchronously, simulating one frame-boundary
        // scheduler callback firing. Tests use this because the UIToolkit scheduler does not advance in
        // EditMode. Not used by production.
        internal void DrainImmediateForTest() => DrainImmediate();

        // Runs the delayed (Deferred / Transition) drain synchronously, simulating the delayed scheduler
        // callback firing. Tests use this because the UIToolkit scheduler does not advance in EditMode.
        // Not used by production.
        internal void DrainDelayedForTest() => DrainDelayed();
    }
}
