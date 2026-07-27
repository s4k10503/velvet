using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the color- and length-valued channels a spring/bezier variant delta resolves beyond the
    /// opacity/translate/scale/rotate quartet: which class pairs become a channel
    /// (<see cref="MotionPropertyClassParser"/> / <see cref="MotionSpringClassParser.Resolve"/>), the values the
    /// drivers write while ticking, the shorthand fan-out on both the write and the clear side, the suspension
    /// of the element's own native USS transition while a driver owns its inline styles, and the deliberate
    /// holes (one-sided and mixed-unit pairs, the pill sentinel, semantic theme tokens) that keep falling back
    /// to the plain class swap.
    /// </summary>
    /// <remarks>
    /// Panel-free by design, exactly like <see cref="MotionSpringDriverTests"/> / <see cref="BezierTweenDriverTests"/>:
    /// the drivers never read <c>resolvedStyle</c> (every value comes from the class strings), and the recurring
    /// tick they register needs a live panel clock the EditMode PlayerLoop never drives — so the tick's own math
    /// is exercised by calling <c>Step</c> directly and reading the INLINE style back. Exact-value cases use the
    /// bezier driver on the linear identity curve <c>cubic-bezier(0,0,1,1)</c>, where eased progress equals
    /// elapsed/duration, so the expected value is plain arithmetic rather than a re-derivation of the solver;
    /// the spring is used where only monotone progress is meaningful. GWT, one assert per case (Assume for
    /// preconditions).
    /// </remarks>
    [TestFixture]
    internal sealed class MotionPropertyChannelTests
    {
        private const float FixedDeltaSec = 1f / 60f;

        // The linear identity curve: CubicBezierEvaluator returns t unchanged for x1==y1 && x2==y2, so a step
        // to half the duration lands the channel at exactly half its travel.
        private const float LinearX1 = 0f;
        private const float LinearY1 = 0f;
        private const float LinearX2 = 1f;
        private const float LinearY2 = 1f;

        private static BezierTweenState CreateHalfway(VisualElement element, string[] from, string[] to)
        {
            var plan = MotionSpringClassParser.Resolve(from, to);
            var state = BezierTweenDriver.Create(plan, LinearX1, LinearY1, LinearX2, LinearY2, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the class pair resolves at least one channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            BezierTweenDriver.Step(element, state!, 0.5f);
            return state!;
        }

        [Test]
        public void Given_ABackgroundColorPair_When_SteppedToHalfTheDuration_Then_TheInlineBackgroundColorIsTheExactMidpoint()
        {
            // Arrange
            var element = new VisualElement();

            // Act — black -> white on a linear curve, sampled at exactly half the duration.
            CreateHalfway(element, new[] { "bg-black" }, new[] { "bg-white" });

            // Assert — the straight-RGBA midpoint of #000000 and #ffffff.
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(new Color(0.5f, 0.5f, 0.5f, 1f)));
        }

        [Test]
        public void Given_ABorderColorPair_When_SteppedToHalfTheDuration_Then_AllFourSidesCarryTheInterpolatedColor()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            CreateHalfway(element, new[] { "border-black" }, new[] { "border-white" });

            // Assert — border-color is a four-slot shorthand, so the driver's per-frame write must reach every side.
            var mid = new Color(0.5f, 0.5f, 0.5f, 1f);
            Assert.That(
                (element.style.borderTopColor.value, element.style.borderRightColor.value,
                    element.style.borderBottomColor.value, element.style.borderLeftColor.value),
                Is.EqualTo((mid, mid, mid, mid)));
        }

        [Test]
        public void Given_AColorPairCarryingAnAlphaModifier_When_SteppedToHalfTheDuration_Then_TheAlphaInterpolatesToo()
        {
            // Arrange
            var element = new VisualElement();

            // Act — the /N modifier overwrites the base color's alpha, so this pair travels 0.2 -> 0.8 in alpha.
            CreateHalfway(element, new[] { "bg-red-500/20" }, new[] { "bg-red-500/80" });

            // Assert
            Assert.That(element.style.backgroundColor.value.a, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_AWidthPairOnTheSpacingScale_When_SteppedToHalfTheDuration_Then_TheInlineWidthIsTheExactMidpoint()
        {
            // Arrange
            var element = new VisualElement();

            // Act — w-0 (0px) -> w-64 (--space-64 == 256px).
            CreateHalfway(element, new[] { "w-0" }, new[] { "w-64" });

            // Assert
            Assert.That(element.style.width.value, Is.EqualTo(new Length(128f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_APaddingShorthandPair_When_SteppedToHalfTheDuration_Then_AllFourEdgesCarryTheInterpolatedLength()
        {
            // Arrange
            var element = new VisualElement();

            // Act — p-0 (0px) -> p-8 (--space-8 == 32px).
            CreateHalfway(element, new[] { "p-0" }, new[] { "p-8" });

            // Assert
            var mid = new Length(16f, LengthUnit.Pixel);
            Assert.That(
                (element.style.paddingTop.value, element.style.paddingRight.value,
                    element.style.paddingBottom.value, element.style.paddingLeft.value),
                Is.EqualTo((mid, mid, mid, mid)));
        }

        [Test]
        public void Given_ABorderRadiusPair_When_SteppedToHalfTheDuration_Then_AllFourCornersCarryTheInterpolatedRadius()
        {
            // Arrange
            var element = new VisualElement();

            // Act — rounded-none (0px) -> rounded-3xl (--radius-3xl == 24px).
            CreateHalfway(element, new[] { "rounded-none" }, new[] { "rounded-3xl" });

            // Assert
            var mid = new Length(12f, LengthUnit.Pixel);
            Assert.That(
                (element.style.borderTopLeftRadius.value, element.style.borderTopRightRadius.value,
                    element.style.borderBottomLeftRadius.value, element.style.borderBottomRightRadius.value),
                Is.EqualTo((mid, mid, mid, mid)));
        }

        [Test]
        public void Given_AnArbitraryLengthPair_When_SteppedToHalfTheDuration_Then_TheBracketMagnitudesInterpolate()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            CreateHalfway(element, new[] { "h-[40px]" }, new[] { "h-[100px]" });

            // Assert
            Assert.That(element.style.height.value, Is.EqualTo(new Length(70f, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_APercentLengthPairOnBothSides_When_SteppedToHalfTheDuration_Then_TheChannelStaysInPercent()
        {
            // Arrange
            var element = new VisualElement();

            // Act — the sizing fractions resolve to percentages of the parent, which UI Toolkit resolves itself.
            CreateHalfway(element, new[] { "w-1/4" }, new[] { "w-3/4" });

            // Assert
            Assert.That(element.style.width.value, Is.EqualTo(new Length(50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_ASpringDrivenColorChannel_When_SteppedRepeatedly_Then_TheInlineColorMovesTowardTheTarget()
        {
            // Arrange — a spring has no fixed duration, so only monotone progress is meaningful here.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "bg-black" }, new[] { "bg-white" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the class pair resolves a background-color channel");
            MotionSpringDriver.ApplyCurrentValues(element, state!);

            // Act
            for (var i = 0; i < 5; i++)
            {
                MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            var early = element.style.backgroundColor.value.r;
            for (var i = 0; i < 40; i++)
            {
                MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            var later = element.style.backgroundColor.value.r;

            // Assert
            Assert.That(later, Is.GreaterThan(early));
        }

        [Test]
        public void Given_ASpringDrivenLengthChannel_When_SteppedUntilSettled_Then_TheInlineLengthRestsNearTheTarget()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "w-0" }, new[] { "w-32" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the class pair resolves a width channel");
            MotionSpringDriver.ApplyCurrentValues(element, state!);

            // Act — step until every channel reports settled (the cap only guards a never-settling regression).
            var settled = false;
            for (var i = 0; i < 600 && !settled; i++)
            {
                settled = MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(settled, Is.True, "Precondition: the spring settled within the tick budget");

            // Assert — w-32 is --space-32 (128px); the settle tolerance is the driver's own pixel rest epsilon.
            Assert.That(element.style.width.value.value, Is.EqualTo(128f).Within(0.1f));
        }

        [Test]
        public void Given_ASettledPropertyDrivenPlay_When_TheInlineOverridesAreCleared_Then_EveryFannedOutCornerIsReleased()
        {
            // Arrange
            var element = new VisualElement();
            var state = CreateHalfway(element, new[] { "rounded-none" }, new[] { "rounded-3xl" });
            Assume.That(element.style.borderTopLeftRadius.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the driver owns the corner radii while it ticks");

            // Act — the scheduler calls this once the play finishes, handing the slots back to the classes.
            BezierTweenDriver.ClearInlineOverrides(element, state);

            // Assert — a shorthand clear must release every corner it wrote, not just the first.
            Assert.That(
                (element.style.borderTopLeftRadius.keyword, element.style.borderTopRightRadius.keyword,
                    element.style.borderBottomLeftRadius.keyword, element.style.borderBottomRightRadius.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AnElementCarryingANativeColorTransition_When_ADriverOwnsItsInlineStyles_Then_TheNativeTransitionNamesNoProperty()
        {
            // Arrange — .transition-colors declares transition-property: background-color, border-color, color,
            // which would restart a fresh native transition on every per-frame inline write the driver makes.
            var element = new VisualElement();
            element.AddToClassList("transition-colors");

            // Act
            CreateHalfway(element, new[] { "bg-black" }, new[] { "bg-white" });

            // Assert — the inline transition-property resolves to no style property at all, so the element
            // computes zero transitions and the driver's writes reach the paint unfiltered.
            var declared = element.style.transitionProperty.value;
            Assert.That(declared is { Count: 1 } && StylePropertyName.IsNullOrEmpty(declared[0]), Is.True);
        }

        [Test]
        public void Given_ADriverSuspendedNativeTransition_When_TheInlineOverridesAreCleared_Then_TheElementsOwnTransitionIsRestored()
        {
            // Arrange
            var element = new VisualElement();
            element.AddToClassList("transition-colors");
            var state = CreateHalfway(element, new[] { "bg-black" }, new[] { "bg-white" });
            Assume.That(element.style.transitionProperty.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the driver suspended the element's native transition while it ticks");

            // Act
            BezierTweenDriver.ClearInlineOverrides(element, state);

            // Assert — clearing the inline override hands transition-property straight back to the cascade.
            Assert.That(element.style.transitionProperty.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AColorNamedOnOnlyOneSide_When_Resolved_Then_NoColorChannelIsPlanned()
        {
            // Arrange / Act — a color has no identity value the silent side could stand in for, unlike the
            // opacity/transform axes, so a one-sided color must fall back to the plain class swap.
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100", "bg-red-500" });
            Assume.That(plan.Opacity, Is.Not.Null, "Precondition: the opacity axis still resolves both sides");

            // Assert
            Assert.That(plan.Colors, Is.Null);
        }

        [Test]
        public void Given_ALengthPairWithMismatchedUnits_When_Resolved_Then_NoLengthChannelIsPlanned()
        {
            // Arrange / Act — a percentage resolves against a laid-out parent this path cannot consult, so a
            // px <-> % pair has no common space to interpolate in.
            var plan = MotionSpringClassParser.Resolve(new[] { "w-1/2" }, new[] { "w-[200px]" });

            // Assert
            Assert.That(plan.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ARoundedFullSide_When_Resolved_Then_NoRadiusChannelIsPlanned()
        {
            // Arrange / Act — rounded-full is a saturating pill sentinel rather than a magnitude, so animating
            // toward it would read as a wildly oversized radius rather than as a shape change.
            var plan = MotionSpringClassParser.Resolve(new[] { "rounded-none" }, new[] { "rounded-full" });

            // Assert
            Assert.That(plan.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ASemanticThemeTokenPair_When_Resolved_Then_NoColorChannelIsPlanned()
        {
            // Arrange / Act — bg-primary / bg-surface resolve through --color-* theme tokens that have no C#
            // mirror, so no numeric endpoint exists to interpolate between.
            var plan = MotionSpringClassParser.Resolve(new[] { "bg-surface" }, new[] { "bg-primary" });

            // Assert
            Assert.That(plan.IsEmpty, Is.True);
        }

        [Test]
        public void Given_AKeywordLengthSide_When_Resolved_Then_NoLengthChannelIsPlanned()
        {
            // Arrange / Act — w-auto is a sizing MODE, not a magnitude.
            var plan = MotionSpringClassParser.Resolve(new[] { "w-0" }, new[] { "w-auto" });

            // Assert
            Assert.That(plan.IsEmpty, Is.True);
        }

        [Test]
        public void Given_AColorsOnlySpringVariantEnter_When_Started_Then_TheCompletionDoesNotFireSynchronously()
        {
            // Arrange — the element already carries the resting (to) class, matching PlayVariantEnter's
            // precondition; a spring play that resolves no channel completes from inside the call itself.
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            element.AddToClassList("bg-blue-500");
            var completed = false;

            // Act
            scheduler.PlayVariantEnter(element, fromClasses: new[] { "bg-red-500" }, toClasses: new[] { "bg-blue-500" },
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f, onComplete: () => completed = true,
                type: TransitionType.Spring);

            // Assert — a colors-only delta is a real animation now, so its completion waits for the spring to settle.
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Given_AColorsOnlySpringVariantEnter_When_Started_Then_TheInlineColorShowsTheFromValue()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            element.AddToClassList("bg-white");

            // Act
            scheduler.PlayVariantEnter(element, fromClasses: new[] { "bg-black" }, toClasses: new[] { "bg-white" },
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f, type: TransitionType.Spring);

            // Assert — the from-pose is written synchronously so the element does not flash at the resting color.
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(new Color(0f, 0f, 0f, 1f)));
        }

        [Test]
        public void Given_AnOvershootingBezierCurve_When_SteppedPastTheTarget_Then_TheEmittedColorStaysRepresentable()
        {
            // Arrange — an anticipate curve samples eased values below 0, which would drive a straight lerp to
            // negative colour components; a colour outside [0,1] is not something any renderer can show.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "bg-black" }, new[] { "bg-white" });
            var state = BezierTweenDriver.Create(plan, x1: 0.5f, y1: -1.5f, x2: 0.5f, y2: 1f, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the class pair resolves a background-color channel");
            Assume.That(CubicBezierEvaluator.Evaluate(0.5f, -1.5f, 0.5f, 1f, 0.25f), Is.LessThan(0f),
                "Precondition: the curve's anticipate lobe is below zero at this sample");

            // Act
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            BezierTweenDriver.Step(element, state!, 0.25f);

            // Assert
            Assert.That(element.style.backgroundColor.value.r, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AVariantDeltaMixingAColorAndTheOpacityAxis_When_Resolved_Then_BothChannelsArePlanned()
        {
            // Arrange / Act — the property channels are additive to the fixed axes, not a replacement for them.
            var plan = MotionSpringClassParser.Resolve(
                new[] { "opacity-0", "bg-black" }, new[] { "opacity-100", "bg-white" });

            // Assert
            Assert.That((plan.Opacity != null, plan.Colors?.Count ?? 0), Is.EqualTo((true, 1)));
        }

        [Test]
        public void Given_TwoUtilitiesOnTheSamePropertyInOneVariant_When_Resolved_Then_TheLastOneWins()
        {
            // Arrange / Act — mirrors the CSS cascade the class list itself would apply.
            var plan = MotionSpringClassParser.Resolve(new[] { "w-0", "w-4" }, new[] { "w-8" });
            Assume.That(plan.Lengths, Is.Not.Null, "Precondition: the width pair resolves a channel");

            // Assert — w-4 is --space-4 (16px), so the later class is the one the channel starts from.
            Assert.That(plan.Lengths![0].From, Is.EqualTo(16f));
        }
    }
}
