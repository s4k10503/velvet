#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Velvet
{
    // Frame-driven state machine walking an AnimationSequenceStep[] for Hooks.UseAnimationSequence. Owns no
    // VisualElement/VNode state of its own, and reaches the plays its steps start only through Playback — Advance
    // is driven by UseFrame's own per-frame delta (see Hooks.UseAnimationSequence), and the label and transition
    // a step commits are read back out through ToState() so the
    // caller can feed it straight into a coordinating V.Motion(animate:, transition:) prop. A step's effect
    // (a To step's label/transition, or a Call step's callback) commits the moment the walker ARRIVES at that
    // step, not once its hold elapses — "holds on this step" means the effect already happened and the cursor
    // is now waiting before moving to the NEXT one, mirroring "float in place for 0.5s" reading as "become
    // floating now, stay floating for 0.5s".
    internal sealed class SequenceWalker
    {
        // Bounds the number of Reset() calls a Call step's own callback can trigger reentrantly (e.g. a
        // "restart on checkpoint" pattern) within one arrival — see ArriveAtStart. Recursing through Arrive
        // itself for this would grow the C# call stack without limit (a Call step that always restarts is a
        // legitimate, if degenerate, construct) and risk an uncatchable StackOverflowException; this instead
        // drains pending reentrant resets in a plain loop, so the call stack never grows past a constant depth.
        private const int MaxReentrantResets = 64;

        private IReadOnlyList<AnimationSequenceStep> _steps = Array.Empty<AnimationSequenceStep>();
        private int _stepIndex;
        private int _passesCompleted;
        private float _elapsedInStepSec;
        private float _currentHoldSec;
        // The holds of every step the cursor has left since the last reseed, and every repeat gap it has waited
        // out, across passes, so TimeSec reads the timeline position without re-deriving them. A double, since a loop adds to it for as long as the
        // sequence plays.
        private double _timeBeforeStepSec;
        private string? _currentLabel;
        private StyleTransitionConfig? _currentTransition;
        private bool _isComplete;
        private bool _inRepeatGap;
        private int _generation;
        private bool _isArriving;
        private IReadOnlyList<AnimationSequenceStep>? _pendingResetSteps;
        // Non-null only while the cursor is held on an Await step, whose hold reads as unbounded until
        // DetachAwait clears both.
        private AwaitHold? _await;

        // The plays the To steps start step by this, through the transition ToState hands out.
        public MotionPlayback Playback { get; } = new();

        // The copy of _currentTransition ToState hands out, kept while _currentTransition stays the same.
        private StyleTransitionConfig? _handedTransition;

        // Set by Hooks.UseAnimationSequence's controls.Pause()/Play(); Advance is never called while true (the
        // caller gates it), and the Spring and Bezier plays the steps started hold with it.
        public bool IsPaused
        {
            get => Playback.IsPaused;
            set => Playback.IsPaused = value;
        }

        // True from Cancel until the next reseed.
        public bool IsCancelled { get; private set; }

        private bool _releaseHeldAfterCommit;

        // Called after every commit of the hook's component.
        public void AfterCommit()
        {
            if (_releaseHeldAfterCommit)
            {
                _releaseHeldAfterCommit = false;
                Playback.ReleaseHeldPlays();
            }
        }

        // Framer Motion's cancel() over the sequence: the cursor stops where it is, and each Spring or Bezier play
        // its steps started returns to its starting values and stops there (MotionPlayback.CancelPlays).
        public void Cancel()
        {
            IsPaused = true;
            IsCancelled = true;
            Playback.CancelPlays();
        }

        public bool IsComplete => _isComplete;

        public int StepIndex => _stepIndex;

        public float TimeSec => (float)(_timeBeforeStepSec + _elapsedInStepSec);

        // Bumped on every committed Arrive (a real step transition, including a same-index re-arrival on a
        // single-step loop), independent of StepIndex — a caller diffing StepIndex alone would miss the
        // single-step-loop case, where "next" wraps back to the same index it started from.
        public int Generation => _generation;

        // The passes a reseed plays, counting the first; null plays without end. Read at a reseed, where zero
        // commits no step, and on every Advance, where a count the finished passes already reach completes the
        // sequence at once and a count above them resumes a completed one.
        public int? Iterations { get; set; } = 1;

        // The gap between one pass's end and the next pass's start, read as each gap begins. Never follows the
        // last pass, so it delays no completion.
        public float RepeatDelaySec { get; set; }

        public bool HasReseeded { get; private set; }

        // Re-seeds the walker at step 0 and immediately commits its effect. Called once per mount (or deps
        // change) from Hooks.UseAnimationSequence's effect, and again from controls.Restart(). Reentrant-safe:
        // a Call step's own callback invoking this (directly, or via controls.Restart()) while an arrival is
        // already in flight defers the reset instead of recursing — see ArriveAtStart.
        public void Reset(IReadOnlyList<AnimationSequenceStep>? steps)
        {
            if (_isArriving)
            {
                _pendingResetSteps = steps ?? Array.Empty<AnimationSequenceStep>();
                return;
            }
            ResetImmediate(steps);
        }

        // Advances the cursor by dt seconds, committing every step whose hold elapses along the way (a
        // zero-hold Wait/Call chain can cross several steps within one call). Returns the committed step
        // index so the caller can diff it against its own re-render trigger. The iteration count is bounded to
        // _steps.Count + 1 so an all-zero-hold sequence playing more than one pass — without end, or a count
        // above one — cannot spin forever inside one call; it carries on over the frames after.
        public int Advance(float dt)
        {
            if (_steps.Count == 0)
            {
                return _stepIndex;
            }
            if (_isComplete)
            {
                ResumeIfPassesRemain();
                return _stepIndex;
            }
            if (!PassesRemain)
            {
                CompleteAtLoweredCount();
                return _stepIndex;
            }
            if (_await != null)
            {
                if (!_await.IsSettled)
                {
                    return _stepIndex;
                }
                // Time left over from before the await counts toward no hold after it; this frame's does.
                _elapsedInStepSec = 0f;
                ReleaseAwait();
            }

            _elapsedInStepSec += dt;
            var guard = _steps.Count + 1;
            while (!_isComplete && _elapsedInStepSec >= _currentHoldSec && guard-- > 0)
            {
                _elapsedInStepSec -= _currentHoldSec;
                _timeBeforeStepSec += _currentHoldSec;
                if (_inRepeatGap)
                {
                    _inRepeatGap = false;
                    ArriveAtStart(0);
                    continue;
                }
                var next = _stepIndex + 1;
                if (next >= _steps.Count)
                {
                    _passesCompleted++;
                    if (!PassesRemain)
                    {
                        _isComplete = true;
                        // A finished sequence reads its full length, not the overshoot past it.
                        _elapsedInStepSec = 0f;
                        break;
                    }
                    if (RepeatDelaySec > 0f)
                    {
                        _inRepeatGap = true;
                        _currentHoldSec = RepeatDelaySec;
                        continue;
                    }
                    next = 0;
                }
                ArriveAtStart(next);
            }
            return _stepIndex;
        }

        // A null Iterations plays without end.
        private bool PassesRemain => Iterations == null || _passesCompleted < Iterations;

        // Completes at a count the finished passes already reach. The wait the cursor was parked on is abandoned
        // only once the walker has completed, as ResetImmediate abandons the one a reseed leaves.
        private void CompleteAtLoweredCount()
        {
            var left = DetachAwait();
            AdoptEndState();
            _inRepeatGap = false;
            _isComplete = true;
            _elapsedInStepSec = 0f;
            left?.Abandon();
        }

        // A count lowered mid-pass shows the state a normal completion holds: the cursor on the last step, the
        // label and transition the To steps leave, folded as Arrive folds them, and the time a completion reads,
        // Iterations passes of the steps' holds with a repeat gap between each. No Call callback runs and an
        // Await step holds for no time. Zero passes commit nothing, so a count lowered to zero leaves the cursor
        // and label as they are and reads time 0.
        private void AdoptEndState()
        {
            string? label = null;
            StyleTransitionConfig? transition = null;
            var firstPassSec = SimulatePass(ref transition, ref label);
            var passes = Iterations ?? 0;
            // The transition a pass leaves is the same from the second pass on, so a second pass costed from the
            // first's carry stands for every later one.
            var laterPassSec = firstPassSec;
            if (passes > 1)
            {
                var carry = transition;
                string? laterLabel = null;
                laterPassSec = SimulatePass(ref carry, ref laterLabel);
            }
            _timeBeforeStepSec = passes == 0
                ? 0
                : firstPassSec + (passes - 1) * (laterPassSec + (double)RepeatDelaySec);
            if (passes == 0)
            {
                return;
            }
            _stepIndex = _steps.Count - 1;
            _currentLabel = label;
            _currentTransition = transition;
        }

        // The holds of one pass from `carry`, the transition the cursor brings to its first step, folding the label
        // and transition each To step leaves as Arrive does.
        private double SimulatePass(ref StyleTransitionConfig? carry, ref string? label)
        {
            double passSec = 0;
            for (var i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].Kind == AnimationSequenceStepKind.To)
                {
                    label = _steps[i].Label;
                    carry = _steps[i].Transition ?? carry ?? StyleTransition.Fade;
                }
                passSec += HoldOf(_steps[i], carry);
            }
            return passSec;
        }

        // The frame after a raised count, not the render that raised it, and the time since the sequence completed
        // does not count toward what follows. A pass already played is followed by the repeat gap before the next
        // starts at step 0, as between any two passes; a sequence that played none, completed by a count of zero,
        // starts at step 0 at once.
        private void ResumeIfPassesRemain()
        {
            if (!PassesRemain)
            {
                return;
            }
            _isComplete = false;
            _elapsedInStepSec = 0f;
            if (_passesCompleted > 0 && RepeatDelaySec > 0f)
            {
                _inRepeatGap = true;
                _currentHoldSec = RepeatDelaySec;
                return;
            }
            ArriveAtStart(0);
        }

        public AnimationSequenceState ToState() => new(_currentLabel, HandedTransition(), _stepIndex, _isComplete);

        // The coordinator Motion is handed a copy carrying Playback, which is how the plays its label starts find
        // the sequence; the fold above keeps the authored configs.
        private StyleTransitionConfig? HandedTransition()
        {
            if (_currentTransition == null)
            {
                return null;
            }
            if (!ReferenceEquals(_handedTransition?.PlaybackOrigin, _currentTransition))
            {
                _handedTransition = _currentTransition.WithPlayback(Playback);
            }
            return _handedTransition;
        }

        // The wait a reseed leaves is abandoned only after the reseed has arrived, here and in ArriveAtStart's
        // drain: its token's callbacks are user code, so one that throws must find the walker reseeded rather
        // than halfway through, and a restart from one must find the new wait installed to abandon in turn.
        // Where the arrival itself threw, its exception stays the one that propagates.
        private void ResetImmediate(IReadOnlyList<AnimationSequenceStep>? steps)
        {
            var left = DetachAwait();
            try
            {
                var isComplete = ApplyStepsReset(steps ?? Array.Empty<AnimationSequenceStep>());
                if (!isComplete)
                {
                    ArriveAtStart(0);
                }
            }
            catch
            {
                AbandonLoggingAnyThrow(left);
                throw;
            }
            left?.Abandon();
        }

        private static void AbandonLoggingAnyThrow(AwaitHold? left)
        {
            try
            {
                left?.Abandon();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        // Shared by ResetImmediate and ArriveAtStart's reentrant drain: both re-seed the walker at step 0
        // from a steps list and must reset every one of these fields together, or a stale label/hold/
        // warned-index could survive across a reseed. Returns the resulting IsComplete so each caller
        // decides its own next step (ArriveAtStart(0) vs break) without duplicating the reset itself.
        private bool ApplyStepsReset(IReadOnlyList<AnimationSequenceStep> steps)
        {
            _steps = steps;
            _stepIndex = 0;
            _passesCompleted = 0;
            HasReseeded = true;
            // A cancel holds its plays at their starting values. The reseed's own plays start from them on the
            // elements whose label it changes, which they can do only in the commit that renders the reseed, so
            // the rest are released after it (AfterCommit).
            _releaseHeldAfterCommit |= IsCancelled;
            IsCancelled = false;
            _elapsedInStepSec = 0f;
            _currentHoldSec = 0f;
            _timeBeforeStepSec = 0f;
            _currentLabel = null;
            _currentTransition = null;
            _isComplete = _steps.Count == 0;
            _inRepeatGap = false;
            WarnAboutUnvalidatedToSteps();
            if (Iterations == 0)
            {
                _isComplete = true;
            }
            return _isComplete;
        }

        // A default(AnimationSequenceStep) (an unfilled array/list slot) bypasses the To/Wait/Call factories'
        // own validation entirely — struct default-construction cannot be blocked by a private constructor in
        // C#. Its Kind reads as To (the enum's zero value) with a null Label, which Arrive would otherwise
        // adopt silently. Caught once per Reset rather than per-Arrive, since the steps list itself is fixed
        // for the lifetime of a single Reset generation.
        private void WarnAboutUnvalidatedToSteps()
        {
            for (var i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                if (step.Kind == AnimationSequenceStepKind.To && step.Label == null)
                {
                    FiberLogger.LogWarning("AnimationSequence",
                        $"steps[{i}] is a default(AnimationSequenceStep), not one built through To/Wait/Call/Await "
                        + "(a likely unfilled array slot). Treating it as a no-op Wait(0) instead of a To step "
                        + "with a null label.");
                }
            }
        }

        // Commits the step at `index` (Arrive), then drains any Reset() calls the step's own Call callback
        // triggered reentrantly. The drain is a plain loop, not recursion through this method, so the C# call
        // stack depth stays constant no matter how many times a callback restarts the sequence from within
        // itself — see MaxReentrantResets.
        private void ArriveAtStart(int index)
        {
            _isArriving = true;
            try
            {
                Arrive(index);
            }
            finally
            {
                _isArriving = false;
            }

            var guard = MaxReentrantResets;
            while (_pendingResetSteps != null && guard-- > 0)
            {
                var pending = _pendingResetSteps;
                _pendingResetSteps = null;
                var left = DetachAwait();
                bool isComplete;
                try
                {
                    isComplete = ApplyStepsReset(pending);
                    if (!isComplete)
                    {
                        _isArriving = true;
                        try
                        {
                            Arrive(0);
                        }
                        finally
                        {
                            _isArriving = false;
                        }
                    }
                }
                catch
                {
                    AbandonLoggingAnyThrow(left);
                    throw;
                }
                left?.Abandon();
                if (isComplete)
                {
                    break;
                }
            }
            if (_pendingResetSteps != null)
            {
                FiberLogger.LogWarning("AnimationSequence",
                    "A step's callback kept restarting the sequence reentrantly past the safety limit "
                    + $"({MaxReentrantResets}); the extra restart requests were dropped this frame.");
                _pendingResetSteps = null;
            }
        }

        // Commits the effect of the step at `index` (fires a Call callback synchronously, or adopts a To
        // step's label/transition), resolves the hold the walker parks on before advancing further, and bumps
        // Generation so a caller diffing it (rather than StepIndex alone) always sees a real commit. A thrown
        // Call callback propagates straight out of here, through Advance(), into the UseFrame tick that called
        // it — UseFrame's own try/catch already routes a user-callback exception to the nearest error boundary,
        // so this needs no guard of its own. Does NOT touch _elapsedInStepSec: Advance's own loop already
        // carries the overshoot past this step's hold into the next one before calling this, and Reset/
        // ArriveAtStart's own callers zero it explicitly for a fresh start — resetting it here a second time
        // would discard that carried-over remainder.
        private void Arrive(int index)
        {
            _stepIndex = index;
            _generation++;
            var step = _steps[index];
            switch (step.Kind)
            {
                case AnimationSequenceStepKind.To:
                    _currentLabel = step.Label;
                    _currentTransition = step.Transition ?? _currentTransition ?? StyleTransition.Fade;
                    _currentHoldSec = HoldOf(step, _currentTransition);
                    break;
                case AnimationSequenceStepKind.Wait:
                    _currentHoldSec = HoldOf(step, _currentTransition);
                    break;
                case AnimationSequenceStepKind.Call:
                    _currentHoldSec = 0f;
                    step.Callback?.Invoke();
                    break;
                case AnimationSequenceStepKind.Await:
                    _currentHoldSec = 0f;
                    BeginAwait(step.AwaitFactory!);
                    break;
            }
        }

        // The hold a To or Wait step parks the cursor for, given the transition the cursor carries into it; every
        // other kind holds for none.
        private static float HoldOf(in AnimationSequenceStep step, StyleTransitionConfig? transition)
        {
            switch (step.Kind)
            {
                case AnimationSequenceStepKind.To:
                    return Math.Max(0f, step.HoldSec ?? ResolveHoldFromTransition(transition!));
                case AnimationSequenceStepKind.Wait:
                    return Math.Max(0f, step.HoldSec ?? 0f);
                default:
                    return 0f;
            }
        }

        // Hooks.UseAnimationSequence calls this on unmount. A task still pending has its token cancelled.
        public void AbandonAwait() => DetachAwait()?.Abandon();

        // The hold is unbounded only while _await holds it.
        private AwaitHold? DetachAwait()
        {
            var hold = _await;
            if (hold != null)
            {
                _await = null;
                _currentHoldSec = 0f;
            }
            return hold;
        }

        // A factory that throws propagates as a throwing Call callback does. The continuation is registered as an
        // await registers it, so where it runs inline the step is released here and crossed in the same Advance
        // as a Call step.
        private void BeginAwait(Func<CancellationToken, VelvetTask> taskFactory)
        {
            var hold = new AwaitHold();
            var task = taskFactory(hold.Token);
            task.GetAwaiter().OnCompleted(() => hold.Settle(VelvetTaskOutcome.Consume(task)));
            _await = hold;
            if (hold.IsSettled)
            {
                ReleaseAwait();
            }
            else
            {
                // Stops Advance's loop on this step until a later frame reads the settle.
                _currentHoldSec = float.PositiveInfinity;
            }
        }

        // Rethrows the task's fault, or a cancellation the walker did not cause, the way a throwing Call callback
        // propagates, with the cursor already free to move on.
        private void ReleaseAwait()
        {
            DetachAwait()!.ThrowIfFailed();
        }

        // One per arrival at an Await step, so a continuation from an earlier arrival writes only to a hold the
        // walker has already dropped and cannot release the step the cursor is on now.
        private sealed class AwaitHold
        {
            private readonly CancellationTokenSource _cancellation = new();
            private VelvetTaskOutcome<AsyncUnit> _outcome;
            private bool _abandoned;

            public bool IsSettled { get; private set; }

            public CancellationToken Token => _cancellation.Token;

            public void Settle(VelvetTaskOutcome<AsyncUnit> outcome)
            {
                _outcome = outcome;
                IsSettled = true;
                if (_abandoned)
                {
                    VelvetTaskScheduler.PublishUnobservedFaults(outcome.Faults);
                }
            }

            // A fault that settled before the walker read it, or that arrives later, is logged as Forget() logs
            // one; the cancellation this causes is not.
            public void Abandon()
            {
                _abandoned = true;
                if (IsSettled)
                {
                    VelvetTaskScheduler.PublishUnobservedFaults(_outcome.Faults);
                    return;
                }
                _cancellation.Cancel();
            }

            public void ThrowIfFailed()
            {
                if (_outcome.Faults != null)
                {
                    _outcome.Faults[0].Throw();
                }
                if (_outcome.Cancellation != null)
                {
                    ExceptionDispatchInfo.Capture(_outcome.Cancellation).Throw();
                }
            }
        }

        // Auto-derives a To step's hold from its transition when the step declares no explicit HoldSec. Only a
        // Tween reads PropertyOverrides, mirroring StyleAnimationScheduler.SlowestPropertyTimeoutMs's own "slowest
        // overridden property wins" rule: a PropertyOverrides entry can give one property a longer duration/delay
        // than the top-level values (StyleTransitionConfig's own documented example: opacity in 0.15s while scale
        // takes 0.5s), and the walker must not advance past a Tween step while a property that override still
        // governs is mid-tween.
        private static float ResolveHoldFromTransition(StyleTransitionConfig transition)
        {
            if (transition.Type == TransitionType.Spring)
            {
                return transition.DelaySec
                    + SpringDurationSec(transition.Stiffness, transition.Damping, transition.Mass);
            }

            var hold = transition.DurationSec + transition.DelaySec;

            // Bezier playback drives every channel with the SAME curve and never reads PropertyOverrides (like a
            // spring's single stiffness/damping/mass), so its real span is the fixed DurationSec + DelaySec.
            // Factoring a longer per-property override in here would park the walker on the step past the moment
            // the tween it describes has actually finished.
            if (transition.Type == TransitionType.Bezier)
            {
                return hold;
            }

            var overrides = transition.PropertyOverrides;
            if (overrides != null)
            {
                for (var i = 0; i < overrides.Count; i++)
                {
                    var o = overrides[i];
                    var overrideHold = (o.DurationSec ?? transition.DurationSec) + (o.DelaySec ?? transition.DelaySec);
                    if (overrideHold > hold)
                    {
                        hold = overrideHold;
                    }
                }
            }
            return hold;
        }

        // Framer Motion's sequence gives a spring segment the duration its generator reports done at: sampled
        // every 50ms, travelling 0→100 when the keyframes carry no distance of their own — as a label carries
        // none — resting within 0.5 of the target at a speed of at most 2 per second, and at most 20 seconds.
        private const double SpringTravel = 100.0;
        private const double SpringRestDelta = 0.5;
        private const double SpringRestSpeed = 2.0;
        private const int SpringSampleMs = 50;
        private const int MaxSpringDurationMs = 20000;

        // Zero for parameters a play refuses to tick, since that play completes at once.
        private static float SpringDurationSec(float stiffness, float damping, float mass)
        {
            if (!SpringIntegrator.AreValidParameters(stiffness, damping, mass))
            {
                return 0f;
            }
            // MUTANT_SURVIVES(equivalent): a sample at the ceiling that rests returns the ceiling, as one that does
            // not rest falls through to it, so `<=` changes nothing.
            for (var ms = 0; ms < MaxSpringDurationMs; ms += SpringSampleMs)
            {
                if (SpringRestsAt(ms / 1000.0, stiffness, damping, mass))
                {
                    return ms / 1000f;
                }
            }
            return MaxSpringDurationMs / 1000f;
        }

        private static bool SpringRestsAt(double t, float stiffness, float damping, float mass)
        {
            var (displacement, velocity) = SpringIntegrator.Solve(SpringTravel, 0.0, t, (stiffness, damping, mass));
            // MUTANT_SURVIVES(equivalent): `<` differs only on a sample landing exactly on 0.5 or on 2, and no
            // sample of the springs the sequence cases time lands on either.
            return Math.Abs(displacement) <= SpringRestDelta && Math.Abs(velocity) <= SpringRestSpeed;
        }
    }
}
