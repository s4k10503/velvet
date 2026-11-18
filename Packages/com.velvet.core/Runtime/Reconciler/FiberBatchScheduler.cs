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
        // Overflow drops the runaway update and throws, as React's limit does (see DrainImmediatePassesUpTo).
        private const int NestedUpdateLimit = 50;

        // How many immediate drains in a row may throw before the updates queued after the last of them are dropped
        // rather than re-armed (see CountRun). A drain that completes resets the count. Held work handed to a later
        // frame (RunInNextFrame) counts as a drain here.
        private const int ConsecutiveThrowingDrainLimit = 3;
        private int _consecutiveThrowingDrains;

        // Bumped each time the bound is reached, which drops every droppable hand-on made before it (RunInNextFrame).
        private int _handOnGeneration;

        // Pending queues: the List keeps enqueue order, which a drain keeps only among the fibers its walk does
        // not reach (AddInTreeOrder), and the HashSet dedups so a fiber dirtied multiple times within one batch
        // is flushed once.
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
        // Flushes any pending passive effects from a prior commit, wired by the owning Reconciler. Run before
        // each drain's render (see Drain).
        private Action? _flushPassiveEffects;

        // Set by intake React would give the sync lane — an update a discrete event's handler makes, a write made
        // while a layout commit runs, a UseSyncExternalStore change — and taken by the pass that renders it, which
        // then flushes the passive effects of its commits as it ends (DrainImmediatePasses). Intake from anywhere
        // else, a passive effect's included, leaves it as it is.
        private bool _syncLaneQueued;

        // Raised while DrainImmediate runs, whose own passes render the sync-lane work its passive flushes queue.
        private bool _inImmediateDrain;

        // Counts every ScheduleImmediate call, for a caller asking whether a flush it ran queued anything; a count
        // of the queue would miss intake that dedups onto a fiber already queued.
        private int _immediateIntake;

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

        internal bool IsDisposed { get; private set; }

        internal bool OwesSyncLane => _syncLaneQueued && _immediateOrder.Count > 0;

        // Count of fibers awaiting the next-frame (Normal / Urgent) batch drain.
        internal int ImmediatePendingCount => _immediateOrder.Count;

        // Count of fibers awaiting the delayed (Transition) batch drain.
        internal int DelayedPendingCount => _delayedOrder.Count;

        // Reused per drain so a fiber re-dirtying itself during flush (re-enqueue) does not mutate the
        // queue being iterated. A single buffer suffices because the _draining guard ensures only one drain
        // (immediate or delayed) is ever live at a time. Cleared after each drain to release fiber refs for GC.
        private readonly List<ComponentFiber> _drainBuffer = new();

        // AddInTreeOrder's, reused across drains as _drainBuffer is. A walk that returns leaves both empty.
        private readonly HashSet<ComponentFiber> _unreached = new();
        private readonly List<ComponentFiber> _walk = new();

        // Each fiber above a component row the last walk passed through, with its render count then
        // (AFiberAboveARowRendered), and OrderRestInTree's copy of what the drain has yet to render.
        private readonly List<(ComponentFiber Fiber, int Renders)> _aboveRows = new();
        private readonly List<ComponentFiber> _rest = new();

        private readonly Action _runImmediateCallback;
        private readonly Action _flushHeld;
        private readonly Action _flushHeldPassiveEffects;

        internal FiberBatchScheduler()
        {
            _runImmediateCallback = RunImmediateCallback;
            // MUTANT_SURVIVES(equivalent, line removed): FiberExecutionContext.Release runs a held request that carries no
            // run as its owner's FlushImmediate, which is what this delegate runs.
            _flushHeld = FlushImmediate;
            _flushHeldPassiveEffects = FlushPendingPassiveEffects;
        }

        // The scheduler every callback of this context runs on: the root context's host scheduler.
        internal IHostScheduler Host { get; } = PanelHostScheduler.Instance;

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
            // An empty tier holds no sync-lane work, whatever left it empty: a drain, a Remove, a drop or a Clear.
            if (_immediateOrder.Count == 0) _syncLaneQueued = false;
            if (FiberWorkLoop.IsInDiscreteEvent || fiber.Reconciler?.Context.InLayoutCommit == true) MarkSyncLane();
            _immediateIntake++;
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
            MarkSyncLane();
            if (_externalStoreRenderQueued) return;
            VelvetMainThread.Post(static scheduler => ((FiberBatchScheduler)scheduler!).FlushOwedExternalStoreRenders(), this);
            _externalStoreRenderQueued = true;
        }

        // Called by the posted flush and before FiberWorkLoop.ContinueReconcile resumes a parked time-sliced pass, so the readers it committed in earlier slices have
        // re-rendered the changed snapshot before a later slice's readers render it.
        internal void FlushOwedExternalStoreRenders()
        {
            if (FiberExecutionContext.IsSet)
            {
                if (_externalStoreRenderQueued && _immediateOrder.Count > 0) HoldFlush();
                return;
            }
            FiberExecutionContext.Enter(this, this, static scheduler => scheduler.FlushOwedExternalStoreRendersWork());
        }

        internal void FlushOwedExternalStoreRendersWork()
        {
            if (_externalStoreRenderQueued && _immediateOrder.Count > 0) FlushImmediateWork();
        }

        private void ArmImmediateDrain()
        {
            if (_immediateScheduled || _anchor == null) return;
            _immediateScheduled = true;
            ScheduledCallbackCount++;
            Host.Schedule(_anchor, _runImmediateCallback);
        }

        private void RunImmediateCallback()
        {
            var outer = Host.Enter(_anchor);
            _inImmediateCallback = true;
            try
            {
                DrainImmediate();
            }
            finally
            {
                _inImmediateCallback = false;
                Host.Exit(outer);
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
            var admission = Host.InPassOf(_anchor) ? AdmissionForNextPass() : Unadmitted();
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
            if (current != null && current.RegisteredIn == Host.CurrentPass) return current;
            var admission = new DelayedAdmission(Host.CurrentPass);
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
            Host.Schedule(_anchor, callback);
        }

        // Held work of this root that FiberExecutionContext's release does not run, made in a later frame on the anchor
        // the tiers' drains are registered on. A root with no anchor registers no drain either, and has no frame. Where
        // droppable, as a range or a resume is, it is dropped past the bound on drains that throw; a resolve is not, for
        // the reason CountRun gives.
        internal void RunInNextFrame(Action run, bool droppable)
        {
            var anchor = _anchor;
            if (anchor == null) return;
            if (droppable && _consecutiveThrowingDrains >= ConsecutiveThrowingDrainLimit) return;
            var handOn = new HandOn(this, run, droppable, _handOnGeneration);
            var host = Host;
            host.Schedule(anchor, () => host.Run(anchor, handOn, static held => held.Run()));
        }

        private sealed class HandOn
        {
            private readonly FiberBatchScheduler _scheduler;
            private readonly Action _run;
            private readonly bool _droppable;
            private readonly int _generation;

            internal HandOn(FiberBatchScheduler scheduler, Action run, bool droppable, int generation)
            {
                _scheduler = scheduler;
                _run = run;
                _droppable = droppable;
                _generation = generation;
            }

            // Logged here, as the release logs what an item throws, rather than thrown out to the panel's scheduler:
            // what that does to the items behind it is held by HeldWorkReleaseTests
            // .Given_AHandedOnRangeWhoseNewRowThrowsAsItMounts_When_ThePanelRunsAPass_Then_ThePanelsOtherScheduledWorkHasRun.
            internal void Run()
            {
                if (_droppable && _generation != _scheduler._handOnGeneration) return;
                var completed = false;
                try
                {
                    _run();
                    completed = true;
                }
                catch (Exception thrown)
                {
                    UnityEngine.Debug.LogException(thrown);
                }
                _scheduler.CountRun(completed);
            }
        }

        // Without the bound, a commit that throws and queues itself again on every drain would throw once a frame, the
        // same reason the nested-update cap drops its runaway rather than re-arming. Reaching it does not end every such
        // run: whatever renders the thrower afterwards folds in the dropped update, which stays in its hook's slot. So it
        // drops the ranges and resumes handed on before it as well, since a drain that completes over the emptied tier
        // resets the count, and a range or resume left to render the dropped update could start the run afresh. A resolve
        // handed on is kept, because it is what retries the render waiting on its read.
        private void CountRun(bool completed)
        {
            _consecutiveThrowingDrains = completed ? 0 : _consecutiveThrowingDrains + 1;
            if (_consecutiveThrowingDrains < ConsecutiveThrowingDrainLimit) return;
            _handOnGeneration++;
            if (_immediateOrder.Count == 0) return;
            FiberLogger.LogError("Scheduler",
                $"{ConsecutiveThrowingDrainLimit} runs of a root's work in a row threw, and the last left updates"
                + " queued. Those updates were dropped.");
            DropImmediateIntake();
        }

        private void RunAdmitCallback(DelayedAdmission admission)
        {
            var outer = Host.Enter(_anchor);
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
                Host.Exit(outer);
            }
        }

        private void RunDelayedCallback(DelayedAdmission admission)
        {
            var outer = Host.Enter(_anchor);
            try
            {
                DrainDelayedTier(admission);
            }
            finally
            {
                Host.Exit(outer);
            }
        }

        private void DrainImmediate()
        {
            if (FiberExecutionContext.IsSet)
            {
                // The registered callback has run, and the held flush registers another for what it leaves queued, unless
                // that flush stands down (DrainImmediateWork), which leaves the tier with no callback registered.
                if (_inImmediateCallback) _immediateScheduled = false;
                HoldFlush();
                return;
            }
            FiberExecutionContext.Enter(this, this, static scheduler => scheduler.DrainImmediateWork());
        }

        // A flush asked for while this scheduler's immediate drain runs, or while a pass of either tier renders, is left to
        // that drain: what a pass, or the passive flush ahead of one, queues renders before the drain returns, except an
        // update the passive flush after a sync-lane pass queues off the sync lane, which DrainImmediatePasses leaves for
        // the tier's next drain. A flush asked for anywhere else in an entry waits for the outermost entry to end, and then
        // runs the passive effects earlier commits left pending before it drains.
        internal void HoldFlush()
        {
            if (_draining || _inImmediateDrain) return;
            FiberExecutionContext.Hold(FiberExecutionContext.Request.Flush, this, this, _flushHeld);
        }

        // Each drain of the immediate tier runs through here, so this is where a scheduler an exception left an entry of
        // in the release running stands down (FiberExecutionContext's release says why): it returns false and leaves the
        // tier and its registration as they are.
        private bool DrainImmediateWork()
        {
            if (FiberExecutionContext.Threw(this)) return false;
            var completed = false;
            var wasInImmediateDrain = _inImmediateDrain;
            _inImmediateDrain = true;
            try
            {
                DrainImmediatePasses();
                completed = true;
            }
            finally
            {
                _inImmediateDrain = wasInImmediateDrain;
                // In a finally because an exception leaves a drain too (DrainExceptionRecoveryTests throws one from
                // an imperative-handle factory), and a flag left raised has ArmImmediateDrain register nothing for a
                // later request until the registered callback lowers it. Intake still queued here was requested by a
                // settle on a drop, or enqueued before the exception, and the loop that would have consumed it has
                // exited. Only the registered callback lowers the flag; see _inImmediateCallback.
                if (_inImmediateCallback) _immediateScheduled = false;
                CountRun(completed);
                if (_immediateOrder.Count == 0) _externalStoreRenderQueued = false;
                if (_immediateOrder.Count > 0) ArmImmediateDrain();
            }
            return true;
        }

        private void DrainImmediatePasses()
        {
            var renderedSyncLane = false;
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
            DrainImmediatePassesUpTo(ref totalPasses, ref renderedSyncLane);
            while (true)
            {
                // Ahead of the flush below: an update an effect there schedules is an ordinary one, and a flag left
                // standing would keep OweExternalStoreFlush from posting the next store change.
                if (_immediateOrder.Count == 0) _externalStoreRenderQueued = false;
                if (!renderedSyncLane) return;
                renderedSyncLane = false;
                // React flushes the passive effects of a sync-lane commit as that commit ends, then flushes sync
                // work: when those effects queued a store hook's forced re-render, it renders the root at every
                // pending lane of its sync set, the default lane an effect's update takes included. So the passes
                // below render the whole tier and count toward the same cap. A flush that forced no re-render leaves
                // what it scheduled for the tier's next drain, as a default-lane update waits for React's next task.
                FlushPendingPassiveEffectsWork();
                if (!_syncLaneQueued) return;
                DrainImmediatePassesUpTo(ref totalPasses, ref renderedSyncLane);
            }
        }

        // Renders the immediate tier when a passive drain queued sync-lane work, then flushes what those renders'
        // commits leave pending in turn. Called once a passive drain outside every immediate drain has run, as
        // React's passive flush ends by flushing sync work.
        internal void FlushSyncWorkAfterPassiveEffects()
        {
            if (_inImmediateDrain || _draining || (_reconcileActiveProbe?.Invoke() ?? false)) return;
            if (!_syncLaneQueued || _immediateOrder.Count == 0) return;
            DrainImmediateWork();
        }

        private void DrainImmediatePassesUpTo(ref int totalPasses, ref bool renderedSyncLane)
        {
            while (_immediateOrder.Count > 0)
            {
                if (totalPasses > NestedUpdateLimit)
                {
                    // Runaway commit-phase loop (a component writing a NEW value on every pass). React throws
                    // from the update that passes its nested-update limit, and no error boundary takes it; so
                    // does this, once the runaway update is dropped, since a drain the throw leaves with that
                    // update still queued would re-arm and burn the full cap again every frame. The UI keeps the
                    // last committed state. Only the IMMEDIATE-tier
                    // lanes are dropped: a fiber can be queued this deep with an unrelated Transition
                    // lane still pending (the runaway's commits may write bystanders each pass), and
                    // wiping that lane would strand its delayed-tier work (a StartTransition left
                    // isPending forever). A Transition lane that starvation-promoted to Normal DURING
                    // the runaway (the cap exceeds the starvation threshold) is dropped with it, but its writes stay
                    // queued in their slots: the flush that promoted it either drained them or rendered an Urgent
                    // lane that skipped them and asked for the Transition lane again, so that lane survives the drop,
                    // and the pending-flag sweep below spares an async action still in flight. The runaway update
                    // itself stays queued in its slot too, and the next render of that component folds it in.
                    DropImmediateIntake();
                    throw new InvalidOperationException(
                        "Maximum update depth exceeded. A component repeatedly schedules state updates from its commit"
                        + " phase (a callback ref or an effect writing a new value on every pass). Velvet limits the"
                        + " number of nested updates, as React does, to prevent infinite loops.");
                }
                totalPasses++;
                renderedSyncLane |= _syncLaneQueued;
                _syncLaneQueued = false;
                Drain(_immediateOrder, _immediateSet, immediateTier: true);
            }
        }

        // Which lanes go, and why only these, is the nested-update cap's comment in DrainImmediatePassesUpTo.
        // Copied out rather than walked in place: a settle below can request a render, which enqueues it again.
        private void DropImmediateIntake()
        {
            var droppedFibers = _immediateOrder.ToArray();
            // A capture waiting for a render was waiting on a lane just dropped, or on one a boundary held back keeps.
            _onIntakeDropped?.Invoke();
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
                dropped.HasPromotedTransition = false;
                // MUTANT_SURVIVES(unreachable, equality): a fiber left dirty with an empty queue is re-enrolled by a Normal update; see IsDirty below.
                if (dropped.LaneQueue.Count == 0)
                {
                    dropped.IsDirty = false;
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
                next ??= Host.InPassOf(_anchor) ? AdmissionForNextPass() : Unadmitted();
                _delayedEntries[fiber] = new DelayedEntry(next, entry.Deferrals);
            }
        }

        private void DrainDelayedTier(DelayedAdmission? admission)
        {
            if (FiberExecutionContext.IsSet)
            {
                FiberExecutionContext.Hold(FiberExecutionContext.Request.DelayedFlush, (object?)admission ?? this, this,
                    (Scheduler: this, Admission: admission), static state => state.Scheduler.DrainDelayedTier(state.Admission));
                return;
            }
            FiberExecutionContext.Enter(this, (Scheduler: this, Admission: admission),
                static state => state.Scheduler.DrainDelayedTierWork(state.Admission));
        }

        private void DrainDelayedTierWork(DelayedAdmission? admission)
        {
            // The immediate queue is committed first rather than trusting the panel to run its callback ahead of
            // this one. A Transition lane that pass re-enrols takes a later admission (see ScheduleDelayed), so
            // only a drain of every admission picks it up below.
            var drained = false;
            try
            {
                drained = DrainImmediateAheadOfTheTransition();
            }
            finally
            {
                // This admission's callback has run and registers nothing again, so where the immediate drain threw or
                // stood down, entries still waiting for it would wait for a drain that never comes while their fibers
                // stay dirty.
                if (!drained && admission != null) ReadmitEntries(admission);
            }
            if (!drained) return;
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
                DrainImmediateWork();
            }
        }

        // React's task for a transition flushes pending passive effects before it picks its lanes, and an update
        // one of them schedules outranks the transition, so it renders first and the transition waits for a later
        // task. Each round renders what the round before it scheduled. Bounded like the immediate drain's passes,
        // since an effect that sets state on every commit would otherwise hold the transition in this callback
        // forever; past the bound the transition renders, and what is still queued waits for the tier's callback.
        private bool DrainImmediateAheadOfTheTransition()
        {
            for (var rounds = 0; ; rounds++)
            {
                if (!DrainImmediateWork()) return false;
                if (rounds == NestedUpdateLimit) return true;
                FlushPendingPassiveEffectsWork();
                if (_immediateOrder.Count == 0) return true;
            }
        }

        private void Drain(List<ComponentFiber> order, HashSet<ComponentFiber>? set, bool immediateTier)
        {
            if (order.Count == 0) return;
            // React flushes pending passive effects before it starts a render, so an earlier commit's effects and
            // presence completions run before this pass renders. Ahead of the copy below, so on the immediate tier
            // an update one of them schedules joins this pass.
            FlushPendingPassiveEffectsWork();
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
                AddInTreeOrder(order, _drainBuffer);
                order.Clear();
                set?.Clear();
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
                    FiberWorkLoop.FlushStateWork(fiber);
                    // Unlike a fiber an ancestor's render subsumes, a component row that render moves still renders
                    // by its own entry, so once a fiber above one the last walk passed through has rendered, what
                    // the drain has yet to render takes its order from the tree again.
                    if (AFiberAboveARowRendered()) OrderRestInTree(i);
                }
                _drainBuffer.Clear();
                // MUTANT_SURVIVES(equivalent, line removed): the next walk clears the list before reading it, so the entries only keep their fibers reachable until then.
                _aboveRows.Clear();
            }
            finally
            {
                // _onDrainEnd applies the pass the drain's renders recorded into and flushes their deferred layout
                // effects (FiberWave). Run it while _draining is still true so a re-entrant FlushImmediate raised from
                // one of those effects still no-ops (defers) rather than re-entering the reconciler. The inner finally guarantees _draining is cleared even
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

        // React renders the updates one render takes in a single walk down the root's tree, so an ancestor renders
        // ahead of its descendants and, of two fibers neither of which holds the other, the one earlier in the tree
        // renders first. The first is what lets a parent's flush subsume a child queued here into the same pass
        // before the child's own entry is reached (the loop in Drain says how), whatever order their setters ran in.
        // Siblings are taken in the order their output sits in the tree (LogicalElementTree), not in the Child
        // chain's: the chain holds a Portal's children, and a component mounted later into a container ahead of a
        // sibling's, behind that sibling. A queued fiber the walk does not reach keeps its enqueue order, behind the
        // ones it does.
        private void AddInTreeOrder(List<ComponentFiber> queued, List<ComponentFiber> into)
        {
            // MUTANT_SURVIVES(equivalent, line removed): an entry an earlier walk left can only add a re-sort, and only the render of a fiber the last walk noted moves a fiber the drain has yet to render, so a re-sort it adds finds the rest in tree order already.
            _aboveRows.Clear();
            var root = queued.Count < 2 ? null : ContextRootAbove(queued);
            if (root == null)
            {
                into.AddRange(queued);
                return;
            }
            foreach (var fiber in queued) _unreached.Add(fiber);
            LogicalElementTree? tree = null;
            try
            {
                AddReachedInPreOrder(root, into, ref tree);
            }
            finally
            {
                tree?.Return();
            }
            foreach (var fiber in queued)
            {
                if (_unreached.Remove(fiber)) into.Add(fiber);
            }
        }

        private void AddReachedInPreOrder(ComponentFiber root, List<ComponentFiber> into, ref LogicalElementTree? tree)
        {
            _walk.Add(root);
            while (_walk.Count > 0)
            {
                var fiber = _walk[_walk.Count - 1];
                _walk.RemoveAt(_walk.Count - 1);
                if (_unreached.Remove(fiber)) into.Add(fiber);
                // A component row, the component a VirtualList's renderer returns as a row, is the one child a
                // mount places in an element of its own rather than inline in its parent's
                // (FiberNodeFactory.CreateForComponentNode).
                if (!fiber.IsInlineMounted)
                {
                    for (var above = fiber.Parent; above != null; above = above.Parent)
                    {
                        _aboveRows.Add((above, above.RenderCount));
                    }
                }
                // Inserted at one index, the children stand in reverse; OrderLatestFirst puts the latest output
                // lowest and leaves a tie in that reverse, so the walk takes the earliest output first and, of a
                // tie, the first in the Child chain.
                var pushed = _walk.Count;
                for (var child = fiber.Child; child != null; child = child.Sibling)
                {
                    if (child.PendingWorkBelow || _unreached.Contains(child)) _walk.Insert(pushed, child);
                }
                OrderLatestFirst(_walk, pushed, root.Reconciler!.Context, ref tree);
            }
        }

        private bool AFiberAboveARowRendered()
        {
            var a = 0;
            while (a < _aboveRows.Count && _aboveRows[a].Fiber.RenderCount == _aboveRows[a].Renders) a++;
            return a != _aboveRows.Count;
        }

        private void OrderRestInTree(int done)
        {
            for (var i = done + 1; i < _drainBuffer.Count; i++) _rest.Add(_drainBuffer[i]);
            _drainBuffer.RemoveRange(done + 1, _rest.Count);
            AddInTreeOrder(_rest, _drainBuffer);
            _rest.Clear();
        }

        private static ComponentFiber? ContextRootAbove(List<ComponentFiber> queued)
        {
            foreach (var fiber in queued)
            {
                var top = fiber;
                while (top.Parent != null) top = top.Parent;
                if (top.Reconciler is { OwnsContext: true }) return top;
            }
            return null;
        }

        private static void OrderLatestFirst(
            List<ComponentFiber> walk, int from, ReconcilerContext context, ref LogicalElementTree? tree)
        {
            for (var i = from + 1; i < walk.Count; i++)
            {
                var fiber = walk[i];
                var j = i - 1;
                while (j >= from && OutputStartsAfter(tree ??= LogicalElementTree.Rent(context), fiber, walk[j]))
                {
                    walk[j + 1] = walk[j];
                    j--;
                }
                walk[j + 1] = fiber;
            }
        }

        // A tie, as where a fiber holding no rows sits in front of a sibling's first, reads false both ways and keeps
        // the Child chain's order, which a container's walk writes for the components it places
        // (GeneralPathReconciler.CommitComponentOrder).
        private static bool OutputStartsAfter(LogicalElementTree tree, ComponentFiber fiber, ComponentFiber other)
        {
            var place = tree.PlaceOf(fiber, out var behindLast);
            var otherPlace = tree.PlaceOf(other, out var otherBehindLast);
            return place != null && otherPlace != null
                && tree.PlaceComesAfter(place, behindLast, otherPlace, otherBehindLast);
        }

        // Whether a drain of the immediate tier can start from here: no drain of either tier and no reconcile pass is
        // on the stack. FlushImmediate checks the first and the last of these; a drain of the immediate tier that
        // is running renders what is queued itself.
        internal bool CanFlushImmediate => !_draining && !_inImmediateDrain && !(_reconcileActiveProbe?.Invoke() ?? false);

        // Marks the immediate tier's work as sync-lane work, which a drain that renders it follows with the passive
        // flush and another round of passes (DrainImmediatePasses).
        internal void OweSyncLane() => MarkSyncLane();

        private void MarkSyncLane()
        {
            _syncLaneQueued = true;
            FiberExecutionContext.OweSyncLane(this);
        }

        // Called by the owning Reconciler: what to forget along with the intake DropImmediateIntake drops.
        internal void SetIntakeDropCallback(Action onDropped) => _onIntakeDropped = onDropped;

        private Action? _onIntakeDropped;

        // Synchronously drains the next-frame (Normal / Urgent) batch now instead of waiting for the scheduled
        // frame-boundary callback. A frame-boundary callback registered earlier in the same batch stays registered
        // and commits whatever arrives before it runs.
        internal void FlushImmediate()
        {
            if (FiberExecutionContext.IsSet)
            {
                HoldFlush();
                return;
            }
            FiberExecutionContext.Enter(this, this, static scheduler => scheduler.FlushImmediateWork());
        }

        // No-op while ANY drain (immediate or delayed) is already running OR a reconcile pass is otherwise on
        // the stack: re-entering would run a nested reconcile at depth > 0, which skips the top-level reset of abort /
        // context-snapshot state and can corrupt the in-flight pass, and would also re-enter Drain and clobber the
        // shared drain buffer mid-iteration. The block spans both tiers on purpose: the hazard is the reconcile
        // already on the stack, not which tier is draining. A time-sliced resume (Reconciler.ContinueReconcile)
        // runs via schedule.Execute rather than through Drain, so _draining is false during it; the reconcile-active
        // probe (SetReconcileActiveProbe) catches that case via SharedReconcileDepth > 0. The update is not lost — it
        // stays queued and commits on the next-frame callback that ScheduleImmediate already registered.
        internal void FlushImmediateWork()
        {
            if (_draining || (_reconcileActiveProbe?.Invoke() ?? false)) return;
            DrainImmediateWork();
        }

        internal int ImmediateIntake => _immediateIntake;

        // React's task flushes pending passive effects and then renders the highest-priority lanes pending, so an
        // update one of those effects schedules renders ahead of the transition the task was resuming. The entry that
        // resumes it ran them as it started, and intake is ImmediateIntake from before that entry.
        internal void FlushUpdatesQueuedAheadOfAResume(int intake)
        {
            if (_immediateIntake != intake) FlushImmediateWork();
        }

        // The entry runs the flush as it starts (FiberExecutionContext.Enter).
        internal void FlushPendingPassiveEffects()
        {
            if (FiberExecutionContext.IsSet)
            {
                FiberExecutionContext.Hold(FiberExecutionContext.Request.PassiveFlush, this, this, _flushHeldPassiveEffects);
                return;
            }
            // MUTANT_SURVIVES(equivalent, line removed): each caller outside an entry opens another right after it,
            // VelvetPreviewHost.Settle its FlushImmediate and DrainImmediateForTest its drain, whose start runs the same
            // flush, and no Runtime caller asks for this one inside an entry.
            FiberExecutionContext.Enter(this, this, static _ => { });
        }

        // Gated like FlushImmediateWork so it never re-enters a live drain / reconcile.
        internal void FlushPendingPassiveEffectsWork()
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
            IsDisposed = true;
        }
    }
}
