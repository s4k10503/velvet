using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Coalesces re-render requests from every fiber sharing one ReconcilerContext into a
    // single frame-boundary flush. A single event handler that calls
    // setState on N different fibers commits in one reconcile pass with no intermediate render between
    // the updates, instead of scheduling N independent IVisualElementScheduler callbacks.
    // Two tiers exist so the Transition lane renders behind Normal / Urgent work: the immediate tier drains on
    // the next panel scheduler pass, and the Transition tier on a later one (see ScheduleDelayed), committing
    // whatever the immediate tier still holds before its own queue. The immediate tier registers one
    // schedule.Execute callback per pending batch, the Transition tier one per admission and one per drain,
    // and each drain takes its whole batch in one pass. The per-fiber lane queue is still popped one lane per
    // FiberWorkLoop.FlushState call inside the drain, so lane priority ordering and starvation promotion are
    // preserved.
    internal sealed class FiberBatchScheduler
    {
        // Cap on the drain-until-quiet passes one DrainImmediate performs — the
        // "Maximum update depth exceeded" limit (one initial pass plus this many nested ones):
        // each pass exists for commit-phase writes (a callback ref or an event dispatched during
        // a commit re-enqueues its fiber mid-drain), and a component whose commit writes a NEW
        // value every pass would otherwise spin the drain forever inside one frame callback.
        // Overflow DROPS the runaway update (see DrainImmediate) rather than deferring it — a
        // deferred runaway re-arms every frame and burns the full cap forever.
        private const int NestedUpdateLimit = 50;

        // How many immediate drains in a row may throw before the updates queued after the last of them are dropped
        // rather than re-armed (see DrainImmediate). A drain that completes resets the count.
        private const int ConsecutiveThrowingDrainLimit = 3;
        private int _consecutiveThrowingDrains;

        // Insertion-ordered pending queues: the List preserves enqueue order so the drain matches the
        // pre-batching schedule.Execute registration order (a parent dirtied before its child flushes
        // first, so the parent's reconcile does not redundantly rebuild the child's subtree), and the
        // HashSet dedups so a fiber dirtied multiple times within one batch is flushed once.
        private readonly List<ComponentFiber> _immediateOrder = new();
        private readonly HashSet<ComponentFiber> _immediateSet = new();
        private readonly List<ComponentFiber> _delayedOrder = new();
        private readonly Dictionary<ComponentFiber, DelayedEntry> _delayedEntries = new();
        private readonly List<ComponentFiber> _delayedDue = new();
        private bool _immediateScheduled;

        // A Transition-tier entry and the drain registration it waits for (see ScheduleDelayed).
        private readonly struct DelayedEntry
        {
            internal readonly DelayedAdmission Admission;
            internal readonly int Deferrals;

            internal DelayedEntry(DelayedAdmission admission, int deferrals)
            {
                Admission = admission;
                Deferrals = deferrals;
            }
        }

        // One registered Transition-tier callback. RegisteredIn is the PanelSchedulerCallback it was registered
        // from, or 0 for the admission a request made outside this panel's passes waits in.
        private sealed class DelayedAdmission
        {
            internal readonly int RegisteredIn;

            internal DelayedAdmission(int registeredIn) => RegisteredIn = registeredIn;
        }

        private DelayedAdmission? _unadmitted;
        private DelayedAdmission? _admittedForNextPass;


        // Only the registered immediate callback retires its registration: an inline drain (a discrete flush,
        // the Transition tier's) leaves it live, so intake arriving before that callback runs rides it rather
        // than registering a second.
        private bool _inImmediateCallback;

        // Set by a UseSyncExternalStore re-render entering the immediate tier and cleared once the tier is
        // empty, by the drain or by Remove. It is also the one posted flush in flight: a post the main thread
        // discards leaves it set only until then. See OweExternalStoreFlush.
        private bool _externalStoreRenderQueued;

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

        // UseStore tearing-guard hooks (ReconcilerContext.BeginStoreSnapshotWave / EndStoreSnapshotWave). Each
        // drain pass opens a wave of its own, whose first UseStore read of a store pins the snapshot the rest of
        // that pass reads. Pinning is active only inside a drain — renders outside one (mount, a synchronous
        // whole-tree flush) read the live store snapshot.
        private Action? _onDrainBegin;
        private Action? _onDrainEnd;
        // Flushes any pending passive effects from a prior commit, wired by the owning Reconciler. Run before a
        // synchronous discrete-event drain so those effects commit before the new update's render.
        private Action? _flushPassiveEffects;

        // Set by ScheduleImmediate whenever intake happens while a drain is live; the delayed drain
        // resets it before draining and reads it after, to decide whether its commits spawned
        // immediate-tier work owed the setState-in-commit boundary pass (see DrainDelayed).
        private bool _immediateWorkArrivedMidDrain;

        // Tree-stable element the drain callbacks are registered on (the root mount element). A
        // descendant's MountPoint can detach from the panel before the next frame, which stops a
        // scheduled item registered on it and would strand still-mounted fibers that joined the same
        // batch; the root mount element outlives every descendant in the tree.
        private VisualElement? _anchor;

        // Number of callbacks either tier registered with the panel scheduler since construction, however many
        // fibers each one serves. Exposed for tests that assert coalescing; not used by production.
        internal int ScheduledCallbackCount { get; private set; }

        // Count of fibers awaiting the next-frame (Normal / Urgent) batch drain.
        internal int ImmediatePendingCount => _immediateOrder.Count;

        // Count of fibers awaiting the delayed (Transition) batch drain.
        internal int DelayedPendingCount => _delayedOrder.Count;

        // Reused per drain so a fiber re-dirtying itself during flush (re-enqueue) does not mutate the
        // queue being iterated. A single buffer suffices because the _draining guard ensures only one drain
        // (immediate or delayed) is ever live at a time. Cleared after each drain to release fiber refs for GC.
        private readonly List<ComponentFiber> _drainBuffer = new();

        // Records the tree-stable element on which drain callbacks are registered. Called once when the
        // root fiber that owns this context mounts.
        internal void SetAnchor(VisualElement anchor) => _anchor = anchor;

        // Also hosts the passive-effect drain (FiberEffects.ScheduleRunEffects): that callback serves the
        // whole context too, so it needs the same tree stability the tier drains do.
        internal VisualElement? Anchor => _anchor;

        // Registers a probe reporting whether a reconcile pass is currently on the stack. Called once by the
        // owning Reconciler so FlushImmediate can block a synchronous discrete-event flush during
        // a time-sliced resume, which runs outside the batch Drain (so _draining is false)
        // yet still has a reconcile on the stack.
        internal void SetReconcileActiveProbe(Func<bool> probe) => _reconcileActiveProbe = probe;

        // Registers the callbacks bracketing each drain that drive the UseStore tearing-guard wave (see
        // _onDrainBegin). Called once by the owning Reconciler.
        internal void SetStoreSnapshotWaveCallbacks(Action onDrainBegin, Action onDrainEnd)
        {
            _onDrainBegin = onDrainBegin;
            _onDrainEnd = onDrainEnd;
        }

        // Registers the pending-passive-effect flush run before a synchronous discrete-event drain.
        // Called once by the owning Reconciler.
        internal void SetPassiveEffectFlush(Action flush) => _flushPassiveEffects = flush;

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
            ArmImmediateDrain();
        }

        // A UseSyncExternalStore re-render is React's sync-lane update, which React flushes in a microtask once
        // the code that raised the notification has returned. The immediate tier is flushed from the main
        // thread's posted work, which is also where FiberWorkLoop.ScheduleDeferredAwaitContinuations posts
        // what it defers, and ahead of a time-sliced pass's next slice (FlushOwedExternalStoreRenders). The
        // frame-boundary callback ScheduleImmediate registered still commits it when neither has.
        internal void OweExternalStoreFlush()
        {
            if (_externalStoreRenderQueued) return;
            VelvetMainThread.Post(static scheduler => ((FiberBatchScheduler)scheduler!).FlushOwedExternalStoreRenders(), this);
            _externalStoreRenderQueued = true;
        }

        // Called by the posted flush and before FiberWorkLoop.ContinueReconcile resumes a parked time-sliced pass, so the readers it committed in earlier slices have
        // re-rendered the changed snapshot before a later slice's readers render it.
        internal void FlushOwedExternalStoreRenders()
        {
            if (_externalStoreRenderQueued && _immediateOrder.Count > 0) FlushImmediate();
        }

        private void ArmImmediateDrain()
        {
            if (_immediateScheduled || _anchor == null) return;
            _immediateScheduled = true;
            ScheduledCallbackCount++;
            _anchor.schedule.Execute(RunImmediateCallback);
        }

        private void RunImmediateCallback()
        {
            var outer = PanelSchedulerCallback.Enter(_anchor?.panel);
            _inImmediateCallback = true;
            try
            {
                DrainImmediate();
            }
            finally
            {
                _inImmediateCallback = false;
                PanelSchedulerCallback.Exit(outer);
            }
        }

        // Short of the bound below, a Transition request does not join a drain the panel can run in the
        // scheduler pass the request was made in, so the render that asked for it — an urgent render reaching
        // UseDeferredValue, the urgent flush that re-enrols a surviving Transition lane — commits in an earlier
        // pass than the transition. Made inside a PanelSchedulerCallback that this scheduler's own panel is
        // running, the request waits for a drain registered from that callback, which that panel's scheduler
        // holds back to a later pass. Made anywhere else — including a callback another panel is running,
        // whose pass does not hold back a registration on this one — nothing places it relative to this
        // panel's passes, so it waits one pass more: for an admission this panel's next pass runs, which
        // registers the drain for a pass after that. TransitionFrameSchedulingTests holds both.
        // The bound: a queued fiber moves to the admission its latest request chose, so an urgent update
        // arriving every frame would keep it moving indefinitely. FiberWorkLoop's starvation promotion stops
        // that only for a fiber whose own flush the update preempts, not for one an ancestor's render
        // subsumes, so after TransitionStarvationThreshold moves the entry stays where it is.
        internal void ScheduleDelayed(ComponentFiber fiber)
        {
            if (fiber?.MountPoint == null) return;
            var queued = _delayedEntries.TryGetValue(fiber, out var entry);
            if (queued && entry.Deferrals >= FiberWorkLoop.TransitionStarvationThreshold) return;
            var admission = PanelSchedulerCallback.InPassOf(_anchor) ? AdmissionForNextPass() : Unadmitted();
            if (!queued)
            {
                _delayedOrder.Add(fiber);
                _delayedEntries[fiber] = new DelayedEntry(admission, 0);
            }
            else if (entry.Admission != admission)
            {
                _delayedEntries[fiber] = new DelayedEntry(admission, entry.Deferrals + 1);
            }
        }

        private DelayedAdmission AdmissionForNextPass()
        {
            var current = _admittedForNextPass;
            if (current != null && current.RegisteredIn == PanelSchedulerCallback.Current) return current;
            var admission = new DelayedAdmission(PanelSchedulerCallback.Current);
            _admittedForNextPass = admission;
            Register(() => RunDelayedCallback(admission));
            return admission;
        }

        private DelayedAdmission Unadmitted()
        {
            var current = _unadmitted;
            if (current != null) return current;
            var admission = new DelayedAdmission(0);
            _unadmitted = admission;
            Register(() => RunAdmitCallback(admission));
            return admission;
        }

        private void Register(Action callback)
        {
            if (_anchor == null) return;
            ScheduledCallbackCount++;
            _anchor.schedule.Execute(callback);
        }

        private void RunAdmitCallback(DelayedAdmission admission)
        {
            var outer = PanelSchedulerCallback.Enter(_anchor?.panel);
            try
            {
                if (_unadmitted == admission) _unadmitted = null;
                for (var i = 0; i < _delayedOrder.Count; i++)
                {
                    var fiber = _delayedOrder[i];
                    var entry = _delayedEntries[fiber];
                    if (entry.Admission == admission)
                    {
                        _delayedEntries[fiber] = new DelayedEntry(AdmissionForNextPass(), entry.Deferrals);
                    }
                }
            }
            finally
            {
                PanelSchedulerCallback.Exit(outer);
            }
        }

        private void RunDelayedCallback(DelayedAdmission admission)
        {
            var outer = PanelSchedulerCallback.Enter(_anchor?.panel);
            try
            {
                DrainDelayedTier(admission);
            }
            finally
            {
                PanelSchedulerCallback.Exit(outer);
            }
        }

        private void DrainImmediate()
        {
            var completed = false;
            try
            {
                DrainImmediatePasses();
                completed = true;
            }
            finally
            {
                // In a finally because an exception leaves a drain too (DrainExceptionRecoveryTests throws one from
                // an imperative-handle factory), and a flag left raised has ArmImmediateDrain register nothing for a
                // later request until the registered callback lowers it. Intake still queued here was requested by a
                // settle on a drop, or enqueued before the exception, and the loop that would have consumed it has
                // exited. Only the registered callback lowers the flag; see _inImmediateCallback.
                if (_inImmediateCallback) _immediateScheduled = false;
                _consecutiveThrowingDrains = completed ? 0 : _consecutiveThrowingDrains + 1;
                if (_consecutiveThrowingDrains >= ConsecutiveThrowingDrainLimit && _immediateOrder.Count > 0)
                {
                    // A commit that throws and queues itself again on every drain would otherwise throw once a frame
                    // for good, the same reason the nested-update cap drops its runaway rather than re-arming.
                    FiberLogger.LogError("Scheduler",
                        $"{ConsecutiveThrowingDrainLimit} update batches in a row threw, and the last left updates"
                        + " queued. Those updates were dropped.");
                    DropImmediateIntake();
                }
                if (_immediateOrder.Count == 0) _externalStoreRenderQueued = false;
                if (_immediateOrder.Count > 0) ArmImmediateDrain();
            }
        }

        private void DrainImmediatePasses()
        {
            // Commit-phase state writes (a callback ref invoked during the patch, an event
            // dispatched from a detach) re-enqueue their fiber mid-drain. Keep draining until the
            // queue is quiet so the follow-up render commits before this frame callback yields —
            // a state update issued during the commit still schedules a follow-up pass before paint.
            // Each pass opens a fresh tearing-guard wave (a commit write moved state forward, so its
            // first UseStore read re-pins the now-current snapshot). _immediateScheduled stays SET
            // for the whole loop: the loop itself consumes every mid-drain enqueue, so registering
            // another frame callback for one would only fire an empty drain next frame (dead
            // scheduler churn that also skews the coalescing counter).
            var totalPasses = 0;
            while (_immediateOrder.Count > 0)
            {
                if (totalPasses > NestedUpdateLimit)
                {
                    // Runaway commit-phase loop (a component writing a NEW value on every pass) — the
                    // kind of bug that would ordinarily deserve a hard failure, but a throw from the
                    // frame callback cannot reach an error boundary and would only re-arm next frame,
                    // burning the full cap every frame forever — so the runaway update is DROPPED
                    // instead and the UI keeps the last committed state. Only the IMMEDIATE-tier
                    // lanes are dropped: a fiber can be queued this deep with an unrelated Transition
                    // lane still pending (the runaway's commits may write bystanders each pass), and
                    // wiping that lane would strand its delayed-tier work (a StartTransition left
                    // isPending forever). A Transition lane that starvation-promoted to Normal DURING
                    // the runaway (the cap exceeds the starvation threshold) is dropped with it —
                    // acceptable: state slots are written eagerly, so the last committed render already
                    // showed the transition's values, and the pending-flag sweep below spares an async
                    // action still in flight.
                    FiberLogger.LogError("Scheduler",
                        "Maximum update depth exceeded. A component repeatedly schedules state"
                        + " updates from its commit phase (a callback ref or an effect writing a"
                        + " new value on every pass). The runaway update was dropped.");
                    DropImmediateIntake();
                    break;
                }
                totalPasses++;
                Drain(_immediateOrder, _immediateSet, immediateTier: true);
            }
        }

        // Which lanes go, and why only these, is the nested-update cap's comment in DrainImmediatePasses.
        // Copied out rather than walked in place: a settle below can request a render, which enqueues it again.
        private void DropImmediateIntake()
        {
            var droppedFibers = _immediateOrder.ToArray();
            _immediateOrder.Clear();
            _immediateSet.Clear();
            for (var i = 0; i < droppedFibers.Length; i++)
            {
                var dropped = droppedFibers[i];
                // Direct field access through Lanes (not the dropped.LaneQueue read accessor): a
                // FiberLaneSet handed back by a property getter is a copy, so Remove() through it
                // would mutate a throwaway value instead of the backing queue.
                dropped.Lanes?.Queue.Remove(FiberUpdatePriority.Urgent);
                dropped.Lanes?.Queue.Remove(FiberUpdatePriority.Normal);
                // The promoted marker retires with the dropped Normal it rode, or it would keep
                // skipping the settle sweep for as long as any surviving lane stays queued.
                // MUTANT_SURVIVES(unreachable): no fiber a fixture drops declares UseTransition, so no lane of a dropped fiber was ever promoted.
                dropped.HasPromotedTransition = false;
                // MUTANT_SURVIVES(unreachable, equality): a fiber left dirty with an empty queue is re-enrolled by a Normal update; see IsDirty below.
                if (dropped.LaneQueue.Count == 0)
                {
                    // MUTANT_SURVIVES(unreachable): a Normal update re-enrols a dirty fiber whose queue is empty, and only a Transition one, which no fiber a fixture drops issues, would be stranded.
                    dropped.IsDirty = false;
                    // MUTANT_SURVIVES(unreachable): no fiber a fixture drops declares UseTransition, so this call finds no slot and no enrolment to settle.
                    dropped.SettleTransitionPending();
                }
                // else: a delayed-tier lane survives — the fiber stays dirty and enrolled on
                // that tier, so its pending Transition work still commits there.
            }
        }

        // Drains every Transition-tier entry, whatever admission it waits for. No panel callback runs this;
        // VelvetPreviewHost.Settle does, to settle a story without the panel.
        internal void DrainDelayed() => DrainDelayedTier(null);

        private void ReadmitEntries(DelayedAdmission spent)
        {
            DelayedAdmission? next = null;
            for (var i = 0; i < _delayedOrder.Count; i++)
            {
                var fiber = _delayedOrder[i];
                var entry = _delayedEntries[fiber];
                if (entry.Admission != spent) continue;
                next ??= PanelSchedulerCallback.InPassOf(_anchor) ? AdmissionForNextPass() : Unadmitted();
                _delayedEntries[fiber] = new DelayedEntry(next, entry.Deferrals);
            }
        }

        private void DrainDelayedTier(DelayedAdmission? admission)
        {
            // The immediate queue is committed first rather than trusting the panel to run its callback ahead of
            // this one. A Transition lane that pass re-enrols takes a later admission (see ScheduleDelayed), so
            // only a drain of every admission picks it up below.
            try
            {
                DrainImmediate();
            }
            catch
            {
                // This admission's callback has run and registers nothing again, so entries still waiting for it
                // would wait for a drain that never comes while their fibers stay dirty.
                if (admission != null) ReadmitEntries(admission);
                throw;
            }
            var kept = 0;
            for (var i = 0; i < _delayedOrder.Count; i++)
            {
                var fiber = _delayedOrder[i];
                if (admission == null || _delayedEntries[fiber].Admission == admission)
                {
                    _delayedDue.Add(fiber);
                    _delayedEntries.Remove(fiber);
                }
                else
                {
                    _delayedOrder[kept++] = fiber;
                }
            }
            _delayedOrder.RemoveRange(kept, _delayedOrder.Count - kept);
            // Only work this drain's commits spawn is owed the setState-in-commit guarantee, so the boundary
            // pass below is gated on the monotonic intake marker (never on a net count, which the drain's
            // own removals or a dedup onto an already-queued fiber would mask).
            _immediateWorkArrivedMidDrain = false;
            Drain(_delayedDue, null, immediateTier: false);
            // A commit-phase write during a DELAYED-tier commit enqueues on the immediate tier; the
            // setState-in-commit guarantee (the follow-up render commits before this frame callback
            // yields) is tier-agnostic, so drain it now rather than leaving a one-frame slot/UI
            // desync for the next immediate callback to converge.
            if (_immediateWorkArrivedMidDrain && _immediateOrder.Count > 0)
            {
                DrainImmediate();
            }
        }

        private void Drain(List<ComponentFiber> order, HashSet<ComponentFiber>? set, bool immediateTier)
        {
            if (order.Count == 0) return;
            // Activate UseStore snapshot pinning for the span of this drain. Bracketed only on the outer drain
            // (a re-entrant call is blocked by _draining before reaching here) so a nested entry cannot reset
            // the wave mid-flush.
            var openedWave = !_draining;
            if (openedWave) _onDrainBegin?.Invoke();
            _draining = true;
            try
            {
                _drainBuffer.Clear();
                _drainBuffer.AddRange(order);
                order.Clear();
                set?.Clear();
                StableSortByTreeDepth(_drainBuffer);
                for (var i = 0; i < _drainBuffer.Count; i++)
                {
                    // A fiber whose ancestor flushed earlier in this same pass may have been subsumed by that
                    // ancestor's inline re-expansion (SubsumeFiberIntoThisPass), which
                    // clears IsDirty and removes the fiber from the pending set. FlushState early-returns on a
                    // non-dirty fiber, so the subsumed entry is skipped rather than re-rendered a second time.
                    // A lane the subsuming render itself requested survives that settle and leaves the fiber
                    // dirty, so the entry is still here; when what survived is delayed-tier work, flushing it
                    // now would render that work in the same pass as the urgent work it is meant to trail.
                    var fiber = _drainBuffer[i];
                    if (immediateTier && HoldsOnlyDelayedTierLanes(fiber))
                    {
                        // Enrolled here rather than trusted to be, so a skipped entry is not stranded whichever
                        // route left the fiber holding only delayed-tier lanes. ScheduleDelayed moves a fiber
                        // waiting on an earlier admission to the one this request chooses, so the skipped work
                        // lands in a later pass, within the bound it describes.
                        FiberWorkLoop.ScheduleFlush(fiber, fiber.LaneQueue.Min);
                        continue;
                    }
                    // Not mirrored on the delayed tier: skipping a delayed entry that holds Normal or Urgent work
                    // would hold that work back rather than keep it from running early.
                    FiberWorkLoop.FlushState(fiber);
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

        private static bool HoldsOnlyDelayedTierLanes(ComponentFiber fiber)
        {
            var lanes = fiber.LaneQueue;
            // MUTANT_SURVIVES(equivalent): an empty set reads Min as Urgent, which is an immediate-tier lane.
            // A guard admitting a zero count therefore still returns false for it.
            return lanes.Count > 0 && !FiberLane.SchedulesOnImmediateTier(lanes.Min);
        }

        // Orders the drain ancestors-before-descendants so a parent's flush (which re-expands its inline
        // children via ComponentRegistry → SubsumeFiberIntoThisPass) subsumes a child into the same pass
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
        // callback registered earlier in the same batch stays registered and commits whatever arrives before it runs.
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

        // Flushes a prior commit's pending passive effects, driven by the discrete-event boundary so an
        // effect runs before that event's render — NOT from the mount / commit-phase FlushImmediate
        // callers, which must leave passive effects pending for the scheduler tick. Gated like
        // FlushImmediate so it never re-enters a live drain / reconcile.
        internal void FlushPendingPassiveEffects()
        {
            if (_draining || (_reconcileActiveProbe?.Invoke() ?? false)) return;
            _flushPassiveEffects?.Invoke();
        }

        // Drops fiber from both pending queues. Called on Unmount / Dispose so a
        // torn-down fiber is not flushed by a still-pending drain callback.
        internal void Remove(ComponentFiber fiber)
        {
            if (_immediateSet.Remove(fiber)) _immediateOrder.Remove(fiber);
            if (_delayedEntries.Remove(fiber)) _delayedOrder.Remove(fiber);
            if (_immediateOrder.Count == 0) _externalStoreRenderQueued = false;
        }

        // Drops every pending fiber and resets the scheduled flags and anchor. Called from the owning
        // Reconciler's Dispose so a still-registered drain callback does not run against a disposed context.
        internal void Clear()
        {
            _immediateOrder.Clear();
            _immediateSet.Clear();
            _delayedOrder.Clear();
            _delayedEntries.Clear();
            _unadmitted = null;
            _admittedForNextPass = null;
            _immediateWorkArrivedMidDrain = false;
            _externalStoreRenderQueued = false;
            _immediateScheduled = false;
            _anchor = null;
            _onDrainBegin = null;
            _onDrainEnd = null;
        }
    }
}
