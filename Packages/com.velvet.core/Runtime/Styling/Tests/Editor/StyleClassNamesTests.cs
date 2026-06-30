using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the <see cref="StyleClassNames"/> class-name builder.
    /// <list type="bullet">
    /// <item><see cref="StyleClassNames.Class"/> joins its parts with a single space and returns the empty string when given no parts.</item>
    /// <item><see cref="StyleClassNames.Class"/> skips <c>null</c> and empty parts, so an all-null/empty input yields the empty string.</item>
    /// <item><see cref="StyleClassNames.When"/> returns the class name when the condition is true and <c>null</c> when it is false,
    /// so it composes directly as a part of <see cref="StyleClassNames.Class"/>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StyleClassNamesTests
    {
        #region StyleClassNames.Class

        [Test]
        public void Given_NoParts_When_ClassBuilt_Then_ReturnsEmptyString()
        {
            // Act
            var result = StyleClassNames.Class();

            // Assert
            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void Given_SinglePart_When_ClassBuilt_Then_ReturnsThatPart()
        {
            // Act
            var result = StyleClassNames.Class("btn");

            // Assert
            Assert.That(result, Is.EqualTo("btn"));
        }

        [Test]
        public void Given_MultipleParts_When_ClassBuilt_Then_JoinsWithSingleSpace()
        {
            // Act
            var result = StyleClassNames.Class("btn", "btn--primary", "btn--lg");

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--primary btn--lg"));
        }

        [Test]
        public void Given_NullParts_When_ClassBuilt_Then_SkipsNullParts()
        {
            // Act
            var result = StyleClassNames.Class("btn", null, "btn--primary");

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--primary"));
        }

        [Test]
        public void Given_EmptyParts_When_ClassBuilt_Then_SkipsEmptyParts()
        {
            // Act
            var result = StyleClassNames.Class("btn", "", "btn--primary");

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--primary"));
        }

        [Test]
        public void Given_AllNullOrEmptyParts_When_ClassBuilt_Then_ReturnsEmptyString()
        {
            // Act
            var result = StyleClassNames.Class(null, "", null, "");

            // Assert
            Assert.That(result, Is.EqualTo(""));
        }

        #endregion

        #region StyleClassNames.When

        [Test]
        public void Given_TrueCondition_When_WhenEvaluated_Then_ReturnsClassName()
        {
            // Act
            var result = StyleClassNames.When(true, "active");

            // Assert
            Assert.That(result, Is.EqualTo("active"));
        }

        [Test]
        public void Given_FalseCondition_When_WhenEvaluated_Then_ReturnsNull()
        {
            // Act
            var result = StyleClassNames.When(false, "active");

            // Assert
            Assert.That(result, Is.Null);
        }

        #endregion

        #region Integration

        [Test]
        public void Given_WhenResultsAsParts_When_ClassBuilt_Then_KeepsOnlyTrueConditionedClasses()
        {
            // Act
            var result = StyleClassNames.Class(
                "btn",
                StyleClassNames.When(true, "btn--active"),
                StyleClassNames.When(false, "btn--disabled"));

            // Assert
            Assert.That(result, Is.EqualTo("btn btn--active"));
        }

        #endregion
    }
}
