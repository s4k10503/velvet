using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// CSS-transition-based animation configuration for <c>V.Motion</c>. Drives a USS class swap
    /// (enter-from → enter-to / exit-from → exit-to); <see cref="DurationSec"/> / <see cref="Easing"/> are
    /// applied as inline styles. Use the presets on <see cref="StyleTransition"/>, optionally tuned via
    /// <see cref="With"/>.
    /// </summary>
    public sealed class StyleTransitionConfig
    {
        /// <summary>
        /// Sentinel value indicating "no transition": <c>V.Motion(transition: StyleTransitionConfig.None)</c>
        /// animates neither its mount nor its exit. Under an AnimatePresence the removed child still stays for the
        /// commit that removed it and leaves on a later render.
        /// </summary>
        public static readonly StyleTransitionConfig None = new() { DurationSec = 0f };

        /// <summary>
        /// The transition a variant play takes on a Motion whose caller named none: each value runs on the transition
        /// Framer Motion picks for its type, a spring for translate, rotate and scale and a 0.3 s ease for the
        /// rest.
        /// </summary>
        internal static readonly StyleTransitionConfig FramerDefault = new()
        {
            Type = TransitionType.Spring, PerValueDefaults = true,
        };

        // Set on FramerDefault and on PlayedOnVariant's copies: the spring's physics and the ease are chosen per
        // channel, not by the knobs.
        internal bool PerValueDefaults { get; init; }

        // The duration Framer's keyframes generator runs where it is given none.
        internal const float DefaultDurationSec = 0.3f;

        // Whether the caller set a knob other than the delay, the repeat options and the child orchestration: Framer's
        // isTransitionDefined. Copy and WithExitClasses carry it rather than their setters' reading of the copy.
        private bool _defined;

        internal bool IsDefined => _defined;

        private StyleTransitionConfig? _played;

        // What a variant play runs on this config: the config itself, or, where it is not IsDefined, the per-value
        // defaults after its delay, on its repeat and orchestrating its children by its knobs.
        internal StyleTransitionConfig PlayedOnVariant => _defined ? this : _played ??= new()
        {
            Type = TransitionType.Spring,
            PerValueDefaults = true,
            DelaySec = DelaySec,
            Repeat = Repeat,
            RepeatType = RepeatType,
            RepeatDelaySec = RepeatDelaySec,
            StaggerChildrenSec = StaggerChildrenSec,
            StaggerDirection = StaggerDirection,
            DelayChildrenSec = DelayChildrenSec,
            When = When,
            _playback = _playback,
            _playbackOrigin = _playbackOrigin,
        };

        /// <summary>USS class string for the initial enter state. Use the parsed array <see cref="EnterFromClasses"/> at runtime.</summary>
        internal string? EnterFromClass
        {
            get => _enterFromClass;
            init => (_enterFromClass, _defined) = (value, true);
        }

        private string? _enterFromClass;

        /// <summary>
        /// USS class string for the final enter state. Use the parsed array <see cref="EnterToClasses"/> at runtime.
        /// Note: removed when the transition completes, so do not define persistent styles in it.
        /// </summary>
        internal string? EnterToClass
        {
            get => _enterToClass;
            init => (_enterToClass, _defined) = (value, true);
        }

        private string? _enterToClass;

        /// <summary>USS class string for the initial exit state. Use the parsed array <see cref="ExitFromClasses"/> at runtime.</summary>
        internal string? ExitFromClass
        {
            get => _exitFromClass;
            init => (_exitFromClass, _defined) = (value, true);
        }

        private string? _exitFromClass;

        /// <summary>USS class string for the final exit state. Use the parsed array <see cref="ExitToClasses"/> at runtime.</summary>
        internal string? ExitToClass
        {
            get => _exitToClass;
            init => (_exitToClass, _defined) = (value, true);
        }

        private string? _exitToClass;

        /// <summary>
        /// Selects the animation model: a USS <c>transition-*</c> class swap (<see cref="TransitionType.Tween"/>,
        /// the default), a physics-integrated spring (<see cref="TransitionType.Spring"/>), or a fixed-duration
        /// tween whose easing is an EXACT numeric cubic-bezier curve (<see cref="TransitionType.Bezier"/>). See
        /// <see cref="TransitionType"/> for the full contract, including what each does and does not animate.
        /// </summary>
        public TransitionType Type
        {
            get => _type;
            init => (_type, _defined) = (value, true);
        }

        private TransitionType _type = TransitionType.Tween;

        // Which of the spring knobs and DurationSec the caller set, since a spring naming none of the three
        // physics knobs and either DurationSec or Bounce takes its physics from those two instead. Every copy
        // below carries these fields rather than the getters, so a copy keeps the set the caller wrote.
        private float? _stiffness;
        private float? _damping;
        private float? _mass;
        private float? _durationSec;
        private DurationSpring? _durationSpring;

        /// <summary>
        /// Spring stiffness (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values snap toward the target faster. Defaults to 100, or, where none of
        /// <see cref="Stiffness"/>, <see cref="Damping"/> and <see cref="Mass"/> is set and
        /// <see cref="DurationSec"/> or <see cref="Bounce"/> is, to the stiffness of the spring that duration and
        /// bounce describe.
        /// </summary>
        public float Stiffness
        {
            get => _stiffness ?? (TakesDurationSpring ? FromDuration.Stiffness : 100f);
            init => (_stiffness, _defined) = (value, true);
        }

        /// <summary>
        /// Spring damping (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values settle with less oscillation. Defaults to 10, or to the damping of the spring
        /// <see cref="DurationSec"/> and <see cref="Bounce"/> describe, as <see cref="Stiffness"/> says.
        /// </summary>
        public float Damping
        {
            get => _damping ?? (TakesDurationSpring ? FromDuration.Damping : 10f);
            init => (_damping, _defined) = (value, true);
        }

        /// <summary>
        /// Spring mass (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values feel heavier / slower to accelerate. Defaults to 1, which is also the mass of a spring
        /// <see cref="DurationSec"/> and <see cref="Bounce"/> describe.
        /// </summary>
        public float Mass
        {
            get => _mass ?? 1f;
            init => (_mass, _defined) = (value, true);
        }

        /// <summary>
        /// How far a spring described by <see cref="DurationSec"/> overshoots its target: 0 settles without
        /// overshooting, and larger values bounce more, as Framer Motion's spring <c>bounce</c> does (one minus
        /// the damping ratio, which is held between 0.05 and 1). Null leaves it at 0.3 wherever
        /// <see cref="DurationSec"/> describes the spring. Only meaningful when <see cref="Type"/> is
        /// <see cref="TransitionType.Spring"/> and none of <see cref="Stiffness"/>, <see cref="Damping"/> and
        /// <see cref="Mass"/> is set; a spring setting any of the three takes its physics from them.
        /// </summary>
        public float? Bounce
        {
            get => _bounce;
            init => (_bounce, _defined) = (value, true);
        }

        private float? _bounce;

        // Framer's getSpringOptions rule: any physics knob the caller set wins over both.
        private bool TakesDurationSpring
            => _stiffness == null && _damping == null && _mass == null && (_durationSec != null || Bounce != null);

        private DurationSpring FromDuration => _durationSpring ??= DurationSpring.Derive(_durationSec, Bounce);

        // When a spring play's pass ends: at once for an instant spring, at DurationSpring.SettleSec for one its
        // duration describes, and null where its physics alone decide.
        internal double? SpringSettleSec => SpringIsInstant ? 0.0 : TakesDurationSpring ? FromDuration.SettleSec : null;

        // What a channel released with a velocity solves its own physics from, null where the physics knobs or
        // an instant spring leave nothing to solve.
        internal DurationSpringDescription? SpringDescription
            => !SpringIsInstant && TakesDurationSpring ? new DurationSpringDescription(_durationSec, Bounce) : null;

        // Framer's animateMotionValue makes a transition with a zero duration and no repeat delay instant before it
        // reads the type, so a spring given a duration of exactly 0 and no repeat delay is, physics knobs or not.
        // An instant animation still waits out its delay, so only one with none lands at once (SpringLandsAtOnce); a
        // delayed one holds its start and lands when the delay ends.
        internal bool SpringIsInstant => Type == TransitionType.Spring && _durationSec == 0f && RepeatDelaySec == 0f;

        // The repeat a variant play on this config plays: none for an instant spring, as Framer's instant animation
        // repeats nothing.
        internal MotionRepeat PlayedRepeat => SpringIsInstant ? default : MotionRepeat.Of(this);

        internal bool SpringLandsAtOnce => SpringLandsAtOnceAfter(0f);

        // additionalDelaySec: the stagger a caller adds to DelaySec, which an instant spring waits out as well.
        internal bool SpringLandsAtOnceAfter(float additionalDelaySec)
            => SpringIsInstant && DelaySec + additionalDelaySec <= 0f;

        // The transition a sequence step plays this one on. Framer's sequence hands a spring's duration to the
        // spring itself rather than making it instant, and the spring holds a zero duration at 0.01 s, which a
        // spring with physics knobs ignores as it ignores any duration but 0.
        internal StyleTransitionConfig InSequence()
            => SpringIsInstant ? Copy(0.01f, Easing, ExitEasing, DelaySec, Repeat, _defined) : this;

        /// <summary>
        /// First control point's X (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// CSS <c>cubic-bezier(x1,y1,x2,y2)</c> parameter order. X may lie outside [0,1], as in Motion's
        /// <c>cubicBezier</c>; a non-finite coordinate degrades to the default curve below with a one-shot warning.
        /// Defaults to <c>cubic-bezier(0.4, 0, 0.2, 1)</c> — the exact curve the
        /// bundled USS only approximates with the <c>ease-in-out</c> keyword.
        /// </summary>
        public float BezierX1
        {
            get => _bezierX1;
            init => (_bezierX1, _defined) = (value, true);
        }

        private float _bezierX1 = 0.4f;

        /// <summary>
        /// First control point's Y (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// Left unclamped so an overshoot/anticipate curve is preserved. See <see cref="BezierX1"/>.
        /// </summary>
        public float BezierY1
        {
            get => _bezierY1;
            init => (_bezierY1, _defined) = (value, true);
        }

        private float _bezierY1 = 0f;

        /// <summary>
        /// Second control point's X (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// See <see cref="BezierX1"/>.
        /// </summary>
        public float BezierX2
        {
            get => _bezierX2;
            init => (_bezierX2, _defined) = (value, true);
        }

        private float _bezierX2 = 0.2f;

        /// <summary>
        /// Second control point's Y (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// Left unclamped so an overshoot/anticipate curve is preserved. See <see cref="BezierX1"/>.
        /// </summary>
        public float BezierY2
        {
            get => _bezierY2;
            init => (_bezierY2, _defined) = (value, true);
        }

        private float _bezierY2 = 1f;

        /// <summary>
        /// The curve an exit of a <see cref="TransitionType.Bezier"/> play runs on, as Framer Motion's exit transition
        /// carries an ease of its own. When null, <see cref="BezierX1"/> to <see cref="BezierY2"/> drive both directions.
        /// Validated as those are.
        /// </summary>
        public BezierCurve? ExitBezier
        {
            get => _exitBezier;
            init => (_exitBezier, _defined) = (value, true);
        }

        private BezierCurve? _exitBezier;

        /// <summary>
        /// The physics an exit of a <see cref="TransitionType.Spring"/> play runs on, as Framer Motion's exit transition
        /// carries a spring of its own. When null, <see cref="Stiffness"/>, <see cref="Damping"/> and
        /// <see cref="Mass"/> (or the spring <see cref="DurationSec"/> and <see cref="Bounce"/> describe) drive both
        /// directions.
        /// </summary>
        public SpringPhysics? ExitSpring
        {
            get => _exitSpring;
            init => (_exitSpring, _defined) = (value, true);
        }

        private SpringPhysics? _exitSpring;

        /// <summary>
        /// Animation duration (seconds). Applied as the inline transition-duration style. Defaults to 0.3, Framer
        /// Motion's default duration. For <see cref="TransitionType.Bezier"/> it IS the (fixed) duration, exactly as for a plain
        /// tween — only the easing sampling differs. For <see cref="TransitionType.Spring"/>, where none of
        /// <see cref="Stiffness"/>, <see cref="Damping"/> and <see cref="Mass"/> is set, it describes the spring
        /// together with <see cref="Bounce"/>, as Framer Motion's spring <c>duration</c> does: a pass of the play
        /// ends on its target once this long has passed (held between 0.01 and 10 seconds). A value of exactly 0
        /// lands any spring on its target as soon as its <see cref="DelaySec"/> has run, where
        /// <see cref="RepeatDelaySec"/> is 0; a spring setting any of the three physics knobs ignores any other
        /// duration.
        /// </summary>
        public float DurationSec
        {
            get => _durationSec ?? DefaultDurationSec;
            init => (_durationSec, _defined) = (value, true);
        }

        /// <summary>
        /// Easing mode. Applied as the inline transition-timing-function style; <c>EaseIn</c>, <c>EaseOut</c>,
        /// <c>EaseInOut</c> and the three back modes run on Framer Motion's curves (<c>Documentation~/motion.md</c>).
        /// Defaults to EaseOut (for enter). Presets in StyleTransition.cs configure enter/exit easing separately.
        /// A <see cref="TransitionType.Spring"/> play's own curve is its physics; it reads this only for a sequence
        /// segment <see cref="Animator"/> plays over more than two keyframes, which runs as a tween. Ignored when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>: the
        /// <see cref="BezierX1"/>/<see cref="BezierY1"/>/<see cref="BezierX2"/>/<see cref="BezierY2"/> control
        /// points ARE the curve (an exact numeric one no <c>EasingMode</c> keyword can express).
        /// </summary>
        public EasingMode Easing
        {
            get => _easing;
            init => (_easing, _defined) = (value, true);
        }

        private EasingMode _easing = EasingMode.EaseOut;

        /// <summary>
        /// Easing mode for exit. When null, <see cref="Easing"/> is reused.
        /// The presets in StyleTransition.cs typically use EaseOut for enter and EaseIn for exit.
        /// Ignored when <see cref="Type"/> is <see cref="TransitionType.Spring"/> or
        /// <see cref="TransitionType.Bezier"/>: a spring's exit physics is <see cref="ExitSpring"/>, and a bezier's exit
        /// curve is <see cref="ExitBezier"/>.
        /// </summary>
        public EasingMode? ExitEasing
        {
            get => _exitEasing;
            init => (_exitEasing, _defined) = (value, true);
        }

        private EasingMode? _exitEasing;

        /// <summary>
        /// Animation start delay (seconds). Applied as the inline CSS transition-delay style.
        /// Foundation for stagger (sequentially delayed animations). 0 means no delay (default).
        /// A negative value starts the animation that far into its run, as CSS and Framer Motion do.
        /// Note: currently a single delay shared between enter and exit. If a separate exit delay is
        /// needed, consider adding ExitDelaySec.
        /// </summary>
        public float DelaySec { get; init; }

        /// <summary>
        /// The transition a <c>layoutId</c> move takes in place of this one — Framer's <c>transition.layout</c>.
        /// When null, the move takes this transition itself; its <see cref="Type"/> decides the curve as it does
        /// for a variant swap. A <c>layoutId</c> Motion whose caller gives <c>V.Motion</c> no transition, duration,
        /// easing or delay moves on Framer's default layout transition, a 0.45 s tween eased by
        /// <c>cubic-bezier(0.4, 0, 0.1, 1)</c>.
        /// </summary>
        public StyleTransitionConfig? Layout
        {
            get => _layout;
            init => (_layout, _defined) = (value, true);
        }

        private StyleTransitionConfig? _layout;

        private readonly float _repeat;
        private readonly float _repeatDelaySec;

        /// <summary>
        /// The passes a play makes after its first — Framer Motion's <c>repeat</c>, so <c>Repeat = 2</c> plays three
        /// times. <c>float.PositiveInfinity</c> repeats until a later play, an exit or an unmount replaces it; 0,
        /// the default, plays once. <see cref="RepeatType"/> decides each later pass's direction and
        /// <see cref="RepeatDelaySec"/> the wait between passes. <c>Documentation~/motion.md</c> owns the full contract, including where a play ends and what a
        /// <c>layoutId</c> move does with it.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative, NaN, or neither a whole number nor
        /// <c>float.PositiveInfinity</c>.</exception>
        public float Repeat
        {
            get => _repeat;
            init
            {
                if (!(value >= 0f) || (!float.IsPositiveInfinity(value) && value != MathF.Floor(value)))
                {
                    throw new ArgumentOutOfRangeException(nameof(Repeat), value,
                        "Repeat counts whole passes after the first: zero or more, or float.PositiveInfinity.");
                }
                _repeat = value;
            }
        }

        /// <summary>
        /// How each pass after the first runs — Framer Motion's <c>repeatType</c>. Defaults to
        /// <see cref="TransitionRepeatType.Loop"/>. Read only when <see cref="Repeat"/> is above zero, but for the
        /// opacity of a single <see cref="Animator"/> call, whose keyframes <see cref="TransitionRepeatType.Mirror"/>
        /// eases pair by pair (<c>Documentation~/motion.md</c>).
        /// </summary>
        public TransitionRepeatType RepeatType { get; init; } = TransitionRepeatType.Loop;

        /// <summary>
        /// Seconds a repeating play holds the value one pass ends on before the next pass starts — Framer Motion's
        /// <c>repeatDelay</c>. No wait follows the last pass, so it never delays completion. 0 by default.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
        public float RepeatDelaySec
        {
            get => _repeatDelaySec;
            init
            {
                if (!(value >= 0f) || float.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(RepeatDelaySec), value,
                        "RepeatDelaySec must be finite and zero or more.");
                }
                _repeatDelaySec = value;
            }
        }

        /// <summary>
        /// Optional per-property transition overrides layered on top of the top-level <see cref="DurationSec"/> /
        /// <see cref="Easing"/> / <see cref="DelaySec"/> (e.g. opacity tweening in 0.15s while scale takes 0.5s).
        /// When set, the implicit "all" catch-all a variant class swap transitions on — the swap carries no
        /// transition-* of its own — stays on the top-level timing and these properties follow it, in declaration
        /// order, each on its own: as with Framer Motion's per-value transitions, a property no override names
        /// keeps the top-level timing. Currently wired only where a variant swap would otherwise set transition-property:
        /// all (a variant-driven enter, or an exit driven by a <c>variants</c> + <c>exit</c> label) — a preset
        /// transition's own USS-declared transition-property is untouched — and in <see cref="Animator"/>, which reads
        /// an entry as a per-value transition. Null (default) preserves today's behavior unchanged.
        /// On a <see cref="TransitionType.Spring"/> or <see cref="TransitionType.Bezier"/> play, a channel an entry
        /// names runs on that entry's timing in place of the play's curve, once whatever the play repeats;
        /// <c>Documentation~/motion.md</c> owns the rule.
        /// </summary>
        public IReadOnlyList<StylePropertyTransition>? PropertyOverrides
        {
            get => _propertyOverrides;
            init => (_propertyOverrides, _defined) = (value, true);
        }

        private IReadOnlyList<StylePropertyTransition>? _propertyOverrides;

        /// <summary>
        /// Delay interval (seconds) applied sequentially to each child Motion that inherits its active
        /// label from this Motion (it declares <c>variants</c> and none of <c>animate</c>, <c>initial</c> and <c>exit</c> — see
        /// <see cref="Velvet.MotionNode.Animate"/>) when that label changes or this Motion's mount enter plays:
        /// the i-th such child, in document order, is delayed an additional <c>DelayChildrenSec + StaggerChildrenSec
        /// * i</c> on top of its OWN <see cref="DelaySec"/>. 0 (default) means no stagger (every inheriting
        /// child responds at the same time). Unlike AnimatePresence's own per-child enter/exit stagger
        /// (<c>V.AnimatePresence(staggerSec:)</c>), this orchestrates a PLAIN parent → child label propagation —
        /// no AnimatePresence boundary is required; toggling this Motion's <c>animate</c> prop is enough. A
        /// descendant naming a label of its own opts out of both the label inheritance and this stagger, as
        /// Framer's controlling variant nodes do. As Framer Motion's <c>staggerChildren</c> does, the index counts
        /// this Motion's own children: a child with variants numbers its own inheriting children from zero,
        /// starting them with itself, and only a Motion with neither variants nor a label of its own hands this
        /// sequence on to the children below it.
        /// </summary>
        public float StaggerChildrenSec { get; init; }

        /// <summary>
        /// The order <see cref="StaggerChildrenSec"/> numbers the inheriting children in, as Framer Motion's
        /// <c>staggerDirection</c>: 1 (default) starts the first child first, and -1 starts the last child first, as
        /// does any other value. A child's number counts back from the declared children of this Motion's node,
        /// where a component counts as one child.
        /// </summary>
        public int StaggerDirection { get; init; } = 1;

        /// <summary>
        /// A fixed delay (seconds) added before any inheriting descendant's staggered delay — see
        /// <see cref="StaggerChildrenSec"/> for the full propagation contract. 0 (default) means no fixed delay.
        /// </summary>
        public float DelayChildrenSec { get; init; }

        /// <summary>
        /// Sequences this Motion's own class swap against its inheriting descendants' swaps (see
        /// <see cref="StaggerChildrenSec"/>) when its active label changes or its mount enter plays. Defaults to
        /// <see cref="TransitionWhen.Together"/>.
        /// </summary>
        public TransitionWhen When { get; init; } = TransitionWhen.Together;

        /// <summary>
        /// Whether an AnimatePresence exit gated by this config plays an animation, rather than settling after the
        /// commit that removed the child. A spring counts as animated whatever its duration — an instant one
        /// (<see cref="SpringIsInstant"/>) included, which lands once its delay and stagger have run — and so does the
        /// degenerate case where the exit's variant pair touches no spring-animatable channel at all, which still
        /// plays through the spring machinery.
        /// </summary>
        internal bool HasExitAnimation => Type == TransitionType.Spring || DurationSec > 0f
            || (DurationSec == 0f && StylePropertyTransition.AnyAnimates(PropertyOverrides));

        // Parsed class-name array caches (lazily initialized).
        private string[]? _enterFromClasses;
        private string[]? _enterToClasses;
        private string[]? _exitFromClasses;
        private string[]? _exitToClasses;

        /// <summary>Parsed array of EnterFromClass. Parsed and cached on first access.</summary>
        internal string[] EnterFromClasses => _enterFromClasses ??= ParseClasses(EnterFromClass);

        /// <summary>Parsed array of EnterToClass.</summary>
        internal string[] EnterToClasses => _enterToClasses ??= ParseClasses(EnterToClass);

        /// <summary>Parsed array of ExitFromClass.</summary>
        internal string[] ExitFromClasses => _exitFromClasses ??= ParseClasses(ExitFromClass);

        /// <summary>Parsed array of ExitToClass.</summary>
        internal string[] ExitToClasses => _exitToClasses ??= ParseClasses(ExitToClass);

        private MotionPlayback? _playback;
        private StyleTransitionConfig? _playbackOrigin;

        // The playback whose time the plays of a Motion handed this config step by, with those of the
        // descendants that take their label from it (MotionVariantResolver.PlaybackForChildren). It rides on the
        // node's own config rather than on the config a swap plays, since a pose naming its own transition plays
        // that one.
        internal MotionPlayback? Playback => _playback;

        // The config WithPlayback copied, for a caller that hands out one copy per config.
        internal StyleTransitionConfig? PlaybackOrigin => _playbackOrigin;

        // A copy of every knob, so a knob added later is carried without being listed here.
        internal StyleTransitionConfig WithPlayback(MotionPlayback playback)
        {
            var copy = (StyleTransitionConfig)MemberwiseClone();
            copy._playback = playback;
            copy._playbackOrigin = this;
            copy._played = null;
            return copy;
        }

        /// <summary>
        /// Builds a new StyleTransitionConfig that overrides duration / easing on top of the preset.
        /// Class-name definitions are copied; only the specified parameters are overridden.
        /// </summary>
        /// <param name="durationSec">Duration (seconds). null preserves the original value.</param>
        /// <param name="easing">Enter easing. null preserves the original value.</param>
        /// <param name="exitEasing">Exit easing. null preserves the original value.</param>
        public StyleTransitionConfig With(
            float? durationSec = null,
            EasingMode? easing = null,
            EasingMode? exitEasing = null,
            float? delaySec = null)
            => Copy(durationSec ?? _durationSec, easing ?? Easing, exitEasing ?? ExitEasing, delaySec ?? DelaySec, Repeat,
                _defined || durationSec != null || easing != null || exitEasing != null);

        // This config with no repeat, for a sequence step whose repeat Framer Motion's sequence ignores.
        internal StyleTransitionConfig WithoutRepeat() => Copy(_durationSec, Easing, ExitEasing, DelaySec, 0f, _defined);

        private StyleTransitionConfig Copy(float? durationSec, EasingMode easing, EasingMode? exitEasing, float delaySec,
            float repeat, bool defined)
        {
            return new StyleTransitionConfig
            {
                EnterFromClass = EnterFromClass,
                EnterToClass = EnterToClass,
                ExitFromClass = ExitFromClass,
                ExitToClass = ExitToClass,
                _durationSec = durationSec,
                Easing = easing,
                ExitEasing = exitEasing,
                DelaySec = delaySec,
                // Passed through unchanged: a copy's callers choose only the top-level timing and the repeat count,
                // not per-property overrides, the child-orchestration knobs, the spring model, the repeat's type and
                // delay, or the transition a layoutId move takes.
                PropertyOverrides = PropertyOverrides,
                Layout = Layout,
                StaggerChildrenSec = StaggerChildrenSec,
                StaggerDirection = StaggerDirection,
                DelayChildrenSec = DelayChildrenSec,
                When = When,
                PerValueDefaults = PerValueDefaults,
                Type = Type,
                _stiffness = _stiffness,
                _damping = _damping,
                _mass = _mass,
                Bounce = Bounce,
                BezierX1 = BezierX1,
                BezierY1 = BezierY1,
                BezierX2 = BezierX2,
                BezierY2 = BezierY2,
                ExitBezier = ExitBezier,
                ExitSpring = ExitSpring,
                Repeat = repeat,
                RepeatType = RepeatType,
                RepeatDelaySec = RepeatDelaySec,
                // Class names are identical, so share the parsed arrays (avoids re-parsing).
                _enterFromClasses = _enterFromClasses,
                _enterToClasses = _enterToClasses,
                _exitFromClasses = _exitFromClasses,
                _exitToClasses = _exitToClasses,
                // V.Motion tunes a sequence's transition through here when its call site also names a duration,
                // easing or delay, and the copy has to stay on the sequence's playback.
                _playback = _playback,
                _defined = defined,
            };
        }

        /// <summary>
        /// Builds a new StyleTransitionConfig for a variant `exit` (see
        /// <see cref="Velvet.MotionNode.Exit"/>): copies every timing / spring / per-property-override knob
        /// unchanged from this config — the enclosing Motion's own <c>transition</c> — but replaces the exit
        /// class pair with the resolved variant classes. Sibling to <see cref="With"/> (which tunes a preset's
        /// timing while keeping its class names): this keeps the timing/spring knobs fixed while replacing the
        /// classes, so the two together cover both directions a caller needs to override without repeating the
        /// growing knob list at each call site.
        /// </summary>
        /// <param name="exitFromClass">The resting variant's own class string (variants[Animate]).</param>
        /// <param name="exitToClass">The exit variant's class string (variants[Exit]).</param>
        internal StyleTransitionConfig WithExitClasses(string exitFromClass, string exitToClass)
        {
            return new StyleTransitionConfig
            {
                ExitFromClass = exitFromClass,
                ExitToClass = exitToClass,
                _durationSec = _durationSec,
                Easing = Easing,
                ExitEasing = ExitEasing,
                DelaySec = DelaySec,
                PropertyOverrides = PropertyOverrides,
                PerValueDefaults = PerValueDefaults,
                Type = Type,
                _stiffness = _stiffness,
                _damping = _damping,
                _mass = _mass,
                Bounce = Bounce,
                BezierX1 = BezierX1,
                BezierY1 = BezierY1,
                BezierX2 = BezierX2,
                BezierY2 = BezierY2,
                ExitBezier = ExitBezier,
                ExitSpring = ExitSpring,
                Repeat = Repeat,
                RepeatType = RepeatType,
                RepeatDelaySec = RepeatDelaySec,
                _defined = _defined,
            };
        }

        private static string[] ParseClasses(string? classNames) => Velvet.V.ParseClassNames(classNames);
    }

    /// <summary>A spring's stiffness, damping and mass; see <see cref="StyleTransitionConfig.Stiffness"/>.</summary>
    public readonly struct SpringPhysics
    {
        public float Stiffness { get; }
        public float Damping { get; }
        public float Mass { get; }

        public SpringPhysics(float stiffness, float damping, float mass)
        {
            Stiffness = stiffness;
            Damping = damping;
            Mass = mass;
        }
    }

    /// <summary>A CSS <c>cubic-bezier(x1, y1, x2, y2)</c> control-point pair; see <see cref="StyleTransitionConfig.BezierX1"/>.</summary>
    public readonly struct BezierCurve
    {
        public float X1 { get; }
        public float Y1 { get; }
        public float X2 { get; }
        public float Y2 { get; }

        public BezierCurve(float x1, float y1, float x2, float y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }
    }

    /// <summary>
    /// A single property's transition override inside <see cref="StyleTransitionConfig.PropertyOverrides"/>. It
    /// replaces the enclosing config for its property, as Framer Motion's per-value transition does: a null
    /// <see cref="DelaySec"/> starts the value with the play, not after its <see cref="StyleTransitionConfig.DelaySec"/>
    /// (a stagger the play waits still counts), and a null <see cref="DurationSec"/> or <see cref="Easing"/> takes
    /// 0.3 s and an ease-out, as Framer Motion's per-value transition takes them. An entry naming neither is no
    /// transition, and its value takes the default transition Framer Motion picks for it.
    /// </summary>
    public readonly struct StylePropertyTransition
    {
        /// <summary>
        /// The USS property name UI Toolkit animates, e.g. <c>"opacity"</c>, <c>"scale"</c>, <c>"translate"</c>,
        /// <c>"rotate"</c>, <c>"background-color"</c> — spelled exactly as UI Toolkit's transition-property expects.
        /// </summary>
        public string Property { get; }

        /// <summary>Duration (seconds). Null runs 0.3 s where <see cref="Easing"/> is set.</summary>
        public float? DurationSec { get; }

        /// <summary>Easing. Null eases out where <see cref="DurationSec"/> is set.</summary>
        public EasingMode? Easing { get; }

        /// <summary>
        /// Delay (seconds) in place of the play's own delay and of the delayChildren and stagger it was handed, as
        /// Framer's per-value delay replaces its orchestration delay; counted from the end of a BeforeChildren or
        /// AfterChildren wait above, which Framer sequences ahead of the play. Null starts with the play, after its
        /// stagger.
        /// </summary>
        public float? DelaySec { get; }

        public StylePropertyTransition(string property, float? durationSec = null, EasingMode? easing = null, float? delaySec = null)
        {
            Property = property;
            DurationSec = durationSec;
            Easing = easing;
            DelaySec = delaySec;
        }

        internal float DurationOrDefaultSec => DurationSec ?? StyleTransitionConfig.DefaultDurationSec;

        internal EasingMode EasingOrDefault => Easing ?? EasingMode.EaseOut;

        // The entry's own delay, counted from where the play's orchestration (a stagger, orchestrationSec) has put
        // it: Framer's animateTarget hands a value's own transition that orchestration delay where it names none, and
        // never the play's own delay.
        internal float OwnDelaySec(float orchestrationSec = 0f)
            => float.IsInfinity(orchestrationSec) ? DelaySec ?? 0f : (DelaySec ?? orchestrationSec) - orchestrationSec;

        // OwnDelaySec counted from the end of playDelaySec, the play's own delay: negative where it is shorter.
        internal float DelayAfterSec(float playDelaySec, float orchestrationSec = 0f)
            => OwnDelaySec(orchestrationSec) - playDelaySec;

        // Framer runs an entry naming a duration or an easing on its keyframes generator; one naming neither is no
        // transition (isTransitionDefined), and its value takes the one getDefaultTransition picks for it.
        internal bool IsDefined => DurationSec != null || Easing != null;

        // The timing a channel of `kind` runs on, starting delaySec after its driver does and travelling from `from`
        // to `to`; accelerated where Framer hands that value the Web Animations API (DrivenTweenTiming.Accelerated),
        // which an entry, carrying no repeat of its own, leaves it on.
        internal TweenChannelTiming TimingFor(float delaySec, ValueDefault kind, float from, float to, bool accelerated)
        {
            if (IsDefined)
            {
                return new TweenChannelTiming(delaySec, Math.Max(0f, DurationOrDefaultSec), EasingOrDefault)
                {
                    Accelerated = accelerated,
                };
            }
            if (kind == ValueDefault.Ease)
            {
                return new TweenChannelTiming(delaySec, MotionSpringDriver.DefaultEaseSec, EasingMode.EaseOut)
                {
                    Bezier = MotionSpringDriver.DefaultEase,
                };
            }
            var spring = MotionSpringDriver.DefaultSpringOf(kind, to);
            return new TweenChannelTiming(delaySec, MotionSpringDriver.DefaultSpringRestSec(spring, Math.Abs(to - from)),
                EasingMode.EaseOut)
            {
                Spring = spring,
                RestSpeed = MotionSpringDriver.DefaultSpringRestSpeed,
            };
        }

        // Where the entry ends, from the end of playDelaySec, for a play whose travel is not known: a default spring
        // is measured over 100, the travel Framer's sequence takes when it cannot read one.
        internal float EndAfterSec(float playDelaySec)
        {
            var durationSec = Math.Max(0f, DurationOrDefaultSec);
            if (!IsDefined)
            {
                var named = DrivenTweenTiming.Named(Property);
                durationSec = 0f;
                foreach (var (longhand, kind) in s_defaultKinds)
                {
                    if (named.Overlaps(StyleLonghandSet.Of(longhand)))
                    {
                        durationSec = Math.Max(durationSec,
                            TimingFor(0f, kind, 0f, UnknownTravel, DrivenTweenTiming.Accelerated(StyleLonghandSet.Of(longhand))).DurationSec);
                    }
                }
                if (!named.IsEmpty && named.Union(s_springLonghands) != s_springLonghands)
                {
                    durationSec = Math.Max(durationSec, MotionSpringDriver.DefaultEaseSec);
                }
            }
            return DelayAfterSec(playDelaySec) + durationSec;
        }

        private const float UnknownTravel = 100f;

        private static readonly (StyleLonghand, ValueDefault)[] s_defaultKinds =
        {
            (StyleLonghand.Translate, ValueDefault.Transform),
            (StyleLonghand.Rotate, ValueDefault.Transform),
            (StyleLonghand.Scale, ValueDefault.Scale),
        };

        private static readonly StyleLonghandSet s_springLonghands = StyleLonghandSet.Of(StyleLonghand.Translate)
            .Union(StyleLonghandSet.Of(StyleLonghand.Rotate)).Union(StyleLonghandSet.Of(StyleLonghand.Scale));

        // How far before the play's own delay ends its earliest entry starts: 0 where none starts sooner.
        internal static float EarliestAfterSec(IReadOnlyList<StylePropertyTransition>? overrides, float playDelaySec,
            float orchestrationSec = 0f)
        {
            var earliest = 0f;
            foreach (var entry in overrides ?? Array.Empty<StylePropertyTransition>())
            {
                earliest = Math.Min(earliest, entry.DelayAfterSec(playDelaySec, orchestrationSec));
            }
            return earliest;
        }

        // Whether an entry animates its value where the play's own duration of 0 animates nothing: one naming no
        // timing runs its default, and one naming a positive duration, or none, runs that.
        internal static bool AnyAnimates(IReadOnlyList<StylePropertyTransition>? overrides)
        {
            var animates = false;
            foreach (var entry in overrides ?? Array.Empty<StylePropertyTransition>()) animates |= entry.DurationOrDefaultSec > 0f;
            return animates;
        }

        // Whether an entry runs its value on a default transition that UI Toolkit's own transition cannot express.
        internal static bool AnyUndefined(IReadOnlyList<StylePropertyTransition>? overrides)
        {
            var undefined = false;
            foreach (var entry in overrides ?? Array.Empty<StylePropertyTransition>()) undefined |= !entry.IsDefined;
            return undefined;
        }
    }

    // Which default transition Framer gives a value no transition names: the ease, the transform spring, or the
    // scale spring.
    internal enum ValueDefault
    {
        Ease,
        Transform,
        Scale,
    }

    /// <summary>
    /// Selects the animation model a <see cref="StyleTransitionConfig"/> plays with — see
    /// <see cref="StyleTransitionConfig.Type"/>.
    /// </summary>
    public enum TransitionType
    {
        /// <summary>
        /// A USS <c>transition-*</c> class swap: <see cref="StyleTransitionConfig.DurationSec"/> /
        /// <see cref="StyleTransitionConfig.Easing"/> drive a fixed-duration tween between the from/to classes.
        /// The default. On a mount whose <see cref="MountOptions.MotionClock"/> is not
        /// <see cref="MotionClock.Realtime"/>, the per-frame driver <see cref="Bezier"/> plays on runs it instead,
        /// on that clock; <c>Documentation~/motion.md</c>'s "Clocks" owns what that covers.
        /// </summary>
        Tween,

        /// <summary>
        /// A physics-integrated spring (see the internal SpringIntegrator): <see cref="StyleTransitionConfig.Stiffness"/>
        /// / <see cref="StyleTransitionConfig.Damping"/> / <see cref="StyleTransitionConfig.Mass"/> decide the
        /// curve and settle time, or <see cref="StyleTransitionConfig.DurationSec"/> and
        /// <see cref="StyleTransitionConfig.Bounce"/> describe both where none of the three is set — CSS/USS
        /// transitions cannot express a spring, so
        /// this is driven by a per-frame tick that writes inline styles directly (like the ring co-fade
        /// tick), not <c>transition-duration</c>/<c>transition-timing-function</c>.
        /// Only <see cref="Velvet.MotionNode"/>'s variant enter/exit (<c>variants</c> + <c>initial</c>/<c>animate</c>/
        /// <c>exit</c>) plays a spring — the classic preset transitions (<see cref="StyleTransition"/>) are always
        /// tweens, since their enter/exit classes are internal to the package.
        /// Which class pairs the tick interpolates, and which fall through to a plain class swap with no
        /// animation on them, is owned by <c>Documentation~/motion.md</c>'s "Driven channels".
        /// </summary>
        Spring,

        /// <summary>
        /// A fixed-duration tween like <see cref="Tween"/>, but with its easing sampled from an EXACT numeric
        /// <c>cubic-bezier(</c><see cref="StyleTransitionConfig.BezierX1"/>, <see cref="StyleTransitionConfig.BezierY1"/>,
        /// <see cref="StyleTransitionConfig.BezierX2"/>, <see cref="StyleTransitionConfig.BezierY2"/><c>)</c> curve
        /// instead of one of UI Toolkit's five <c>EasingMode</c> keywords (which cannot express an arbitrary
        /// cubic-bezier). Like <see cref="Spring"/>, this cannot be expressed by CSS/USS transitions, so it is
        /// driven by a per-frame tick that writes inline styles directly — off the same resolved channel plan,
        /// so its channel scope is <see cref="Spring"/>'s exactly. The curve drives both enter and exit unless
        /// <see cref="StyleTransitionConfig.ExitBezier"/> gives an exit its own, and a channel a <see cref="StyleTransitionConfig.PropertyOverrides"/> entry names runs that entry's timing. A zero
        /// <see cref="StyleTransitionConfig.DurationSec"/> completes immediately with no animation, exactly like a
        /// zero-duration <see cref="Tween"/>.
        /// </summary>
        Bezier,
    }

    /// <summary>
    /// How each pass after the first of a repeating play runs — Framer Motion's <c>repeatType</c>. See
    /// <see cref="StyleTransitionConfig.Repeat"/>.
    /// </summary>
    public enum TransitionRepeatType
    {
        /// <summary>Every pass runs from the from-values to the to-values. The default.</summary>
        Loop,

        /// <summary>
        /// Every second pass plays the one before it backwards in time — CSS's
        /// <c>animation-direction: alternate</c>. A bezier's easing comes back reversed, so an ease-out pass
        /// returns as an ease-in one, and a spring retraces its path.
        /// </summary>
        Reverse,

        /// <summary>
        /// Every second pass runs from the to-values back to the from-values: a bezier on the same easing, so an
        /// ease-out pass returns ease-out, and a spring released from the to-values.
        /// </summary>
        Mirror,
    }

    /// <summary>
    /// Sequences a Motion's own class swap against its inheriting descendants' swaps — see
    /// <see cref="StyleTransitionConfig.When"/> and <see cref="StyleTransitionConfig.StaggerChildrenSec"/>.
    /// </summary>
    public enum TransitionWhen
    {
        /// <summary>
        /// This Motion and its inheriting descendants animate at the same time, offset only by
        /// <see cref="StyleTransitionConfig.StaggerChildrenSec"/> / <see cref="StyleTransitionConfig.DelayChildrenSec"/>.
        /// The default.
        /// </summary>
        Together,

        /// <summary>
        /// Inheriting descendants wait for this Motion's OWN transition to finish before starting: every
        /// descendant's computed delay additionally includes this Motion's own
        /// <see cref="StyleTransitionConfig.DelaySec"/> and the time its play takes — its
        /// <see cref="StyleTransitionConfig.DurationSec"/>, or a spring's time to rest, over every
        /// <see cref="StyleTransitionConfig.Repeat"/> pass. The swap does not even START until DelaySec has
        /// elapsed. A play repeating without end never finishes, so its descendants never start.
        /// </summary>
        BeforeChildren,

        /// <summary>
        /// This Motion's own play waits for its inheriting descendants' plays to end: it starts once the latest of
        /// them has, measured as <see cref="BeforeChildren"/> measures a play's span, and then adds its own
        /// <see cref="StyleTransitionConfig.DelaySec"/>. The descendants start as under <see cref="Together"/>, at
        /// <see cref="StyleTransitionConfig.DelayChildrenSec"/> plus their stagger slot.
        /// </summary>
        AfterChildren,
    }
}
