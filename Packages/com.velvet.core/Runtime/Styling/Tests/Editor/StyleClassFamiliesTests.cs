using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // Every pass whose gate reads a class array's family bit must find that bit wherever its own reading acts on the
    // array: a bit missing for a token the reading acts on skips the pass for an element that carries the token.
    internal sealed class StyleClassFamiliesTests
    {
        // Forms beside the bundled utilities that only a pass's own parse reads.
        private static readonly string[] s_written =
        {
            "skew-x-[8deg]", "-skew-x-6", "skew-[0.1turn]", "skew-y-[-3deg]", "!skew-x-3", "skew-x-[1]",
            "shadow-[0_0_4px_red]", "shadow-lg/50", "inset-shadow-sm", "inset-shadow-[0_1px_2px_red]", "inset-ring",
            "inset-ring-2", "!shadow-md", "shadow-md!", "shadow-red-500",
            "ring-[3px]", "ring-red-500/50", "outline-[2px]", "-outline-offset-2", "ring-offset-[3px]",
            "backdrop-blur-[4px]", "-backdrop-hue-rotate-30", "!backdrop-blur-sm", "backdrop-filter-[blur(2px)]",
            "z-[5]", "-z-10", "z-(--z)", "!z-20", "order-[3]", "-order-1", "order-(--o)",
            "gap-[4px]", "!gap-2", "gap-x-[1rem]", "space-x-[2px]", "-space-y-4", "space-x-reverse",
            "grid-cols-[repeat(3,1fr)]", "grid-cols-(--cols)",
            "divide-x-[2px]", "!divide-y", "divide-red-500", "divide-y-reverse",
            "border-[3px]", "border-t-[0.5rem]", "border-x-4", "border-s-2", "!border-2",
            "transition-filter", "hover:transition-filter", "ease-in", "md:ease-in-out", "transition-[opacity]",
            "animate-spin", "animate-pulse", "animate-ping", "animate-bounce", "animate-hue", "animate-gradient",
            "animate-shimmer", "animate-spin-[2500ms]", "animate-[spin_1s_linear]", "animate-none",
            "border-dashed", "border-dotted", "border-solid",
            "bg-linear-to-r", "bg-linear-45", "-bg-linear-45", "bg-radial-[at_top]", "bg-conic", "bg-conic-180",
            "bg-gradient-to-r", "bg-linear-[to_right,red,blue]",
            "pointer-events-none!", "!pointer-events-auto",
            "absolute", "relative", "!absolute",
            "[&>*]:mt-2", "[&>*]:shadow-lg",
        };

        private static readonly string[] s_variantPrefixes =
        {
            string.Empty, "hover:", "md:", "dark:hover:", "group-hover:", "[&>*]:", "has-[:checked]:", "first:", "!",
        };

        private static string[] Corpus()
        {
            var bases = StyleUtilityTableProbe.ClassNames().Concat(s_written).Distinct().ToArray();
            var tokens = new List<string>(bases.Length * (s_variantPrefixes.Length + 1));
            foreach (var cls in bases)
            {
                foreach (var prefix in s_variantPrefixes)
                {
                    tokens.Add(prefix + cls);
                }
                tokens.Add(cls + "!");
                tokens.Add("-" + cls);
            }
            return tokens.ToArray();
        }

        // Each family's own reading of a class array: the parse its pass acts on, or for a family whose gate is the
        // whole reading, that gate's token predicate.
        private static readonly (StyleClassFamily Family, Func<string[], bool> Acts)[] s_readings =
        {
            (StyleClassFamily.Gradient, c => StyleGradientClass.TryExtract(c, out _)),
            (StyleClassFamily.Animate, c => StyleAnimateClass.TryExtract(c, out _)),
            (StyleClassFamily.FilterTransition, c => c.Any(FiberFilterTransitionApplier.IsFilterTransitionToken)),
            (StyleClassFamily.EaseCurve, c => c.Any(StyleEaseCurve.IsTimingToken)),
            (StyleClassFamily.Skew, c => StyleSkewClass.TryGetWinningSkewClasses(c, out _, out _)),
            (StyleClassFamily.Shadow, c => StyleShadowClass.TryExtract(c, out _)),
            (StyleClassFamily.BorderStyle, c => StyleBorderStyleClass.TryGetWinningBorderStyleClass(c, out _)),
            (StyleClassFamily.BorderWidth, BorderPreflight.HasBorderWidth),
            (StyleClassFamily.Ring, c => StyleRingClass.TryExtract(c, out _)),
            (StyleClassFamily.Backdrop, c => StyleBackdropClass.TryExtract(c, out _)),
            (StyleClassFamily.ChildVariant, c => StyleChildVariantClass.TryExtract(c, out _)),
            (StyleClassFamily.Gap, c => StyleGapClass.Extract(c).IsActive),
            (StyleClassFamily.Grid, c => StyleGridClass.TryExtract(c, out _)),
            (StyleClassFamily.Divide, c => StyleDivideClass.TryExtract(c, out _)),
            (StyleClassFamily.PointerEvents, c => StylePointerEventsClass.Read(c) != PointerEventsMode.Inherit),
            (StyleClassFamily.Stacking, c => c.Any(FiberZLayerCoordinator.IsStackingToken)),
            (StyleClassFamily.ZIndex, c => StyleZIndexClass.TryExtract(c, out _)),
            (StyleClassFamily.Order, c => StyleOrderClass.TryExtract(c, out _, out _)),
        };

        // A gradient activator draws only beside a colour stop, so each token is read beside one; a stop opens no
        // activator, so it sets no gradient bit of its own.
        private static string[] ArrayOf(string token, StyleClassFamily family)
            => family == StyleClassFamily.Gradient ? new[] { token, "from-red-500" } : new[] { token };

        private static IEnumerable<TestCaseData> Families()
            => s_readings.Select(r => new TestCaseData(r.Family).SetName(
                "Given_TheCorpusTokens_When_The" + r.Family + "ReadingActsOnOne_Then_ItsFamilyBitIsSet"));

        [TestCaseSource(nameof(Families))]
        public void Given_TheCorpusTokens_When_AFamilysReadingActsOnOne_Then_ItsFamilyBitIsSet(StyleClassFamily family)
        {
            // Arrange
            var acts = s_readings.Single(r => r.Family == family).Acts;
            var read = Corpus().Select(t => ArrayOf(t, family)).Where(acts).ToArray();

            // Act
            var unflagged = read.Length == 0
                ? new[] { "no corpus token is read by " + family }
                : read.Where(c => !StyleClassFamilies.Has(c, family)).Select(c => c[0]).ToArray();

            // Assert
            Assert.That(unflagged, Is.Empty);
        }
    }
}
