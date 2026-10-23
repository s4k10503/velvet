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

        /// <summary>Completes when the animation finishes, and is canceled if it is stopped first (Framer's <c>then</c>).</summary>
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
            _done.TrySetCanceled();
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
            if (_timeSec < _endSec)
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

        /// <summary>Animates <paramref name="target"/> through <paramref name="values"/> on <paramref name="transition"/> (0.3 s when null).</summary>
        public AnimateControls Animate(VisualElement target, AnimateValues values, StyleTransitionConfig? transition = null)
            => Animate(new[] { new AnimateSegment(target, values, transition) });

        /// <summary>
        /// Animates the elements <paramref name="selector"/> names inside the scope (<c>.class</c>, <c>#name</c>, a type name,
        /// compounds of them, descendants joined by a space, groups joined by a comma). Throws when the scope is not mounted.
        /// </summary>
        public AnimateControls Animate(string selector, AnimateValues values, StyleTransitionConfig? transition = null)
            => Animate(new[] { new AnimateSegment(selector, values, transition) });

        /// <summary>
        /// Plays <paramref name="sequence"/>: each segment starts where its <see cref="AnimateAt"/> puts it, so segments
        /// overlap. The default starts it when the previous one ends.
        /// </summary>
        public AnimateControls Animate(IReadOnlyList<AnimateSegment> sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            var tracks = new List<ScopedTrack>();
            var end = ScopedTimeline.Place(sequence, ElementsOf, tracks);
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
        private const float DefaultDurationSec = 0.3f;

        // Appends each segment's tracks and returns where the timeline ends.
        public static double Place(IReadOnlyList<AnimateSegment> sequence, Func<AnimateSegment, IReadOnlyList<VisualElement>> elementsOf,
            List<ScopedTrack> tracks)
        {
            double previousStart = 0, previousEnd = 0, end = 0;
            foreach (var segment in sequence)
            {
                var start = StartOf(segment.At, previousStart, previousEnd);
                var first = tracks.Count;
                foreach (var element in elementsOf(segment))
                {
                    AddTracks(tracks, element, segment, start + (segment.Transition?.DelaySec ?? 0f));
                }
                var segmentEnd = start;
                for (var i = first; i < tracks.Count; i++) segmentEnd = Math.Max(segmentEnd, tracks[i].EndSec);
                (previousStart, previousEnd, end) = (start, segmentEnd, Math.Max(end, segmentEnd));
            }
            return end;
        }

        private static double StartOf(AnimateAt at, double previousStart, double previousEnd) => at.Kind switch
        {
            AnimateAt.Placement.Absolute => Math.Max(0.0, at.Seconds),
            AnimateAt.Placement.WithPrevious => previousStart,
            AnimateAt.Placement.Offset => Math.Max(0.0, previousEnd + at.Seconds),
            _ => previousEnd,
        };

        private static void AddTracks(List<ScopedTrack> tracks, VisualElement element, AnimateSegment segment, double start)
        {
            var values = segment.Values;
            Add(tracks, element, AnimateChannel.Opacity, values.Opacity, segment.Transition, start);
            Add(tracks, element, AnimateChannel.X, values.X, segment.Transition, start);
            Add(tracks, element, AnimateChannel.Y, values.Y, segment.Transition, start);
            Add(tracks, element, AnimateChannel.Scale, values.Scale, segment.Transition, start);
            Add(tracks, element, AnimateChannel.Rotate, values.RotateDeg, segment.Transition, start);
        }

        private static void Add(List<ScopedTrack> tracks, VisualElement element, AnimateChannel channel, float[]? keys,
            StyleTransitionConfig? transition, double start)
        {
            if (keys is { Length: > 0 })
            {
                tracks.Add(new ScopedTrack(element, channel, keys, transition, start, DefaultDurationSec));
            }
        }
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
        private const float MaxSpringSec = 20f;

        private readonly VisualElement _element;
        private readonly AnimateChannel _channel;
        private readonly StyleTransitionConfig? _transition;
        private readonly double _startSec;
        private float[] _keys;
        private readonly float _durationSec;
        private bool _begun;

        public ScopedTrack(VisualElement element, AnimateChannel channel, float[] keys, StyleTransitionConfig? transition,
            double startSec, float defaultDurationSec)
        {
            (_element, _channel, _keys, _transition, _startSec) = (element, channel, keys, transition, startSec);
            _durationSec = DurationOf(keys, transition, defaultDurationSec);
        }

        public double EndSec => _startSec + _durationSec;

        private bool IsSpring => _transition is { Type: TransitionType.Spring } && _keys.Length <= 2;

        // A spring runs until it rests, a tween for its duration.
        private static float DurationOf(float[] keys, StyleTransitionConfig? transition, float defaultSec)
        {
            if (transition == null)
            {
                return defaultSec;
            }
            if (transition.Type != TransitionType.Spring || keys.Length > 2)
            {
                return Mathf.Max(0f, transition.DurationSec);
            }
            return transition.SpringSettleSec is { } settle
                ? (float)settle
                : Mathf.Min(MotionSpringDriver.PassDurationSec(100f, transition.Stiffness, transition.Damping, transition.Mass), MaxSpringSec);
        }

        private float _initial;

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
            if (local >= _durationSec)
            {
                return _keys[_keys.Length - 1];
            }
            if (IsSpring)
            {
                var spring = _transition!;
                return MotionSpringDriver.SampleSpring(_keys[0], _keys[1], 1f, 0f, local,
                    (spring.Stiffness, spring.Damping, spring.Mass), spring.SpringSettleSec);
            }
            var progress = Ease(_durationSec > 0f ? (float)(local / _durationSec) : 1f);
            var position = progress * (_keys.Length - 1);
            var index = Mathf.Min(_keys.Length - 2, Mathf.FloorToInt(position));
            return Mathf.LerpUnclamped(_keys[index], _keys[index + 1], position - index);
        }

        private float Ease(float t)
        {
            if (_transition is { Type: TransitionType.Bezier } bezier)
            {
                return CubicBezierEvaluator.Evaluate(bezier.BezierX1, bezier.BezierY1, bezier.BezierX2, bezier.BezierY2, t);
            }
            return UssEasing.Evaluate(_transition?.Easing ?? EasingMode.EaseOut, t);
        }

        private float Read()
        {
            var shown = _element.resolvedStyle;
            return _channel switch
            {
                AnimateChannel.Opacity => shown.opacity,
                AnimateChannel.X => ScopedPose.Of(_element).X,
                AnimateChannel.Y => ScopedPose.Of(_element).Y,
                AnimateChannel.Scale => shown.scale.value.x,
                _ => shown.rotate.angle.ToDegrees(),
            };
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
                    _element.style.scale = new Scale(new Vector2(value, value));
                    break;
                default:
                    _element.style.rotate = new Rotate(Angle.Degrees(value));
                    break;
            }
        }

        private void WriteTranslate(float value)
        {
            var pose = ScopedPose.Of(_element);
            if (_channel == AnimateChannel.X) pose.X = value;
            else pose.Y = value;
            SeatedTranslate.Write(_element, new Translate(new Length(pose.X), new Length(pose.Y)));
        }
    }
}
