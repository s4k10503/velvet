namespace Velvet
{
    // How long a variant play on a config runs from the end of its delay until it has finished, as Framer Motion's
    // animation promise measures it: every pass a Bezier or Spring play repeats and the waits between them, a
    // Spring's slowest channel deciding, and infinity for a play that repeats without end.
    internal static class MotionPlaySpan
    {
        public static double Of(StyleTransitionConfig config, string[]? fromClasses, string[]? toClasses)
        {
            switch (config.Type)
            {
                case TransitionType.Bezier:
                    // A zero duration lands the pose at once, so its repeat delay never runs.
                    return config.DurationSec > 0f ? MotionRepeat.Of(config).TotalSec(config.DurationSec) : 0.0;
                case TransitionType.Spring:
                    return SpringIntegrator.AreValidParameters(config.Stiffness, config.Damping, config.Mass)
                        ? MotionSpringDriver.SpanSec(MotionSpringClassParser.Resolve(fromClasses, toClasses),
                            config.Stiffness, config.Damping, config.Mass, MotionRepeat.Of(config))
                        : 0.0;
                default:
                    return config.DurationSec;
            }
        }
    }
}
