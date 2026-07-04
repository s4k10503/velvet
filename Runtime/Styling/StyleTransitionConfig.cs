using System;
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
                // Class names are identical, so share the parsed arrays (avoids re-parsing).
                _enterFromClasses = _enterFromClasses,
                _enterToClasses = _enterToClasses,
                _exitFromClasses = _exitFromClasses,
                _exitToClasses = _exitToClasses,
            };
        }

        private static string[] ParseClasses(string? classNames) => Velvet.V.ParseClassNames(classNames);
    }
}
