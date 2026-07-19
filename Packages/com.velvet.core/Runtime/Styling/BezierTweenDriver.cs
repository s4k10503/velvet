using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// One bezier-tweened channel: the value it started from and the value it is heading to, plus the value it
    /// snaps back toward on an exit-cancel hand-off (its <see cref="From"/> at construction — same semantics as
    /// <see cref="SpringChannel.RestingTarget"/>).
    /// </summary>
    internal sealed class BezierTweenChannel
    {
        // From is not readonly: Retarget freezes the channel's current sampled value here to begin a reversal
        // from wherever the forward tween had reached.
        public float From;
        public float To;
        public readonly float RestingTarget;

        public BezierTweenChannel(float from, float to)
        {
            From = from;
            To = to;
            RestingTarget = from;
        }
    }

    /// <summary>
    /// Running state for one bezier-driven Motion variant enter/exit: up to five channels (see
    /// <see cref="SpringAxis"/>; translate x/y are always present together, never alone — see
    /// <see cref="MotionSpringClassParser.Resolve"/>), the four cubic-bezier control-point coordinates, the
    /// fixed duration, elapsed time, the recurring tick handle, and the completion callback. Owned by
    /// <c>StyleAnimationScheduler</c>'s <c>PendingAnimation</c>.
    /// </summary>
    internal sealed class BezierTweenState
    {
        public BezierTweenChannel? Opacity;
        public BezierTweenChannel? TranslateX;
        public BezierTweenChannel? TranslateY;
        public BezierTweenChannel? Scale;
        public BezierTweenChannel? Rotate;

        // CSS-parameter order: cubic-bezier(X1,Y1,X2,Y2).
        public float X1;
        public float Y1;
        public float X2;
        public float Y2;

        public float DurationSec;
        public float ElapsedSec;

        // The recurring tick, scheduled on the panel root. Paused (and nulled) once the tween completes, or on cancel.
        public IVisualElementScheduledItem? Tick;

        // Runs once the tween completes (after the driver has already cleared the inline overrides). The
        // natural-completion caller sets this to its onComplete; an exit-cancel hand-off (see
        // StyleAnimationScheduler.CancelPending) clears it to null — a reversal completing is not "finishing"
        // anything the original caller asked for.
        public System.Action? OnSettled;
    }

    /// <summary>
    /// Mechanics for a bezier-driven Motion variant enter/exit: builds the per-channel state from a resolved
    /// <see cref="MotionSpringClassParser.SpringPlan"/> (the same driver-agnostic channel resolution the spring
    /// path uses), and applies/steps/clears the inline styles a channel owns, sampling its progress through
    /// <see cref="CubicBezierEvaluator"/>. The bezier sibling of <see cref="MotionSpringDriver"/>: architecturally
    /// a per-frame direct-value-write model (bypassing CSS transitions to get an EXACT curve), but with a
    /// deterministic fixed-duration completion instead of the spring's physics-derived settle. Takes no dependency
    /// on scheduling or panels — see <see cref="StyleAnimationScheduler"/> for the piece that decides WHEN to
    /// start/stop/retarget one of these and owns the recurring tick.
    /// </summary>
    internal static class BezierTweenDriver
    {
        /// <summary>
        /// Builds the running state from a resolved plan and the curve/duration, or null when the plan animates
        /// nothing (the caller should treat this exactly like a zero-duration tween: land the classes and complete
        /// immediately).
        /// </summary>
        public static BezierTweenState? Create(MotionSpringClassParser.SpringPlan plan,
            float x1, float y1, float x2, float y2, float durationSec)
        {
            if (plan.IsEmpty)
            {
                return null;
            }
            var state = new BezierTweenState
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                DurationSec = durationSec,
            };
            if (plan.Opacity is { } o) state.Opacity = new BezierTweenChannel(o.from, o.to);
            if (plan.TranslateX is { } tx) state.TranslateX = new BezierTweenChannel(tx.from, tx.to);
            if (plan.TranslateY is { } ty) state.TranslateY = new BezierTweenChannel(ty.from, ty.to);
            if (plan.Scale is { } s) state.Scale = new BezierTweenChannel(s.from, s.to);
            if (plan.Rotate is { } r) state.Rotate = new BezierTweenChannel(r.from, r.to);
            return state;
        }

        /// <summary>
        /// Writes each channel's CURRENT eased value as an inline style, synchronously — so the element shows the
        /// from-pose on the very first rendered frame instead of flashing at the (already-applied) resting
        /// classes' value until the first tick runs.
        /// </summary>
        public static void ApplyCurrentValues(VisualElement element, BezierTweenState state)
            => ApplyEased(element, state, CurrentEased(state));

        /// <summary>
        /// Advances elapsed time by <paramref name="dtSec"/> and re-applies the inline styles at the newly eased
        /// progress. Returns true once elapsed time has reached the fixed duration — a deterministic completion,
        /// unlike the spring's convergence-based settle.
        /// </summary>
        public static bool Step(VisualElement element, BezierTweenState state, float dtSec)
        {
            if (dtSec > 0f)
            {
                state.ElapsedSec += dtSec;
            }
            ApplyEased(element, state, CurrentEased(state));
            return state.ElapsedSec >= state.DurationSec;
        }

        /// <summary>Clears every inline override this state ever wrote, letting the (already-resting) classes take back over.</summary>
        public static void ClearInlineOverrides(VisualElement element, BezierTweenState state)
        {
            if (state.Opacity != null) element.style.opacity = StyleKeyword.Null;
            if (state.TranslateX != null || state.TranslateY != null) element.style.translate = StyleKeyword.Null;
            if (state.Scale != null) element.style.scale = StyleKeyword.Null;
            if (state.Rotate != null) element.style.rotate = StyleKeyword.Null;
        }

        /// <summary>
        /// Freezes each active channel's CURRENT sampled value as its new <see cref="BezierTweenChannel.From"/>,
        /// points its <see cref="BezierTweenChannel.To"/> back at <see cref="BezierTweenChannel.RestingTarget"/>,
        /// and resets <see cref="BezierTweenState.ElapsedSec"/> to zero — a fresh full-duration reversal from
        /// wherever the forward tween currently is (exactly how a re-triggered CSS transition behaves, not a
        /// time-reversed replay). The exit-cancel hand-off.
        /// </summary>
        public static void Retarget(BezierTweenState state)
        {
            var eased = CurrentEased(state);
            RetargetChannel(state.Opacity, eased);
            RetargetChannel(state.TranslateX, eased);
            RetargetChannel(state.TranslateY, eased);
            RetargetChannel(state.Scale, eased);
            RetargetChannel(state.Rotate, eased);
            state.ElapsedSec = 0f;
        }

        private static void RetargetChannel(BezierTweenChannel? channel, float eased)
        {
            if (channel == null)
            {
                return;
            }
            channel.From = Mathf.LerpUnclamped(channel.From, channel.To, eased);
            channel.To = channel.RestingTarget;
        }

        // The eased output for the current elapsed fraction. A zero duration reads as complete (eased at
        // progress 1) rather than dividing by zero — the scheduler never builds one (ValidateBezierParameters
        // rejects a zero duration), but a direct driver caller might.
        private static float CurrentEased(BezierTweenState state)
        {
            var progress = state.DurationSec > 0f
                ? Mathf.Clamp01(state.ElapsedSec / state.DurationSec)
                : 1f;
            return CubicBezierEvaluator.Evaluate(state.X1, state.Y1, state.X2, state.Y2, progress);
        }

        // LerpUnclamped (not Lerp): an overshoot/anticipate curve samples eased values outside [0,1], and the
        // channel value must actually pass its target for that to be visible — clamping would silently flatten it.
        private static void ApplyEased(VisualElement element, BezierTweenState state, float eased)
        {
            if (state.Opacity != null)
            {
                element.style.opacity = Mathf.LerpUnclamped(state.Opacity.From, state.Opacity.To, eased);
            }
            if (state.TranslateX != null || state.TranslateY != null)
            {
                var x = state.TranslateX != null ? Mathf.LerpUnclamped(state.TranslateX.From, state.TranslateX.To, eased) : 0f;
                var y = state.TranslateY != null ? Mathf.LerpUnclamped(state.TranslateY.From, state.TranslateY.To, eased) : 0f;
                element.style.translate = new Translate(new Length(x), new Length(y));
            }
            if (state.Scale != null)
            {
                var v = Mathf.LerpUnclamped(state.Scale.From, state.Scale.To, eased);
                element.style.scale = new Scale(new Vector2(v, v));
            }
            if (state.Rotate != null)
            {
                var v = Mathf.LerpUnclamped(state.Rotate.From, state.Rotate.To, eased);
                element.style.rotate = new Rotate(Angle.Degrees(v));
            }
        }
    }
}
