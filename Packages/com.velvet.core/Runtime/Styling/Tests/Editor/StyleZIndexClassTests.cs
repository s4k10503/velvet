using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>z-*</c> utility parser (StyleZIndexClass) against Tailwind v4's z utility: a bare
    /// non-negative integer (<c>z-15</c>), the arbitrary bracket form (<c>z-[999]</c>, <c>z-[-5]</c>), a leading
    /// "-" negating either, the cascade (last-wins), and rejection of anything that only looks like a z-* token
    /// (a suffix that is not an integer, a bare integer with a leading zero, a non-integer bracket). GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleZIndexClassTests
    {
        [Test]
        public void Given_AClassListWithNoZToken_When_CheckedForAZIndexClass_Then_ItReportsNone()
        {
            // Arrange
            var classes = new[] { "flex", "items-center", "absolute" };

            // Act
            var has = StyleZIndexClass.HasZIndexClass(classes);

            // Assert
            Assert.That(has, Is.False);
        }

        [Test]
        public void Given_AClassListWithAZToken_When_CheckedForAZIndexClass_Then_ItReportsPresent()
        {
            // Arrange
            var classes = new[] { "absolute", "z-10" };

            // Act
            var has = StyleZIndexClass.HasZIndexClass(classes);

            // Assert
            Assert.That(has, Is.True);
        }

        [Test]
        public void Given_ANegatedZTokenOnly_When_CheckedForAZIndexClass_Then_ItReportsPresent()
        {
            // Arrange — the prefix scan strips a leading dash before checking for "z-".
            var classes = new[] { "-z-20" };

            // Act
            var has = StyleZIndexClass.HasZIndexClass(classes);

            // Assert
            Assert.That(has, Is.True);
        }

        // GREEN_ON_BASE(refactor): the z-0 parse that giving TryParse a nullable result for z-auto must not move.
        [Test]
        public void Given_ZZero_When_Parsed_Then_ItResolvesToZero()
        {
            // Arrange / Act
            var ok = StyleZIndexClass.TryParse("z-0", out var z);

            // Assert
            Assert.That((ok, (int?)z), Is.EqualTo((true, (int?)0)));
        }

        [Test]
        public void Given_ZFifty_When_Parsed_Then_ItResolvesToFifty()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("z-50", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(50));
        }

        [Test]
        public void Given_AMidScaleNamedLevel_When_Parsed_Then_ItResolvesToThatLevel()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("z-30", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(30));
        }

        [Test]
        public void Given_ANegatedNamedLevel_When_Parsed_Then_TheResolvedValueIsNegative()
        {
            // Arrange / Act — Tailwind's negative-value convention: the sign prefixes the whole class.
            StyleZIndexClass.TryParse("-z-10", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(-10));
        }

        [Test]
        public void Given_APositiveArbitraryBracketValue_When_Parsed_Then_ItResolvesToThatInteger()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("z-[999]", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(999));
        }

        [Test]
        public void Given_ANegativeArbitraryBracketValue_When_Parsed_Then_ItResolvesToTheNegativeInteger()
        {
            // Arrange — the bracket itself carries the sign; there is no separate "-z-[5]" form.
            StyleZIndexClass.TryParse("z-[-5]", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(-5));
        }

        [Test]
        public void Given_ANonIntegerBracketValue_When_Parsed_Then_ItIsRejected()
        {
            // Arrange / Act
            var ok = StyleZIndexClass.TryParse("z-[abc]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ABareIntegerOffTheOldNamedLevels_When_Parsed_Then_ItResolvesToThatInteger()
        {
            // Arrange / Act — Tailwind v4 takes any non-negative integer as a bare z value.
            StyleZIndexClass.TryParse("z-15", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(15));
        }

        [Test]
        public void Given_ANegatedBareInteger_When_Parsed_Then_ItResolvesToItsNegation()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("-z-15", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(-15));
        }

        [Test]
        public void Given_ANegatedBracketInteger_When_Parsed_Then_TheBracketValueIsNegated()
        {
            // Arrange / Act — Tailwind v4 emits calc(5 * -1) for -z-[5].
            StyleZIndexClass.TryParse("-z-[5]", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(-5));
        }

        // GREEN_ON_BASE(characterization): the base rejects z-07 as off its named scale; this pins the
        // leading-zero rule that rejects it now.
        [Test]
        public void Given_ABareIntegerWithALeadingZero_When_Parsed_Then_ItIsRejected()
        {
            // Arrange / Act — Tailwind's isPositiveInteger requires the digits to print back unchanged.
            var ok = StyleZIndexClass.TryParse("z-07", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ZAuto_When_Parsed_Then_ItParsesAsAuto()
        {
            // Act
            var ok = StyleZIndexClass.TryParse("z-auto", out var z);

            // Assert
            Assert.That((ok, z), Is.EqualTo((true, (int?)null)));
        }

        [Test]
        public void Given_ANamedLevelFollowedByZAuto_When_Extracted_Then_NoZValueIsFound()
        {
            // Arrange — Tailwind's z-auto resets z-index to auto, so the element is left unstacked.
            var classes = new[] { "z-10", "z-auto" };

            // Act
            var found = StyleZIndexClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_AnImportantZAutoBeforeALaterPlainLevel_When_Extracted_Then_NoZValueIsFound()
        {
            // Arrange
            var classes = new[] { "!z-auto", "z-10" };

            // Act
            var found = StyleZIndexClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_AUserClassThatMerelyStartsWithZDash_When_Parsed_Then_ItIsRejected()
        {
            // Arrange — "z-boost" passes the cheap prefix scan but is not a real z utility.
            var ok = StyleZIndexClass.TryParse("z-boost", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_TwoZClassesInCascadeOrder_When_Extracted_Then_TheLaterClassWins()
        {
            // Arrange — CSS cascade: later classes win.
            var classes = new[] { "z-10", "z-40" };

            // Act
            StyleZIndexClass.TryExtract(classes, out var z);

            // Assert
            Assert.That(z, Is.EqualTo(40));
        }

        [Test]
        public void Given_ALeadingImportantZClassBeforeALaterPlainOne_When_Extracted_Then_TheImportantOneWins()
        {
            // Arrange
            var classes = new[] { "!z-10", "z-40" };

            // Act
            StyleZIndexClass.TryExtract(classes, out var z);

            // Assert
            Assert.That(z, Is.EqualTo(10));
        }

        [Test]
        public void Given_ATrailingImportantArbitraryZClassBeforeALaterPlainOne_When_Extracted_Then_TheImportantOneWins()
        {
            // Arrange
            var classes = new[] { "z-[5]!", "z-40" };

            // Act
            StyleZIndexClass.TryExtract(classes, out var z);

            // Assert
            Assert.That(z, Is.EqualTo(5));
        }

        [Test]
        public void Given_OnlyAnUnparseableZLookingToken_When_Extracted_Then_NoZValueIsFound()
        {
            // Arrange — every z-looking token in the list fails to parse.
            var classes = new[] { "flex", "z-boost" };

            // Act
            var found = StyleZIndexClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_ALeadingImportantBangOnANamedLevel_When_CheckedForAZIndexClass_Then_ItReportsPresent()
        {
            // Arrange
            var classes = new[] { "!z-10" };

            // Act
            var has = StyleZIndexClass.HasZIndexClass(classes);

            // Assert
            Assert.That(has, Is.True);
        }

        [Test]
        public void Given_ALeadingImportantBangOnANamedLevel_When_Parsed_Then_ItResolvesTheSameLevel()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("!z-10", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(10));
        }

        [Test]
        public void Given_ATrailingImportantBangOnANamedLevel_When_Parsed_Then_ItResolvesTheSameLevel()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("z-10!", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(10));
        }

        [Test]
        public void Given_ALeadingImportantBangOnAnArbitraryBracketValue_When_Parsed_Then_ItResolvesTheBracketInteger()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("!z-[5]", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(5));
        }

        [Test]
        public void Given_ATrailingImportantBangOnAnArbitraryBracketValue_When_Parsed_Then_ItResolvesTheBracketInteger()
        {
            // Arrange / Act
            StyleZIndexClass.TryParse("z-[5]!", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(5));
        }

        [Test]
        public void Given_ANegatedTokenWithAnEmbeddedBang_When_Parsed_Then_ItIsRejected()
        {
            // Arrange / Act — the bang sits after the leading dash, a shape StripImportant does not recognize
            // (it only strips a bang at the very first or very last character), so this stays rejected.
            var ok = StyleZIndexClass.TryParse("-!z-10", out _);

            // Assert
            Assert.That(ok, Is.False);
        }
    }
}
