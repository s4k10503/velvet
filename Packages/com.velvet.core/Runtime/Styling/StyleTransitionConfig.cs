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

        /// <summary>USS class string for the initial enter state. Use the parsed array <see cref="EnterFromClasses"/> at runtime.</summary>
        internal string? EnterFromClass { get; init; }

        /// <summary>
        /// USS class string for the final enter state. Use the parsed array <see cref="EnterToClasses"/> at runtime.
        /// Note: removed when the transition completes, so do not define persistent styles in it.
        /// </summary>
        internal string? EnterToClass { get; init; }

        /// <summary>USS class string for the initial exit state. Use the parsed array <see cref="ExitFromClasses"/> at runtime.</summary>
        internal string? ExitFromClass { get; init; }

        /// <summary>USS class string for the final exit state. Use the parsed array <see cref="ExitToClasses"/> at runtime.</summary>
        internal string? ExitToClass { get; init; }

        /// <summary>
        /// Selects the animation model: a USS <c>transition-*</c> class swap (<see cref="TransitionType.Tween"/>,
        /// the default), a physics-integrated spring (<see cref="TransitionType.Spring"/>), or a fixed-duration
        /// tween whose easing is an EXACT numeric cubic-bezier curve (<see cref="TransitionType.Bezier"/>). See
        /// <see cref="TransitionType"/> for the full contract, including what each does and does not animate.
        /// </summary>
        public TransitionType Type { get; init; } = TransitionType.Tween;

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
            init => _stiffness = value;
        }

        /// <summary>
        /// Spring damping (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values settle with less oscillation. Defaults to 10, or to the damping of the spring
        /// <see cref="DurationSec"/> and <see cref="Bounce"/> describe, as <see cref="Stiffness"/> says.
        /// </summary>
        public float Damping
        {
            get => _damping ?? (TakesDurationSpring ? FromDuration.Damping : 10f);
            init => _damping = value;
        }

        /// <summary>
        /// Spring mass (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values feel heavier / slower to accelerate. Defaults to 1, which is also the mass of a spring
        /// <see cref="DurationSec"/> and <see cref="Bounce"/> describe.
        /// </summary>
        public float Mass
        {
            get => _mass ?? 1f;
            init => _mass = value;
        }

        /// <summary>
        /// How far a spring described by <see cref="DurationSec"/> overshoots its target: 0 settles without
        /// overshooting, and larger values bounce more, as Framer Motion's spring <c>bounce</c> does (one minus
        /// the damping ratio, which is held between 0.05 and 1). Null leaves it at 0.3 wherever
        /// <see cref="DurationSec"/> describes the spring. Only meaningful when <see cref="Type"/> is
        /// <see cref="TransitionType.Spring"/> and none of <see cref="Stiffness"/>, <see cref="Damping"/> and
        /// <see cref="Mass"/> is set; a spring setting any of the three takes its physics from them.
        /// </summary>
        public float? Bounce { get; init; }

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
            => SpringIsInstant ? Copy(0.01f, Easing, ExitEasing, DelaySec, Repeat) : this;

        /// <summary>
        /// First control point's X (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// CSS <c>cubic-bezier(x1,y1,x2,y2)</c> parameter order. X must stay in [0,1] (a timing function must be
        /// monotone in time); a value outside that range is invalid per the <c>cubic-bezier()</c> spec and
        /// degrades to the default curve below with a one-shot warning, rather than being silently clamped.
        /// Defaults to <c>cubic-bezier(0.4, 0, 0.2, 1)</c> — the exact curve the
        /// bundled USS only approximates with the <c>ease-in-out</c> keyword.
        /// </summary>
        public float BezierX1 { get; init; } = 0.4f;

        /// <summary>
        /// First control point's Y (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// Left unclamped so an overshoot/anticipate curve is preserved. See <see cref="BezierX1"/>.
        /// </summary>
        public float BezierY1 { get; init; } = 0f;

        /// <summary>
        /// Second control point's X (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// Must stay in [0,1]. See <see cref="BezierX1"/>.
        /// </summary>
        public float BezierX2 { get; init; } = 0.2f;

        /// <summary>
        /// Second control point's Y (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// Left unclamped so an overshoot/anticipate curve is preserved. See <see cref="BezierX1"/>.
        /// </summary>
        public float BezierY2 { get; init; } = 1f;

        /// <summary>
        /// The curve an exit of a <see cref="TransitionType.Bezier"/> play runs on, as Framer Motion's exit transition
        /// carries an ease of its own. When null, <see cref="BezierX1"/> to <see cref="BezierY2"/> drive both directions.
        /// Validated as those are.
        /// </summary>
        public BezierCurve? ExitBezier { get; init; }

        /// <summary>
        /// Animation duration (seconds). Applied as the inline transition-duration style.
        /// For <see cref="TransitionType.Bezier"/> it IS the (fixed) duration, exactly as for a plain
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
            get => _durationSec ?? 0f;
            init => _durationSec = value;
        }

        /// <summary>
        /// Easing mode. Applied as the inline transition-timing-function style.
        /// Defaults to EaseOut (for enter). Presets in StyleTransition.cs configure enter/exit easing separately.
        /// Ignored when <see cref="Type"/> is <see cref="TransitionType.Spring"/>: the physics integration IS the
        /// curve. Also ignored when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>: the
        /// <see cref="BezierX1"/>/<see cref="BezierY1"/>/<see cref="BezierX2"/>/<see cref="BezierY2"/> control
        /// points ARE the curve (an exact numeric one no <c>EasingMode</c> keyword can express).
        /// </summary>
        public EasingMode Easing { get; init; } = EasingMode.EaseOut;

        /// <summary>
        /// Easing mode for exit. When null, <see cref="Easing"/> is reused.
        /// The presets in StyleTransition.cs typically use EaseOut for enter and EaseIn for exit.
        /// Ignored when <see cref="Type"/> is <see cref="TransitionType.Spring"/> or
        /// <see cref="TransitionType.Bezier"/>: a spring's single stiffness/damping/mass drives both directions, and a
        /// bezier's exit curve is <see cref="ExitBezier"/>.
        /// </summary>
        public EasingMode? ExitEasing { get; init; }

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
        public StyleTransitionConfig? Layout { get; init; }

        private readonly float _repeat;
        private readonly float _repeatDelaySec;

        /// <summary>
        /// The passes a play makes after its first — Framer Motion's <c>repeat</c>, so <c>Repeat = 2</c> plays three
        /// times. <c>float.PositiveInfinity</c> repeats until a later play, an exit or an unmount replaces it; 0,
        /// the default, plays once. <see cref="RepeatType"/> decides each later pass's direction and
        /// <see cref="RepeatDelaySec"/> the wait between passes. Played when <see cref="Type"/> is
        /// <see cref="TransitionType.Bezier"/> or <see cref="TransitionType.Spring"/>; a
        /// <see cref="TransitionType.Tween"/> play plays once, and the first in a mounted tree logs a warning.
        /// <c>Documentation~/motion.md</c> owns the full contract, including where a play ends and what a
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
        /// <see cref="TransitionRepeatType.Loop"/>. Read only when <see cref="Repeat"/> is above zero.
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
        /// transition's own USS-declared transition-property is untouched. Null (default) preserves today's
        /// behavior unchanged.
        /// Not read when <see cref="Type"/> is <see cref="TransitionType.Spring"/> or
        /// <see cref="TransitionType.Bezier"/>: both drive every animated channel with the SAME curve — a spring's
        /// <see cref="Stiffness"/> / <see cref="Damping"/> / <see cref="Mass"/>, a bezier's four control points —
        /// so there is no per-property override of the model itself (only <see cref="TransitionType.Tween"/>'s
        /// per-property duration/easing/delay can be overridden this way).
        /// </summary>
        public IReadOnlyList<StylePropertyTransition>? PropertyOverrides { get; init; }

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
        internal bool HasExitAnimation => Type == TransitionType.Spring || DurationSec > 0f;

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
            => Copy(durationSec ?? _durationSec, easing ?? Easing, exitEasing ?? ExitEasing, delaySec ?? DelaySec, Repeat);

        // This config with no repeat, for a sequence step whose repeat Framer Motion's sequence ignores.
        internal StyleTransitionConfig WithoutRepeat() => Copy(_durationSec, Easing, ExitEasing, DelaySec, 0f);

        private StyleTransitionConfig Copy(float? durationSec, EasingMode easing, EasingMode? exitEasing, float delaySec,
            float repeat)
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
                DelayChildrenSec = DelayChildrenSec,
                When = When,
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
                Repeat = Repeat,
                RepeatType = RepeatType,
                RepeatDelaySec = RepeatDelaySec,
            };
        }

        private static string[] ParseClasses(string? classNames) => Velvet.V.ParseClassNames(classNames);
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
    /// A single property's transition override inside <see cref="StyleTransitionConfig.PropertyOverrides"/>.
    /// Any null field falls back to the enclosing config's corresponding top-level value — <see cref="Easing"/>
    /// falls back to the config's EFFECTIVE easing for the direction being played (<c>Easing</c> for an enter,
    /// <c>ExitEasing ?? Easing</c> for an exit), matching how the top-level fields already resolve per direction.
    /// </summary>
    public readonly struct StylePropertyTransition
    {
        /// <summary>
        /// The USS property name UI Toolkit animates, e.g. <c>"opacity"</c>, <c>"scale"</c>, <c>"translate"</c>,
        /// <c>"rotate"</c>, <c>"background-color"</c> — spelled exactly as UI Toolkit's transition-property expects.
        /// </summary>
        public string Property { get; }

        /// <summary>Duration override (seconds). Null falls back to <see cref="StyleTransitionConfig.DurationSec"/>.</summary>
        public float? DurationSec { get; }

        /// <summary>Easing override. Null falls back to the enclosing config's effective easing for the direction being played.</summary>
        public EasingMode? Easing { get; }

        /// <summary>Delay override (seconds). Null falls back to <see cref="StyleTransitionConfig.DelaySec"/>.</summary>
        public float? DelaySec { get; }

        public StylePropertyTransition(string property, float? durationSec = null, EasingMode? easing = null, float? delaySec = null)
        {
            Property = property;
            DurationSec = durationSec;
            Easing = easing;
            DelaySec = delaySec;
        }
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
        /// so its channel scope is <see cref="Spring"/>'s exactly. One curve drives BOTH enter and exit
        /// (there is no separate exit curve, mirroring the spring's single stiffness/damping/mass),
        /// and <see cref="StyleTransitionConfig.PropertyOverrides"/> is not read (no per-property curve). A zero
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
        /// The name implies this Motion's own transition would wait for every inheriting descendant to finish
        /// first. Not implemented: setting this value logs a warning and behaves like <see cref="Together"/> (no
        /// parent/child sequencing) rather than silently applying the wrong delay.
        /// </summary>
        AfterChildren,
    }
}
