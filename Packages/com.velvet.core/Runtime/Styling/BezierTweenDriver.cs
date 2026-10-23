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
    internal sealed class BezierTweenChannel : IGestureMaskable
    {
        // From is not readonly: Retarget freezes the channel's current sampled value here to begin a reversal
        // from wherever the forward tween had reached.
        public float From;
        public float To;
        // The unit the channel's values are in; only a translate takes another than pixels.
        public LengthUnit Unit;
        // Set while a gesture channel holds the property (GestureChannelMask.MaskChannels): the channel runs on and
        // writes nothing.
        public bool Masked { get; set; }
        // Not readonly: a play continuing from one it interrupts (BezierTweenDriver.ContinueFrom) starts elsewhere.
        public float RestingTarget;
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
    internal sealed class BezierColorChannel : IPropertyChannel
    {
        public readonly ArbitraryProperty Property;
        public readonly Color From;
        public readonly Color To;
        public readonly BezierTweenChannel Progress;

        ArbitraryProperty IPropertyChannel.Property => Property;
        IGestureMaskable IPropertyChannel.Channel => Progress;

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
    internal sealed class BezierLengthChannel : IPropertyChannel
    {
        public readonly ArbitraryProperty Property;
        public readonly LengthUnit Unit;
        public readonly BezierTweenChannel Value;

        ArbitraryProperty IPropertyChannel.Property => Property;
        IGestureMaskable IPropertyChannel.Channel => Value;

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
    internal sealed class BezierTweenState : IGestureMaskableState
    {
        bool IGestureMaskableState.IsExit { set => Exit = value; }

        (IGestureMaskable? Opacity, IGestureMaskable? TranslateX, IGestureMaskable? TranslateY, IGestureMaskable? Scale,
            IGestureMaskable? Rotate) IGestureMaskableState.Scalars => (Opacity, TranslateX, TranslateY, Scale, Rotate);

        IGestureMaskable? IGestureMaskableState.ScaleY => ScaleY;

        IReadOnlyList<IPropertyChannel>? IGestureMaskableState.ColorChannels => Colors;
        IReadOnlyList<IPropertyChannel>? IGestureMaskableState.LengthChannels => Lengths;

        // As MotionSpringState.Exit.
        public bool Exit;
        public BezierTweenChannel? Opacity;
        public BezierTweenChannel? TranslateX;
        public BezierTweenChannel? TranslateY;
        public BezierTweenChannel? Scale;
        // The y factor when the play's scale differs per axis; Scale is then the x factor.
        public BezierTweenChannel? ScaleY;
        public BezierTweenChannel? Rotate;
        public List<BezierColorChannel>? Colors;
        public List<BezierLengthChannel>? Lengths;
        public VisualElement? NativeLayoutOwner;

        // CSS-parameter order: cubic-bezier(X1,Y1,X2,Y2).
        public float X1;
        public float Y1;
        public float X2;
        public float Y2;

        // DurationSec is one pass; ElapsedSec counts from the play's start across every pass Repeat adds.
        public float DurationSec;
        public float ElapsedSec;
        // Set by a seek to the start of the run, cleared by the next step: a channel of no duration then still shows
        // its From where it would otherwise land, since the play has not advanced past its start.
        public bool SeekedToStart;
        public MotionRepeat Repeat;

        // Set for a Tween a mount's clock drives (StyleAnimationScheduler.StartDrivenTween): the curve UI Toolkit
        // eases that tween's transition by, which takes the control points' place.
        public EasingMode? Easing;
        // How long after the tick starts a channel with no Timing of its own begins its curve.
        public float DelaySec;
        // Added to a channel's delay to give the transition-delay it stands for, which a reversal shortens.
        public float DelayBaseSec;
        // The latest end among the transition's entries other than the top-level one, driven by a channel or not.
        public float SlowestTimingEndSec;

        // The elapsed time the play completes at, once every pass has run and its slowest channel has landed. A
        // driven Tween never repeats, so its Repeat is one pass.
        public double EndSec => System.Math.Max(DelaySec + Repeat.TotalSec(DurationSec), SlowestTimingEndSec);

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
            float x1, float y1, float x2, float y2, float durationSec, MotionRepeat repeat = default)
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
            state.Repeat = repeat;
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
            AssignTiming(state.ScaleY, timing.For(StyleLonghandSet.Of(StyleLonghand.Scale)));
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
            if (plan.TranslateX is { } tx) state.TranslateX = new BezierTweenChannel(tx.from, tx.to) { Unit = plan.TranslateXUnit };
            if (plan.TranslateY is { } ty) state.TranslateY = new BezierTweenChannel(ty.from, ty.to) { Unit = plan.TranslateYUnit };
            if (plan.Scale is { } s) state.Scale = new BezierTweenChannel(s.from, s.to);
            if (plan.ScaleY is { } sy) state.ScaleY = new BezierTweenChannel(sy.from, sy.to);
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
            if (state.Easing.HasValue)
            {
                // A driven Tween's swap also changes what its channels do not write, which a transition the element's
                // own classes declare would animate on the panel's time; the suspension lands it with the swap.
                MotionNativeTransitionGuard.SuspendIfIntercepted(element, state, MotionTransitionSlots.All,
                    StyleLonghandSet.Empty);
            }
            else
            {
                MotionNativeTransitionGuard.NarrowIfIntercepted(element, state, DrivenSlots(state),
                    MotionNativeTransitionGuard.LengthLonghands(element, element, state.Lengths, static channel => channel.Property));
            }
            StyleAnimateDriver.HoldAgainstLoop(element, state, DrivenSlots(state));
            ApplyEased(element, state);
        }

        /// <summary>
        /// Takes back the all-slots suspension a driven Tween's <see cref="ApplyCurrentValues"/> put in place, leaving
        /// the one its channels alone call for, for a play held after a cancel: the element's other transitions run
        /// again while the hold lasts.
        /// </summary>
        public static void NarrowSuspension(VisualElement element, BezierTweenState state)
        {
            if (!state.Easing.HasValue)
            {
                return;
            }
            MotionNativeTransitionGuard.Release(element, state);
            MotionNativeTransitionGuard.NarrowIfIntercepted(element, state, DrivenSlots(state),
                MotionNativeTransitionGuard.LengthLonghands(element, element, state.Lengths, static channel => channel.Property));
        }

        private static void SyncLayoutOwner(VisualElement element, BezierTweenState state)
            => state.NativeLayoutOwner = MotionNativeTransitionGuard.SyncLayoutOwner(element, state,
                state.NativeLayoutOwner, state.Lengths, static channel => channel.Property, DrivenSlots(state));

        // The slot groups this play writes — the bezier sibling of MotionSpringDriver's own.
        // The slots the play writes: a channel a gesture state holds writes nothing (GestureChannelMask).
        private static MotionTransitionSlots DrivenSlots(BezierTweenState state)
            => GestureChannelMask.DrivenSlots(state);

        /// <summary>
        /// Advances elapsed time by <paramref name="dtSec"/> and re-applies the inline styles at the newly eased
        /// progress. Returns true once elapsed time has reached the fixed duration of every pass — a deterministic
        /// completion, unlike the spring's convergence-based settle — and never under an endless repeat.
        /// </summary>
        public static bool Step(VisualElement element, BezierTweenState state, float dtSec)
        {
            if (dtSec > 0f)
            {
                state.ElapsedSec = state.Repeat.Fold(state.ElapsedSec + dtSec, state.DurationSec);
                state.SeekedToStart = false;
            }
            ApplyEased(element, state);
            return state.ElapsedSec >= state.EndSec;
        }

        // Whether the play ends on its from-values rather than its to-values (MotionRepeat.EndsAtFrom).
        public static bool EndsAtFrom(BezierTweenState state) => state.Repeat.EndsAtFrom;

        /// <summary>
        /// Writes the tween at <paramref name="timeSec"/> into its run, earlier or later than where it stands, and
        /// reports whether that time has reached <see cref="BezierTweenState.EndSec"/>. A play on a <c>MotionPlayback</c> is driven by
        /// this, and a cancel of one writes it at time 0.
        /// </summary>
        public static bool SeekTo(VisualElement element, BezierTweenState state, float timeSec)
        {
            state.ElapsedSec = Mathf.Max(0f, timeSec);
            state.SeekedToStart = timeSec <= 0f;
            ApplyEased(element, state);
            return state.ElapsedSec >= state.EndSec;
        }

        /// <summary>The bezier sibling of <see cref="MotionSpringDriver.StartValues"/>.</summary>
        public static MotionSpringClassParser.SpringPlan StartValues(BezierTweenState state)
        {
            var plan = MotionSpringClassParser.Holding(
                (state.Opacity?.From, state.TranslateX?.From, state.TranslateY?.From, state.Scale?.From,
                    state.Rotate?.From),
                state.Colors, static c => new MotionSpringClassParser.ColorChannelPlan(c.Property, c.From, c.From),
                state.Lengths, static l => new MotionSpringClassParser.LengthChannelPlan(l.Property, l.Value.From,
                    l.Value.From, l.Unit));
            plan.TranslateXUnit = state.TranslateX?.Unit ?? LengthUnit.Pixel;
            plan.TranslateYUnit = state.TranslateY?.Unit ?? LengthUnit.Pixel;
            if (state.ScaleY != null) plan.ScaleY = (state.ScaleY.From, state.ScaleY.From);
            return plan;
        }

        /// <summary>
        /// Releases every inline slot this state ever wrote — and the transition suspension
        /// <see cref="ApplyCurrentValues"/> put in place — letting the (already-resting) classes take back over.
        /// See <see cref="MotionSpringDriver.ClearInlineOverrides"/> for why the surviving arbitrary-value
        /// layers and holds are re-asserted only after every slot has been nulled.
        /// </summary>
        public static void ClearInlineOverrides(VisualElement element, BezierTweenState state)
        {
            if (state.Opacity != null) MotionOpacity.Write(element, StyleKeyword.Null);
            if (state.TranslateX != null || state.TranslateY != null) SeatedTranslate.Write(element, StyleKeyword.Null);
            if (state.Scale != null) element.style.scale = StyleKeyword.Null;
            if (state.Rotate != null) element.style.rotate = StyleKeyword.Null;
            if (state.Colors != null)
            {
                foreach (var c in state.Colors) StyleArbitraryValueResolver.ReleaseDriven(element, c.Property);
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths) StyleArbitraryValueResolver.ReleaseDriven(element, l.Property);
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
                SeatedTranslate.Write(element, StyleKeyword.Null);
            }
            if (state.Scale != null && named.Contains(StyleLonghand.Scale))
            {
                state.Scale = null;
                state.ScaleY = null;
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
            if (!state.Easing.HasValue) GestureChannelMask.ResyncNarrowing(element, state);
        }

        /// <summary>
        /// Hands the channels <see cref="GestureChannelMask.MaskChannels"/> held for a gesture, among <paramref name="named"/>, back to
        /// the play from what <paramref name="shown"/> reads the element showing: each runs from there to its target
        /// over the play's duration again, from now, on its easing or its curve, and the play runs until it lands, as
        /// Framer Motion animates a value a gesture state lets go of back to its animate target on that target's
        /// transition.
        /// </summary>
        public static void ReturnChannels(VisualElement element, BezierTweenState state, StyleLonghandSet named,
            MotionSlotContext shown)
        {
            var returned = Restart(state, state.Opacity, named, (StyleLonghand.Opacity, ArbitraryProperty.Opacity), shown);
            returned |= Restart(state, state.TranslateX, named, (StyleLonghand.Translate, ArbitraryProperty.TranslateX), shown)
                | Restart(state, state.TranslateY, named, (StyleLonghand.Translate, ArbitraryProperty.TranslateY), shown);
            returned |= Restart(state, state.Scale, named, (StyleLonghand.Scale, ArbitraryProperty.Scale), shown)
                | Restart(state, state.ScaleY, named, (StyleLonghand.Scale, ArbitraryProperty.Scale), shown);
            returned |= Restart(state, state.Rotate, named, (StyleLonghand.Rotate, ArbitraryProperty.Rotate), shown);
            for (var i = 0; i < (state.Colors?.Count ?? 0); i++)
            {
                returned |= ReturnColor(state, i, (named, shown));
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths)
                {
                    if (!GestureChannelMask.ReturnsProperty(l.Value, l.Property, named, shown))
                    {
                        continue;
                    }
                    if (shown.TryReadShown(l.Property, l.Unit, out var length))
                    {
                        l.Value.From = length.Value;
                    }
                    Restart(state, l.Value);
                    returned = true;
                }
            }
            if (returned)
            {
                if (!state.Easing.HasValue) GestureChannelMask.ResyncNarrowing(element, state);
                ApplyEased(element, state);
            }
        }

        // Returns the colour channel at index from what the element shows, as a fresh channel, since a colour
        // channel's endpoints are fixed.
        private static bool ReturnColor(BezierTweenState state, int index, (StyleLonghandSet Named, MotionSlotContext Shown) gesture)
        {
            var colors = state.Colors!;
            var channel = colors[index];
            if (!GestureChannelMask.ReturnsProperty(channel.Progress, channel.Property, gesture.Named, gesture.Shown))
            {
                return false;
            }
            var from = gesture.Shown.TryReadShown(channel.Property, LengthUnit.Pixel, out var shown) ? shown.Color
                : MotionPropertyInterpolation.LerpColor(channel.From, channel.To,
                    Mathf.LerpUnclamped(channel.Progress.From, channel.Progress.To,
                        Eased(state, channel.Progress, SharedEased(state))));
            var fresh = new BezierColorChannel(channel.Property, from, channel.To);
            Restart(state, fresh.Progress, channel.Progress.Timing);
            colors[index] = fresh;
            return true;
        }

        private static bool Restart(BezierTweenState state, BezierTweenChannel? channel, StyleLonghandSet named,
            (StyleLonghand Longhand, ArbitraryProperty Slot) slot, MotionSlotContext shown)
        {
            if (!GestureChannelMask.Returns(channel, named, slot.Longhand, slot.Slot, shown))
            {
                return false;
            }
            if (shown.TryReadShown(slot.Slot, channel!.Unit, out var value))
            {
                channel!.From = value.Value;
            }
            Restart(state, channel!);
            return true;
        }

        // Runs the channel from its start over its duration again from now — its own timing's where it has one (a
        // PropertyOverrides entry, a reversal), else the play's — on that easing or the play's curve, and keeps the
        // play running until it lands.
        private static void Restart(BezierTweenState state, BezierTweenChannel channel, TweenChannelTiming? own = null)
        {
            var timing = own ?? channel.Timing;
            var duration = timing?.DurationSec ?? state.DurationSec;
            channel.Timing = timing is { } given
                ? new TweenChannelTiming(state.ElapsedSec, duration, given.Easing, given.Curve)
                : new TweenChannelTiming(state.ElapsedSec, duration, state.Easing ?? EasingMode.Linear,
                    curve: !state.Easing.HasValue);
            state.SlowestTimingEndSec = Mathf.Max(state.SlowestTimingEndSec, state.ElapsedSec + duration);
        }

        // The bezier sibling of MotionSpringDriver.HoldTranslate.
        public static void HoldTranslate(BezierTweenState state, Length x, Length y)
        {
            state.TranslateX = new BezierTweenChannel(x.value, x.value) { Unit = x.unit };
            state.TranslateY = new BezierTweenChannel(y.value, y.value) { Unit = y.unit };
        }

        /// <summary>
        /// What each channel of <paramref name="state"/> shows and how fast it is moving, the curve's slope at the
        /// play's time times <paramref name="timeScale"/>, the play's seconds per second on screen; a color carries
        /// no velocity, as Framer Motion tracks none for one, and a finished play none at all.
        /// </summary>
        public static MotionDrawnValues Drawn(BezierTweenState state, float timeScale)
        {
            (float, float)? Of(BezierTweenChannel? channel, ArbitraryProperty? length = null)
                => channel == null ? null
                    : (ValueAt(state, channel, state.ElapsedSec, length), VelocityOf(state, channel, length) * timeScale);

            var drawn = MotionDrawnValues.Of(
                new MotionDrawnValues.Channels(
                    Of(state.Opacity), Of(state.TranslateX), Of(state.TranslateY), Of(state.Scale), Of(state.Rotate)),
                state.Colors,
                c => (c.Property, MotionPropertyInterpolation.LerpColor(c.From, c.To, ValueAt(state, c.Progress, state.ElapsedSec, null))),
                state.Lengths,
                l =>
                {
                    var (value, velocity) = Of(l.Value, l.Property)!.Value;
                    return (l.Property, l.Unit, value, velocity);
                });
            drawn.ScaleY = Of(state.ScaleY);
            drawn.TranslateXUnit = state.TranslateX?.Unit ?? LengthUnit.Pixel;
            drawn.TranslateYUnit = state.TranslateY?.Unit ?? LengthUnit.Pixel;
            return drawn;
        }

        // The channel's value at timeSec, the play's elapsed time put back after: a length through its property's
        // interpolation, any other channel through Value.
        private static float ValueAt(BezierTweenState state, BezierTweenChannel channel, float timeSec, ArbitraryProperty? length)
        {
            var now = state.ElapsedSec;
            try
            {
                state.ElapsedSec = timeSec;
                var shared = SharedEased(state);
                return length is { } property
                    ? MotionPropertyInterpolation.LerpLength(property, channel.From, channel.To, Eased(state, channel, shared))
                    : Value(state, channel, shared);
            }
            finally
            {
                state.ElapsedSec = now;
            }
        }

        /// <summary>
        /// Starts each channel of <paramref name="next"/> that <paramref name="interrupted"/> draws from the value
        /// drawn, the bezier sibling of <see cref="MotionSpringDriver.ContinueFrom"/> less the velocity a tween does
        /// not carry. A length in another unit keeps its own start.
        /// </summary>
        public static void ContinueFrom(BezierTweenState next, MotionDrawnValues interrupted)
        {
            Continue(next.Opacity, interrupted.Opacity);
            Continue(next.TranslateX, interrupted.TranslateXUnit == next.TranslateX?.Unit ? interrupted.TranslateX : null);
            Continue(next.TranslateY, interrupted.TranslateYUnit == next.TranslateY?.Unit ? interrupted.TranslateY : null);
            Continue(next.Scale, interrupted.Scale);
            Continue(next.ScaleY, interrupted.ScaleY ?? interrupted.Scale);
            Continue(next.Rotate, interrupted.Rotate);
            if (next.Lengths != null)
            {
                foreach (var length in next.Lengths)
                {
                    if (interrupted.LengthOf(length.Property, length.Unit) is { } drawn && drawn.SameUnit)
                    {
                        length.Value.From = length.Value.RestingTarget = drawn.Value;
                    }
                }
            }
            interrupted.ContinueColors(next.Colors, color => color.Property, (color, drawn) =>
            {
                var continued = new BezierColorChannel(color.Property, drawn, color.To);
                continued.Progress.Timing = color.Progress.Timing;
                return continued;
            });
        }

        private static void Continue(BezierTweenChannel? next, (float Value, float Velocity)? interrupted)
        {
            if (next != null && interrupted is { } drawn)
            {
                next.From = next.RestingTarget = drawn.Value;
            }
        }

        /// <summary>
        /// Freezes each active channel's CURRENT sampled value as its new <see cref="BezierTweenChannel.From"/>,
        /// points its <see cref="BezierTweenChannel.To"/> back at <see cref="BezierTweenChannel.RestingTarget"/>,
        /// and resets <see cref="BezierTweenState.ElapsedSec"/> to zero — a fresh reversal from wherever the forward
        /// tween currently is, not a time-reversed replay: over the full duration for a bezier, and for a driven Tween
        /// over the shortened timing its native transition's reversal takes. The exit-cancel hand-off. The reversal is
        /// one pass whatever the play repeated.
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
            RetargetChannel(state, state.ScaleY, shared);
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
            state.Repeat = default;
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

        private const double VelocitySlopeStepSec = 1.0 / 1000.0;

        /// <summary>
        /// The channel's rate of change per second at the play's current elapsed time, which a spring that interrupts
        /// the play starts with, as Framer Motion starts a value's next animation with the motion value's velocity.
        /// A finished play has stopped.
        /// </summary>
        internal static float VelocityOf(BezierTweenState state, BezierTweenChannel? channel, ArbitraryProperty? length = null)
        {
            if (channel == null || state.ElapsedSec >= state.EndSec)
            {
                return 0f;
            }
            var now = state.ElapsedSec;
            // MUTANT_SURVIVES(equivalent, boundary): now is a float, which never equals 1/1000 held as a double.
            var from = now >= VelocitySlopeStepSec ? now - VelocitySlopeStepSec : now;
            // MUTANT_SURVIVES(equivalent, boundary): the same comparison as the line above.
            var to = now >= VelocitySlopeStepSec ? now : now + VelocitySlopeStepSec;
            var before = ValueAt(state, channel, (float)from, length);
            var after = ValueAt(state, channel, (float)to, length);
            return (after - before) / (float)(to - from);
        }

        // The eased output for the channel at the current elapsed time: its own Timing where it has one, else
        // shared, the state's curve at the state's delay and duration (SharedEased), evaluated once a frame for
        // every channel taking it.
        private static float Eased(BezierTweenState state, BezierTweenChannel channel, float shared)
        {
            if (channel.Timing is not { } own)
            {
                return shared;
            }
            var progress = Progress(state, state.ElapsedSec - own.DelaySec, own.DurationSec);
            return own.Curve
                ? CubicBezierEvaluator.Evaluate(state.X1, state.Y1, state.X2, state.Y2, progress)
                : UssEasing.Evaluate(own.Easing, progress);
        }

        // A zero duration reads as complete once its delay has passed rather than dividing by zero — the scheduler
        // builds one only from a PropertyOverrides entry of no positive duration or a reversal at its very start,
        // but a direct driver caller might.
        private static float SharedEased(BezierTweenState state)
        {
            return state.Easing is { } mode
                ? UssEasing.Evaluate(mode, Progress(state, state.ElapsedSec - state.DelaySec, state.DurationSec))
                : RepeatedBezierFraction(state);
        }

        // A bezier play's curve over every pass Repeat adds. It has no delay of its own (DelaySec is a driven Tween's),
        // so elapsed time is the time into the play. A finished play shows the end MotionRepeat.EndsAtFrom names, as
        // Framer Motion's getFinalKeyframe overrides its last sample.
        private static float RepeatedBezierFraction(BezierTweenState state)
        {
            if (state.DurationSec <= 0f)
            {
                return state.SeekedToStart ? 0f : CubicBezierEvaluator.Evaluate(state.X1, state.Y1, state.X2, state.Y2, 1f);
            }
            if (state.ElapsedSec >= state.EndSec)
            {
                return state.Repeat.EndsAtFrom ? 0f : 1f;
            }
            var progress = (float)(state.Repeat.PassTime(state.ElapsedSec, state.DurationSec, out var mirrored)
                / state.DurationSec);
            var eased = CubicBezierEvaluator.Evaluate(state.X1, state.Y1, state.X2, state.Y2, progress);
            return mirrored ? 1f - eased : eased;
        }

        private static float Progress(BezierTweenState state, float activeSec, float durationSec)
            => durationSec > 0f ? Mathf.Clamp01(activeSec / durationSec)
                : (state.SeekedToStart ? activeSec > 0f : activeSec >= 0f) ? 1f : 0f;

        private static void ApplyEased(VisualElement element, BezierTweenState state)
        {
            SyncLayoutOwner(element, state);
            var shared = SharedEased(state);
            if (state.Opacity is { Masked: false })
            {
                MotionOpacity.Write(element, Value(state, state.Opacity, shared));
            }
            if ((state.TranslateX != null || state.TranslateY != null) && state.TranslateX?.Masked != true)
            {
                var x = state.TranslateX != null ? Value(state, state.TranslateX, shared) : 0f;
                var y = state.TranslateY != null ? Value(state, state.TranslateY, shared) : 0f;
                SeatedTranslate.Write(element, new Translate(
                    new Length(x, state.TranslateX?.Unit ?? LengthUnit.Pixel),
                    new Length(y, state.TranslateY?.Unit ?? LengthUnit.Pixel)));
            }
            if (state.Scale is { Masked: false })
            {
                var v = Value(state, state.Scale, shared);
                element.style.scale = new Scale(new Vector2(v, state.ScaleY != null ? Value(state, state.ScaleY, shared) : v));
            }
            if (state.Rotate is { Masked: false })
            {
                var v = Value(state, state.Rotate, shared);
                element.style.rotate = new Rotate(Angle.Degrees(v));
            }
            if (state.Colors != null)
            {
                foreach (var c in state.Colors)
                {
                    if (!c.Progress.Masked)
                    {
                        var progress = Value(state, c.Progress, shared);
                        StyleArbitraryValueResolver.ApplyDriven(element,
                            new ArbitraryStyle(c.Property, MotionPropertyInterpolation.LerpColor(c.From, c.To, progress)),
                            state.Exit);
                    }
                }
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths)
                {
                    if (!l.Value.Masked)
                    {
                        var v = MotionPropertyInterpolation.LerpLength(l.Property, l.Value.From, l.Value.To,
                            Eased(state, l.Value, shared));
                        StyleArbitraryValueResolver.ApplyDriven(element, new ArbitraryStyle(l.Property, v, l.Unit), state.Exit);
                    }
                }
            }
            StyleAnimateDriver.ReassertLoop(element, DrivenSlots(state));
        }
    }
}
