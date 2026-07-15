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
        // fall back to this fixed estimate and log once.
        private const float FallbackSpringHoldSec = 0.5f;

        private IReadOnlyList<AnimationSequenceStep> _steps = Array.Empty<AnimationSequenceStep>();
        private int _stepIndex;
        private float _elapsedInStepSec;
        private float _currentHoldSec;
        private string? _currentLabel;
        private StyleTransitionConfig? _currentTransition;
        private bool _isComplete;

        // Frozen by Hooks.UseAnimationSequence's controls.Pause()/Play(); Advance is simply never called while
        // true (the caller gates it), so there is nothing more for this flag to do here.
        public bool IsPaused { get; set; }

        public bool IsComplete => _isComplete;

        public int StepIndex => _stepIndex;

        // Re-seeds the walker at step 0 and immediately commits its effect. Called exactly once per mount (or
        // deps change) from Hooks.UseAnimationSequence's effect, and again from controls.Restart() — both
        // callers are responsible for not calling this more than once per logical "start", since a Call step 0
        // fires its callback here.
        public void Reset(IReadOnlyList<AnimationSequenceStep>? steps)
        {
            _steps = steps ?? Array.Empty<AnimationSequenceStep>();
            _stepIndex = 0;
            _elapsedInStepSec = 0f;
            _currentHoldSec = 0f;
            _currentLabel = null;
            _currentTransition = null;
            _isComplete = _steps.Count == 0;
            if (!_isComplete)
            {
                Arrive(0);
            }
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
                Arrive(next);
            }
            return _stepIndex;
        }

        public AnimationSequenceState ToState() => new(_currentLabel, _currentTransition, _stepIndex, _isComplete);

        // Commits the effect of the step at `index` (fires a Call callback synchronously, or adopts a To
        // step's label/transition) and resolves the hold the walker parks on before advancing further. A
        // thrown Call callback propagates straight out of here, through Advance(), into the UseFrame tick that
        // called it — UseFrame's own try/catch already routes a user-callback exception to the nearest error
        // boundary, so this needs no guard of its own.
        private void Arrive(int index)
        {
            _stepIndex = index;
            _elapsedInStepSec = 0f;
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
            }
        }

        private static float ResolveHoldFromTransition(StyleTransitionConfig transition)
        {
            if (transition.Type == TransitionType.Spring)
            {
                FiberLogger.LogWarning("AnimationSequence",
                    "A To step used a Spring transition with no explicit holdSec — a spring's settle time is "
                    + $"physics-derived, not statically knowable. Falling back to {FallbackSpringHoldSec}s.");
                return FallbackSpringHoldSec;
            }
            return transition.DurationSec + transition.DelaySec;
        }
    }
}
