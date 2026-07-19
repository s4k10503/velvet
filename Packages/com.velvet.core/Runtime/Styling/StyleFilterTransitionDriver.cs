using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-element state for a filter-* transition (opt-in via the transition-filter class). Holds the
    // one-shot tick that lerps the inline filter's parameters from the current applied value to the newly
    // composed one, the precomputed aligned interpolation slots, and the exact static list to settle to.
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
    // uses for animate-hue. UI Toolkit cannot CSS-transition the inline filter — a [filter] transition-property
    // stops it repainting the filter after the first frame — so the transition-filter utility deliberately sets
    // only duration + timing and this driver lerps the filter parameters itself, writing a fresh inline list per
    // frame (same repaint-dirtying reason as the Hue arm of StyleAnimateDriver).
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
            public readonly FilterParameter[] From;
            public readonly FilterParameter[] To;

            public Channel(FilterFunctionType type, FilterParameter[] from, FilterParameter[] to)
            {
                Type = type;
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
        // resolver perform its instant write. Everything a non-opting element pays is the first line's lookup.
        internal static bool TryStartOrRedirect(VisualElement element, List<FilterFunction>? to)
        {
            // A binding exists only while the reconciler sees transition-filter on the element, so its presence
            // IS the opt-in gate — a non-opting element pays only this lookup.
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
            var easing = element.resolvedStyle.transitionTimingFunction.FirstOrDefault().mode;

            // Read the CURRENT applied list as the from-side. During an in-flight tween this is last frame's
            // interpolated list, so a redirect starts from where the eye is — not the tween's original start.
            var from = element.style.filter.value;
            if (!TryBuildChannels(from, to, out var channels))
            {
                // Non-interpolable (a custom filter, or an ambiguous add/remove) → discrete instant write.
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
                var fn = new FilterFunction(channel.Type);
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
                if (progress >= 1f)
                {
                    // Settle to the EXACT composed static list so the tween lands on the resolver's own value.
                    if (b.Target != null)
                    {
                        element.style.filter = b.Target;
                    }
                    else
                    {
                        element.style.filter = StyleKeyword.Null;
                    }
                    b.Scheduled?.Pause();
                    b.Scheduled = null;
                    return;
                }
                ApplyFrame(element, b, progress);
            }).Every(StyleAnimateDriver.TickMs);
        }

        // Pauses + drops the tick, keeping the binding registered (idle). Used when a change resolves to an
        // instant write (off-panel / zero-duration / non-interpolable) and by Detach.
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
                if (b.Target != null)
                {
                    element.style.filter = b.Target;
                }
                else
                {
                    element.style.filter = StyleKeyword.Null;
                }
            }
            Cancel(b);
            Unregister(element);
        }

        #region Channel alignment

        // Builds the aligned interpolation slots for from→to (both always in canonical filter order). Returns
        // false — meaning "not interpolable, write instantly" — for a custom filter or an ambiguous add/remove
        // (a UITK filter type that appears more than once, e.g. grayscale-* + saturate- both rendering as
        // Grayscale, which cannot be paired by type alone). A null from is treated as an empty list (a
        // freshly-mounted element with no inline filter reads null, not []).
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
            // A custom filter binds opaque shader material state; a numeric cross-fade is meaningless.
            if (ContainsCustom(from) || ContainsCustom(to))
            {
                return false;
            }

            // Fast path: identical type sequence — the common case (a value change on the same filter set).
            if (SameTypeSequence(from, to))
            {
                var paired = new Channel[fromCount];
                for (var k = 0; k < fromCount; k++)
                {
                    var f = from![k];
                    var t = to![k];
                    if (f.parameterCount != t.parameterCount)
                    {
                        return false;
                    }
                    paired[k] = new Channel(f.type, Snapshot(f), Snapshot(t));
                }
                channels = paired;
                return true;
            }

            // Different sequences: a filter was added or removed. Pairing by type is only unambiguous when each
            // UITK type occurs at most once per list; otherwise (a repeated type) fall back to an instant write.
            if (HasRepeatedType(from) || HasRepeatedType(to))
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
                    if (f.type == t.type)
                    {
                        if (f.parameterCount != t.parameterCount)
                        {
                            return false;
                        }
                        merged.Add(new Channel(f.type, Snapshot(f), Snapshot(t)));
                        i++;
                        j++;
                    }
                    else if (CanonicalRank(f.type) < CanonicalRank(t.type))
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
        private static Channel FadeIn(FilterFunction f) => new Channel(f.type, IdentityParams(f), Snapshot(f));
        private static Channel FadeOut(FilterFunction f) => new Channel(f.type, Snapshot(f), IdentityParams(f));

        private static bool ContainsCustom(List<FilterFunction>? list)
        {
            if (list == null)
            {
                return false;
            }
            foreach (var f in list)
            {
                if (f.type == FilterFunctionType.Custom)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameTypeSequence(List<FilterFunction>? a, List<FilterFunction>? b)
        {
            var ac = a?.Count ?? 0;
            var bc = b?.Count ?? 0;
            if (ac != bc)
            {
                return false;
            }
            for (var k = 0; k < ac; k++)
            {
                if (a![k].type != b![k].type)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasRepeatedType(List<FilterFunction>? list)
        {
            if (list == null || list.Count < 2)
            {
                return false;
            }
            for (var i = 0; i < list.Count; i++)
            {
                for (var k = i + 1; k < list.Count; k++)
                {
                    if (list[i].type == list[k].type)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Canonical composition order of the UITK filter types (mirrors s_filterOrder in the resolver). Only
        // consulted after repeated types are ruled out, so Grayscale mapping to one rank is unambiguous.
        private static int CanonicalRank(FilterFunctionType type) => type switch
        {
            FilterFunctionType.Blur => 0,
            FilterFunctionType.Tint => 1,
            FilterFunctionType.Contrast => 2,
            FilterFunctionType.Grayscale => 3,
            FilterFunctionType.HueRotate => 4,
            FilterFunctionType.Invert => 5,
            FilterFunctionType.Sepia => 6,
            _ => 7,
        };

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

        // The neutral parameters for a filter type — the value at which it is a no-op. Contrast is 1 (identity
        // multiply); every other float filter (blur, grayscale/saturate, hue-rotate, invert, sepia) is off at
        // 0; Tint (brightness) multiplies by a color, so its identity is white.
        private static FilterParameter[] IdentityParams(FilterFunction f)
        {
            var count = f.parameterCount;
            var arr = new FilterParameter[count];
            for (var k = 0; k < count; k++)
            {
                var p = f.GetParameter(k);
                arr[k] = p.type == FilterParameterType.Color
                    ? new FilterParameter(Color.white)
                    : new FilterParameter(f.type == FilterFunctionType.Contrast ? 1f : 0f);
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
                EasingMode.EaseIn => CubicBezier(0.42f, 0f, 1f, 1f, t),
                EasingMode.EaseOut => CubicBezier(0f, 0f, 0.58f, 1f, t),
                EasingMode.EaseInOut => CubicBezier(0.42f, 0f, 0.58f, 1f, t),
                EasingMode.Ease => CubicBezier(0.25f, 0.1f, 0.25f, 1f, t),
                _ => t,
            };
        }

        // Standard CSS cubic-bezier easing: solve X(s)=x for the curve parameter s (Newton on the eased time
        // axis, control points 0,x1,x2,1), then evaluate Y(s) (control points 0,y1,y2,1). A small, deliberately
        // approximate solver — exact arbitrary-bezier easing is out of scope here.
        private static float CubicBezier(float x1, float y1, float x2, float y2, float x)
        {
            var s = x;
            for (var iter = 0; iter < 6; iter++)
            {
                var dx = BezierAxis(x1, x2, s) - x;
                if (Mathf.Abs(dx) < 1e-5f)
                {
                    break;
                }
                var slope = BezierAxisDerivative(x1, x2, s);
                if (Mathf.Abs(slope) < 1e-6f)
                {
                    break;
                }
                s -= dx / slope;
            }
            s = Mathf.Clamp01(s);
            return BezierAxis(y1, y2, s);
        }

        // Cubic bezier along one axis with fixed endpoints 0 and 1; c1, c2 are the two inner control coords.
        private static float BezierAxis(float c1, float c2, float s)
        {
            var u = 1f - s;
            return (3f * u * u * s * c1) + (3f * u * s * s * c2) + (s * s * s);
        }

        private static float BezierAxisDerivative(float c1, float c2, float s)
        {
            var u = 1f - s;
            return (3f * u * u * c1) + (6f * u * s * (c2 - c1)) + (3f * s * s * (1f - c2));
        }

        #endregion
    }
}
