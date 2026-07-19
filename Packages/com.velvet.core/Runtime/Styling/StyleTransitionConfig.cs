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
        /// requests immediate mount/unmount with no animation.
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
        /// <remarks>Equivalent to setting Framer Motion's <c>transition.type</c> to <c>"tween"</c> / <c>"spring"</c>
        /// for users migrating from Framer Motion — except Velvet defaults to <c>Tween</c> where Framer defaults
        /// transform-like values to a spring; spring is opt-in here.</remarks>
        public TransitionType Type { get; init; } = TransitionType.Tween;

        /// <summary>
        /// Spring stiffness (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values snap toward the target faster. Framer Motion's default (100).
        /// </summary>
        public float Stiffness { get; init; } = 100f;

        /// <summary>
        /// Spring damping (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values settle with less oscillation. Framer Motion's default (10).
        /// </summary>
        public float Damping { get; init; } = 10f;

        /// <summary>
        /// Spring mass (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Spring"/>).
        /// Higher values feel heavier / slower to accelerate. Framer Motion's default (1).
        /// </summary>
        public float Mass { get; init; } = 1f;

        /// <summary>
        /// First control point's X (only meaningful when <see cref="Type"/> is <see cref="TransitionType.Bezier"/>).
        /// CSS <c>cubic-bezier(x1,y1,x2,y2)</c> parameter order. X must stay in [0,1] (a timing function must be
        /// monotone in time); a value outside that range is invalid per the <c>cubic-bezier()</c> spec and
        /// degrades to the default curve below with a one-shot warning, rather than being silently clamped.
        /// Defaults to Tailwind's own default curve, <c>cubic-bezier(0.4, 0, 0.2, 1)</c> — the exact curve the
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
        /// Animation duration (seconds). Applied as the inline transition-duration style.
        /// Ignored when <see cref="Type"/> is <see cref="TransitionType.Spring"/>: a spring's settle time is
        /// decided entirely by <see cref="Stiffness"/> / <see cref="Damping"/> / <see cref="Mass"/>, not a fixed
        /// duration. For <see cref="TransitionType.Bezier"/> it IS the (fixed) duration, exactly as for a plain
        /// tween — only the easing sampling differs.
        /// </summary>
        public float DurationSec { get; init; }

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
        /// <see cref="TransitionType.Bezier"/>: like a spring's single stiffness/damping/mass, one bezier curve
        /// drives BOTH directions — there is no separate exit curve.
        /// </summary>
        public EasingMode? ExitEasing { get; init; }

        /// <summary>
        /// Animation start delay (seconds). Applied as the inline CSS transition-delay style.
        /// Foundation for stagger (sequentially delayed animations). 0 means no delay (default).
        /// Negative values are ignored (transition-delay is not set; behaves as no delay).
        /// Note: currently a single delay shared between enter and exit. If a separate exit delay is
        /// needed, consider adding ExitDelaySec.
        /// </summary>
        public float DelaySec { get; init; }

        /// <summary>
        /// Optional per-property transition overrides layered on top of the top-level <see cref="DurationSec"/> /
        /// <see cref="Easing"/> / <see cref="DelaySec"/> (e.g. opacity tweening in 0.15s while scale takes 0.5s).
        /// When set, transition-property switches from the implicit "all" catch-all — used for a variant class
        /// swap, which carries no transition-* of its own — to EXACTLY these properties, in declaration order:
        /// overrides REPLACE transition-property: all rather than layering on top of it, matching CSS semantics
        /// where an explicit transition-property list transitions only what it names. Name every property that
        /// should animate. Currently wired only where a variant swap would otherwise set transition-property:
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
        /// Delay interval (seconds) applied sequentially to each DESCENDANT Motion that inherits its active
        /// label from this Motion (it declares <c>variants</c> but no own <c>animate</c> — see
        /// <see cref="Velvet.MotionNode.Animate"/>) when that ambient label changes: the i-th such inheriting
        /// descendant, visited in document order, is delayed an additional <c>DelayChildrenSec + StaggerChildrenSec
        /// * i</c> on top of its OWN <see cref="DelaySec"/>. 0 (default) means no stagger (every inheriting
        /// descendant responds at the same time). Unlike AnimatePresence's own per-child enter/exit stagger
        /// (<c>V.AnimatePresence(staggerSec:)</c>), this orchestrates a PLAIN parent → child label propagation —
        /// no AnimatePresence boundary is required; toggling this Motion's <c>animate</c> prop is enough. A
        /// descendant with its OWN explicit <c>animate</c> opts out of both the label inheritance and this
        /// stagger — it is driven by its own render, not this propagation (matching Framer Motion, where an
        /// explicit <c>animate</c> override disconnects a component from its parent's variant propagation). The
        /// stagger index is transitive: an inheriting descendant with no stagger config of its own passes this
        /// orchestration through to ITS OWN inheriting children, who continue claiming from the SAME sequence.
        /// </summary>
        /// <remarks>Equivalent to Framer Motion's <c>transition.staggerChildren</c> for users migrating from Framer Motion.</remarks>
        public float StaggerChildrenSec { get; init; }

        /// <summary>
        /// A fixed delay (seconds) added before any inheriting descendant's staggered delay — see
        /// <see cref="StaggerChildrenSec"/> for the full propagation contract. 0 (default) means no fixed delay.
        /// </summary>
        /// <remarks>Equivalent to Framer Motion's <c>transition.delayChildren</c> for users migrating from Framer Motion.</remarks>
        public float DelayChildrenSec { get; init; }

        /// <summary>
        /// Sequences this Motion's own class swap against its inheriting descendants' swaps (see
        /// <see cref="StaggerChildrenSec"/>) when its active label changes. Defaults to
        /// <see cref="TransitionWhen.Together"/>.
        /// </summary>
        /// <remarks>Equivalent to Framer Motion's <c>transition.when</c> for users migrating from Framer Motion.</remarks>
        public TransitionWhen When { get; init; } = TransitionWhen.Together;

        /// <summary>
        /// Whether an AnimatePresence exit gated by this config should be treated as animated (kept mounted as
        /// an exiting ghost) rather than removed instantly. A spring's settle time is decided by
        /// <see cref="Stiffness"/> / <see cref="Damping"/> / <see cref="Mass"/>, not <see cref="DurationSec"/>
        /// (documented as ignored for <see cref="TransitionType.Spring"/>), so a spring counts as animated
        /// regardless of DurationSec — including the degenerate case where the exit's variant pair touches no
        /// spring-animatable channel at all, which still plays through the spring machinery (completing on its
        /// own, deferred, rather than being pre-empted by an instant-removal gate keyed on this flag).
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
        {
            return new StyleTransitionConfig
            {
                EnterFromClass = EnterFromClass,
                EnterToClass = EnterToClass,
                ExitFromClass = ExitFromClass,
                ExitToClass = ExitToClass,
                DurationSec = durationSec ?? DurationSec,
                Easing = easing ?? Easing,
                ExitEasing = exitEasing ?? ExitEasing,
                DelaySec = delaySec ?? DelaySec,
                // Passed through unchanged: With() only tunes the top-level timing, not per-property overrides,
                // the child-orchestration knobs, or the spring model.
                PropertyOverrides = PropertyOverrides,
                StaggerChildrenSec = StaggerChildrenSec,
                DelayChildrenSec = DelayChildrenSec,
                When = When,
                Type = Type,
                Stiffness = Stiffness,
                Damping = Damping,
                Mass = Mass,
                BezierX1 = BezierX1,
                BezierY1 = BezierY1,
                BezierX2 = BezierX2,
                BezierY2 = BezierY2,
                // Class names are identical, so share the parsed arrays (avoids re-parsing).
                _enterFromClasses = _enterFromClasses,
                _enterToClasses = _enterToClasses,
                _exitFromClasses = _exitFromClasses,
                _exitToClasses = _exitToClasses,
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
                DurationSec = DurationSec,
                Easing = Easing,
                ExitEasing = ExitEasing,
                DelaySec = DelaySec,
                PropertyOverrides = PropertyOverrides,
                Type = Type,
                Stiffness = Stiffness,
                Damping = Damping,
                Mass = Mass,
                BezierX1 = BezierX1,
                BezierY1 = BezierY1,
                BezierX2 = BezierX2,
                BezierY2 = BezierY2,
            };
        }

        private static string[] ParseClasses(string? classNames) => Velvet.V.ParseClassNames(classNames);
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
        /// The default.
        /// </summary>
        Tween,

        /// <summary>
        /// A physics-integrated spring (see the internal SpringIntegrator): <see cref="StyleTransitionConfig.Stiffness"/>
        /// / <see cref="StyleTransitionConfig.Damping"/> / <see cref="StyleTransitionConfig.Mass"/> decide the
        /// curve and settle time instead of a fixed duration — CSS/USS transitions cannot express a spring, so
        /// this is driven by a per-frame tick that writes inline styles directly (like the drop-shadow co-fade
        /// tick), not <c>transition-duration</c>/<c>transition-timing-function</c>.
        /// Only <see cref="Velvet.MotionNode"/>'s variant enter/exit (<c>variants</c> + <c>initial</c>/<c>animate</c>/
        /// <c>exit</c>) plays a spring — the classic preset transitions (<see cref="StyleTransition"/>) are always
        /// tweens, since their enter/exit classes are internal to the package.
        /// Only OPACITY and the transform trio — translate x/y (pixels only; percentage-based translate such as
        /// <c>translate-x-1/2</c> or <c>translate-x-full</c> is out of scope), uniform scale, and rotate (degrees)
        /// — are spring-animated; colors, arbitrary lengths (width/height/margin/…), and per-axis
        /// <c>scale-x-</c>/<c>scale-y-</c> are out of scope and are NOT animated by a spring (they still apply as
        /// plain classes, just without a tween/spring transition on them).
        /// </summary>
        Spring,

        /// <summary>
        /// A fixed-duration tween like <see cref="Tween"/>, but with its easing sampled from an EXACT numeric
        /// <c>cubic-bezier(</c><see cref="StyleTransitionConfig.BezierX1"/>, <see cref="StyleTransitionConfig.BezierY1"/>,
        /// <see cref="StyleTransitionConfig.BezierX2"/>, <see cref="StyleTransitionConfig.BezierY2"/><c>)</c> curve
        /// instead of one of UI Toolkit's five <c>EasingMode</c> keywords (which cannot express an arbitrary
        /// cubic-bezier). Like <see cref="Spring"/>, this cannot be expressed by CSS/USS transitions, so it is
        /// driven by a per-frame tick that writes inline styles directly — and so shares the spring's channel
        /// scope EXACTLY: only OPACITY and the transform trio (translate x/y in pixels, uniform scale, rotate
        /// degrees) are animated; everything else applies as a plain class with no tween. One curve drives BOTH
        /// enter and exit (there is no separate exit curve, mirroring the spring's single stiffness/damping/mass),
        /// and <see cref="StyleTransitionConfig.PropertyOverrides"/> is not read (no per-property curve). A zero
        /// <see cref="StyleTransitionConfig.DurationSec"/> completes immediately with no animation, exactly like a
        /// zero-duration <see cref="Tween"/>.
        /// </summary>
        Bezier,
    }

    /// <summary>
    /// Sequences a Motion's own class swap against its inheriting descendants' swaps — see
    /// <see cref="StyleTransitionConfig.When"/> and <see cref="StyleTransitionConfig.StaggerChildrenSec"/>.
    /// </summary>
    /// <remarks>Equivalent to Framer Motion's <c>transition.when</c> for users migrating from Framer Motion.</remarks>
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
        /// <see cref="StyleTransitionConfig.DelaySec"/> + <see cref="StyleTransitionConfig.DurationSec"/> — the
        /// full span of its swap, not just the duration, since the swap does not even START until DelaySec has
        /// elapsed.
        /// </summary>
        BeforeChildren,

        /// <summary>
        /// In Framer Motion, this Motion's own transition would wait for every inheriting descendant to finish
        /// first. Not implemented: Velvet applies this Motion's own class swap before its descendants are even
        /// visited during the reconcile walk, so the descendant count / durations needed to delay THIS swap are
        /// not known in time. Setting this value logs a warning and behaves like <see cref="Together"/> (no
        /// parent/child sequencing) rather than silently applying the wrong delay.
        /// </summary>
        AfterChildren,
    }
}
