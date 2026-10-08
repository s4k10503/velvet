#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Velvet
{
    // Frame-driven state machine walking an AnimationSequenceStep[] for Hooks.UseAnimationSequence. Owns no
    // VisualElement/VNode/scheduler state of its own — Advance is driven by UseFrame's own per-frame delta
    // (see Hooks.UseAnimationSequence), and every observable effect is read back out through ToState() so the
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
        private float _elapsedInStepSec;
        private float _currentHoldSec;
        private string? _currentLabel;
        private StyleTransitionConfig? _currentTransition;
        private bool _isComplete;
        private int _generation;
        private bool _isArriving;
        private IReadOnlyList<AnimationSequenceStep>? _pendingResetSteps;
        // Non-null only while the cursor is held on an Await step, whose hold reads as unbounded until
        // DetachAwait clears both.
        private AwaitHold? _await;

        // Frozen by Hooks.UseAnimationSequence's controls.Pause()/Play(); Advance is simply never called while
        // true (the caller gates it), so there is nothing more for this flag to do here.
        public bool IsPaused { get; set; }

        public bool IsComplete => _isComplete;

        public int StepIndex => _stepIndex;

        // Bumped on every committed Arrive (a real step transition, including a same-index re-arrival on a
        // single-step loop), independent of StepIndex — a caller diffing StepIndex alone would miss the
        // single-step-loop case, where "next" wraps back to the same index it started from.
        public int Generation => _generation;

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
        // _steps.Count + 1 so an all-zero-hold loop (with loop: true) cannot spin forever inside one call — it
        // still keeps progressing on every subsequent frame instead.
        public int Advance(float dt, bool loop)
        {
            if (_isComplete || _steps.Count == 0)
            {
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
                var next = _stepIndex + 1;
                if (next >= _steps.Count)
                {
                    if (!loop)
                    {
                        _isComplete = true;
                        break;
                    }
                    next = 0;
                }
                ArriveAtStart(next);
            }
            return _stepIndex;
        }

        public AnimationSequenceState ToState() => new(_currentLabel, _currentTransition, _stepIndex, _isComplete);

        // The wait a reseed leaves is abandoned only after the reseed has arrived, here and in ArriveAtStart's
        // drain: its token's callbacks are user code, so one that throws must find the walker reseeded rather
        // than halfway through, and a restart from one must find the new wait installed to abandon in turn.
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
            finally
            {
                left?.Abandon();
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
            _elapsedInStepSec = 0f;
            _currentHoldSec = 0f;
            _currentLabel = null;
            _currentTransition = null;
            _isComplete = _steps.Count == 0;
            WarnAboutUnvalidatedToSteps();
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
                try
                {
                    var isComplete = ApplyStepsReset(pending);
                    if (isComplete)
                    {
                        break;
                    }
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
                finally
                {
                    left?.Abandon();
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
                    _currentHoldSec = Math.Max(0f, step.HoldSec ?? ResolveHoldFromTransition(_currentTransition));
                    break;
                case AnimationSequenceStepKind.Wait:
                    _currentHoldSec = Math.Max(0f, step.HoldSec ?? 0f);
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
