using System.Collections.Generic;
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
        // Set on a driven Tween's channel whose property a PropertyOverrides entry names (DrivenTweenTiming), and on
        // each of its channels once it reverses (BezierTweenDriver.Retarget).
        public TweenChannelTiming? Timing;

        public BezierTweenChannel(float from, float to)
        {
            From = from;
            To = to;
            RestingTarget = from;
        }
    }

    /// <summary>
    /// One bezier-driven color channel: a scalar 0→1 progress channel feeding a straight-RGBA lerp between the
    /// endpoints — the bezier sibling of <see cref="SpringColorChannel"/>, whose doc carries the rationale for
    /// interpolating one progress value rather than four per-component ones.
    /// </summary>
    internal sealed class BezierColorChannel
    {
        public readonly ArbitraryProperty Property;
        public readonly Color From;
        public readonly Color To;
        public readonly BezierTweenChannel Progress;

        public BezierColorChannel(ArbitraryProperty property, Color from, Color to)
        {
            Property = property;
            From = from;
            To = to;
            Progress = new BezierTweenChannel(0f, 1f);
        }
    }

    /// <summary>
    /// One bezier-driven length channel: the property to write, the unit both endpoints share, and the
    /// magnitude channel itself. The bezier sibling of <see cref="SpringLengthChannel"/>.
    /// </summary>
    internal sealed class BezierLengthChannel
    {
        public readonly ArbitraryProperty Property;
        public readonly LengthUnit Unit;
        public readonly BezierTweenChannel Value;

        public BezierLengthChannel(ArbitraryProperty property, float from, float to, LengthUnit unit)
        {
            Property = property;
            Unit = unit;
            Value = new BezierTweenChannel(from, to);
        }
    }

    /// <summary>
    /// Running state for one bezier-driven Motion variant enter/exit: the five fixed axes (see
    /// <see cref="SpringAxis"/>; translate x/y are always present together, never alone — see
    /// <see cref="MotionSpringClassParser.Resolve"/>) plus any color/length property channels the delta
    /// resolved, the four cubic-bezier control-point coordinates, the fixed duration, elapsed time, the
    /// recurring tick handle, and the completion callback. Owned by <c>StyleAnimationScheduler</c>'s
    /// <c>PendingAnimation</c>.
    /// </summary>
    internal sealed class BezierTweenState
    {
        public BezierTweenChannel? Opacity;
        public BezierTweenChannel? TranslateX;
        public BezierTweenChannel? TranslateY;
        public BezierTweenChannel? Scale;
        public BezierTweenChannel? Rotate;
        public List<BezierColorChannel>? Colors;
        public List<BezierLengthChannel>? Lengths;
        public VisualElement? NativeLayoutOwner;

        // CSS-parameter order: cubic-bezier(X1,Y1,X2,Y2).
        public float X1;
        public float Y1;
        public float X2;
        public float Y2;

        public float DurationSec;
        public float ElapsedSec;

        // Set for a Tween a mount's clock drives (StyleAnimationScheduler.StartDrivenTween): the curve UI Toolkit
        // eases that tween's transition by, which takes the control points' place.
        public EasingMode? Easing;
        // How long after the tick starts a channel with no Timing of its own begins its curve.
        public float DelaySec;
        // Added to a channel's delay to give the transition-delay it stands for, which a reversal shortens.
        public float DelayBaseSec;
        // The latest end among the transition's entries other than the top-level one, driven by a channel or not.
        public float SlowestTimingEndSec;

        // The elapsed time the play completes at, once its slowest channel has landed.
        public float EndSec => Mathf.Max(DelaySec + DurationSec, SlowestTimingEndSec);

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
            var state = Build(plan);
            state.X1 = x1;
            state.Y1 = y1;
            state.X2 = x2;
            state.Y2 = y2;
            state.DurationSec = durationSec;
            return state;
        }

        /// <summary>
        /// Builds the running state of a Tween a mount's clock drives in place of UI Toolkit's transition. An empty
        /// plan still builds one: it moves nothing and completes once <paramref name="timing"/> has run out, as the
        /// transition's own completion waits for its timing.
        /// </summary>
        public static BezierTweenState CreateTween(MotionSpringClassParser.SpringPlan plan, DrivenTweenTiming timing)
        {
            var state = Build(plan);
            state.Easing = timing.Easing;
            state.DurationSec = timing.DurationSec;
            state.DelaySec = timing.DelaySec;
            state.DelayBaseSec = timing.StartDelaySec;
            state.SlowestTimingEndSec = timing.SlowestEntryEndSec;
            var translate = timing.For(StyleLonghandSet.Of(StyleLonghand.Translate));
            AssignTiming(state.Opacity, timing.For(StyleLonghandSet.Of(StyleLonghand.Opacity)));
            AssignTiming(state.TranslateX, translate);
            AssignTiming(state.TranslateY, translate);
            AssignTiming(state.Scale, timing.For(StyleLonghandSet.Of(StyleLonghand.Scale)));
            AssignTiming(state.Rotate, timing.For(StyleLonghandSet.Of(StyleLonghand.Rotate)));
            foreach (var c in state.Colors ?? s_noColors)
            {
                AssignTiming(c.Progress, timing.For(StyleArbitraryLonghands.Of(c.Property)));
            }
            foreach (var l in state.Lengths ?? s_noLengths)
            {
                AssignTiming(l.Value, timing.For(StyleArbitraryLonghands.Of(l.Property)));
            }
            return state;
        }

        private static readonly List<BezierColorChannel> s_noColors = new();
        private static readonly List<BezierLengthChannel> s_noLengths = new();

        private static void AssignTiming(BezierTweenChannel? channel, TweenChannelTiming? timing)
        {
            if (channel != null)
            {
                channel.Timing = timing;
            }
        }

        private static BezierTweenState Build(MotionSpringClassParser.SpringPlan plan)
        {
            var state = new BezierTweenState();
            if (plan.Opacity is { } o) state.Opacity = new BezierTweenChannel(o.from, o.to);
            if (plan.TranslateX is { } tx) state.TranslateX = new BezierTweenChannel(tx.from, tx.to);
            if (plan.TranslateY is { } ty) state.TranslateY = new BezierTweenChannel(ty.from, ty.to);
            if (plan.Scale is { } s) state.Scale = new BezierTweenChannel(s.from, s.to);
            if (plan.Rotate is { } r) state.Rotate = new BezierTweenChannel(r.from, r.to);
            if (plan.Colors != null)
            {
                var colors = new List<BezierColorChannel>(plan.Colors.Count);
                foreach (var c in plan.Colors)
                {
                    colors.Add(new BezierColorChannel(c.Property, c.From, c.To));
                }
                state.Colors = colors;
            }
            if (plan.Lengths != null)
            {
                var lengths = new List<BezierLengthChannel>(plan.Lengths.Count);
                foreach (var l in plan.Lengths)
                {
                    lengths.Add(new BezierLengthChannel(l.Property, l.From, l.To, l.Unit));
                }
                state.Lengths = lengths;
            }
            return state;
        }

        /// <summary>
        /// Writes each channel's CURRENT eased value as an inline style, synchronously — so the element shows
        /// the from-pose on the very first rendered frame instead of flashing at the (already-applied) resting
        /// classes' value until the first tick runs — after suspending the element's native transitions when
        /// this play drives a property one of them could intercept. See
        /// <see cref="MotionSpringDriver.ApplyCurrentValues"/> for why the suspension goes up with this first
        /// write rather than with the recurring tick.
        /// </summary>
        public static void ApplyCurrentValues(VisualElement element, BezierTweenState state)
        {
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, state, DrivenSlots(state),
                MotionNativeTransitionGuard.LengthLonghands(element, element, state.Lengths, static channel => channel.Property));
            if (state.Easing.HasValue)
            {
                // A driven Tween's swap also changes what its channels do not write, which a transition the element's
                // own classes declare would animate on the panel's time; the suspension lands it with the swap.
                MotionNativeTransitionGuard.SuspendIfIntercepted(element, state, MotionTransitionSlots.All,
                    StyleLonghandSet.Empty);
            }
            StyleAnimateDriver.HoldAgainstLoop(element, state, DrivenSlots(state));
            ApplyEased(element, state);
        }

        private static void SyncLayoutOwner(VisualElement element, BezierTweenState state)
            => state.NativeLayoutOwner = MotionNativeTransitionGuard.SyncLayoutOwner(element, state,
                state.NativeLayoutOwner, state.Lengths, static channel => channel.Property, DrivenSlots(state));

        // The slot groups this play writes — the bezier sibling of MotionSpringDriver's own.
        private static MotionTransitionSlots DrivenSlots(BezierTweenState state)
        {
            var slots = MotionTransitionSlots.None;
            if (state.Opacity != null) slots |= MotionTransitionSlots.Opacity;
            if (state.TranslateX != null || state.TranslateY != null) slots |= MotionTransitionSlots.Translate;
            if (state.Scale != null) slots |= MotionTransitionSlots.Scale;
            if (state.Rotate != null) slots |= MotionTransitionSlots.Rotate;
            if (state.Colors != null) slots |= MotionTransitionSlots.Color;
            if (state.Lengths != null) slots |= MotionTransitionSlots.Length;
            return slots;
        }

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
            ApplyEased(element, state);
            return state.ElapsedSec >= state.EndSec;
        }

        /// <summary>
        /// Writes the tween at <paramref name="timeSec"/> into its run, earlier or later than where it stands, and
        /// reports whether that time has reached <see cref="BezierTweenState.EndSec"/>. A play on a <c>MotionPlayback</c> is driven by
        /// this, and a cancel of one writes it at time 0.
        /// </summary>
        public static bool SeekTo(VisualElement element, BezierTweenState state, float timeSec)
        {
            state.ElapsedSec = Mathf.Max(0f, timeSec);
            ApplyEased(element, state);
            return state.ElapsedSec >= state.EndSec;
        }

        /// <summary>The bezier sibling of <see cref="MotionSpringDriver.StartValues"/>.</summary>
        public static MotionSpringClassParser.SpringPlan StartValues(BezierTweenState state)
            => MotionSpringClassParser.Holding(
                (state.Opacity?.From, state.TranslateX?.From, state.TranslateY?.From, state.Scale?.From,
                    state.Rotate?.From),
                state.Colors, static c => new MotionSpringClassParser.ColorChannelPlan(c.Property, c.From, c.From),
                state.Lengths, static l => new MotionSpringClassParser.LengthChannelPlan(l.Property, l.Value.From,
                    l.Value.From, l.Unit));

        /// <summary>
        /// Releases every inline slot this state ever wrote — and the transition suspension
        /// <see cref="ApplyCurrentValues"/> put in place — letting the (already-resting) classes take back over.
        /// See <see cref="MotionSpringDriver.ClearInlineOverrides"/> for why the surviving arbitrary-value
        /// layers are re-asserted only after every slot has been nulled.
        /// </summary>
        public static void ClearInlineOverrides(VisualElement element, BezierTweenState state)
        {
            if (state.Opacity != null) MotionOpacity.Write(element, StyleKeyword.Null);
            if (state.TranslateX != null || state.TranslateY != null) element.style.translate = StyleKeyword.Null;
            if (state.Scale != null) element.style.scale = StyleKeyword.Null;
            if (state.Rotate != null) element.style.rotate = StyleKeyword.Null;
            if (state.Colors != null)
            {
                foreach (var c in state.Colors) StyleArbitraryValueResolver.ClearInline(element, c.Property);
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths) StyleArbitraryValueResolver.ClearInline(element, l.Property);
            }
            StyleArbitraryValueResolver.ReapplyLayeredValues(element);
            StyleAnimateDriver.HoldAgainstLoop(element, state, MotionTransitionSlots.None);
            StyleAnimateDriver.ReassertLoop(element, DrivenSlots(state));
            MotionNativeTransitionGuard.Release(element, state);
            if (state.NativeLayoutOwner != null) MotionNativeTransitionGuard.Release(state.NativeLayoutOwner, state);
            state.NativeLayoutOwner = null;
        }

        /// <summary>
        /// The bezier sibling of <see cref="MotionSpringDriver.ReleaseChannels"/>.
        /// </summary>
        public static void ReleaseChannels(VisualElement element, BezierTweenState state, StyleLonghandSet named)
        {
            if (state.Opacity != null && named.Contains(StyleLonghand.Opacity))
            {
                state.Opacity = null;
                MotionOpacity.Write(element, StyleKeyword.Null);
            }
            // X and Y are created together, so they are dropped together.
            if (state.TranslateX != null && named.Contains(StyleLonghand.Translate))
            {
                state.TranslateX = null;
                state.TranslateY = null;
                element.style.translate = StyleKeyword.Null;
            }
            if (state.Scale != null && named.Contains(StyleLonghand.Scale))
            {
                state.Scale = null;
                element.style.scale = StyleKeyword.Null;
            }
            if (state.Rotate != null && named.Contains(StyleLonghand.Rotate))
            {
                state.Rotate = null;
                element.style.rotate = StyleKeyword.Null;
            }
            state.Colors?.RemoveAll(c => MotionSpringDriver.ReleasesProperty(element, c.Property, named));
            state.Lengths?.RemoveAll(l => MotionSpringDriver.ReleasesProperty(element, l.Property, named));
            StyleArbitraryValueResolver.ReapplyLayeredValues(element, named);
            StyleAnimateDriver.HoldAgainstLoop(element, state, DrivenSlots(state));
            StyleAnimateDriver.ReassertLoop(element, DrivenSlots(state));
            SyncLayoutOwner(element, state);
        }

        // The bezier sibling of MotionSpringDriver.HoldTranslate.
        public static void HoldTranslate(BezierTweenState state, float x, float y)
        {
            state.TranslateX = new BezierTweenChannel(x, x);
            state.TranslateY = new BezierTweenChannel(y, y);
        }

        /// <summary>
        /// Freezes each active channel's CURRENT sampled value as its new <see cref="BezierTweenChannel.From"/>,
        /// points its <see cref="BezierTweenChannel.To"/> back at <see cref="BezierTweenChannel.RestingTarget"/>,
        /// and resets <see cref="BezierTweenState.ElapsedSec"/> to zero — a fresh reversal from wherever the forward
        /// tween currently is, not a time-reversed replay: over the full duration for a bezier, and for a driven Tween
        /// over the shortened timing its native transition's reversal takes. The exit-cancel hand-off.
        /// </summary>
        public static void Retarget(BezierTweenState state)
        {
            var shared = SharedEased(state);
            if (state.Easing.HasValue)
            {
                // The forward play's slowest end no longer applies: only the shortened timings Shorten gathers do.
                state.SlowestTimingEndSec = 0f;
            }
            RetargetChannel(state, state.Opacity, shared);
            RetargetChannel(state, state.TranslateX, shared);
            RetargetChannel(state, state.TranslateY, shared);
            RetargetChannel(state, state.Scale, shared);
            RetargetChannel(state, state.Rotate, shared);
            if (state.Colors != null)
            {
                foreach (var c in state.Colors) RetargetChannel(state, c.Progress, shared);
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths) RetargetChannel(state, l.Value, shared);
            }
            if (state.Easing.HasValue)
            {
                // Every channel carries its own shortened timing from here on.
                state.DurationSec = state.DelaySec = state.DelayBaseSec = 0f;
            }
            state.ElapsedSec = 0f;
        }

        private static void RetargetChannel(BezierTweenState state, BezierTweenChannel? channel, float shared)
        {
            if (channel == null)
            {
                return;
            }
            var eased = Eased(state, channel, shared);
            if (state.Easing is { } easing)
            {
                Shorten(state, channel, eased, easing);
            }
            channel.From = Mathf.LerpUnclamped(channel.From, channel.To, eased);
            channel.To = channel.RestingTarget;
        }

        // A driven Tween's reversal takes the transition its native counterpart would start: CSS Transitions' reversing
        // shortening, which shortens the duration and a negative delay by the old transition's eased output. That is
        // the whole factor here, since only a forward play reverses (StyleAnimationScheduler.CancelBezierPending).
        // FilterTransitionPanelTests' reversal cases pin UI Toolkit shortening a reversed linear transition so.
        private static void Shorten(BezierTweenState state, BezierTweenChannel channel, float eased, EasingMode easing)
        {
            var factor = Mathf.Clamp01(Mathf.Abs(eased));
            var own = channel.Timing ?? new TweenChannelTiming(state.DelaySec, state.DurationSec, easing);
            var delaySec = own.DelaySec + state.DelayBaseSec;
            // MUTANT_SURVIVES(equivalent, boundary): a zero delay shortens to zero either way.
            var shortened = new TweenChannelTiming(delaySec < 0f ? delaySec * factor : delaySec, own.DurationSec * factor, own.Easing);
            channel.Timing = shortened;
            state.SlowestTimingEndSec = Mathf.Max(state.SlowestTimingEndSec, shortened.DelaySec + shortened.DurationSec);
        }

        // The channel's value at the current elapsed time. LerpUnclamped (not Lerp): an overshoot/anticipate curve
        // samples eased values outside [0,1], and the channel value must actually pass its target for that to be
        // visible — clamping would silently flatten it.
        private static float Value(BezierTweenState state, BezierTweenChannel channel, float shared)
            => Mathf.LerpUnclamped(channel.From, channel.To, Eased(state, channel, shared));

        // The eased output for the channel at the current elapsed time: its own Timing where it has one, else
        // shared, the state's curve at the state's delay and duration (SharedEased), evaluated once a frame for
        // every channel taking it.
        private static float Eased(BezierTweenState state, BezierTweenChannel channel, float shared)
            => channel.Timing is { } own
                ? UssEasing.Evaluate(own.Easing, Progress(state.ElapsedSec - own.DelaySec, own.DurationSec))
                : shared;

        // A zero duration reads as complete once its delay has passed rather than dividing by zero — the scheduler
        // builds one only from a PropertyOverrides entry of no positive duration or a reversal at its very start,
        // but a direct driver caller might.
        private static float SharedEased(BezierTweenState state)
        {
            var progress = Progress(state.ElapsedSec - state.DelaySec, state.DurationSec);
            return state.Easing is { } mode
                ? UssEasing.Evaluate(mode, progress)
                : CubicBezierEvaluator.Evaluate(state.X1, state.Y1, state.X2, state.Y2, progress);
        }

        private static float Progress(float activeSec, float durationSec)
            => durationSec > 0f ? Mathf.Clamp01(activeSec / durationSec) : activeSec >= 0f ? 1f : 0f;

        private static void ApplyEased(VisualElement element, BezierTweenState state)
        {
            SyncLayoutOwner(element, state);
            var shared = SharedEased(state);
            if (state.Opacity != null)
            {
                MotionOpacity.Write(element, Value(state, state.Opacity, shared));
            }
            if (state.TranslateX != null || state.TranslateY != null)
            {
                var x = state.TranslateX != null ? Value(state, state.TranslateX, shared) : 0f;
                var y = state.TranslateY != null ? Value(state, state.TranslateY, shared) : 0f;
                element.style.translate = new Translate(new Length(x), new Length(y));
            }
            if (state.Scale != null)
            {
                var v = Value(state, state.Scale, shared);
                element.style.scale = new Scale(new Vector2(v, v));
            }
            if (state.Rotate != null)
            {
                var v = Value(state, state.Rotate, shared);
                element.style.rotate = new Rotate(Angle.Degrees(v));
            }
            if (state.Colors != null)
            {
                foreach (var c in state.Colors)
                {
                    var progress = Value(state, c.Progress, shared);
                    StyleArbitraryValueResolver.ApplyInline(element,
                        new ArbitraryStyle(c.Property, MotionPropertyInterpolation.LerpColor(c.From, c.To, progress)));
                }
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths)
                {
                    var v = MotionPropertyInterpolation.LerpLength(l.Property, l.Value.From, l.Value.To, Eased(state, l.Value, shared));
                    StyleArbitraryValueResolver.ApplyInline(element, new ArbitraryStyle(l.Property, v, l.Unit));
                }
            }
            StyleAnimateDriver.ReassertLoop(element, DrivenSlots(state));
        }
    }
}
