#nullable enable
using System;

namespace Velvet
{
    // Which effect an AnimationSequenceStep carries. Internal: callers only ever construct a step through
    // the public static factories below, never read Kind directly.
    internal enum AnimationSequenceStepKind
    {
        To,
        Wait,
        Call,
    }

    /// <summary>
    /// One step in an animation sequence played by <see cref="Hooks.UseAnimationSequence"/>. A step is
    /// exactly one of: a variant-label change (<see cref="To"/>), a pure timing gap (<see cref="Wait"/>), or a
    /// synchronous C# callback (<see cref="Call"/>).
    /// </summary>
    public readonly struct AnimationSequenceStep
    {
        internal AnimationSequenceStepKind Kind { get; }

        /// <summary>The variant label to activate. Non-null only on a <see cref="To"/> step.</summary>
        public string? Label { get; }

        /// <summary>
        /// The transition driving this label change. Null means "reuse the most recent non-null transition
        /// earlier in the sequence", falling back to <see cref="StyleTransition"/>'s <c>Fade</c> preset (the
        /// same default <c>V.Motion</c> itself uses) if none has been set yet. Only meaningful on a
        /// <see cref="To"/> step.
        /// </summary>
        public StyleTransitionConfig? Transition { get; }

        /// <summary>
        /// How long the sequence holds on this step before advancing. On a <see cref="To"/> step, null derives
        /// the hold from <see cref="Transition"/> (<c>DurationSec + DelaySec</c> for
        /// <see cref="TransitionType.Tween"/> or <see cref="TransitionType.Bezier"/>, both fixed-duration); a
        /// <see cref="TransitionType.Spring"/>-typed <see cref="To"/>
        /// step with no explicit hold logs a warning and falls back to a fixed estimate, since a spring's
        /// settle time is physics-derived and not statically knowable (matches
        /// <see cref="StyleTransitionConfig"/>'s own documented "DurationSec is ignored for Spring" contract).
        /// Required on <see cref="Wait"/> (negative values clamp to 0). Always 0 on <see cref="Call"/>.
        /// </summary>
        public float? HoldSec { get; }

        /// <summary>The callback to invoke. Non-null only on a <see cref="Call"/> step.</summary>
        public Action? Callback { get; }

        private AnimationSequenceStep(AnimationSequenceStepKind kind, string? label,
            StyleTransitionConfig? transition, float? holdSec, Action? callback)
        {
            Kind = kind;
            Label = label;
            Transition = transition;
            HoldSec = holdSec;
            Callback = callback;
        }

        /// <summary>
        /// Activates <paramref name="label"/> on the sequence's coordinator — feeds straight into
        /// <c>V.Motion(animate:, transition:)</c>. Descendant Motions with no own <c>animate</c> inherit it
        /// exactly as they do for any hand-toggled label, including <c>StaggerChildrenSec</c> fan-out when
        /// <paramref name="transition"/> declares it — "one at a time" across a list of such descendants needs
        /// no separate multi-target API.
        /// </summary>
        /// <param name="label">The variant label to activate. Must not be null.</param>
        /// <param name="transition">See <see cref="Transition"/>.</param>
        /// <param name="holdSec">See <see cref="HoldSec"/>.</param>
        public static AnimationSequenceStep To(string label, StyleTransitionConfig? transition = null, float? holdSec = null)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            return new AnimationSequenceStep(AnimationSequenceStepKind.To, label, transition, holdSec, null);
        }

        /// <summary>Holds the current label for <paramref name="seconds"/> before advancing. No visual effect of its own.</summary>
        /// <param name="seconds">Hold duration. Negative values clamp to 0.</param>
        public static AnimationSequenceStep Wait(float seconds)
            => new(AnimationSequenceStepKind.Wait, null, null, Math.Max(0f, seconds), null);

        /// <summary>
        /// Fires <paramref name="callback"/> synchronously on arrival, then advances immediately — never holds
        /// the cursor. Step 0's callback re-fires under the Editor's StrictMode mount double-invoke diagnostic
        /// (same expectation as any <c>UseEffect</c> mount factory with a non-idempotent body): write it to
        /// tolerate running twice if the sequence's own mount matters, exactly as React's own StrictMode
        /// guidance recommends for an effect that isn't naturally idempotent.
        /// </summary>
        /// <param name="callback">The callback to invoke. Must not be null.</param>
        public static AnimationSequenceStep Call(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return new AnimationSequenceStep(AnimationSequenceStepKind.Call, null, null, 0f, callback);
        }
    }

    /// <summary>Per-render snapshot of a sequence, returned by <see cref="Hooks.UseAnimationSequence"/>.</summary>
    public readonly struct AnimationSequenceState
    {
        /// <summary>The label of the most recently activated <see cref="AnimationSequenceStep"/> <c>To</c> step, or null before the first one runs.</summary>
        public string? CurrentLabel { get; }

        /// <summary>The transition that accompanied <see cref="CurrentLabel"/>.</summary>
        public StyleTransitionConfig? CurrentTransition { get; }

        /// <summary>Index into the authored <c>steps</c> array of the step currently holding the cursor.</summary>
        public int StepIndex { get; }

        /// <summary>True once the cursor has advanced past the last step. Never latches true when <c>loop</c> is set.</summary>
        public bool IsComplete { get; }

        internal AnimationSequenceState(string? currentLabel, StyleTransitionConfig? currentTransition, int stepIndex, bool isComplete)
        {
            CurrentLabel = currentLabel;
            CurrentTransition = currentTransition;
            StepIndex = stepIndex;
            IsComplete = isComplete;
        }
    }

    /// <summary>Imperative controls for a sequence started by <see cref="Hooks.UseAnimationSequence"/>.</summary>
    public readonly struct AnimationSequenceControls
    {
        /// <summary>Resumes advancing (idempotent). Also what <c>autoplay: true</c> starts with on mount.</summary>
        public Action Play { get; }

        /// <summary>Freezes the cursor at its current step — elapsed time stops accumulating toward the next hold.</summary>
        public Action Pause { get; }

        /// <summary>Returns to step 0 and re-commits its effect (firing a <c>Call</c> step 0's callback again). Does not implicitly unpause.</summary>
        public Action Restart { get; }

        internal AnimationSequenceControls(Action play, Action pause, Action restart)
        {
            Play = play;
            Pause = pause;
            Restart = restart;
        }
    }
}
