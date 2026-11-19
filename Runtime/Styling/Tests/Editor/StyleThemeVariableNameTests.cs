using System;
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
    /// <remarks>
    /// The table is reached by name, so the fixture compiles on a tree that has no such table; there the case reads
    /// null, which its expected value is not.
    /// </remarks>
    internal sealed class StyleThemeVariableNameTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly Type s_table = typeof(StyleThemeClass).Assembly.GetType("Velvet.StyleThemeVariableName");

        private static IDictionary Names() => s_table?.GetField("s_names", Private)?.GetValue(null) as IDictionary;

        private static Func<string, string, string> Builder()
        {
            var method = s_table?.GetMethod("Of", Private);
            return method == null
                ? null
                : (Func<string, string, string>)Delegate.CreateDelegate(typeof(Func<string, string, string>), method);
        }

        private static int Cap() => s_table?.GetField("MaxCached", Private)?.GetRawConstantValue() is int cap ? cap : 0;

        [SetUp]
        public void SetUp() => Names()?.Clear();

        [TearDown]
        public void TearDown() => Names()?.Clear();

        [Test]
        public void Given_ATableHoldingAsManyNamesAsItIsCappedAt_When_ANewNameIsAskedForTwice_Then_EachAskBuildsItAgain()
        {
            // Arrange
            var build = Builder();
            for (var i = 0; build != null && i < Cap(); i++)
            {
                build("--test-", i.ToString(CultureInfo.InvariantCulture));
            }

            // Act
            var kept = build == null
                ? (bool?)null
                : ReferenceEquals(build("--test-", "beyond"), build("--test-", "beyond"));

            // Assert
            Assert.That(kept, Is.EqualTo(false));
        }
    }
}
