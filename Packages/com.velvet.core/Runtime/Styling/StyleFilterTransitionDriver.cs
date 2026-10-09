using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-element state for a Velvet-driven filter-* transition. Holds the one-shot tick that lerps the
    // inline filter's parameters from the current applied value to the newly composed one, the precomputed
    // aligned interpolation slots, and the exact static list to settle to.
    internal sealed class StyleFilterTransitionBinding
    {
        // The one-shot tick, null when idle/settled. Scheduled on the PANEL ROOT (not the element) so a keyed
        // reorder — which briefly detaches the element and would make UI Toolkit silently drop a per-element
        // scheduled item — does not stall the tween. Paused + nulled on settle and on teardown.
        public IVisualElementScheduledItem? Scheduled;
        // Start on Clock. Progress is elapsed/duration so a dropped frame never accumulates drift.
        public double StartTime;
        // The clock of the mount that created the element, read as the tween starts (MotionClock.Of).
        public MotionClock Clock = MotionClock.Realtime;
        public float DurationSec;
        public float DelaySec;
        public EasingMode Easing;
        // The value a change has to return to for it to reverse this tween, and the tween's reversing shortening
        // factor (TryStartOrRedirect).
        public List<FilterFunction>? AdjustedStart;
        public float ReversingFactor = 1f;
        // Precomputed aligned interpolation slots. Parameters are snapshotted at start (decoupled from the
        // live inline list the tick overwrites every frame).
        public StyleFilterTransitionDriver.Channel[] Channels = Array.Empty<StyleFilterTransitionDriver.Channel>();
        // The exact static list to settle to on completion (null = clear the inline filter).
        public List<FilterFunction>? Target;
    }

    // Drives filter-* transitions (blur / brightness / contrast / …) via the scheduler tween Velvet already
    // uses for animate-hue, lerping the filter parameters itself and writing a fresh inline list per frame
    // (same repaint-dirtying reason as the Hue arm of StyleAnimateDriver).
    //
    // Velvet runs a filter change wherever an entry runs for filter, except where the engine's own animation takes
    // the write with that entry's timing (EngineTimesFilterWrites) on a mount whose MotionClock is the panel's,
    // which keeps the engine's shortening of a reversed transition there. The inline-filter setter animates a list write by the entry for background-size;
    // where that entry runs on other timing than filter's, StyleFilterEngineWrite writes past it with transitions
    // suspended. TryFindTransition decides which entry runs. That decision is TryStartOrRedirect's resolvedStyle
    // probe, not the class list, and the tick re-checks it every frame so a value that changes mid-tween hands
    // over instead of fighting.
    //
    // The write hook (TryStartOrRedirect) sits inside StyleArbitraryValueResolver.ApplyCombinedFilter — the sole
    // site that composes and writes style.filter — so it covers every filter path (base blur-md, arbitrary
    // blur-[6px], custom filter-[name:args], and the variant path hover:blur-md) with no per-manipulator wiring.
    //
    // animate-hue owns the filter while it runs, as a CSS animation hides a transition of the property it animates:
    // each frame and settle here re-asserts the loop (StyleAnimateDriver.ReassertLoop), a change under it starts no
    // tween (StyleAnimateDriver.DrivesFilter), and a tween already running keeps its clock and shows again once the
    // motion ends.
    //
    // The phase math (ApplyFrame / channel alignment) is pure and unit-tested directly; the scheduler
    // wiring runs at runtime (the EditMode PlayerLoop does not tick, so tests drive ApplyFrame at explicit
    // phases). The resolver runs during event callbacks with no ReconcilerContext, so the element→binding
    // lookup lives in a ConditionalWeakTable (same reason the layer map does); ReconcilerContext mirrors the
    // bindings the reconciler made so the dispose sweep can enumerate them.
    internal static class StyleFilterTransitionDriver
    {
        // One precomputed interpolation slot: a filter function whose parameters lerp From→To. Parameters are
        // snapshotted so the tick's per-frame overwrite of the live inline list cannot alias them.
        internal readonly struct Channel
        {
            public readonly FilterFunctionType Type;
            // The bound definition for a Custom function — first-party brightness/saturate or a user
            // filter-[name:args]; null for a native filter type. ApplyFrame rebuilds the function through it so
            // a rebuilt Custom rebinds its shader material instead of collapsing to a definition-less Custom
            // that renders nothing.
            public readonly FilterFunctionDefinition? Definition;
            public readonly FilterParameter[] From;
            public readonly FilterParameter[] To;

            public Channel(FilterFunctionType type, FilterFunctionDefinition? definition, FilterParameter[] from, FilterParameter[] to)
            {
                Type = type;
                Definition = definition;
                From = from;
                To = to;
            }
        }

        private static readonly ConditionalWeakTable<VisualElement, StyleFilterTransitionBinding> s_bindings = new();

        // The binding the element's tweens run on, created on first use. The reconciler binds an element carrying
        // transition-filter up front so it can pause the tick at teardown; the write hook binds any other element a
        // tween starts on, since a hand-authored transition list names filter without that class.
        public static StyleFilterTransitionBinding Bind(VisualElement element)
            => s_bindings.GetValue(element, _ => new StyleFilterTransitionBinding());

        public static void Unregister(VisualElement element) => s_bindings.Remove(element);

        // Stops whatever tween the element has without writing to it, for an element about to serve another consumer.
        public static void Release(VisualElement element)
        {
            if (s_bindings.TryGetValue(element, out var b))
            {
                Cancel(b);
            }
        }

        // The write hook, called from ApplyCombinedFilter with the freshly composed target list (null = clear).
        // Returns true iff it took ownership of the write (started or redirected a tween); false lets the
        // resolver perform its instant write.
        internal static bool TryStartOrRedirect(VisualElement element, List<FilterFunction>? to)
        {
            s_bindings.TryGetValue(element, out var bound);
            // The value a running tween heads for is no change to it: it runs on, and its frame is written back over
            // whatever the element shows now.
            if (bound?.Scheduled != null && SameList(bound.Target, to))
            {
                ApplyFrame(element, bound, Progress(bound));
                return true;
            }
            // Off-panel: there is no host to tick and no paint to animate, so apply instantly (CSS does not
            // transition an off-render value either). The RESOLVED transition lists — not the class list — decide
            // which animator owns the change (see the note on the class), and the entry that runs for filter gives
            // the tween its timing.
            var durationMs = 0;
            var delayMs = 0;
            var easing = EasingMode.Ease;
            var channels = Array.Empty<Channel>();
            var clock = MotionClock.Of(element);
            // An animate-hue motion masks a filter utility's change, so it starts no transition, as a CSS
            // animation masks the change in both the before- and after-change styles; and nor does the change an
            // ended motion uncovers (StyleFilterEngineWrite.WithoutTransition).
            // A mount on another clock than the panel's leaves no write for the engine to animate, since the
            // engine animates it on the panel's time.
            var runs = element.panel != null && !StyleAnimateDriver.DrivesFilter(element)
                && !StyleFilterEngineWrite.TransitionsWithheld
                && !(clock.StepsOnPanelTime && EngineTimesFilterWrites(element))
                && TryFindTransition(element, FilterPropertyName, null, out durationMs, out delayMs, out easing);
            // Read the CURRENT applied list as the from-side. During an in-flight tween this is last frame's
            // interpolated list, so a redirect starts from where the eye is — not the tween's original start.
            if (!runs || !TryBuildChannels(element.style.filter.value, to, out channels))
            {
                // Nothing runs for the tween, or the change does not interpolate: a discrete instant write.
                if (bound != null)
                {
                    Cancel(bound);
                }
                return false;
            }
            if (ChannelsAreNoOp(channels))
            {
                // from == to: nothing to animate; let the resolver write the identical value.
                return false;
            }

            // A change back to where the running tween started reverses it as the engine reverses a transition: CSS
            // Transitions' reversing shortening, whose factor is the running tween's eased output weighted by that
            // tween's own factor, and which shortens the duration and a negative delay.
            var factor = 1f;
            var adjustedStart = element.style.filter.value;
            if (bound?.Scheduled != null && SameList(bound.AdjustedStart, to))
            {
                var eased = UssEasing.Evaluate(bound.Easing, Progress(bound));
                factor = Mathf.Clamp01(Mathf.Abs(eased * bound.ReversingFactor + 1f - bound.ReversingFactor));
                adjustedStart = bound.Target;
            }
            var b = bound ?? Bind(element);
            b.Channels = channels;
            b.Target = to;
            b.AdjustedStart = adjustedStart;
            b.ReversingFactor = factor;
            b.DurationSec = Mathf.Max(0, durationMs) / 1000f * factor;
            // MUTANT_SURVIVES(equivalent, boundary): a zero delay shortens to zero either way.
            b.DelaySec = delayMs < 0 ? delayMs / 1000f * factor : delayMs / 1000f;
            b.Easing = easing;
            b.Clock = clock;
            b.StartTime = clock.NowSec;
            // Write the start frame now so there is no one-frame flash of the pre-change value.
            ApplyFrame(element, b, Progress(b));
            // Reuse a running tick — resetting StartTime/Channels/Target redirects it in place (the tick reads
            // the binding fields each frame).
            if (b.Scheduled == null)
            {
                StartTick(element, b);
            }
            return true;
        }

        // The USS names of the filter property and of the one the inline-filter setter animates a list write by.
        private const string FilterPropertyName = "filter";
        private const string BackgroundSizePropertyName = "background-size";
        private const string BackgroundScaleModePropertyName = "-unity-background-scale-mode";

        // True where an entry runs for background-size, which the inline-filter setter animates a list write by
        // (see the note on the class).
        internal static bool EngineAnimatesFilterWrites(VisualElement element)
            => TryFindTransition(element, BackgroundSizePropertyName, BackgroundScaleModePropertyName, out _, out _, out _);

        // True where an entry runs for filter itself.
        internal static bool FilterTransitionRuns(VisualElement element)
            => TryFindTransition(element, FilterPropertyName, null, out _, out _, out _);

        // True where the entry the setter animates a list write by runs with the duration, delay and curve of the
        // one that runs for filter, so the engine's animation of the write takes filter's own timing. Anywhere else
        // the tween runs a filter change, or nothing does (FilterTransitionPanelTests' background-size cases).
        internal static bool EngineTimesFilterWrites(VisualElement element)
        {
            var filterRuns = TryFindTransition(element, FilterPropertyName, null,
                out var filterDuration, out var filterDelay, out var filterEasing);
            var setterRuns = TryFindTransition(element, BackgroundSizePropertyName, BackgroundScaleModePropertyName,
                out var setterDuration, out var setterDelay, out var setterEasing);
            // MUTANT_SURVIVES(equivalent, clause removed): a running filter entry never has the (0, 0) timing reported here.
            // An entry runs only where its duration floored at 0 plus its delay is positive, and (0, 0) is what
            // TryFindTransition reports for a setter entry it did not find.
            return filterRuns && setterRuns
                && (filterDuration, filterDelay, filterEasing) == (setterDuration, setterDelay, setterEasing);
        }

        // The entry UI Toolkit runs a property's transition by, read off the resolved lists as
        // ComputedTransitionUtils reads them: each entry takes the duration, delay and curve at its own index with
        // every list wrapping, an entry whose duration floored at 0 plus its delay is not positive is dropped, and
        // the last remaining entry naming the property, its shorthand or `all` runs. Times are whole milliseconds,
        // rounded as the engine rounds them.
        internal static bool TryFindTransition(VisualElement element, string property, string? shorthand,
            out int durationMs, out int delayMs, out EasingMode easing)
        {
            var resolved = element.resolvedStyle;
            var lists = new TransitionLists(AsList(resolved.transitionProperty), AsList(resolved.transitionDuration),
                AsList(resolved.transitionDelay), AsList(resolved.transitionTimingFunction));
            return TryFindTransition(lists, property, shorthand, out durationMs, out delayMs, out easing);
        }

        // The same entry, read off lists given here: an element's inline lists or the ones its rules cascade to
        // (MotionOpacity).
        internal static bool TryFindTransition(TransitionLists lists, string property, string? shorthand,
            out int durationMs, out int delayMs, out EasingMode easing)
        {
            var (properties, durations, delays, curves) = (lists.Properties, lists.Durations, lists.Delays, lists.Curves);
            for (var i = properties.Count - 1; i >= 0; i--)
            {
                var name = properties[i].ToString();
                // A name UI Toolkit does not know, `none` among them, reads back as null, which a null shorthand would
                // otherwise match.
                if (name == null) continue;
                if (name != property && name != shorthand && name != "all")
                {
                    continue;
                }
                var duration = durations.Count == 0 ? 0 : Milliseconds(durations[i % durations.Count]);
                var delay = delays.Count == 0 ? 0 : Milliseconds(delays[i % delays.Count]);
                if (Mathf.Max(0, duration) + delay <= 0)
                {
                    continue;
                }
                durationMs = duration;
                delayMs = delay;
                easing = curves.Count == 0 ? EasingMode.Ease : curves[i % curves.Count].mode;
                return true;
            }
            durationMs = 0;
            delayMs = 0;
            easing = EasingMode.Ease;
            return false;
        }

        private static int Milliseconds(TimeValue time)
            => Mathf.RoundToInt(time.unit == TimeUnit.Millisecond ? time.value : time.value * 1000f);

        // Indexed rather than foreach'd: the resolved lists are typed as interfaces, so enumerating one boxes an
        // enumerator, and the tick reads them every frame. The copy is the fallback for a resolved value that is
        // not a list.
        private static IList<T> AsList<T>(IEnumerable<T> resolved) => resolved as IList<T> ?? resolved.ToList();

        // The pre-easing progress at this moment, 0 through the delay and 1 from the end of the run: a zero duration
        // jumps to the end once the delay has passed, as a USS transition does.
        private static float Progress(StyleFilterTransitionBinding b)
        {
            var active = b.Clock.NowSec - b.StartTime - b.DelaySec;
            return Mathf.Clamp01((float)(active / Math.Max(b.DurationSec, 1e-6)));
        }

        // Applies one frame at progress t (pre-easing). Pure: builds a FRESH list every call (UI Toolkit's
        // inline-filter setter dirties the element for repaint only when the backing list REFERENCE changes,
        // so a reused list would paint the first frame then freeze). Public so tests drive specific phases
        // without the runtime scheduler.
        public static void ApplyFrame(VisualElement element, StyleFilterTransitionBinding b, float t)
        {
            var e = UssEasing.Evaluate(b.Easing, t);
            var list = new List<FilterFunction>(b.Channels.Length);
            foreach (var channel in b.Channels)
            {
                // A Custom channel whose definition was destroyed mid-tween compares equal to null (a dead
                // asset). The engine's FilterFunction constructor throws on one, and a Custom rebuilt without
                // its definition would render nothing anyway, so drop the channel — the same degrade the
                // resolver takes when a registered definition dies under it.
                if (channel.Type == FilterFunctionType.Custom && channel.Definition == null)
                {
                    continue;
                }
                // A Custom (the first-party brightness/saturate, or a user filter-[name:args]) must be rebuilt
                // through its definition so it rebinds its shader; a native type is rebuilt by its
                // FilterFunctionType.
                var fn = channel.Definition != null
                    ? new FilterFunction(channel.Definition)
                    : new FilterFunction(channel.Type);
                for (var k = 0; k < channel.From.Length; k++)
                {
                    fn.AddParameter(LerpParam(channel.From[k], channel.To[k], e));
                }
                list.Add(fn);
            }
            StyleFilterEngineWrite.WriteFrame(element, list);
            StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.Filter);
        }

        private static void StartTick(VisualElement element, StyleFilterTransitionBinding b)
        {
            var host = element.panel.visualTree;
            b.Scheduled = host.schedule.Execute(() =>
            {
                var progress = Progress(b);
                if (progress >= 1f)
                {
                    Settle(element, b);
                    return;
                }
                // Off the panel nothing paints, so no frame is written; the tween's frames resume if the element comes
                // back. An element handed to another consumer is released by the pool reset (Release).
                if (element.panel == null)
                {
                    return;
                }
                // The resolved transition lists can change WHILE the tween runs — a class swap the reconciler keeps
                // bound, or an inline transition-property written by a Motion play. Where the engine's own animation
                // takes a filter write from that moment, every frame write is taken over by it and restarted from the
                // painted value, so the paint would crawl behind a target that moves each tick. Hand over by
                // settling once instead, as where nothing runs for filter any more.
                if ((b.Clock.StepsOnPanelTime && EngineTimesFilterWrites(element)) || !FilterTransitionRuns(element))
                {
                    Settle(element, b);
                    return;
                }
                ApplyFrame(element, b, progress);
            }).Every(StyleAnimateDriver.TickMs);
        }

        // Writes the EXACT composed static list the tween was heading for (null = clear the inline filter) and
        // stops ticking. The target is the resolver's own value, written the way the resolver writes it, so the
        // tween lands where an instant write would have. NOTE the target is the list composed when the tween
        // started: a definition destroyed since then is still in it, and is skipped by the resolver's next compose
        // rather than here.
        private static void Settle(VisualElement element, StyleFilterTransitionBinding b)
        {
            StyleFilterEngineWrite.Write(element, b.Target);
            StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.Filter);
            Cancel(b);
        }

        // Pauses + drops the tick, keeping the binding registered (idle). Used when a change resolves to an
        // instant write (off-panel / zero-duration / not the tween's to run / non-interpolable) and by Detach.
        private static void Cancel(StyleFilterTransitionBinding b)
        {
            b.Scheduled?.Pause();
            b.Scheduled = null;
        }

        // Full teardown: settle a still-running tween, cancel the tick, unregister. Dropping the transition-filter
        // class while a filter-* class the element still carries is unchanged does NOT re-resolve the static
        // value (the reconciler only re-asserts filters it saw change), so a mid-frame interpolated value would
        // otherwise freeze onto the element — settle it to the tween's target. Off-panel teardown skips the
        // write: the element is unmounting and the pool reset scrubs style.filter before reuse.
        public static void Detach(VisualElement element, StyleFilterTransitionBinding b)
        {
            if (b.Scheduled != null && element.panel != null)
            {
                Settle(element, b);
            }
            Cancel(b);
            Unregister(element);
        }

        #region Channel alignment

        // Builds the interpolation slots for from→to by the Filter Effects rule: functions pair by position, the
        // longer list's tail fades from or to its neutral, and the change does not interpolate (false: write
        // instantly) where a paired position holds a different channel or parameters that do not line up. A null
        // from is treated as an empty list (a freshly-mounted element with no inline filter reads null, not []).
        internal static bool TryBuildChannels(List<FilterFunction>? from, List<FilterFunction>? to,
            out Channel[] channels)
        {
            channels = Array.Empty<Channel>();
            var fromCount = from?.Count ?? 0;
            var toCount = to?.Count ?? 0;
            if (fromCount == 0 && toCount == 0)
            {
                return false;
            }
            var aligned = new Channel[Math.Max(fromCount, toCount)];
            for (var k = 0; k < aligned.Length; k++)
            {
                if (k >= toCount)
                {
                    aligned[k] = FadeOut(from![k]);
                }
                else if (k >= fromCount)
                {
                    aligned[k] = FadeIn(to![k]);
                }
                else
                {
                    var f = from![k];
                    var t = to![k];
                    if (!SameChannel(f, t) || f.parameterCount != t.parameterCount || !ParameterTypesAlign(f, t))
                    {
                        return false;
                    }
                    aligned[k] = new Channel(f.type, DefinitionOf(f), Snapshot(f), Snapshot(t));
                }
            }
            channels = aligned;
            return true;
        }

        // A filter present only in the to-list fades IN from its neutral value; one present only in from fades
        // OUT to it. Matches CSS filter-list padding.
        private static Channel FadeIn(FilterFunction f) => new Channel(f.type, DefinitionOf(f), IdentityParams(f), Snapshot(f));
        private static Channel FadeOut(FilterFunction f) => new Channel(f.type, DefinitionOf(f), Snapshot(f), IdentityParams(f));

        // The definition to rebind when reconstructing a function each frame: the bound custom definition for a
        // built-in custom, null for a native type (ApplyFrame rebuilds that by FilterFunctionType).
        private static FilterFunctionDefinition? DefinitionOf(FilterFunction f)
            => f.type == FilterFunctionType.Custom ? f.customDefinition : null;

        // Two paired functions interpolate only when each slot holds the same kind of value: a definition may
        // declare a color slot where its counterpart declares a float, and lerping across those is meaningless.
        private static bool ParameterTypesAlign(FilterFunction a, FilterFunction b)
        {
            for (var k = 0; k < a.parameterCount; k++)
            {
                if (a.GetParameter(k).type != b.GetParameter(k).type)
                {
                    return false;
                }
            }
            return true;
        }

        // Two functions share a channel when they are the same native filter type, or both Custom bound to the
        // SAME definition — brightness, saturate and each user filter-[name:args] are distinct channels even
        // though all of them are FilterFunctionType.Custom.
        private static bool SameChannel(FilterFunction a, FilterFunction b)
        {
            if (a.type != b.type)
            {
                return false;
            }
            return a.type != FilterFunctionType.Custom
                || ReferenceEquals(a.customDefinition, b.customDefinition);
        }

        private static FilterParameter[] Snapshot(FilterFunction f)
        {
            var count = f.parameterCount;
            var arr = new FilterParameter[count];
            for (var k = 0; k < count; k++)
            {
                arr[k] = f.GetParameter(k);
            }
            return arr;
        }

        // The neutral parameters for a filter — the value at which it is a no-op, which a channel present on
        // only one side fades from or to. A CUSTOM function declares its own per-slot neutral, and that
        // declaration is the same value the engine pads a filter-list transition with, so it is read straight
        // off the definition rather than guessed from the function's shape. For a native type the neutral
        // follows the CSS definition: contrast is off at 1, every other float filter (blur, grayscale,
        // hue-rotate, invert, sepia) is off at 0, and a color parameter's identity is white.
        private static FilterParameter[] IdentityParams(FilterFunction f)
        {
            var count = f.parameterCount;
            var arr = new FilterParameter[count];
            var declarations = f.type == FilterFunctionType.Custom ? f.customDefinition?.parameters : null;
            var floatIdentity = f.type == FilterFunctionType.Contrast ? 1f : 0f;
            for (var k = 0; k < count; k++)
            {
                // A declaration shorter than the live parameter list leaves that slot without a declared
                // neutral; fall back to the native rule for its type. NOTE the ?. above is a plain reference
                // check — it does NOT use the engine's destroyed-object equality, so a destroyed-but-uncollected
                // definition still reaches this. That is safe only because reading the parameter declarations
                // touches managed fields; anything here that reaches native state must test the definition with
                // == null instead (as the frame apply does).
                if (declarations != null && k < declarations.Length)
                {
                    var declared = declarations[k].interpolationDefaultValue;
                    if (declared.type == f.GetParameter(k).type)
                    {
                        arr[k] = declared;
                        continue;
                    }
                }
                arr[k] = f.GetParameter(k).type == FilterParameterType.Color
                    ? new FilterParameter(Color.white)
                    : new FilterParameter(floatIdentity);
            }
            return arr;
        }

        // The function at the neutral a one-sided channel fades from or to.
        internal static FilterFunction NeutralOf(FilterFunction f)
        {
            var neutral = new FilterFunction(f.type) { customDefinition = f.customDefinition };
            foreach (var parameter in IdentityParams(f))
            {
                neutral.AddParameter(parameter);
            }
            return neutral;
        }

        private static bool SameList(List<FilterFunction>? a, List<FilterFunction>? b)
        {
            var count = a?.Count ?? 0;
            if (count != (b?.Count ?? 0))
            {
                return false;
            }
            for (var k = 0; k < count; k++)
            {
                var f = a![k];
                var t = b![k];
                if (!SameChannel(f, t) || !SameParameters(f, t))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameParameters(FilterFunction a, FilterFunction b)
        {
            if (a.parameterCount != b.parameterCount)
            {
                return false;
            }
            for (var k = 0; k < a.parameterCount; k++)
            {
                if (!ParamEqual(a.GetParameter(k), b.GetParameter(k)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ChannelsAreNoOp(Channel[] channels)
        {
            foreach (var c in channels)
            {
                for (var k = 0; k < c.From.Length; k++)
                {
                    if (!ParamEqual(c.From[k], c.To[k]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool ParamEqual(FilterParameter a, FilterParameter b)
        {
            if (a.type != b.type)
            {
                return false;
            }
            return a.type == FilterParameterType.Color
                ? a.colorValue == b.colorValue
                : Mathf.Approximately(a.floatValue, b.floatValue);
        }

        private static FilterParameter LerpParam(FilterParameter from, FilterParameter to, float e)
            => from.type == FilterParameterType.Color
                ? new FilterParameter(Color.Lerp(from.colorValue, to.colorValue, e))
                : new FilterParameter(Mathf.Lerp(from.floatValue, to.floatValue, e));

        #endregion
    }

    // The four lists a transition is declared by, a missing one read as empty.
    internal readonly struct TransitionLists
    {
        public TransitionLists(IList<StylePropertyName>? properties, IList<TimeValue>? durations, IList<TimeValue>? delays,
            IList<EasingFunction>? curves)
        {
            Properties = properties ?? Array.Empty<StylePropertyName>();
            Durations = durations ?? Array.Empty<TimeValue>();
            Delays = delays ?? Array.Empty<TimeValue>();
            Curves = curves ?? Array.Empty<EasingFunction>();
        }

        public IList<StylePropertyName> Properties { get; }
        public IList<TimeValue> Durations { get; }
        public IList<TimeValue> Delays { get; }
        public IList<EasingFunction> Curves { get; }
    }
}
