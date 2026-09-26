using System;
using System.Linq;
using Velvet.StyleTable;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins what the corner-radius declaration sheet restates for each radius shape a rule can declare.
    /// </summary>
    public sealed class CornerRadiusDeclarationSheetTests
    {
        private static CornerRadiusDeclarationResult Build(params string[] sheets) =>
            CornerRadiusDeclarationSheet.Build(
                sheets.Select((text, i) => new UssSourceText($"/styles/_test{i}.uss", text)).ToList());

        // The emitted rules, without the header comment every sheet carries.
        private static string[] RulesOf(CornerRadiusDeclarationResult result) =>
            result.EmittedSheet
                .Split('\n')
                .Where(line => line.StartsWith(".", StringComparison.Ordinal))
                .ToArray();

        [Fact]
        public void Given_AOneValueShorthand_When_Derived_Then_EveryCornerIsRestatedWithThatValue()
        {
            // Arrange
            var result = Build(".rounded-full { border-radius: var(--radius-full); }");

            // Act
            var rules = RulesOf(result);

            // Assert
            Assert.Equal(
                new[]
                {
                    ".rounded-full { --velvet-radius-top-left: var(--radius-full); " +
                    "--velvet-radius-top-right: var(--radius-full); " +
                    "--velvet-radius-bottom-right: var(--radius-full); " +
                    "--velvet-radius-bottom-left: var(--radius-full); }",
                },
                rules);
        }

        [Fact]
        public void Given_CornerLonghands_When_Derived_Then_OnlyThoseCornersAreRestated()
        {
            // Arrange
            var result = Build(
                ".rounded-r { border-top-right-radius: 4px; border-bottom-right-radius: 4px; }");

            // Act
            var rules = RulesOf(result);

            // Assert
            Assert.Equal(
                new[] { ".rounded-r { --velvet-radius-top-right: 4px; --velvet-radius-bottom-right: 4px; }" },
                rules);
        }

        [Fact]
        public void Given_ALonghandAfterTheShorthand_When_Derived_Then_TheLaterDeclarationWinsItsCorner()
        {
            // Arrange
            var result = Build(".pill-tl { border-radius: 8px; border-top-left-radius: 2px; }");

            // Act
            var rules = RulesOf(result);

            // Assert
            Assert.Equal(
                new[]
                {
                    ".pill-tl { --velvet-radius-top-left: 2px; --velvet-radius-top-right: 8px; " +
                    "--velvet-radius-bottom-right: 8px; --velvet-radius-bottom-left: 8px; }",
                },
                rules);
        }

        [Fact]
        public void Given_RulesAcrossSheets_When_Derived_Then_TheRestatementsKeepTheirCascadeOrderAndSkipTheRest()
        {
            // Arrange — the middle rule declares no radius, so it has nothing to restate.
            var result = Build(
                ".rounded-md { border-radius: 6px; }",
                ".bg-white { background-color: rgb(255, 255, 255); }",
                ".rounded-tl-lg { border-top-left-radius: 8px; }");

            // Act
            var selectors = RulesOf(result).Select(rule => rule.Substring(0, rule.IndexOf(' '))).ToArray();

            // Assert
            Assert.Equal(new[] { ".rounded-md", ".rounded-tl-lg" }, selectors);
        }

        [Fact]
        public void Given_AMultiValueShorthand_When_Derived_Then_TheUnsupportedShorthandIsReported()
        {
            // Arrange
            var result = Build(".mixed { border-radius: 4px 8px; }");

            // Act
            var codes = result.Problems.Select(problem => problem.Code).ToArray();

            // Assert
            Assert.Equal(new[] { UssProblemCode.UnsupportedRadiusShorthand }, codes);
        }
    }
}
