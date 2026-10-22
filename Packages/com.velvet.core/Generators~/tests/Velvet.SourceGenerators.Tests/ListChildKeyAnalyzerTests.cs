using System.Linq;
using Velvet.SourceGenerators.Keys;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Tests for <see cref="ListChildKeyAnalyzer"/>. Each sample maps a list through a selector written in
    /// <c>Rows</c>, and the case reads back the VEL600 messages it draws.
    /// </summary>
    public sealed class ListChildKeyAnalyzerTests
    {
        private const string UnkeyedComponentMessage =
            "'V.Component' builds a child of a list without a key; each child in a list should have a unique key";

        private static string Vel600Messages(string rows)
        {
            var source = @"
using System.Linq;
using Velvet;

namespace MyApp
{
    public static class Ui
    {
        public static VNode[] Select(int[] items, System.Func<int, VNode> render) => null;

        public static ComponentNode Row() => null;
    }

    public static class Page
    {
        private static VNode Row() => null;

        public static object Rows(int[] items) => " + rows + @";
    }
}";
            var diagnostics = GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new ListChildKeyAnalyzer());
            return string.Join("|", diagnostics.Where(d => d.Id == "VEL600").Select(d => d.GetMessage()));
        }

        [Fact]
        public void Given_ASelectorReturningAComponentWithoutAKey_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "items.Select(item => V.Component(Row)).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorReturningAKeyedComponent_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "items.Select(item => V.Component(Row, key: item.ToString())).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ASelectorPassingANullKey_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "items.Select(item => V.Component(Row, key: null)).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorCastingAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "items.Select(item => (VNode)V.Component(Row)).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_AConditionalBranchBuildingAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "items.Select(item => item > 0 ? V.Component(Row) : null).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ABlockSelectorReturningAnUnkeyedComponentFromAnIf_When_Analyzed_Then_Vel600NamesTheFactory()
        {
            // Arrange
            const string rows = "items.Select(item => { if (item > 0) return V.Component(Row); return null; }).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorReturningText_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — a text node is a string child in React, which takes no key.
            const string rows = "items.Select(item => (VNode)V.Text(item.ToString())).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ASelectorReturningWhenOverAnUnkeyedComponent_When_Analyzed_Then_Vel600NamesTheComponent()
        {
            // Arrange — V.When hands back the node its factory builds.
            const string rows = "items.Select(item => V.When(item > 0, () => V.Component(Row))).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(UnkeyedComponentMessage, messages);
        }

        [Fact]
        public void Given_ASelectorReturningAFactoryResultThatIsNoNode_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "items.Select(item => V.Routes()).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ALambdaInsideTheSelectorReturningAnUnkeyedComponent_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — the node reaches the list through a call, which is not followed.
            const string rows =
                "items.Select(item => { System.Func<VNode> later = () => V.Component(Row); return later(); }).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ASelectorReturningANodeItsOwnMethodBuilds_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange — whether that method keys the node is decided in its body, which is not read here.
            const string rows = "items.Select(item => Ui.Row()).ToArray()";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }

        [Fact]
        public void Given_ASelectThatIsNotLinq_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            const string rows = "Ui.Select(items, item => V.Component(Row))";

            // Act
            var messages = Vel600Messages(rows);

            // Assert
            Assert.Equal(string.Empty, messages);
        }
    }
}
