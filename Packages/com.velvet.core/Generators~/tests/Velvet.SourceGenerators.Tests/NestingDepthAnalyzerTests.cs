using System.Linq;
using Velvet.SourceGenerators.CodeShape;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins the depth definition VEL500 enforces, one case per decision that definition had to make.
    /// A case that only proved "deep reports, shallow does not" would leave every one of them free to move.
    /// </summary>
    public sealed class NestingDepthAnalyzerTests
    {
        [Fact]
        public void Given_AMethodNestingFourLevels_When_Analyzed_Then_ItIsNotReported()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(int[] values, bool flag, int mode)
    {
        if (flag)
        {
            foreach (var value in values)
            {
                while (value > 0)
                {
                    switch (mode)
                    {
                        case 1:
                            System.Console.WriteLine(value);
                            break;
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AMethodNestingFiveLevels_When_Analyzed_Then_ItIsReportedWithTheMeasuredDepth()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(int[] values, bool flag, int mode)
    {
        if (flag)
        {
            foreach (var value in values)
            {
                while (value > 0)
                {
                    switch (mode)
                    {
                        case 1:
                            if (value == 7)
                            {
                                System.Console.WriteLine(value);
                            }
                            break;
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnElseIfChainOfSixBranches_When_Analyzed_Then_TheChainCountsAsOneLevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(int mode)
    {
        if (mode == 0) { System.Console.WriteLine(0); }
        else if (mode == 1) { System.Console.WriteLine(1); }
        else if (mode == 2) { System.Console.WriteLine(2); }
        else if (mode == 3) { System.Console.WriteLine(3); }
        else if (mode == 4) { System.Console.WriteLine(4); }
        else { System.Console.WriteLine(5); }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_FiveBracelessIfBodies_When_Analyzed_Then_DroppingTheBracesDoesNotDropTheDepth()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d, bool e)
    {
        if (a)
            if (b)
                if (c)
                    if (d)
                        if (e)
                            System.Console.WriteLine(1);
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ATryWithACatchAndAFinally_When_Analyzed_Then_AllThreeShareOneLevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(bool a, bool b, bool c)
    {
        try
        {
            if (a) { if (b) { if (c) { System.Console.WriteLine(1); } } }
        }
        catch (System.Exception)
        {
            if (a) { if (b) { if (c) { System.Console.WriteLine(2); } } }
        }
        finally
        {
            if (a) { if (b) { if (c) { System.Console.WriteLine(3); } } }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ADeepBlockWrappedInALambda_When_Analyzed_Then_TheLambdaBodyStillCountsFromWhereItSits()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d)
    {
        if (a)
        {
            System.Action run = () =>
            {
                if (b) { if (c) { if (d) { System.Console.WriteLine(1); } } }
            };
            run();
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AUsingDeclarationInsideFourLevels_When_Analyzed_Then_TheDeclarationFormOpensNoLevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(System.IDisposable disposable, int[] values, bool flag, int mode)
    {
        if (flag)
        {
            foreach (var value in values)
            {
                while (value > 0)
                {
                    switch (mode)
                    {
                        case 1:
                            using var scope = disposable;
                            System.Console.WriteLine(value);
                            break;
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AUsingStatementWrappingFourLevels_When_Analyzed_Then_TheBlockFormOpensALevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(System.IDisposable disposable, int[] values, bool flag, int mode)
    {
        using (disposable)
        {
            if (flag)
            {
                foreach (var value in values)
                {
                    while (value > 0)
                    {
                        switch (mode)
                        {
                            case 1:
                                System.Console.WriteLine(value);
                                break;
                        }
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADeepBodyInAPropertyGetter_When_Analyzed_Then_TheAccessorIsReportedByItsOwnName()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static bool Flag;

    public static int Value
    {
        get
        {
            if (Flag)
            {
                while (Flag)
                {
                    do
                    {
                        lock (typeof(Shape))
                        {
                            if (Flag) { return 1; }
                        }
                    }
                    while (Flag);
                }
            }
            return 0;
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Value.get' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnAssemblyWithoutTheOptIn_When_ItNestsFiveLevels_Then_NothingIsReported()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d, bool e)
    {
        if (a) { if (b) { if (c) { if (d) { if (e) { System.Console.WriteLine(1); } } } } }
    }
}";

            // Act
            var diagnostics = Vel500InAssemblyWithoutTheOptIn(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_TheMarkerKeyWithAValueThatIsNotEnforce_When_Analyzed_Then_NothingIsReported()
        {
            // The gate has to compare the value, not merely find the key: a future per-assembly off switch
            // is spelled with this key, and a gate that ignored the value would read it as an opt-in.
            // Arrange
            const string source = @"
[assembly: System.Reflection.AssemblyMetadata(""Velvet.CodeShape"", ""off"")]

public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d, bool e)
    {
        if (a) { if (b) { if (c) { if (d) { if (e) { System.Console.WriteLine(1); } } } } }
    }
}";

            // Act
            var diagnostics = Vel500InAssemblyWithoutTheOptIn(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AnAttributeMerelyNamedLikeTheMarker_When_Analyzed_Then_NothingIsReported()
        {
            // The gate keys on the attribute type, not on its name ending in AssemblyMetadata: a consumer's
            // own attribute must not be able to opt their assembly into an error this package cannot fix.
            // Arrange
            const string source = @"
[assembly: Mine.MyAssemblyMetadata(""Velvet.CodeShape"", ""enforce"")]

namespace Mine
{
    [System.AttributeUsage(System.AttributeTargets.Assembly)]
    public sealed class MyAssemblyMetadataAttribute : System.Attribute
    {
        public MyAssemblyMetadataAttribute(string key, string value) { }
    }
}

public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d, bool e)
    {
        if (a) { if (b) { if (c) { if (d) { if (e) { System.Console.WriteLine(1); } } } } }
    }
}";

            // Act
            var diagnostics = Vel500InAssemblyWithoutTheOptIn(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_TheMarkerAttributeWithNoArguments_When_Analyzed_Then_TheAnalyzerDoesNotThrow()
        {
            // Analyzers run on erroneous compilations in the IDE, so the gate reads argument slots that may
            // not exist. Vel500InAssemblyWithoutTheOptIn turns the resulting AD0001 into a failure.
            // Arrange
            const string source = @"
[assembly: System.Reflection.AssemblyMetadata]

public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d, bool e)
    {
        if (a) { if (b) { if (c) { if (d) { if (e) { System.Console.WriteLine(1); } } } } }
    }
}";

            // Act
            var diagnostics = Vel500InAssemblyWithoutTheOptIn(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ADeepExpressionBodiedMethod_When_Analyzed_Then_TheExpressionBodyIsMeasured()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static bool Flag;

    public static System.Action Run() => () =>
    {
        if (Flag) { if (Flag) { if (Flag) { if (Flag) { System.Console.WriteLine(1); } } } }
    };
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADeepLambdaInAPropertyInitializer_When_Analyzed_Then_TheInitializerIsMeasured()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static bool Flag;

    public static System.Action Parked { get; } = () =>
    {
        if (Flag) { if (Flag) { if (Flag) { if (Flag) { System.Console.WriteLine(1); } } } }
    };
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Parked' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADeepLambdaInAnEventFieldInitializer_When_Analyzed_Then_TheInitializerIsMeasured()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static bool Flag;

    public static event System.Action Parked = () =>
    {
        if (Flag) { if (Flag) { if (Flag) { if (Flag) { System.Console.WriteLine(1); } } } }
    };
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Parked' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADeepLambdaInTheSecondDeclarator_When_Analyzed_Then_ThatDeclaratorIsNamed()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static bool Flag;

    public static System.Action Shallow = () => { }, Deep = () =>
    {
        if (Flag) { if (Flag) { if (Flag) { if (Flag) { System.Console.WriteLine(1); } } } }
    };
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Deep' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADeepExpressionBodiedIndexer_When_Analyzed_Then_TheIndexerBodyIsMeasured()
        {
            // Arrange
            const string source = @"
public class Shape
{
    public bool Flag;

    public System.Action this[int index] => () =>
    {
        if (Flag) { if (Flag) { if (Flag) { if (Flag) { System.Console.WriteLine(index); } } } }
    };
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'this[]' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_TheExpressionLevelBranchFormsInsideFourLevels_When_Analyzed_Then_NoneOpensALevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(int[] values, bool flag, int mode)
    {
        if (flag)
        {
            foreach (var value in values)
            {
                while (value > 0)
                {
                    switch (mode)
                    {
                        case 1:
                            var ternary = flag ? 1 : 2;
                            var andAlso = flag && value > 1 && mode > 2 && ternary > 3;
                            var arm = mode switch { 1 => ""a"", 2 => ""b"", _ => ""c"" };
                            System.Console.WriteLine(andAlso + arm);
                            break;
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_TheCompilationContextBlocksInsideFourLevels_When_Analyzed_Then_NoneOpensALevel()
        {
            // These hold today only by falling through to the default arm, so a refactor that spells the
            // level-opening set out explicitly could add them without anything going red.
            // Arrange
            const string source = @"
public static class Shape
{
    public static unsafe void Run(int[] values, bool flag, int mode)
    {
        if (flag)
        {
            foreach (var value in values)
            {
                while (value > 0)
                {
                    switch (mode)
                    {
                        case 1:
                            checked
                            {
                                unsafe
                                {
                                    fixed (int* p = values)
                                    {
                                        System.Console.WriteLine(p[0] + value);
                                    }
                                }
                            }
                            break;
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_FiveNestedForLoops_When_Analyzed_Then_EachForOpensALevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(int n)
    {
        for (var a = 0; a < n; a++)
        {
            for (var b = 0; b < n; b++)
            {
                for (var c = 0; c < n; c++)
                {
                    for (var d = 0; d < n; d++)
                    {
                        for (var e = 0; e < n; e++)
                        {
                            System.Console.WriteLine(e);
                        }
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ATryWrappingFourLevels_When_Analyzed_Then_TheTryOpensALevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d)
    {
        try
        {
            if (a) { if (b) { if (c) { if (d) { System.Console.WriteLine(1); } } } }
        }
        catch (System.Exception)
        {
        }
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADeepBlockInALocalFunction_When_Analyzed_Then_TheLocalFunctionBodyOpensALevel()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static void Run(bool a, bool b, bool c, bool d)
    {
        void Helper()
        {
            if (a) { if (b) { if (c) { if (d) { System.Console.WriteLine(1); } } } }
        }

        Helper();
    }
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Run' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ADeepLambdaHoistedToAFieldInitializer_When_Analyzed_Then_TheInitializerIsMeasured()
        {
            // Arrange
            const string source = @"
public static class Shape
{
    public static bool Flag;

    public static readonly System.Action Parked = () =>
    {
        if (Flag) { if (Flag) { if (Flag) { if (Flag) { System.Console.WriteLine(1); } } } }
    };
}";

            // Act
            var diagnostics = Vel500In(source);

            // Assert
            Assert.Equal("Member 'Parked' nests 5 levels deep; the limit is 4", Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AQueryExpressionInsideFourLevels_When_Analyzed_Then_TheQueryOpensNoLevel()
        {
            // Spelled out rather than built by Vel500In: a using directive has to precede the assembly
            // attribute, and query syntax does not bind without one.
            // Arrange
            const string source = @"
using System.Linq;

[assembly: System.Reflection.AssemblyMetadata(""Velvet.CodeShape"", ""enforce"")]

public static class Shape
{
    public static void Run(int[] values, bool flag, int mode)
    {
        if (flag)
        {
            foreach (var value in values)
            {
                while (value > 0)
                {
                    switch (mode)
                    {
                        case 1:
                            var pairs = from a in values from b in values select a + b;
                            System.Console.WriteLine(pairs);
                            break;
                    }
                }
            }
        }
    }
}";

            // Act
            var diagnostics = Vel500InAssemblyWithoutTheOptIn(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        /// <summary>Compiles the source into an assembly that opts into the code-shape rules.</summary>
        private static System.Collections.Generic.List<Microsoft.CodeAnalysis.Diagnostic> Vel500In(string source) =>
            Vel500InAssemblyWithoutTheOptIn(OptIn + source);

        /// <summary>
        /// Fails loudly on AD0001 instead of filtering it away. Roslyn reports an analyzer that threw as
        /// AD0001, so a bare "no VEL500 diagnostics" filter reads a crashed analyzer as a clean one — which
        /// is the reading every <c>Assert.Empty</c> case here would otherwise be making.
        /// </summary>
        private static System.Collections.Generic.List<Microsoft.CodeAnalysis.Diagnostic>
            Vel500InAssemblyWithoutTheOptIn(string source)
        {
            var all = GeneratorTestHelper.RunAnalyzer(source, new NestingDepthAnalyzer());
            var crashed = all.Where(diagnostic => diagnostic.Id == "AD0001").ToList();
            if (crashed.Count > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    "The analyzer threw instead of reporting: " + crashed[0].GetMessage());
            }

            return all.Where(diagnostic => diagnostic.Id == "VEL500").ToList();
        }

        private const string OptIn =
            "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"enforce\")]\n";
    }
}
