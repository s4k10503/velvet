using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Velvet.SourceGenerators.CodeShape;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins the branch definition VEL501 enforces, one case per construct the definition had to rule on.
    /// Each case sits one branch either side of the limit, so a construct that stopped counting — or started
    /// — moves the outcome rather than only the number in the message.
    /// </summary>
    public sealed class BranchCountAnalyzerTests
    {
        [Fact]
        public void Given_TwentyIfStatements_When_Analyzed_Then_TheLimitIsNotExceeded()
        {
            // Arrange
            var source = Body(Ifs(20));

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_TwentyOneIfStatements_When_Analyzed_Then_ItIsReportedWithTheMeasuredCount()
        {
            // Arrange
            var source = Body(Ifs(21));

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnElseIfChainOfTwentyOneConditions_When_Analyzed_Then_EveryArmOfTheChainIsCounted()
        {
            // The one place this definition must disagree with the depth limit, which reads the same chain as
            // a single level.
            // Arrange
            var chain = new StringBuilder("        if (mode == 0) { }\n");
            for (var i = 1; i < 21; i++)
            {
                chain.Append($"        else if (mode == {i}) {{ }}\n");
            }
            var source = Body(chain.ToString());

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnElseIfChainClosedByAnElse_When_Analyzed_Then_TheTrailingElseIsNotCounted()
        {
            // Arrange
            var chain = new StringBuilder("        if (mode == 0) { }\n");
            for (var i = 1; i < 20; i++)
            {
                chain.Append($"        else if (mode == {i}) {{ }}\n");
            }
            chain.Append("        else { }\n");
            var source = Body(chain.ToString());

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AForLoopAboveTwentyIfStatements_When_Analyzed_Then_TheForHeaderIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        for (var i = 0; i < value; i++) { }\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AForeachLoopAboveTwentyIfStatements_When_Analyzed_Then_TheForeachHeaderIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        foreach (var item in values) { }\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AWhileLoopAboveTwentyIfStatements_When_Analyzed_Then_TheWhileHeaderIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        while (flag) { break; }\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADoLoopAboveTwentyIfStatements_When_Analyzed_Then_TheDoHeaderIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        do { break; } while (flag);\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ALogicalAndAboveTwentyIfStatements_When_Analyzed_Then_TheAndIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var both = flag && other;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ALogicalOrAboveTwentyIfStatements_When_Analyzed_Then_TheOrIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var either = flag || other;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ACoalesceAboveTwentyIfStatements_When_Analyzed_Then_TheCoalesceIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var resolved = text ?? string.Empty;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ACoalesceAssignmentAboveTwentyIfStatements_When_Analyzed_Then_TheAssignmentIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        text ??= string.Empty;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AConditionalExpressionAboveTwentyIfStatements_When_Analyzed_Then_TheTernaryIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var picked = flag ? 1 : 2;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ANullConditionalAccessAboveTwentyIfStatements_When_Analyzed_Then_ItIsNotCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var rendered = obj?.ToString();\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ACaseLabelAboveTwentyIfStatements_When_Analyzed_Then_TheCaseIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        switch (mode) { case 1: break; }\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_TwentyCaseLabelsAndADefault_When_Analyzed_Then_TheDefaultIsNotCounted()
        {
            // Arrange
            var body = new StringBuilder("        switch (mode)\n        {\n");
            for (var i = 0; i < 20; i++)
            {
                body.Append($"            case {i}: break;\n");
            }
            body.Append("            default: break;\n        }\n");
            var source = Body(body.ToString());

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ASwitchExpressionArmAboveTwentyIfStatements_When_Analyzed_Then_TheArmIsCounted()
        {
            // The count the measurement behind the backlog figure missed entirely.
            // Arrange
            var source = Body(Ifs(20) + "        var mapped = mode switch { 1 => 1, _ => 0 };\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_TwentySwitchExpressionArmsAndADiscardArm_When_Analyzed_Then_TheDiscardArmIsNotCounted()
        {
            // Arrange
            var arms = new StringBuilder("        var mapped = mode switch\n        {\n");
            for (var i = 0; i < 20; i++)
            {
                arms.Append($"            {i} => {i},\n");
            }
            arms.Append("            _ => -1,\n        };\n");
            var source = Body(arms.ToString());

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ACatchClauseAboveTwentyIfStatements_When_Analyzed_Then_TheCatchIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        try { } catch (System.Exception) { }\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AFilteredCatchAboveNineteenIfStatements_When_Analyzed_Then_TheFilterCountsBesideTheCatch()
        {
            // Arrange
            var source = Body(Ifs(19) + "        try { } catch (System.Exception) when (flag) { }\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AGuardedCaseLabelAboveNineteenIfStatements_When_Analyzed_Then_TheGuardCountsBesideTheCase()
        {
            // Arrange
            var source = Body(
                Ifs(19) + "        switch (mode) { case 1 when flag: break; default: break; }\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnAndPatternAboveTwentyIfStatements_When_Analyzed_Then_TheCombinatorIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var inRange = value is > 0 and < 10;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnOrPatternAboveTwentyIfStatements_When_Analyzed_Then_TheCombinatorIsCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var edge = value is 0 or 1;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ANotPatternAboveTwentyIfStatements_When_Analyzed_Then_TheNegationIsNotCounted()
        {
            // Arrange
            var source = Body(Ifs(20) + "        var present = value is not 0;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ATypeTestAboveTwentyIfStatements_When_Analyzed_Then_TheTestAloneIsNotCounted()
        {
            // A bare `is` computes a value; what makes it a decision is the construct that consumes it, and
            // that construct is what gets charged.
            // Arrange
            var source = Body(Ifs(20) + "        var named = obj is string;\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_TwentyOneBranchesInsideALambda_When_Analyzed_Then_TheyCountTowardTheDeclaringMember()
        {
            // Arrange
            var source = Body("        System.Action parked = () =>\n        {\n" + Ifs(21) + "        };\n        parked();\n");

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_TwentyOneBranchesInAFieldInitializer_When_Analyzed_Then_TheInitializerIsMeasured()
        {
            // Arrange
            var source = @"
public static class Shape
{
    public static bool Flag;

    public static readonly System.Action Parked = () =>
    {
" + Ifs(21, "Flag") + @"
    };
}";

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal("Member 'Parked' makes 21 branching decisions; the limit is 20",
                Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_TwentyOneBranchesInAPropertyGetter_When_Analyzed_Then_TheAccessorIsReportedByItsOwnName()
        {
            // Arrange
            var source = @"
public static class Shape
{
    public static bool Flag;

    public static int Value
    {
        get
        {
" + Ifs(21, "Flag") + @"
            return 0;
        }
    }
}";

            // Act
            var diagnostics = Vel501In(source);

            // Assert
            Assert.Equal("Member 'Value.get' makes 21 branching decisions; the limit is 20",
                Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnAssemblyWithoutTheOptIn_When_ItExceedsTheLimit_Then_NothingIsReported()
        {
            // Arrange
            var source = Body(Ifs(21));

            // Act
            var diagnostics = Vel501Raw(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_TheMarkerCarryingAnotherValue_When_ItExceedsTheLimit_Then_NothingIsReported()
        {
            // The gate is the key/value PAIR. A comparison that only checked the attribute is present, or
            // only that the key matches, would opt every consumer in the moment they used
            // AssemblyMetadata for anything of their own under this key.
            // Arrange
            var source = Marker(CodeShapeMembers.MarkerKey, "audit") + Body(Ifs(21));

            // Act
            var diagnostics = Vel501Raw(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_TheEnforceValueUnderAnotherKey_When_ItExceedsTheLimit_Then_NothingIsReported()
        {
            // Arrange
            var source = Marker("Some.Other.Key", CodeShapeMembers.MarkerValue) + Body(Ifs(21));

            // Act
            var diagnostics = Vel501Raw(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ASecondUnrelatedMetadataAttribute_When_TheMarkerIsAlsoPresent_Then_TheLimitStillApplies()
        {
            // Arrange
            var source = Marker("Some.Other.Key", "whatever")
                + Marker(CodeShapeMembers.MarkerKey, CodeShapeMembers.MarkerValue)
                + Body(Ifs(21));

            // Act
            var diagnostics = Vel501Raw(source);

            // Assert
            Assert.Equal(Message(21), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnotherAttributeCarryingTheMarkerStrings_When_ItExceedsTheLimit_Then_NothingIsReported()
        {
            // The gate reads the attribute TYPE, not just its arguments. Matching on the two strings alone
            // would opt an assembly in off any attribute that happens to take a name/value pair.
            // Arrange
            var source = $"[assembly: Lookalike(\"{CodeShapeMembers.MarkerKey}\", \"{CodeShapeMembers.MarkerValue}\")]\n"
                + Body(Ifs(21))
                + @"
[System.AttributeUsage(System.AttributeTargets.Assembly)]
public sealed class LookalikeAttribute : System.Attribute
{
    public LookalikeAttribute(string key, string value) { Key = key; Value = value; }
    public string Key { get; }
    public string Value { get; }
}";

            // Act
            var diagnostics = Vel501Raw(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AMalformedMarkerWithOneArgument_When_Analyzed_Then_TheAnalyzerNeitherFiresNorThrows()
        {
            // An analyzer runs against code mid-edit, so a marker the user has not finished typing reaches
            // the gate with the wrong argument count. Indexing it unguarded throws, and Roslyn reports that
            // as AD0001 rather than as this diagnostic — which is why the assertion covers both IDs.
            // Arrange
            var source = $"[assembly: System.Reflection.AssemblyMetadata(\"{CodeShapeMembers.MarkerKey}\")]\n"
                + Body(Ifs(21));

            // Act
            var reported = GeneratorTestHelper.RunAnalyzer(source, new BranchCountAnalyzer())
                .Where(diagnostic => diagnostic.Id is "VEL501" or "AD0001")
                .ToList();

            // Assert
            Assert.Empty(reported);
        }

        private static string Marker(string key, string value) =>
            $"[assembly: System.Reflection.AssemblyMetadata(\"{key}\", \"{value}\")]\n";

        private static string Message(int branches) =>
            $"Member 'Run' makes {branches} branching decisions; the limit is {BranchCountAnalyzer.MaxBranches}";

        private static string Ifs(int count, string condition = "flag")
        {
            var builder = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                builder.Append($"        if ({condition}) {{ }}\n");
            }

            return builder.ToString();
        }

        private static string Body(string statements) => @"
public static class Shape
{
    public static void Run(bool flag, bool other, int mode, int value, object obj, string text, int[] values)
    {
" + statements + @"
    }
}";

        /// <summary>Compiles the source into an assembly that opts into the code-shape rules.</summary>
        private static List<Diagnostic> Vel501In(string source) => Vel501Raw(OptIn + source);

        private static List<Diagnostic> Vel501Raw(string source) =>
            GeneratorTestHelper.RunAnalyzer(source, new BranchCountAnalyzer())
                .Where(diagnostic => diagnostic.Id == "VEL501")
                .ToList();

        private const string OptIn =
            "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"enforce\")]\n";
    }
}
