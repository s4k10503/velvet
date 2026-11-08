namespace Velvet
{
    // How long a variant play on a config runs from the end of its delay until it has finished, as Framer Motion's
    // animation promise measures it: every pass a play repeats and the waits between them, a
    // Spring's slowest channel deciding, and infinity for a play that repeats without end.
    internal static class MotionPlaySpan
    {
        // How long a BeforeChildren parent holds its children: its delay and the span of what plays, and nothing for
        // a play with no channel, as Framer Motion's animation promise for a variant with no value to animate
        // resolves at once, the delay included.
        public static double HeldBy(StyleTransitionConfig config, string[]? fromClasses, string[]? toClasses,
            MotionSlotContext? context = null)
            => config.Type is TransitionType.Bezier or TransitionType.Spring
                && MotionSpringClassParser.Resolve(fromClasses, toClasses, context: context).IsEmpty
                    ? 0.0
                    : config.DelaySec + Of(config, fromClasses, toClasses, context);

        // A Bezier play's passes, or where its slowest PropertyOverrides entry ends where that is later.
        internal static double BezierSpanSec(StyleTransitionConfig config)
            => System.Math.Max(MotionRepeat.Of(config).TotalSec(config.DurationSec), OverridesEndSec(config));

        // Where the slowest PropertyOverrides entry ends, from the end of the play's delay.
        internal static double OverridesEndSec(StyleTransitionConfig config)
        {
            var end = 0.0;
            foreach (var entry in config.PropertyOverrides ?? System.Array.Empty<StylePropertyTransition>())
            {
                end = System.Math.Max(end, entry.EndAfterSec(config.DelaySec));
            }
            return end;
        }

        private static double BezierEndSec(StyleTransitionConfig config, string[]? fromClasses, string[]? toClasses,
            MotionSlotContext? context)
        {
            var state = BezierTweenDriver.WithOverrides(
                BezierTweenDriver.Create(MotionSpringClassParser.Resolve(fromClasses, toClasses, context: context),
                    config.BezierX1, config.BezierY1, config.BezierX2, config.BezierY2, config.DurationSec,
                    MotionRepeat.Of(config)),
                config.PropertyOverrides, config.DelaySec);
            return state == null ? 0.0 : state.EndSec + StylePropertyTransition.EarliestAfterSec(config.PropertyOverrides, config.DelaySec);
        }

        // context: what the play is planned against (StyleAnimationScheduler.SlotContext), so the span counts only
        // the channels the play has.
        public static double Of(StyleTransitionConfig config, string[]? fromClasses, string[]? toClasses,
            MotionSlotContext? context = null)
        {
            switch (config.Type)
            {
                case TransitionType.Bezier:
                    // A zero duration lands the pose at once, as a play with no channel does, so its repeat delay
                    // never runs. The built state measures an entry's default spring over the channel's own travel.
                    return config.DurationSec > 0f ? BezierEndSec(config, fromClasses, toClasses, context) : 0.0;
                case TransitionType.Spring:
                    // An instant spring ends where its delay does.
                    return !config.SpringIsInstant
                        && SpringIntegrator.AreValidParameters(config.Stiffness, config.Damping, config.Mass)
                        ? StylePropertyTransition.EarliestAfterSec(config.PropertyOverrides, config.DelaySec)
                        + MotionSpringDriver.SpanSec(
                            MotionSpringDriver.WithOverrides(
                                MotionSpringDriver.Create(MotionSpringClassParser.Resolve(fromClasses, toClasses, context: context),
                                    config.Stiffness, config.Damping, config.Mass, MotionRepeat.Of(config), config.SpringSettleSec,
                                    perValueDefaults: config.PerValueDefaults),
                                config.PropertyOverrides, config.DelaySec),
                            MotionRepeat.Of(config))
                        : 0.0;
                default:
                    return StyleAnimationScheduler.TweensOnSwap(config)
                        ? MotionRepeat.Of(config).TotalSec(config.DurationSec)
                        : config.DurationSec;
            }
        }
    }
}
