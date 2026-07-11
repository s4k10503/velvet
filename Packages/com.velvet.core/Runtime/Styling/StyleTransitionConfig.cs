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
        /// Animation duration (seconds). Applied as the inline transition-duration style.
        /// </summary>
        public float DurationSec { get; init; }

        /// <summary>
        /// Easing mode. Applied as the inline transition-timing-function style.
        /// Defaults to EaseOut (for enter). Presets in StyleTransition.cs configure enter/exit easing separately.
        /// </summary>
        public EasingMode Easing { get; init; } = EasingMode.EaseOut;

        /// <summary>
        /// Easing mode for exit. When null, <see cref="Easing"/> is reused.
        /// The presets in StyleTransition.cs typically use EaseOut for enter and EaseIn for exit.
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
        /// </summary>
        public IReadOnlyList<StylePropertyTransition>? PropertyOverrides { get; init; }

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
                // Passed through unchanged: With() only tunes the top-level timing, not per-property overrides.
                PropertyOverrides = PropertyOverrides,
                // Class names are identical, so share the parsed arrays (avoids re-parsing).
                _enterFromClasses = _enterFromClasses,
                _enterToClasses = _enterToClasses,
                _exitFromClasses = _exitFromClasses,
                _exitToClasses = _exitToClasses,
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
}
