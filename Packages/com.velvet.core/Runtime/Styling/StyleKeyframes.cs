#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// One stop of a keyframe animation registered with <see cref="VelvetKeyframes"/>: the values the animation holds
    /// at <see cref="Offset"/> (0 to 1 through the loop). A property a stop leaves null takes no value there; a property
    /// no stop names is not animated, and one that is named between stops starts and ends on the element's own value
    /// where no stop at 0 or 1 names it.
    /// </summary>
    public readonly struct KeyframeStop
    {
        /// <summary>Where in the loop the stop sits, from 0 to 1.</summary>
        public float Offset { get; }

        /// <summary>The opacity, replacing the element's own.</summary>
        public float? Opacity { get; init; }

        /// <summary>Pixels added to the element's own translate along x.</summary>
        public float? TranslateXPx { get; init; }

        /// <summary>Pixels added to the element's own translate along y.</summary>
        public float? TranslateYPx { get; init; }

        /// <summary>The factor the element's own scale is multiplied by.</summary>
        public float? Scale { get; init; }

        /// <summary>Degrees added to the element's own rotation.</summary>
        public float? RotateDeg { get; init; }

        /// <summary>
        /// The curve from this stop to the next, as a CSS keyframe's <c>animation-timing-function</c> applies to the
        /// interval it starts. Null takes the animation's own.
        /// </summary>
        public BezierCurve? Ease { get; init; }

        public KeyframeStop(float offset)
        {
            Offset = offset;
            Opacity = null;
            TranslateXPx = null;
            TranslateYPx = null;
            Scale = null;
            RotateDeg = null;
            Ease = null;
        }
    }

    /// <summary>
    /// The registry behind <c>animate-&lt;name&gt;</c> and <c>animate-[&lt;name&gt;_&lt;time&gt;_&lt;timing&gt;_infinite]</c>:
    /// keyframe animations, as CSS <c>@keyframes</c> declares them, which an element loops through the same driver as
    /// <c>animate-pulse</c> and its siblings. Register before the first render that uses the name.
    /// </summary>
    public static class VelvetKeyframes
    {
        private static readonly Dictionary<string, KeyframeSet> s_sets = new(StringComparer.Ordinal);

        /// <summary>
        /// Registers <paramref name="name"/> for <c>animate-name</c>, looping over <paramref name="durationSec"/> on
        /// <paramref name="timing"/> (CSS <c>ease</c> when null), replacing an earlier registration of the name.
        /// </summary>
        /// <exception cref="ArgumentException">The name is empty, spelled like a built-in or holds a character a class
        /// cannot, the stops are none or out of range, or the duration is not positive.</exception>
        public static void Register(string name, IReadOnlyList<KeyframeStop> stops, float durationSec = 1f,
            BezierCurve? timing = null)
        {
            var set = KeyframeSet.Build(name, stops, durationSec, timing ?? KeyframeSet.Ease);
            lock (s_sets)
            {
                s_sets[name] = set;
            }
        }

        /// <summary>Removes a registration; true where there was one.</summary>
        public static bool Unregister(string name)
        {
            lock (s_sets)
            {
                return s_sets.Remove(name);
            }
        }

        internal static bool TryGet(string name, out KeyframeSet set)
        {
            lock (s_sets)
            {
                return s_sets.TryGetValue(name, out set!);
            }
        }
    }

    // One registered animation, resolved for the driver.
    internal sealed class KeyframeSet
    {
        internal static readonly BezierCurve Ease = new(0.25f, 0.1f, 0.25f, 1f);

        private static readonly string[] s_builtIns =
        {
            "none", "gradient", "shimmer", "hue", "pulse", "spin", "ping", "bounce",
        };

        public string Name { get; }
        public float DefaultDurationSec { get; }
        public BezierCurve DefaultTiming { get; }
        public KeyframeTrack? Opacity { get; }
        public KeyframeTrack? TranslateX { get; }
        public KeyframeTrack? TranslateY { get; }
        public KeyframeTrack? Scale { get; }
        public KeyframeTrack? Rotate { get; }

        public MotionTransitionSlots Slots
            => (Opacity != null ? MotionTransitionSlots.Opacity : MotionTransitionSlots.None)
                | (TranslateX != null || TranslateY != null ? MotionTransitionSlots.Translate : MotionTransitionSlots.None)
                | (Scale != null ? MotionTransitionSlots.Scale : MotionTransitionSlots.None)
                | (Rotate != null ? MotionTransitionSlots.Rotate : MotionTransitionSlots.None);

        private KeyframeSet(string name, float durationSec, BezierCurve timing, IReadOnlyList<KeyframeStop> stops)
        {
            Name = name;
            DefaultDurationSec = durationSec;
            DefaultTiming = timing;
            Opacity = KeyframeTrack.Of(stops, static s => s.Opacity);
            TranslateX = KeyframeTrack.Of(stops, static s => s.TranslateXPx);
            TranslateY = KeyframeTrack.Of(stops, static s => s.TranslateYPx);
            Scale = KeyframeTrack.Of(stops, static s => s.Scale);
            Rotate = KeyframeTrack.Of(stops, static s => s.RotateDeg);
        }

        public static KeyframeSet Build(string name, IReadOnlyList<KeyframeStop> stops, float durationSec, BezierCurve timing)
        {
            Validate(name, stops, durationSec);
            return new KeyframeSet(name, durationSec, timing, stops);
        }

        private static void Validate(string name, IReadOnlyList<KeyframeStop> stops, float durationSec)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(s_invalid) >= 0 || Array.IndexOf(s_builtIns, name) >= 0)
            {
                throw new ArgumentException($"'{name}' is not a name a keyframe animation can take.", nameof(name));
            }
            if (stops == null || stops.Count == 0)
            {
                throw new ArgumentException("A keyframe animation needs at least one stop.", nameof(stops));
            }
            foreach (var stop in stops)
            {
                if (!(stop.Offset >= 0f && stop.Offset <= 1f))
                {
                    throw new ArgumentException($"A stop's offset must be from 0 to 1, not {stop.Offset}.", nameof(stops));
                }
            }
            if (!(durationSec > 0f) || float.IsInfinity(durationSec))
            {
                throw new ArgumentException("The duration must be positive.", nameof(durationSec));
            }
        }

        private static readonly char[] s_invalid = { ' ', '_', '[', ']', '(', ')', ',', '!', ':' };

        // The curve a CSS timing-function keyword or cubic-bezier() names.
        public static bool TryParseTiming(string token, out BezierCurve curve)
        {
            curve = default;
            switch (token)
            {
                case "linear": curve = new BezierCurve(0f, 0f, 1f, 1f); return true;
                case "ease": curve = Ease; return true;
                case "ease-in": curve = new BezierCurve(0.42f, 0f, 1f, 1f); return true;
                case "ease-out": curve = new BezierCurve(0f, 0f, 0.58f, 1f); return true;
                case "ease-in-out": curve = new BezierCurve(0.42f, 0f, 0.58f, 1f); return true;
            }
            return TryParseCubicBezier(token, out curve);
        }

        private static bool TryParseCubicBezier(string token, out BezierCurve curve)
        {
            curve = default;
            const string open = "cubic-bezier(";
            if (!token.StartsWith(open, StringComparison.Ordinal) || !token.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }
            var parts = token.Substring(open.Length, token.Length - open.Length - 1).Split(',');
            if (parts.Length != 4)
            {
                return false;
            }
            var v = new float[4];
            for (var i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])
                    || float.IsNaN(v[i]) || float.IsInfinity(v[i]))
                {
                    return false;
                }
            }
            if (v[0] < 0f || v[0] > 1f || v[2] < 0f || v[2] > 1f)
            {
                return false;
            }
            curve = new BezierCurve(v[0], v[1], v[2], v[3]);
            return true;
        }
    }

    // The stops of a registered animation that name one property, in offset order.
    internal sealed class KeyframeTrack
    {
        private readonly List<(float Offset, float Value, BezierCurve? Ease)> _points;

        private KeyframeTrack(List<(float, float, BezierCurve?)> points) => _points = points;

        public static KeyframeTrack? Of(IReadOnlyList<KeyframeStop> stops, Func<KeyframeStop, float?> read)
        {
            var points = new List<(float, float, BezierCurve?)>();
            foreach (var stop in stops)
            {
                if (read(stop) is { } value) points.Add((stop.Offset, value, stop.Ease));
            }
            var ordered = points.Select(static (p, i) => (p, i)).OrderBy(static x => x.p.Item1).ThenBy(static x => x.i)
                .Select(static x => x.p).ToList();
            return ordered.Count == 0 ? null : new KeyframeTrack(ordered);
        }

        // The value at loop position t. Where no stop sits at 0 or 1 the property starts and ends on
        // implicitStart and implicitEnd, which are the element's own.
        public float Eval(float t, float implicitStart, float implicitEnd, BezierCurve timing)
        {
            var lead = _points[0].Offset > 0f ? 1 : 0;
            var count = _points.Count + lead + (_points[_points.Count - 1].Offset < 1f ? 1 : 0);
            if (count == 1)
            {
                return _points[0].Value;
            }
            for (var i = 0; i < count - 1; i++)
            {
                var (aOffset, aValue, aEase) = Point(i, lead, implicitStart, implicitEnd);
                var (bOffset, bValue, _) = Point(i + 1, lead, implicitStart, implicitEnd);
                if (t < bOffset || i == count - 2)
                {
                    var span = bOffset - aOffset;
                    var local = span > 0f ? Mathf.Clamp01((t - aOffset) / span) : 1f;
                    var curve = aEase ?? timing;
                    return Mathf.LerpUnclamped(aValue, bValue, CubicBezierEvaluator.Evaluate(curve.X1, curve.Y1, curve.X2, curve.Y2, local));
                }
            }
            return _points[0].Value;
        }

        // The k-th of the track's stops with the implicit start before the first and the implicit end after the last.
        private (float Offset, float Value, BezierCurve? Ease) Point(int k, int lead, float implicitStart, float implicitEnd)
        {
            if (lead == 1 && k == 0) return (0f, implicitStart, null);
            var index = k - lead;
            return index >= _points.Count ? (1f, implicitEnd, null) : _points[index];
        }
    }
}
