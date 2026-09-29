using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Runs the wrapper <c>[MemoizeMethod]</c> generates, compiled as a C# 9 consumer against the V.Memoized
    /// overloads the package ships, and reads the dependency list it hands V.Memoized. The snapshot tests pin
    /// the emitted text; what they cannot show is which overload that text binds to, which decides what the
    /// list holds.
    /// </summary>
    public sealed class MemoizeMethodDependencyTests
    {
        private static string DependenciesOf(string members, string calls)
        {
            var source = @"
namespace MyApp
{
    public partial class Page
    {
" + members + @"
    }

    public static class Probe
    {
        private static string Show(global::Velvet.VNode node)
        {
            var deps = ((global::Velvet.MemoNode)node).Dependencies;
            return deps == null
                ? ""<no list>""
                : string.Join("","", System.Linq.Enumerable.Select(deps, d => d == null ? ""null"" : d.ToString()));
        }

        public static string Run()
        {
            var page = new Page();
            return " + calls + @";
        }
    }
}";
            var run = GeneratorTestHelper.RunAsCSharp9Consumer(source);
            return (string)GeneratorTestHelper.Invoke(run, "MyApp.Probe", "Run")!;
        }

        [Fact]
        public void Given_AGenericMethod_When_CalledWithTwoTypeArguments_Then_EachTypeArgumentIsADependency()
        {
            // Arrange
            const string members = @"
        [global::Velvet.MemoizeMethod]
        public partial global::Velvet.VNode Cell<T>(string label);
        private global::Velvet.VNode Cell_Impl<T>(string label) => null;";

            // Act
            var deps = DependenciesOf(members, @"Show(page.Cell<int>(""x"")) + ""|"" + Show(page.Cell<float>(""x""))");

            // Assert
            Assert.Equal("x,System.Int32|x,System.Single", deps);
        }

        [Fact]
        public void Given_AParamsArrayAfterAnotherParameter_When_Called_Then_ItsElementsAreTheDependencies()
        {
            // Arrange
            const string members = @"
        [global::Velvet.MemoizeMethod]
        public partial global::Velvet.VNode Row(string title, params string[] cells);
        private global::Velvet.VNode Row_Impl(string title, string[] cells) => null;";

            // Act
            var deps = DependenciesOf(members, @"Show(page.Row(""t"", ""a"", ""b""))");

            // Assert
            Assert.Equal("t,2,a,b", deps);
        }

        [Fact]
        public void Given_AValueTypeParamsArray_When_CalledEmptyAndWithNull_Then_TheLengthTellsThemApart()
        {
            // Arrange
            const string members = @"
        [global::Velvet.MemoizeMethod]
        public partial global::Velvet.VNode Nums(params int[] xs);
        private global::Velvet.VNode Nums_Impl(int[] xs) => null;";

            // Act
            var deps = DependenciesOf(
                members, @"Show(page.Nums(1, 2)) + ""|"" + Show(page.Nums()) + ""|"" + Show(page.Nums(null))");

            // Assert
            Assert.Equal("2,1,2|0|null", deps);
        }

        [Fact]
        public void Given_ASingleArrayParameterPassedNull_When_Called_Then_NullIsItsOneDependency()
        {
            // Arrange
            const string members = @"
        [global::Velvet.MemoizeMethod]
        public partial global::Velvet.VNode Tags(string[] tags);
        private global::Velvet.VNode Tags_Impl(string[] tags) => null;";

            // Act
            var deps = DependenciesOf(members, @"Show(page.Tags(null))");

            // Assert
            Assert.Equal("null", deps);
        }
    }
}
