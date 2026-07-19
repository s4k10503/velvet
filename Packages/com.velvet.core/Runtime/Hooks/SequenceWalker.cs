#nullable enable
using System;
using System.Collections.Generic;

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
        // A Spring-typed To step with no explicit HoldSec has no statically knowable settle time; rather than
        // stall the sequence forever (or throw from inside a per-frame tick — this codebase's animation config
        // validators warn-and-degrade instead of throwing, see StyleAnimationScheduler.ValidateSpringParameters),
        // fall back to this fixed estimate and log once per step index per Reset.
        private const float FallbackSpringHoldSec = 0.5f;

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
        private HashSet<int>? _springFallbackWarnedIndices;

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

        private void ResetImmediate(IReadOnlyList<AnimationSequenceStep>? steps)
        {
            _steps = steps ?? Array.Empty<AnimationSequenceStep>();
            _stepIndex = 0;
            _elapsedInStepSec = 0f;
            _currentHoldSec = 0f;
            _currentLabel = null;
            _currentTransition = null;
            _isComplete = _steps.Count == 0;
            _springFallbackWarnedIndices?.Clear();
            WarnAboutUnvalidatedToSteps();
            if (!_isComplete)
            {
                ArriveAtStart(0);
            }
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
                        $"steps[{i}] is a default(AnimationSequenceStep), not one built through To/Wait/Call "
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
                _steps = pending;
                _stepIndex = 0;
                _elapsedInStepSec = 0f;
                _currentHoldSec = 0f;
                _currentLabel = null;
                _currentTransition = null;
                _isComplete = _steps.Count == 0;
                _springFallbackWarnedIndices?.Clear();
                WarnAboutUnvalidatedToSteps();
                if (_isComplete)
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
                    _currentHoldSec = Math.Max(0f, step.HoldSec ?? ResolveHoldFromTransition(index, _currentTransition));
                    break;
                case AnimationSequenceStepKind.Wait:
                    _currentHoldSec = Math.Max(0f, step.HoldSec ?? 0f);
                    break;
                case AnimationSequenceStepKind.Call:
                    _currentHoldSec = 0f;
                    step.Callback?.Invoke();
                    break;
            }
        }

        // Auto-derives a To step's hold from its transition when the step declares no explicit HoldSec. A Spring
        // has no statically knowable settle time and a Bezier drives every channel with one fixed-duration curve
        // — both are handled specially below and ignore PropertyOverrides. Only a Tween reads them, mirroring
        // StyleAnimationScheduler.SlowestPropertyTimeoutMs's own "slowest overridden property wins" rule: a
        // PropertyOverrides entry can give one property a longer duration/delay than the top-level values
        // (StyleTransitionConfig's own documented example: opacity in 0.15s while scale takes 0.5s), and the
        // walker must not advance past a Tween step while a property that override still governs is mid-tween.
        private float ResolveHoldFromTransition(int index, StyleTransitionConfig transition)
        {
            if (transition.Type == TransitionType.Spring)
            {
                if ((_springFallbackWarnedIndices ??= new HashSet<int>()).Add(index))
                {
                    FiberLogger.LogWarning("AnimationSequence",
                        "A To step used a Spring transition with no explicit holdSec — a spring's settle time is "
                        + $"physics-derived, not statically knowable. Falling back to {FallbackSpringHoldSec}s.");
                }
                return FallbackSpringHoldSec;
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
    }
}
