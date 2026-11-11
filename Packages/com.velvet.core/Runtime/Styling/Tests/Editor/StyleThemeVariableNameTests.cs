using System.Collections;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the table of theme variable names stops keeping names once it holds as many as it is capped at.
    /// The table is a memo of a pure concatenation, so emptying it around a case changes no other case's answer.
    /// GWT, one assert per case.
    /// </summary>
    internal sealed class StyleThemeVariableNameTests
    {
        private static IDictionary Names()
            => (IDictionary)typeof(StyleThemeVariableName)
                .GetField("s_names", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static int Cap()
            => (int)typeof(StyleThemeVariableName)
                .GetField("MaxCached", BindingFlags.NonPublic | BindingFlags.Static)
                .GetRawConstantValue();

        [SetUp]
        public void SetUp() => Names().Clear();

        [TearDown]
        public void TearDown() => Names().Clear();

        [Test]
        public void Given_ATableHoldingAsManyNamesAsItIsCappedAt_When_ANewNameIsAskedForTwice_Then_EachAskBuildsItAgain()
        {
            // Arrange
            for (var i = 0; i < Cap(); i++)
            {
                StyleThemeVariableName.Of("--test-", i.ToString(CultureInfo.InvariantCulture));
            }

            // Act
            var first = StyleThemeVariableName.Of("--test-", "beyond");
            var second = StyleThemeVariableName.Of("--test-", "beyond");

            // Assert
            Assert.That(ReferenceEquals(first, second), Is.False);
        }
    }
}
