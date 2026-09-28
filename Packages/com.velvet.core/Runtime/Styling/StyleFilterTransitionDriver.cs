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
        // Wall-clock start (Time.realtimeSinceStartupAsDouble). Progress is elapsed/duration so a dropped
        // frame never accumulates drift.
        public double StartTime;
        public float DurationSec;
        public EasingMode Easing;
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
    // Velvet takes over when the resolved transition-property CONTAINS filter — what .transition-filter pins,
    // and equally true of a hand-authored list naming filter among others. Under a whole-property value the
    // inline-filter setter runs the write as its own native animation (which no public API can cancel), so the
    // tween stands down there and the engine animates it. That decision is TryStartOrRedirect's resolvedStyle
    // probe, not the class list, and the tick re-checks it every frame so a value that changes mid-tween hands
    // over instead of fighting.
    //
    // NB the setter does not test the transition list against `filter` at all: it tests it against
    // `background-size`, and takes its animating path when an entry equals that or is a shorthand covering it.
    // Three values therefore trip it — a whole-property value, `background-size`, and the
    // `-unity-background-scale-mode` shorthand. The probe's rule is an OBSERVED equivalence, measured on this
    // editor, not a restatement of the engine's own condition: it holds only because the values Velvet's own
    // utilities produce never name those. Two consequences, neither diagnosed anywhere. A hand-authored list
    // naming filter AND either of those two satisfies the probe and the setter at once, so both animators run
    // the same property. And a list naming one of them WITHOUT filter animates the change natively even though
    // nothing about it mentions filter, so "names neither, therefore discrete" holds only for lists that also
    // avoid the background-size family.
    //
    // The write hook (TryStartOrRedirect) sits inside StyleArbitraryValueResolver.ApplyCombinedFilter — the sole
    // site that composes and writes style.filter — so it covers every filter path (base blur-md, arbitrary
    // blur-[6px], custom filter-[name:args], and the variant path hover:blur-md) with no per-manipulator wiring.
    //
    // Two documented precedence notes:
    // - animate-hue owns style.filter unconditionally while active; combining transition-filter + animate-hue on
    //   one element is unsupported (Hue wins). Hue's own Detach re-asserts static filters through
    //   ApplyCombinedFilter, so a benign one-shot tween may kick off right after a Hue Detach — harmless.
    // - On the very reconcile patch that ADDS transition-filter, the class-driven filter write runs (through
    //   SyncClassDrivenStyling) BEFORE the applier enables the binding, so a value that changes in that same
    //   patch applies instantly, not tweened — matching CSS, which likewise does not retroactively animate a
    //   value that changed in the same paint the transition-property first became active. Every later change
    //   (the common case: a hover-driven variant swap, which runs through the manipulator's event callback)
    //   transitions correctly.
    //
    // The phase math (ApplyFrame / Ease / channel alignment) is pure and unit-tested directly; the scheduler
    // wiring runs at runtime (the EditMode PlayerLoop does not tick, so tests drive ApplyFrame at explicit
    // phases). The resolver runs during event callbacks with no ReconcilerContext, so the element→binding
    // lookup lives in a ConditionalWeakTable (same reason the layer map does); ReconcilerContext mirrors the
    // refs only so the dispose sweep can enumerate them.
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

        // Enrolls a binding so the write hook can find it during a resolver callback. The reconciler owns the
        // binding lifecycle (create on transition-filter present, Detach on absent / teardown).
        public static void Register(VisualElement element, StyleFilterTransitionBinding binding)
            => s_bindings.AddOrUpdate(element, binding);

        public static void Unregister(VisualElement element) => s_bindings.Remove(element);

        // The write hook, called from ApplyCombinedFilter with the freshly composed target list (null = clear).
        // Returns true iff it took ownership of the write (started or redirected a tween); false lets the
        // resolver perform its instant write. An element with no binding pays only the first line's lookup.
        internal static bool TryStartOrRedirect(VisualElement element, List<FilterFunction>? to)
        {
            // The binding is the driver's per-element state, not the decision: the reconciler creates one
            // wherever a transition-filter class appears, and the resolvedStyle probe below rules on whether
            // this particular change is the tween's to run.
            if (!s_bindings.TryGetValue(element, out var b))
            {
                return false;
            }
            // Off-panel: there is no host to tick and no paint to animate, so apply instantly (CSS does not
            // transition an off-render value either).
            if (element.panel == null)
            {
                Cancel(b);
                return false;
            }
            // Duration + curve come from the resolved transition-* longhands the transition-filter class (and
            // any duration-* / ease-* override) set. Already-resolved seconds; no unit conversion. The resolved
            // lists are IEnumerable, so read the first entry (the whole-property value) via FirstOrDefault — an
            // empty list yields 0s, which the guard below treats as "no transition, write instantly".
            var duration = element.resolvedStyle.transitionDuration.FirstOrDefault().value;
            if (duration <= 0f)
            {
                Cancel(b);
                return false;
            }
            // The RESOLVED transition-property — not the class list — decides which animator owns the change.
            // A list CONTAINING filter (alongside any number of other properties) keeps the inline-filter
            // setter on its plain direct-write path, the only path where a per-frame tween write actually
            // paints what it wrote. A whole-property value instead makes that setter run the write as a native
            // animation that nothing can cancel, so the tween stands down and lets it animate. Any other list
            // leaves the change discrete — EXCEPT one naming the background-size family, which trips the same
            // native animation without mentioning filter at all (see the note on the class).
            if (!ResolvedTransitionNamesFilter(element))
            {
                Cancel(b);
                return false;
            }
            var easing = element.resolvedStyle.transitionTimingFunction.FirstOrDefault().mode;

            // Read the CURRENT applied list as the from-side. During an in-flight tween this is last frame's
            // interpolated list, so a redirect starts from where the eye is — not the tween's original start.
            var from = element.style.filter.value;
            if (!TryBuildChannels(from, to, out var channels))
            {
                // Non-interpolable (an ambiguous add/remove, mismatched slots) → discrete instant write.
                Cancel(b);
                return false;
            }
            if (ChannelsAreNoOp(channels))
            {
                // from == to: nothing to animate; let the resolver write the identical value.
                return false;
            }

            b.Channels = channels;
            b.Target = to;
            b.DurationSec = duration;
            b.Easing = easing;
            b.StartTime = Time.realtimeSinceStartupAsDouble;
            // Write the start frame now so there is no one-frame flash of the pre-change value.
            ApplyFrame(element, b, 0f);
            // Reuse a running tick — resetting StartTime/Channels/Target redirects it in place (the tick reads
            // the binding fields each frame).
            if (b.Scheduled == null)
            {
                StartTick(element, b);
            }
            return true;
        }

        // The USS name of the filter property, as it appears in a resolved transition-property list.
        private const string FilterPropertyName = "filter";

        // Indexed rather than foreach'd: the resolved list is typed as an interface, so enumerating it boxes an
        // enumerator — once per tick per animating element, since the tick re-checks this every frame. The
        // backing value is a list today; the enumerated fallback keeps the answer correct rather than silently
        // disabling the tween should that ever stop being true.
        private static bool ResolvedTransitionNamesFilter(VisualElement element)
        {
            var resolved = element.resolvedStyle.transitionProperty;
            if (resolved is IList<StylePropertyName> properties)
            {
                for (var k = 0; k < properties.Count; k++)
                {
                    if (properties[k].ToString() == FilterPropertyName)
                    {
                        return true;
                    }
                }
                return false;
            }
            foreach (var property in resolved)
            {
                if (property.ToString() == FilterPropertyName)
                {
                    return true;
                }
            }
            return false;
        }

        // Applies one frame at progress t (pre-easing). Pure: builds a FRESH list every call (UI Toolkit's
        // inline-filter setter dirties the element for repaint only when the backing list REFERENCE changes,
        // so a reused list would paint the first frame then freeze). Public so tests drive specific phases
        // without the runtime scheduler.
        public static void ApplyFrame(VisualElement element, StyleFilterTransitionBinding b, float t)
        {
            var e = Ease(b.Easing, t);
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
            element.style.filter = list;
        }

        private static void StartTick(VisualElement element, StyleFilterTransitionBinding b)
        {
            var host = element.panel.visualTree;
            b.Scheduled = host.schedule.Execute(() =>
            {
                var elapsed = Time.realtimeSinceStartupAsDouble - b.StartTime;
                var progress = b.DurationSec > 0f ? (float)(elapsed / b.DurationSec) : 1f;
                // The resolved transition-property can stop naming filter WHILE the tween runs — a class swap
                // the reconciler keeps bound, or an inline transition-property written by a Motion play. From
                // that moment every frame write is taken over by the setter's own animation and restarted from
                // the painted value, so the paint would crawl behind a target that moves each tick. Hand over
                // by settling once instead.
                if (progress >= 1f || !ResolvedTransitionNamesFilter(element))
                {
                    Settle(element, b);
                    return;
                }
                ApplyFrame(element, b, progress);
            }).Every(StyleAnimateDriver.TickMs);
        }

        // Writes the EXACT composed static list the tween was heading for (null = clear the inline filter) and
        // stops ticking. The target is the resolver's own value, so the tween lands where a plain instant write
        // would have. NOTE the target is the list composed when the tween started: a definition destroyed since
        // then is still in it, and is skipped by the resolver's next compose rather than here.
        private static void Settle(VisualElement element, StyleFilterTransitionBinding b)
        {
            if (b.Target != null)
            {
                element.style.filter = b.Target;
            }
            else
            {
                element.style.filter = StyleKeyword.Null;
            }
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

        // Builds the aligned interpolation slots for from→to (both always in canonical filter order). Returns
        // false — meaning "not interpolable, write instantly" — for an ambiguous add/remove (a channel that
        // appears more than once, which cannot be paired by identity alone), for an add/remove carrying two or
        // more distinct user customs (they share one canonical rank, so the merge cannot order them), and for a
        // slot pair whose parameters do not line up. A null from is treated as an empty list (a freshly-mounted
        // element with no inline filter reads null, not []).
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

            // Fast path: identical channel sequence — the common case (a value change on the same filter set).
            // A user filter-[name:args] custom rides this path too: SameChannel already pairs customs by
            // reference-equal definition, so the slots that survive here are the same shader with the same
            // declared parameters, and interpolating those arguments is what animating the filter means.
            if (SameChannelSequence(from, to))
            {
                var paired = new Channel[fromCount];
                for (var k = 0; k < fromCount; k++)
                {
                    var f = from![k];
                    var t = to![k];
                    if (f.parameterCount != t.parameterCount || !ParameterTypesAlign(f, t))
                    {
                        return false;
                    }
                    paired[k] = new Channel(f.type, DefinitionOf(f), Snapshot(f), Snapshot(t));
                }
                channels = paired;
                return true;
            }

            // Different sequences: a filter was added or removed. Pairing by channel is only unambiguous when
            // each channel occurs at most once per list; otherwise (a repeat) fall back to an instant write.
            if (HasRepeatedChannel(from) || HasRepeatedChannel(to))
            {
                return false;
            }
            // The merge below is a sorted merge over CanonicalRank, which gives EVERY user filter-[name:args]
            // custom the same last rank. One distinct user custom is still the only channel at that rank, so
            // both lists stay strictly ordered and the merge is well defined; two or more tie, and the merge
            // would emit them in an order the resolver does not compose in. Snap only that case — an unpaired
            // user custom fades from its own declared neutral like any other channel (see IdentityParams).
            if (!AtMostOneUserCustomDefinition(from, to))
            {
                return false;
            }

            var merged = new List<Channel>(fromCount + toCount);
            int i = 0, j = 0;
            while (i < fromCount || j < toCount)
            {
                if (i < fromCount && j < toCount)
                {
                    var f = from![i];
                    var t = to![j];
                    if (SameChannel(f, t))
                    {
                        if (f.parameterCount != t.parameterCount || !ParameterTypesAlign(f, t))
                        {
                            return false;
                        }
                        merged.Add(new Channel(f.type, DefinitionOf(f), Snapshot(f), Snapshot(t)));
                        i++;
                        j++;
                    }
                    else if (CanonicalRank(f) < CanonicalRank(t))
                    {
                        merged.Add(FadeOut(f));
                        i++;
                    }
                    else
                    {
                        merged.Add(FadeIn(t));
                        j++;
                    }
                }
                else if (i < fromCount)
                {
                    merged.Add(FadeOut(from![i]));
                    i++;
                }
                else
                {
                    merged.Add(FadeIn(to![j]));
                    j++;
                }
            }
            channels = merged.ToArray();
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

        // A USER custom (filter-[name:args]), as opposed to a first-party brightness/saturate custom. Only the
        // add/remove merge distinguishes them: it has no per-name ordering for user customs.
        private static bool IsUserCustom(FilterFunction f)
            => f.type == FilterFunctionType.Custom && !BuiltInFilterDefinitions.IsBuiltIn(f.customDefinition);

        // True when the two lists hold AT MOST ONE distinct user-custom definition between them; false once a
        // second appears, which is the case that ties CanonicalRank. Definitions are compared by reference, the
        // same identity SameChannel pairs on.
        private static bool AtMostOneUserCustomDefinition(List<FilterFunction>? from, List<FilterFunction>? to)
        {
            FilterFunctionDefinition? definition = null;
            return ScanUserCustoms(from, ref definition) && ScanUserCustoms(to, ref definition);
        }

        private static bool ScanUserCustoms(List<FilterFunction>? list, ref FilterFunctionDefinition? definition)
        {
            if (list == null)
            {
                return true;
            }
            foreach (var f in list)
            {
                if (!IsUserCustom(f))
                {
                    continue;
                }
                if (definition == null)
                {
                    definition = f.customDefinition;
                }
                else if (!ReferenceEquals(definition, f.customDefinition))
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

        private static bool SameChannelSequence(List<FilterFunction>? a, List<FilterFunction>? b)
        {
            var ac = a?.Count ?? 0;
            var bc = b?.Count ?? 0;
            if (ac != bc)
            {
                return false;
            }
            for (var k = 0; k < ac; k++)
            {
                if (!SameChannel(a![k], b![k]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasRepeatedChannel(List<FilterFunction>? list)
        {
            if (list == null || list.Count < 2)
            {
                return false;
            }
            for (var i = 0; i < list.Count; i++)
            {
                for (var k = i + 1; k < list.Count; k++)
                {
                    if (SameChannel(list[i], list[k]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Canonical composition order of the filter channels (mirrors s_filterOrder in the resolver): the two
        // first-party customs slot by their definition — brightness right after blur, saturate right after
        // invert — so an add/remove merge keeps the same order the resolver composes in. A user custom ranks
        // last, matching the resolver composing every user custom after the built-ins; the caller admits at
        // most one distinct user custom precisely because they all share that one rank.
        private static int CanonicalRank(FilterFunction f)
        {
            if (f.type == FilterFunctionType.Custom)
            {
                return BuiltInFilterDefinitions.IsBrightness(f.customDefinition) ? 1
                    : BuiltInFilterDefinitions.IsSaturate(f.customDefinition) ? 6
                    : 8;
            }
            return f.type switch
            {
                FilterFunctionType.Blur => 0,
                FilterFunctionType.Contrast => 2,
                FilterFunctionType.Grayscale => 3,
                FilterFunctionType.HueRotate => 4,
                FilterFunctionType.Invert => 5,
                FilterFunctionType.Sepia => 7,
                _ => 8,
            };
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

        #region Easing

        // Maps the five easing curves Velvet's .ease-* utilities expose onto their standard CSS cubic-bezier
        // control points and evaluates them; any curve not exposed by a Velvet utility (only reachable via a
        // hand-authored resolvedStyle) falls back to linear.
        private static float Ease(EasingMode mode, float t)
        {
            t = Mathf.Clamp01(t);
            return mode switch
            {
                EasingMode.Linear => t,
                EasingMode.EaseIn => CubicBezierEvaluator.Evaluate(0.42f, 0f, 1f, 1f, t),
                EasingMode.EaseOut => CubicBezierEvaluator.Evaluate(0f, 0f, 0.58f, 1f, t),
                EasingMode.EaseInOut => CubicBezierEvaluator.Evaluate(0.42f, 0f, 0.58f, 1f, t),
                EasingMode.Ease => CubicBezierEvaluator.Evaluate(0.25f, 0.1f, 0.25f, 1f, t),
                _ => t,
            };
        }

        #endregion
    }
}
