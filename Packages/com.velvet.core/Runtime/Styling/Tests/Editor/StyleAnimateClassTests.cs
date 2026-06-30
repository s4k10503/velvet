using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Parser coverage for <see cref="StyleAnimateClass"/>: the animate-* motion utilities resolved into an
    /// <see cref="AnimateSpec"/>. animate-gradient / -shimmer / -hue carry per-mode default durations; a
    /// -[&lt;time&gt;] suffix overrides; animate-none cancels (cascade last-wins). Unknown animate-* tokens are
    /// not claimed, leaving the namespace open. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleAnimateClassTests
    {
        private static AnimateSpec Extract(params string[] classNames)
        {
            Assume.That(StyleAnimateClass.TryExtract(classNames, out var spec), Is.True,
                "Precondition: the class list resolves to an animation");
            return spec;
        }

        // (token, expected mode) — one row per recognized animate-* motion. Verbatim from the per-mode tests.
        private static readonly TestCaseData[] ModeCases =
        {
            new TestCaseData("animate-gradient", AnimateMode.Gradient).SetName("Given_AnimateGradient_When_Extracted_Then_ModeIsGradient"),
            new TestCaseData("animate-shimmer", AnimateMode.Shimmer).SetName("Given_AnimateShimmer_When_Extracted_Then_ModeIsShimmer"),
            new TestCaseData("animate-hue", AnimateMode.Hue).SetName("Given_AnimateHue_When_Extracted_Then_ModeIsHue"),
            new TestCaseData("animate-pulse", AnimateMode.Pulse).SetName("Given_AnimatePulse_When_Extracted_Then_ModeIsPulse"),
        };

        [TestCaseSource(nameof(ModeCases))]
        public void Mode_FromToken(string token, object expected)
        {
            // Given the animate-* token / When extracted / Then the mode resolves.
            // expected is typed object because AnimateMode is internal (a public test method cannot take it directly).
            Assert.That(Extract(token).Mode, Is.EqualTo((AnimateMode)expected));
        }

        // (token, expected default seconds) — verbatim from the per-mode default-duration tests. animate-shimmer
        // is omitted: it carries a distinct default (no default-duration test existed for it), kept out of the table.
        private static readonly TestCaseData[] DefaultDurationCases =
        {
            new TestCaseData("animate-gradient", 3f).SetName("Given_AnimateGradient_When_Extracted_Then_UsesDefaultDuration"),
            new TestCaseData("animate-hue", 4f).SetName("Given_AnimateHue_When_Extracted_Then_UsesDefaultFourSeconds"),
            new TestCaseData("animate-pulse", 2f).SetName("Given_AnimatePulse_When_Extracted_Then_UsesDefaultTwoSeconds"),
        };

        [TestCaseSource(nameof(DefaultDurationCases))]
        public void DefaultDuration_FromToken(string token, float expectedSec)
        {
            // Given the animate-* token with no -[<time>] override / When extracted / Then the per-mode default applies.
            Assert.That(Extract(token).DurationSec, Is.EqualTo(expectedSec));
        }

        [Test]
        public void Given_DurationSuffixInSeconds_When_Extracted_Then_OverridesDefault()
        {
            Assert.That(Extract("animate-gradient-[2s]").DurationSec, Is.EqualTo(2f));
        }

        [Test]
        public void Given_DurationSuffixInMilliseconds_When_Extracted_Then_ResolvesToSeconds()
        {
            Assert.That(Extract("animate-hue-[500ms]").DurationSec, Is.EqualTo(0.5f));
        }

        [Test]
        public void Given_AnimateNone_When_Extracted_Then_NoAnimation()
        {
            // animate-none is the explicit cancel: recognized as a token but resolves to no animation.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-none" }, out _), Is.False);
        }

        [Test]
        public void Given_GradientThenNone_When_Extracted_Then_LaterNoneCancels()
        {
            // Cascade: the later animate-none wins over the earlier animate-gradient.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-gradient", "animate-none" }, out _), Is.False);
        }

        [Test]
        public void Given_GradientThenHue_When_Extracted_Then_LastModeWins()
        {
            Assert.That(Extract("animate-gradient", "animate-hue").Mode, Is.EqualTo(AnimateMode.Hue));
        }

        [Test]
        public void Given_UnknownAnimateToken_When_Extracted_Then_NotClaimed()
        {
            // animate-spin is not a Velvet motion (yet); the namespace stays open, so it is not claimed.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-spin" }, out _), Is.False);
        }

        [Test]
        public void Given_InvalidDurationSuffix_When_Extracted_Then_NotClaimed()
        {
            // animate-hue-[abc] has an unparseable time, so the whole token is rejected.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-hue-[abc]" }, out _), Is.False);
        }

        [Test]
        public void Given_PlainUtilities_When_Extracted_Then_NotAnimated()
        {
            Assert.That(StyleAnimateClass.TryExtract(new[] { "bg-red-500", "p-4" }, out _), Is.False);
        }

        [Test]
        public void Given_TwoSpecsSameModeAndDuration_When_Compared_Then_Equal()
        {
            Assert.That(new AnimateSpec(AnimateMode.Hue, 4f), Is.EqualTo(new AnimateSpec(AnimateMode.Hue, 4f)));
        }
    }
}
