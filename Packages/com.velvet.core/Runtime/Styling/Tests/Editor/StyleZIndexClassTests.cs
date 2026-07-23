using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>z-*</c> utility parser (StyleZIndexClass): the fixed named scale
    /// (<c>z-0</c>…<c>z-50</c>), its negated form (<c>-z-10</c>), the arbitrary bracket form
    /// (<c>z-[999]</c>, <c>z-[-5]</c>), the cascade (last-wins), and rejection of anything that only
    /// looks like a z-* token (an unrecognized named level, a non-integer bracket, a bare "z-"
    /// prefix). GWT, one assert per case.
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

        [Test]
        public void Given_ZZero_When_Parsed_Then_ItResolvesToZero()
        {
            // Arrange / Act
            var ok = StyleZIndexClass.TryParse("z-0", out var z);

            // Assert
            Assert.That((ok, z), Is.EqualTo((true, 0)));
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
        public void Given_AnUnrecognizedNamedLevel_When_Parsed_Then_ItIsRejected()
        {
            // Arrange — z-15 is not on the fixed scale; only z-[15] would express it.
            var ok = StyleZIndexClass.TryParse("z-15", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ZAuto_When_Parsed_Then_ItIsRejected()
        {
            // Arrange — z-auto is not part of Velvet's simplified two-layer numeric scale.
            var ok = StyleZIndexClass.TryParse("z-auto", out _);

            // Assert
            Assert.That(ok, Is.False);
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
            // Arrange / Act — the bang is a no-op for this non-cascade utility; it must not block parsing.
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
