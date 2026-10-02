using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Velvet.SourceGenerators.Keys;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins what VEL600 reads as a mapped list and as an unkeyed element of it, one case per shape the
    /// definition rules on. Each sample is compiled before it is analyzed, so a case reporting nothing is not one
    /// whose call failed to bind.
    /// </summary>
    public sealed class MissingListKeyAnalyzerTests
    {
        // A component body returning a Fragment whose children argument is the given expression, over a list of
        // strings named items.
        private static string Children(string expression) => @"
using System.Linq;
using Velvet;

namespace MyApp
{
    public static class Page
    {
        public static VNode Render(string[] items, bool flag)
        {
            return V.Fragment(" + expression + @");
        }
    }
}";

        private static ImmutableArray<Diagnostic> Vel600(string source) =>
            GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new MissingListKeyAnalyzer())
                .Where(d => d.Id == "VEL600")
                .ToImmutableArray();

        [Fact]
        public void Given_AMappedElementWithNoKey_When_Analyzed_Then_ItIsReported()
        {
            // Arrange
            var source = Children("items.Select(item => V.Outlet(item)).ToArray()");

            // Act
            var diagnostics = Vel600(source);

            // Assert — the element is what is underlined, and nothing else is.
            Assert.Equal(new[] { "V.Outlet(item)" },
                diagnostics.Select(d => d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)));
        }

        [Fact]
        public void Given_AMappedElementWithAKey_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            var source = Children("items.Select(item => V.Outlet(item, key: item)).ToArray()");

            // Act
            var diagnostics = Vel600(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AMappedElementWhoseKeyIsTheNullLiteral_When_Analyzed_Then_ItIsReported()
        {
            // Arrange
            var source = Children("items.Select(item => V.Outlet(item, key: null)).ToArray()");

            // Act
            var diagnostics = Vel600(source);

            // Assert
            Assert.Single(diagnostics);
        }

        [Fact]
        public void Given_AMappedArrayThatReachesNoFactory_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            var source = Children("new VNode[] { Store(items.Select(item => V.Outlet(item)).ToArray()) }")
                .Replace("public static VNode Render", "private static VNode Store(VNode[] nodes) => null;\n        public static VNode Render");

            // Act
            var diagnostics = Vel600(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ABlockBodiedSelectorReturningTwoUnkeyedElements_When_Analyzed_Then_BothAreReported()
        {
            // Arrange
            var source = Children(
                "items.Select(item => { if (item.Length > 0) return V.Outlet(item); return V.Outlet(null); }).ToArray()");

            // Act
            var diagnostics = Vel600(source);

            // Assert
            Assert.Equal(2, diagnostics.Length);
        }

        [Fact]
        public void Given_ASelectorChoosingBetweenTwoUnkeyedElements_When_Analyzed_Then_BothAreReported()
        {
            // Arrange
            var source = Children("items.Select(item => flag ? V.Outlet(item) : V.Outlet(null)).ToArray()");

            // Act
            var diagnostics = Vel600(source);

            // Assert
            Assert.Equal(2, diagnostics.Length);
        }

        [Fact]
        public void Given_AReturnInsideALambdaTheSelectorBuilds_When_Analyzed_Then_ItIsNotTheSelectorsElement()
        {
            // Arrange
            var source = Children(
                "items.Select(item => { System.Func<VNode> later = () => V.Outlet(item); return V.Outlet(item, key: item); }).ToArray()");

            // Act
            var diagnostics = Vel600(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AMappedElementFromAFactoryWithNoKeyParameter_When_Analyzed_Then_NothingIsReported()
        {
            // V.Text cannot be given a key, so asking for one would have no remedy.
            // Arrange
            var source = Children("items.Select(item => V.Text(item)).ToArray()");

            // Act
            var diagnostics = Vel600(source);

            // Assert
            Assert.Empty(diagnostics);
        }
    }
}
