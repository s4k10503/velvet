using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Velvet.SourceGenerators.CodeShape;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins what VEL502 counts as a parameter, one case per declaration form and per exclusion the definition
    /// had to rule on. Each case sits one parameter either side of the limit, so a form that stopped counting
    /// — or started — changes whether anything is reported rather than only the number in the message.
    /// </summary>
    public sealed class ParameterCountAnalyzerTests
    {
        [Fact]
        public void Given_SixParameters_When_Analyzed_Then_TheLimitIsNotExceeded()
        {
            // Arrange
            var source = Method(Parameters(6));

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_SevenParameters_When_Analyzed_Then_ItIsReportedWithTheMeasuredCount()
        {
            // Arrange
            var source = Method(Parameters(7));

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Run", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AFactoryShapedMemberOfTwentyOptionalParameters_When_Analyzed_Then_ItIsNotReported()
        {
            // The exemption the V.* factory surface rests on: a named-optional props list costs a call site
            // nothing, so none of it is charged.
            // Arrange
            var source = Method(Parameters(1) + ", " + Parameters(20, start: 1, optional: true));

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AFactoryShapedMemberBesideAnEightArgumentHelper_When_Analyzed_Then_OnlyTheHelperIsReported()
        {
            // The distinction the exemption exists to draw: it follows the shape of the declaration, not the
            // file or the type it sits in, so a helper landing among the factories is still covered.
            // Arrange
            var source = @"
public static class V
{
    public static object Motion(" + Parameters(21, optional: true) + @") => null;

    public static object Helper(" + Parameters(8) + @") => null;
}";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Helper", 8), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_SixParametersFollowedByAParamsArray_When_Analyzed_Then_TheParamsArrayIsNotCounted()
        {
            // A call can supply no element at all, so it adds nothing to what the caller must line up.
            // Arrange
            var source = Method(Parameters(6) + ", params int[] rest");

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_SevenParametersFollowedByAParamsArray_When_Analyzed_Then_TheSevenAreStillReported()
        {
            // Arrange
            var source = Method(Parameters(7) + ", params int[] rest");

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Run", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnExtensionMethodWithSixParametersBesideItsReceiver_When_Analyzed_Then_TheReceiverIsCounted()
        {
            // The receiver is written at every call site in a fixed position, exactly as a first argument is.
            // Arrange
            var source = @"
public static class Shape
{
    public static void Run(this object self, " + Parameters(6) + @") { }
}";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Run", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AConstructorWithSevenParameters_When_Analyzed_Then_ItIsReported()
        {
            // Arrange
            var source = @"
public sealed class Shape
{
    public Shape(" + Parameters(7) + @") { }
}";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Shape", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnIndexerWithSevenParameters_When_Analyzed_Then_ItIsReported()
        {
            // Arrange
            var source = @"
public sealed class Shape
{
    public int this[" + Parameters(7) + @"] => 0;
}";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("this[]", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADelegateWithSevenParameters_When_Analyzed_Then_ItIsReported()
        {
            // A delegate declares a signature every caller of every instance must satisfy, so it is measured
            // even though it owns no body for the sibling rules to walk.
            // Arrange
            var source = "public delegate void Shape(" + Parameters(7) + ");";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Shape", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ALocalFunctionWithSevenParameters_When_Analyzed_Then_ItIsReported()
        {
            // Wrapping the declaration in a local function must not be an escape, for the reason the sibling
            // rules do not reset at one either.
            // Arrange
            var source = @"
public static class Shape
{
    public static void Run()
    {
        void Inner(" + Parameters(7) + @") { }
        Inner(0, 0, 0, 0, 0, 0, 0);
    }
}";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Inner", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ARecordWithSevenPositionalParameters_When_Analyzed_Then_ItsPrimaryConstructorIsReported()
        {
            // Arrange
            var source = "public sealed record Shape(" + Parameters(7) + ");";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Shape", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AClassWithSevenPrimaryConstructorParameters_When_Analyzed_Then_ItIsReported()
        {
            // The pinned Roslyn reference exposes no ParameterList on a class, so this is reached through the
            // child node instead — and the test host, like Unity's compiler, is a newer Roslyn that parses it.
            // Arrange
            var source = "public sealed class Shape(" + Parameters(7) + ") { }";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Shape", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AStructWithSevenPrimaryConstructorParameters_When_Analyzed_Then_ItIsReported()
        {
            // Arrange
            var source = "public struct Shape(" + Parameters(7) + ") { }";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Shape", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ARecordStructWithSevenPositionalParameters_When_Analyzed_Then_ItIsReported()
        {
            // Its own syntax kind, distinct from both the record and the struct forms above.
            // Arrange
            var source = "public readonly record struct Shape(" + Parameters(7) + ");";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Equal(Message("Shape", 7), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AClassWithoutAPrimaryConstructor_When_Analyzed_Then_NothingIsReportedForTheClass()
        {
            // A class is registered for the primary-constructor case, so every plain class in every opted-in
            // assembly reaches the same code path with no list to measure.
            // Arrange
            var source = @"
public sealed class Shape
{
    public void Run(" + Parameters(6) + @") { }
}";

            // Act
            var diagnostics = Vel502In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AnAssemblyWithoutTheMarker_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            var source = Method(Parameters(21));

            // Act
            var diagnostics = Vel502Raw(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AnAssemblyMarkedWithAnotherValue_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            var source =
                "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"off\")]\n"
                + Method(Parameters(21));

            // Act
            var diagnostics = Vel502Raw(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        private static string Parameters(int count, int start = 0, bool optional = false)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append("int p").Append(start + i);
                if (optional) builder.Append(" = 0");
            }

            return builder.ToString();
        }

        private static string Method(string parameters) => @"
public static class Shape
{
    public static void Run(" + parameters + @") { }
}";

        private static string Message(string member, int required) =>
            $"Member '{member}' demands {required} arguments from every caller; the limit is "
            + ParameterCountAnalyzer.MaxParameters;

        /// <summary>Compiles the source into an assembly that opts into the code-shape rules.</summary>
        private static List<Diagnostic> Vel502In(string source) => Vel502Raw(OptIn + source);

        private static List<Diagnostic> Vel502Raw(string source) =>
            GeneratorTestHelper.RunAnalyzer(source, new ParameterCountAnalyzer())
                .Where(diagnostic => diagnostic.Id == "VEL502")
                .ToList();

        private const string OptIn =
            "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"enforce\")]\n";
    }
}
