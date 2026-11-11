#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The values <see cref="Animator.Animate(VisualElement, AnimateValues, StyleTransitionConfig)"/> animates, each an
    /// array of keyframes as Framer Motion's <c>animate</c> takes them: two or more run through in turn over the
    /// transition, and one is a value to animate to from the element's current one. A property left null is left alone.
    /// </summary>
    public readonly struct AnimateValues
    {
        /// <summary>Opacity.</summary>
        public float[]? Opacity { get; init; }

        /// <summary>Translate along x, in pixels (Framer's <c>x</c>).</summary>
        public float[]? X { get; init; }

        /// <summary>Translate along y, in pixels (Framer's <c>y</c>).</summary>
        public float[]? Y { get; init; }

        /// <summary>A uniform scale factor.</summary>
        public float[]? Scale { get; init; }

        /// <summary>Rotation in degrees.</summary>
        public float[]? RotateDeg { get; init; }
    }

    /// <summary>
    /// Where a segment of a sequence starts, as Framer Motion's <c>at</c>: after the previous segment ends (the default),
    /// at a time on the sequence's timeline, with the previous segment (<c>"&lt;"</c>), or an offset from the previous
    /// segment's end (<c>"+0.5"</c>, <c>"-0.2"</c>).
    /// </summary>
    public readonly struct AnimateAt
    {
        internal enum Placement
        {
            AfterPrevious,
            Absolute,
            WithPrevious,
            Offset,
        }

        internal Placement Kind { get; }
        internal float Seconds { get; }

        private AnimateAt(Placement kind, float seconds)
        {
            Kind = kind;
            Seconds = seconds;
        }

        /// <summary>A time in seconds on the sequence's timeline.</summary>
        public static AnimateAt Time(float seconds) => new(Placement.Absolute, seconds);

        /// <summary>The start of the previous segment.</summary>
        public static AnimateAt WithPrevious => new(Placement.WithPrevious, 0f);

        /// <summary>An offset in seconds from the end of the previous segment; negative overlaps it.</summary>
        public static AnimateAt Offset(float seconds) => new(Placement.Offset, seconds);
    }

    /// <summary>One entry of a sequence given to <see cref="Animator.Animate(IReadOnlyList{AnimateSegment})"/>.</summary>
    public readonly struct AnimateSegment
    {
        internal VisualElement? Element { get; }
        internal string? Selector { get; }
        internal AnimateValues Values { get; }
        internal StyleTransitionConfig? Transition { get; }
        internal AnimateAt At { get; }

        /// <summary>A segment animating the elements <paramref name="selector"/> names inside the scope.</summary>
        public AnimateSegment(string selector, AnimateValues values, StyleTransitionConfig? transition = null, AnimateAt at = default)
        {
            Element = null;
            Selector = selector ?? throw new ArgumentNullException(nameof(selector));
            Values = values;
            Transition = transition;
            At = at;
        }

        /// <summary>A segment animating <paramref name="element"/>.</summary>
        public AnimateSegment(VisualElement element, AnimateValues values, StyleTransitionConfig? transition = null, AnimateAt at = default)
        {
            Element = element ?? throw new ArgumentNullException(nameof(element));
            Selector = null;
            Values = values;
            Transition = transition;
            At = at;
        }
    }

    /// <summary>
    /// A running animation or sequence from <see cref="Animator"/>: Framer Motion's animation controls.
    /// </summary>
    public sealed class AnimateControls
    {
        private readonly List<ScopedTrack> _tracks;
        private readonly double _endSec;
        private readonly VelvetTaskCompletionSource _done = new();
        private readonly List<Action> _thens = new();
        private double _timeSec;
        private float _speed = 1f;

        internal AnimateControls(List<ScopedTrack> tracks, double endSec)
        {
            _tracks = tracks;
            _endSec = endSec;
        }

        /// <summary>The rate time passes at: 1 by default, 0 holds the animation as a pause does. A negative, NaN or infinite value throws.</summary>
        public float Speed
        {
            get => _speed;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "A speed is a finite number of zero or more.");
                }
                _speed = value;
            }
        }

        /// <summary>Whether <see cref="Pause"/> holds it.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Seconds into the timeline.</summary>
        public float TimeSec => (float)_timeSec;

        /// <summary>True once every value has reached its last keyframe.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>True once <see cref="Stop"/> or <see cref="Cancel"/> ended it, or its scope unmounted.</summary>
        public bool IsStopped { get; private set; }

        /// <summary>
        /// Completes when the animation finishes (Framer's <c>finished</c>). It never settles once <see cref="Stop"/> or
        /// <see cref="Cancel"/> ends the animation first, as Framer's promise does not.
        /// </summary>
        public VelvetTask Finished => _done.Task;

        /// <summary>Runs <paramref name="callback"/> when the animation finishes, at once if it has.</summary>
        public void Then(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (IsFinished) callback();
            else if (!IsStopped) _thens.Add(callback);
        }

        /// <summary>Resumes after <see cref="Pause"/>.</summary>
        public void Play() => IsPaused = false;

        /// <summary>Holds the animation where it is.</summary>
        public void Pause() => IsPaused = true;

        /// <summary>Ends the animation where it is, leaving each value at what it shows.</summary>
        public void Stop() => End(restore: false);

        /// <summary>Ends the animation and puts each value it began back to where it started.</summary>
        public void Cancel() => End(restore: true);

        private void End(bool restore)
        {
            if (IsFinished || IsStopped)
            {
                return;
            }
            IsStopped = true;
            if (restore)
            {
                foreach (var track in _tracks) track.Restore();
            }
            _thens.Clear();
        }

        // Moves time on by dt seconds at the current speed and writes each value; true once nothing is left to do.
        internal bool Tick(float dt)
        {
            if (IsFinished || IsStopped)
            {
                return true;
            }
            if (!IsPaused)
            {
                _timeSec = Math.Min(_endSec, _timeSec + dt * _speed);
            }
            foreach (var track in _tracks) track.Sample(_timeSec);
            if (!_tracks.TrueForAll(track => track.HasEnded(_timeSec)))
            {
                return false;
            }
            IsFinished = true;
            _done.TrySetResult();
            var thens = _thens.ToArray();
            _thens.Clear();
            foreach (var then in thens) then();
            return true;
        }
    }

    /// <summary>
    /// What <see cref="Hooks.UseAnimate"/> returns for animating elements inside its scope, Framer Motion's
    /// <c>animate</c> of <c>useAnimate</c>. Animations are cancelled when the scope's component unmounts.
    /// </summary>
    public sealed class Animator
    {
        private readonly Ref<VisualElement> _scope;
        private readonly List<AnimateControls> _running = new();

        internal Animator(Ref<VisualElement> scope) => _scope = scope;

        internal Ref<VisualElement> Scope => _scope;

        /// <summary>
        /// Animates <paramref name="target"/> through <paramref name="values"/> on <paramref name="transition"/>. Where
        /// that is null or sets nothing but <see cref="StyleTransitionConfig.DelaySec"/>, the repeat options and the
        /// child-orchestration knobs, each value takes Framer Motion's default transition for it;
        /// <c>Documentation~/motion.md</c> lists them.
        /// </summary>
        public AnimateControls Animate(VisualElement target, AnimateValues values, StyleTransitionConfig? transition = null)
            => Play(new[] { new AnimateSegment(target, values, transition) }, ScopedTimeline.Kind.Single);

        /// <summary>
        /// Animates the elements <paramref name="selector"/> names inside the scope (<c>.class</c>, <c>#name</c>, a type name,
        /// compounds of them, descendants joined by a space, groups joined by a comma), on the transition
        /// <see cref="Animate(VisualElement, AnimateValues, StyleTransitionConfig)"/> takes. Throws when the scope is not
        /// mounted.
        /// </summary>
        public AnimateControls Animate(string selector, AnimateValues values, StyleTransitionConfig? transition = null)
            => Play(new[] { new AnimateSegment(selector, values, transition) }, ScopedTimeline.Kind.Single);

        /// <summary>
        /// Plays <paramref name="sequence"/>: each segment starts where its <see cref="AnimateAt"/> puts it, so segments
        /// overlap. The default starts it when the previous one ends. A segment with no transition runs 0.3 s on an
        /// ease-out, as a segment of Framer Motion's sequence does.
        /// </summary>
        public AnimateControls Animate(IReadOnlyList<AnimateSegment> sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            return Play(sequence, ScopedTimeline.Kind.Sequence);
        }

        private AnimateControls Play(IReadOnlyList<AnimateSegment> sequence, ScopedTimeline.Kind kind)
        {
            var tracks = new List<ScopedTrack>();
            var end = ScopedTimeline.Place(sequence, ElementsOf, tracks, kind);
            var controls = new AnimateControls(tracks, end);
            if (!controls.Tick(0f))
            {
                _running.Add(controls);
            }
            return controls;
        }

        private IReadOnlyList<VisualElement> ElementsOf(AnimateSegment segment)
        {
            if (segment.Element != null)
            {
                return new[] { segment.Element };
            }
            var scope = _scope.Current ?? throw new InvalidOperationException("The animate scope is not mounted: give its ref to an element.");
            return AnimateSelector.Resolve(scope, segment.Selector!);
        }

        internal void Tick(float dt)
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                if (_running[i].Tick(dt)) _running.RemoveAt(i);
            }
        }

        internal void StopAll()
        {
            foreach (var controls in _running.ToArray()) controls.Stop();
            _running.Clear();
        }
    }

    internal enum AnimateChannel
    {
        Opacity,
        X,
        Y,
        Scale,
        Rotate,
    }

    // Lays a sequence out on a timeline.
    internal static class ScopedTimeline
    {
        // Framer's animate() of one target and its sequence choose a missing transition, a spring and a repeat
        // differently.
        internal enum Kind
        {
            Single,
            Sequence,
        }

        // Appends each segment's tracks and returns where the timeline ends.
        public static double Place(IReadOnlyList<AnimateSegment> sequence, Func<AnimateSegment, IReadOnlyList<VisualElement>> elementsOf,
            List<ScopedTrack> tracks, Kind kind)
        {
            double previousStart = 0, previousEnd = 0, end = 0;
            foreach (var segment in sequence)
            {
                var start = StartOf(segment.At, previousStart, previousEnd);
                var first = tracks.Count;
                foreach (var element in elementsOf(segment))
                {
                    AddTracks(tracks, element, segment, start, kind);
                }
                var segmentEnd = start;
                for (var i = first; i < tracks.Count; i++) segmentEnd = Math.Max(segmentEnd, tracks[i].EndSec);
                (previousStart, previousEnd, end) = (start, segmentEnd, Math.Max(end, segmentEnd));
            }
            return end;
        }

#pragma warning disable CS8524 // no discard arm: a new placement has to say where its segment starts
        private static double StartOf(AnimateAt at, double previousStart, double previousEnd) => at.Kind switch
        {
            AnimateAt.Placement.Absolute => Math.Max(0.0, at.Seconds),
            AnimateAt.Placement.WithPrevious => previousStart,
            AnimateAt.Placement.Offset => Math.Max(0.0, previousEnd + at.Seconds),
            AnimateAt.Placement.AfterPrevious => previousEnd,
        };
#pragma warning restore CS8524

        private static void AddTracks(List<ScopedTrack> tracks, VisualElement element, AnimateSegment segment, double start,
            Kind kind)
        {
            var values = segment.Values;
            foreach (var (channel, keys) in new[]
            {
                (AnimateChannel.Opacity, values.Opacity),
                (AnimateChannel.X, values.X),
                (AnimateChannel.Y, values.Y),
                (AnimateChannel.Scale, values.Scale),
                (AnimateChannel.Rotate, values.RotateDeg),
            })
            {
                if (keys is { Length: > 0 })
                {
                    var transition = ValueTransition(segment.Transition, channel, kind);
                    tracks.Add(new ScopedTrack(element, channel, keys, transition, start + (transition?.DelaySec ?? 0f), kind));
                }
            }
        }

        // The transition one value takes where a PropertyOverrides entry names its property, the last such entry
        // winning: a single call takes the entry alone, as Framer's getValueTransition hands it, with the delay
        // OwnOrCallDelaySec gives it; a sequence lays the entry over the transition, as its own getValueTransition does.
        private static StyleTransitionConfig? ValueTransition(StyleTransitionConfig? transition, AnimateChannel channel,
            Kind kind)
        {
            var overrides = transition?.PropertyOverrides;
            for (var i = (overrides?.Count ?? 0) - 1; i >= 0; i--)
            {
                var entry = overrides![i];
                if (!DrivenTweenTiming.Named(entry.Property).Overlaps(StyleLonghandSet.Of(LonghandOf(channel))))
                {
                    continue;
                }
                return kind == ScopedTimeline.Kind.Single
                    ? s_undefined.With(entry.DurationSec, entry.Easing, delaySec: OwnOrCallDelaySec(entry, transition!))
                    : transition!.With(entry.DurationSec, entry.Easing, delaySec: entry.DelaySec);
            }
            return transition;
        }

        private static readonly StyleTransitionConfig s_undefined = new();

        // Framer's animateMotionValue: `valueTransition.delay || transition.delay`, so an entry's delay of 0, as one
        // naming none, takes the call's.
        private static float OwnOrCallDelaySec(StylePropertyTransition entry, StyleTransitionConfig transition)
            => (entry.DelaySec ?? 0f) != 0f ? entry.DelaySec!.Value : transition.DelaySec;

#pragma warning disable CS8524 // no discard arm: a new channel has to say which property names it
        private static StyleLonghand LonghandOf(AnimateChannel channel) => channel switch
        {
            AnimateChannel.Opacity => StyleLonghand.Opacity,
            AnimateChannel.X => StyleLonghand.Translate,
            AnimateChannel.Y => StyleLonghand.Translate,
            AnimateChannel.Scale => StyleLonghand.Scale,
            AnimateChannel.Rotate => StyleLonghand.Rotate,
        };
#pragma warning restore CS8524
    }

    // The translate an element's x and y tracks compose into one inline value.
    internal sealed class ScopedPose
    {
        private static readonly ConditionalWeakTable<VisualElement, ScopedPose> s_poses = new();

        public float X;
        public float Y;

        public static ScopedPose Of(VisualElement element) => s_poses.GetValue(element, static e =>
        {
            var shown = e.resolvedStyle.translate;
            return new ScopedPose { X = float.IsNaN(shown.x) ? 0f : shown.x, Y = float.IsNaN(shown.y) ? 0f : shown.y };
        });
    }

    // One value of one element over a stretch of the timeline.
    internal sealed class ScopedTrack
    {
        // Framer's transition for more than two keyframes given no transition.
        private const float DefaultKeyframesSec = 0.8f;

        // The travel a sequence measures a spring over where a segment gives one keyframe, its start unknown then.
        private const float UnknownSpringTravel = 100f;

        private const float MaxSpringSec = 20f;

        private readonly VisualElement _element;
        private readonly AnimateChannel _channel;
        private readonly double _startSec;
        private float[] _keys;
        private readonly Func<float, float> _ease;
        private readonly (float Stiffness, float Damping, float Mass)? _spring;
        private readonly double? _springSettleSec;
        private float? _springRestSpeed;
        // The travel the spring is measured over; NaN where that is the value's own.
        private readonly float _springTravel = float.NaN;
        private readonly float _passSec;
        private readonly MotionRepeat _repeat;
        private readonly double _totalSec;
        // A single call's opacity on the Web Animations API (OnWebAnimations) off a spring eases over the whole run,
        // between keyframes evenly spaced along it, as Framer hands one easing to element.animate(); off it, Framer's
        // keyframes generator eases each pair.
        private readonly bool _easesWholeRun;
        private bool _begun;
        private float _initial;

        public ScopedTrack(VisualElement element, AnimateChannel channel, float[] keys, StyleTransitionConfig? transition,
            double startSec, ScopedTimeline.Kind kind)
        {
            (_element, _channel, _keys, _startSec) = (element, channel, keys, startSec);
            _ease = EaseOut;
            var single = kind == ScopedTimeline.Kind.Single;
            var accelerated = OnWebAnimations(channel, transition, single);
            if (single && (transition == null || !transition.IsDefined))
            {
                _passSec = DefaultTransition(out _ease, out _spring);
            }
            else if (transition?.Type == TransitionType.Spring && (single || _keys.Length <= 2))
            {
                var spring = transition!;
                _spring = (spring.Stiffness, spring.Damping, spring.Mass);
                _springSettleSec = spring.SpringSettleSec;
                if (!single)
                {
                    _springTravel = _keys.Length == 2 ? Math.Abs(_keys[1] - _keys[0]) : UnknownSpringTravel;
                }
                _passSec = _springSettleSec is { } settle
                    ? (float)settle
                    : single
                        ? RestSec(spring.Stiffness, spring.Damping, spring.Mass, null)
                        : Mathf.Min(MotionSpringDriver.PassDurationSec(_springTravel, spring.Stiffness, spring.Damping,
                            spring.Mass), MaxSpringSec);
            }
            else
            {
                _passSec = Mathf.Max(0f, transition?.DurationSec ?? StyleTransitionConfig.DefaultDurationSec);
                if (transition is { Type: TransitionType.Bezier } bezier)
                {
                    var (x1, y1, x2, y2) = (bezier.BezierX1, bezier.BezierY1, bezier.BezierX2, bezier.BezierY2);
                    _ease = t => CubicBezierEvaluator.Evaluate(x1, y1, x2, y2, t);
                }
                else if (transition != null)
                {
                    var easing = transition.Easing;
                    _ease = t => MotionEasing.Evaluate(easing, t, accelerated);
                }
            }

            _repeat = RepeatOf(transition, single, _spring.HasValue);
            _totalSec = _repeat.Count > 0f ? _repeat.TotalSec(_passSec) : _passSec;
            _easesWholeRun = single && accelerated;
        }

        public double EndSec => _startSec + _totalSec;

        // Whether Framer runs the value on the Web Animations API (supportsBrowserAnimation): opacity, the one of the five
        // in accelerated-values.ts, unless a single call's repeat keeps it off (MotionRepeat.KeepsWebAnimations) or its
        // cubic-bezier has an x outside [0, 1], which the browser refuses and Framer then runs on its keyframes
        // generator. A sequence writes its repeats into its keyframes and leaves the animation none.
        private static bool OnWebAnimations(AnimateChannel channel, StyleTransitionConfig? transition, bool single)
        {
            if (channel != AnimateChannel.Opacity) return false;
            if (transition == null) return true;
            return (!single || MotionRepeat.Of(transition).KeepsWebAnimations)
                && (transition.Type != TransitionType.Bezier
                    || (Mathf.Clamp01(transition.BezierX1) == transition.BezierX1
                        && Mathf.Clamp01(transition.BezierX2) == transition.BezierX2));
        }

        private static float EaseOut(float t) => MotionEasing.Evaluate(EasingMode.EaseOut, t);

        private static float DefaultEase(float t) => CubicBezierEvaluator.Evaluate(MotionSpringDriver.DefaultEaseX1,
            MotionSpringDriver.DefaultEaseY1, MotionSpringDriver.DefaultEaseX2, MotionSpringDriver.DefaultEaseY2, t);

        // Framer's getDefaultTransition.
        private float DefaultTransition(out Func<float, float> ease, out (float Stiffness, float Damping, float Mass)? spring)
        {
            ease = EaseOut;
            spring = null;
            if (_keys.Length > 2)
            {
                return DefaultKeyframesSec;
            }
            switch (_channel)
            {
                case AnimateChannel.Opacity:
                    ease = DefaultEase;
                    return MotionSpringDriver.DefaultEaseSec;
                case AnimateChannel.Scale:
                    spring = (MotionSpringDriver.ScaleSpringStiffness, MotionSpringDriver.ScaleSpringDamping(_keys[^1]), 1f);
                    break;
                default:
                    spring = (MotionSpringDriver.TransformSpringStiffness, MotionSpringDriver.TransformSpringDamping, 1f);
                    break;
            }
            var (stiffness, damping, mass) = spring.Value;
            return RestSec(stiffness, damping, mass, MotionSpringDriver.DefaultSpringRestSpeed);
        }

        // Where a single animation's spring rests, measured up to 20 s; one resting later, or never, runs on until it
        // rests (HasEnded), as Framer's JSAnimation ends when its generator is done. A sequence's is cut at
        // MaxSpringSec, as the easing Framer samples from it is.
        private float RestSec(float stiffness, float damping, float mass, float? restSpeed)
        {
            _springRestSpeed = restSpeed;
            return MotionSpringDriver.PassDurationSec(OwnTravel(), stiffness, damping, mass, 0f, restSpeed);
        }

        // Whether the track has nothing left to show at timeSec on the timeline.
        public bool HasEnded(double timeSec)
        {
            var local = timeSec - _startSec;
            if (!float.IsPositiveInfinity(_passSec))
            {
                return local >= _totalSec;
            }
            if (!_begun || _spring == null)
            {
                return false;
            }
            return MotionSpringDriver.SpringRestsAt(_keys[0], _keys[^1], local, _spring.Value, _springRestSpeed);
        }

        // A single animation's spring runs over the value's own travel, so a lone keyframe's start is read when the
        // track is laid out rather than when it begins.
        private float OwnTravel()
        {
            if (_keys.Length == 1)
            {
                _keys = new[] { Read(), _keys[0] };
            }
            return Math.Abs(_keys[^1] - _keys[0]);
        }

        // A sequence plays a reverse pass of a curve its ease names as a mirrored one: it reverses the keyframes, and
        // reverses only an easing it built from a spring.
        private static MotionRepeat RepeatOf(StyleTransitionConfig? transition, bool single, bool spring)
        {
            if (transition == null || transition.Repeat <= 0f || (!single && transition.Repeat >= SequenceWalker.MaxSegmentRepeat))
            {
                return default;
            }
            var type = !single && !spring && transition.RepeatType == TransitionRepeatType.Reverse
                ? TransitionRepeatType.Mirror
                : transition.RepeatType;
            return new MotionRepeat(transition.Repeat, type, transition.RepeatDelaySec);
        }

        // Reads the element's current value where a single keyframe names only where to head.
        private void Begin()
        {
            _begun = true;
            if (_keys.Length == 1)
            {
                _keys = new[] { Read(), _keys[0] };
            }
            _initial = _keys[0];
        }

        public void Sample(double timeSec)
        {
            var local = timeSec - _startSec;
            if (local < 0.0)
            {
                return;
            }
            if (!_begun)
            {
                Begin();
            }
            Write(ValueAt(local));
        }

        public void Restore()
        {
            if (_begun)
            {
                Write(_initial);
            }
        }

        private float ValueAt(double local)
        {
            // A pass that never rests repeats nothing.
            if (_repeat.Count <= 0f || float.IsPositiveInfinity(_passSec))
            {
                return PassValue(Math.Min(local, _passSec), mirrored: false);
            }
            if (_totalSec == 0.0)
            {
                return _repeat.EndsAtFrom ? _keys[0] : _keys[^1];
            }
            var passTime = _repeat.PassTime(Math.Min(local, _totalSec), _passSec, out var mirrored);
            return PassValue(Math.Min(passTime, _passSec), mirrored);
        }

        // The value a pass shows `t` into it; a mirrored pass runs the keyframes from the last back to the first.
        private float PassValue(double t, bool mirrored)
        {
            var last = _keys.Length - 1;
            if (_spring is { } spring)
            {
                var (from, to) = mirrored ? (_keys[last], _keys[0]) : (_keys[0], _keys[last]);
                if (from == to)
                {
                    return to;
                }
                var scale = float.IsNaN(_springTravel) ? 1f : _springTravel / Math.Abs(to - from);
                return MotionSpringDriver.SampleSpring(from, to, scale, 0f, t, spring, _springSettleSec, _springRestSpeed);
            }
            if (last == 0)
            {
                return _keys[0];
            }
            var progress = _passSec > 0f ? Mathf.Clamp01((float)(t / _passSec)) : 1f;
            if (_easesWholeRun)
            {
                // An easing past either end carries the value on along the end pair.
                var along = _ease(progress) * last;
                var pair = Mathf.Clamp(Mathf.FloorToInt(along), 0, last - 1);
                return Mathf.LerpUnclamped(_keys[pair], _keys[pair + 1], along - pair);
            }
            var position = progress * last;
            var index = Mathf.Min(last - 1, Mathf.FloorToInt(position));
            var (a, b) = mirrored ? (_keys[last - index], _keys[last - index - 1]) : (_keys[index], _keys[index + 1]);
            return Mathf.LerpUnclamped(a, b, _ease(position - index));
        }

        private float Read()
        {
            var shown = _element.resolvedStyle;
#pragma warning disable CS8524 // no discard arm: a new channel has to say what it reads
            return _channel switch
            {
                AnimateChannel.Opacity => shown.opacity,
                AnimateChannel.X => ScopedPose.Of(_element).X,
                AnimateChannel.Y => ScopedPose.Of(_element).Y,
                AnimateChannel.Scale => shown.scale.value.x,
                AnimateChannel.Rotate => shown.rotate.angle.ToDegrees(),
            };
#pragma warning restore CS8524
        }

        private void Write(float value)
        {
            switch (_channel)
            {
                case AnimateChannel.Opacity:
                    MotionOpacity.Write(_element, value);
                    break;
                case AnimateChannel.X:
                case AnimateChannel.Y:
                    WriteTranslate(value);
                    break;
                case AnimateChannel.Scale:
                    AuthoredTransform.SetScale(_element, new Scale(new Vector2(value, value)));
                    break;
                default:
                    AuthoredTransform.SetRotate(_element, new Rotate(Angle.Degrees(value)));
                    break;
            }
        }

        private void WriteTranslate(float value)
        {
            var pose = ScopedPose.Of(_element);
            if (_channel == AnimateChannel.X) pose.X = value;
            else pose.Y = value;
            _element.style.translate = new Translate(new Length(pose.X), new Length(pose.Y));
        }
    }
}
