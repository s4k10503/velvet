#nullable enable
using System;
using System.Threading;

namespace Velvet
{
    // Internal: callers construct a step only through the public static factories below, never by
    // reading Kind directly.
    internal enum AnimationSequenceStepKind
    {
        To,
        Wait,
        Call,
        Await,
    }

    /// <summary>
    /// One step in an animation sequence played by <see cref="Hooks.UseAnimationSequence"/>. A step is
    /// exactly one of: a variant-label change (<see cref="To"/>), a pure timing gap (<see cref="Wait"/>), a
    /// synchronous C# callback (<see cref="Call"/>), or a wait on a task (<see cref="Await"/>).
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
        /// <see cref="TransitionType.Spring"/>-typed <see cref="To"/> step holds for its <c>DelaySec</c> plus the
        /// duration Framer Motion's sequence gives the same spring — see the motion guide's Timelines section.
        /// Required on <see cref="Wait"/> (negative values clamp to 0). Always 0 on <see cref="Call"/> and
        /// <see cref="Await"/>.
        /// </summary>
        public float? HoldSec { get; }

        /// <summary>The callback to invoke. Non-null only on a <see cref="Call"/> step.</summary>
        public Action? Callback { get; }

        /// <summary>The function whose task the step waits on. Non-null only on an <see cref="Await"/> step.</summary>
        public Func<CancellationToken, VelvetTask>? AwaitFactory { get; }

        private AnimationSequenceStep(AnimationSequenceStepKind kind, string? label,
            StyleTransitionConfig? transition, float? holdSec, Action? callback,
            Func<CancellationToken, VelvetTask>? awaitFactory = null)
        {
            Kind = kind;
            Label = label;
            Transition = transition;
            HoldSec = holdSec;
            Callback = callback;
            AwaitFactory = awaitFactory;
        }

        /// <summary>
        /// Activates <paramref name="label"/> on the sequence's coordinator — feeds straight into
        /// <c>V.Motion(animate:, transition:)</c>. Descendant Motions naming no label of their own inherit it
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
        /// (same expectation as any <c>UseEffect</c> mount factory with a non-idempotent body), so write it to
        /// tolerate running twice if the sequence's own mount matters.
        /// </summary>
        /// <param name="callback">The callback to invoke. Must not be null.</param>
        public static AnimationSequenceStep Call(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return new AnimationSequenceStep(AnimationSequenceStepKind.Call, null, null, 0f, callback);
        }

        /// <summary>
        /// Calls <paramref name="taskFactory"/> on arrival and holds the cursor until the task it returns
        /// settles — an <c>await</c> between the animations of Framer Motion's <c>useAnimate</c>. A restart, a
        /// <c>deps</c> change or unmount that leaves the step with the task still pending cancels the token. The
        /// motion guide's Timelines section owns what a settle, a fault and a pause do.
        /// </summary>
        /// <param name="taskFactory">Returns the task to wait on. Must not be null.</param>
        public static AnimationSequenceStep Await(Func<CancellationToken, VelvetTask> taskFactory)
        {
            if (taskFactory == null) throw new ArgumentNullException(nameof(taskFactory));
            return new AnimationSequenceStep(AnimationSequenceStepKind.Await, null, null, 0f, null, taskFactory);
        }
    }

    /// <summary>Per-render snapshot of a sequence, returned by <see cref="Hooks.UseAnimationSequence"/>.</summary>
    public readonly struct AnimationSequenceState
    {
        /// <summary>The label of the most recently activated <see cref="AnimationSequenceStep"/> <c>To</c> step, or null before the first one runs.</summary>
        public string? CurrentLabel { get; }

        /// <summary>
        /// The transition that accompanied <see cref="CurrentLabel"/>: a copy of the step's own, equal to it in
        /// every setting, that also ties the <c>Spring</c> and <c>Bezier</c> plays a <c>V.Motion</c> handed it
        /// starts to this sequence's <see cref="AnimationSequenceControls"/>.
        /// </summary>
        public StyleTransitionConfig? CurrentTransition { get; }

        /// <summary>Index into the authored <c>steps</c> array of the step currently holding the cursor.</summary>
        public int StepIndex { get; }

        /// <summary>
        /// True once the cursor has advanced past the last step of the last pass, or a lowered <c>iterations</c>
        /// has ended the sequence, and from the mount render on for zero <c>iterations</c> or an empty
        /// <c>steps</c> list. A raised count clears it. Under <c>loop</c>, latches only for an empty list.
        /// </summary>
        public bool IsComplete { get; }

        internal AnimationSequenceState(string? currentLabel, StyleTransitionConfig? currentTransition, int stepIndex, bool isComplete)
        {
            CurrentLabel = currentLabel;
            CurrentTransition = currentTransition;
            StepIndex = stepIndex;
            IsComplete = isComplete;
        }
    }

    /// <summary>
    /// Imperative controls for a sequence started by <see cref="Hooks.UseAnimationSequence"/>, Framer Motion's
    /// sequence playback controls. They drive the sequence's own timeline — its step cursor and clock — and the
    /// <c>Spring</c> and <c>Bezier</c> plays its steps' labels start on a <c>V.Motion</c> handed
    /// <see cref="AnimationSequenceState.CurrentTransition"/>: see the motion guide's Timelines section for what
    /// each reaches.
    /// </summary>
    public readonly struct AnimationSequenceControls
    {
        private readonly SequenceWalker _walker;

        /// <summary>
        /// Resumes advancing (idempotent), the cursor and the <c>Spring</c> and <c>Bezier</c> plays its steps
        /// started alike. Also what <c>autoplay: true</c> starts with on mount. After <see cref="Cancel"/>, starts
        /// the sequence again from step 0.
        /// </summary>
        public Action Play { get; }

        /// <summary>
        /// Freezes the cursor at its current step — elapsed time stops accumulating toward the next hold — and
        /// holds the <c>Spring</c> and <c>Bezier</c> plays its steps started where they are.
        /// </summary>
        public Action Pause { get; }

        /// <summary>Returns to step 0 and re-commits its effect (firing a <c>Call</c> step 0's callback again). Does not implicitly unpause.</summary>
        public Action Restart { get; }

        /// <summary>
        /// Stops the sequence, Framer Motion's <c>cancel()</c>: the cursor stays where it is, and each
        /// <c>Spring</c> or <c>Bezier</c> play its steps started returns to the values it started from and stops
        /// there, without its completion running. <see cref="Play"/> starts the sequence again from step 0.
        /// </summary>
        public Action Cancel { get; }

        /// <summary>
        /// The rate the timeline and the <c>Spring</c> and <c>Bezier</c> plays its steps started advance at,
        /// delays included, Framer Motion's <c>speed</c>: 1 by
        /// default, 2 twice as fast, 0.5 half as fast, and 0 holds both where they are. Setting it re-times the
        /// running plays from the next frame. Throws <see cref="ArgumentOutOfRangeException"/> for a negative,
        /// NaN or infinite value.
        /// </summary>
        public float Speed
        {
            get => _walker.Playback.Rate;
            set => _walker.Playback.Rate = value;
        }

        /// <summary>
        /// Seconds into the sequence's timeline, counting each step's hold at its authored length. Read live, not
        /// per render; the motion guide's Timelines section owns the rest.
        /// </summary>
        public float TimeSec => _walker.TimeSec;

        internal AnimationSequenceControls(Action play, Action pause, Action restart, Action cancel,
            SequenceWalker walker)
        {
            Play = play;
            Pause = pause;
            Restart = restart;
            Cancel = cancel;
            _walker = walker;
        }
    }
}
