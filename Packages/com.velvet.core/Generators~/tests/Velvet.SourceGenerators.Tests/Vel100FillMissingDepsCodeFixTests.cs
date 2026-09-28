using System.Threading.Tasks;
using Velvet.SourceGenerators.AutoDeps;
using Velvet.SourceGenerators.CodeFixes;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    public sealed class Vel100FillMissingDepsCodeFixTests
    {
        private const string Title = "Add missing local to hook deps array";

        private static string Render(string body) => @"
namespace MyApp.Pages
{
    public static class HomePage
    {
        public static void Render()
        {
            var a = 1;
            var b = 2;
" + body + @"
        }
    }
}";

        private static Task<string> FixAsync(string source) =>
            CodeFixTestHelper.ApplyCodeFixAsync(
                source,
                new UseEffectExhaustiveDepsAnalyzer(),
                new Vel100FillMissingDepsCodeFixProvider(),
                codeActionTitle: Title,
                expectedDiagnosticId: "VEL100");

        [Fact]
        public async Task Given_SingleLineDepsArray_When_Fixed_Then_TheLocalIsAppendedOnTheSameLine()
        {
            // Arrange
            var source = Render(@"            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(a + b), new object[] { a });");

            // Act
            var fixedText = await FixAsync(source);

            // Assert
            Assert.Contains("new object[] { a, b });", fixedText);
        }

        [Fact]
        public async Task Given_EmptyDepsArray_When_Fixed_Then_TheLocalBecomesItsOnlyElement()
        {
            // Arrange
            var source = Render(@"            global::Velvet.Hooks.UseEffect(() => () => System.Console.WriteLine(a), new object[] { });");

            // Act
            var fixedText = await FixAsync(source);

            // Assert
            Assert.Contains("new object[] { a });", fixedText);
        }

        [Fact]
        public async Task Given_MemoizedWithKeyDepsArray_When_Fixed_Then_TheArrayAfterTheFactoryGrows()
        {
            // Arrange
            var source = Render(@"            global::Velvet.V.MemoizedWithKey(""k"", () => a == b ? null : null, new object[] { a });");

            // Act
            var fixedText = await FixAsync(source);

            // Assert
            Assert.Contains("new object[] { a, b });", fixedText);
        }

        [Fact]
        public async Task Given_LooseParamsDeps_When_FixesAreRequested_Then_NoneIsRegistered()
        {
            // Arrange
            var source = Render(@"            var c = 3;
            global::Velvet.Hooks.UseMemo(() => a + b + c, a, c);");

            // Act
            var titles = await CodeFixTestHelper.RegisteredTitlesAsync(
                source,
                new UseEffectExhaustiveDepsAnalyzer(),
                new Vel100FillMissingDepsCodeFixProvider(),
                expectedDiagnosticId: "VEL100");

            // Assert
            Assert.Empty(titles);
        }
    }
}
