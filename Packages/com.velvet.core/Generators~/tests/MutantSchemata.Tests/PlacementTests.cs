using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Velvet.MutantSchemata.Tests
{
    /// <summary>
    /// What the rewriter places and what it declines, over sources compiled against this runtime. A placed
    /// mutant is read back by selecting its guard's branch in the rewritten file and comparing the method
    /// with the one the direct edit produces, which is the program the mutant's own launch would compile.
    /// </summary>
    public sealed class PlacementTests
    {
        private const string Project = "/schemata-tests";
        private const int Id = 1;

        private sealed class Rewritten
        {
            public Program.AssemblyResult Result = null!;
            public SortedDictionary<int, string> Declined = new();
            public string Direct = "";
        }

        private static Rewritten Rewrite(string text, int line, string before, string replacement, string op, int column)
        {
            var lines = text.Split('\n');
            var mutant = new MutantInput
            {
                id = Id, line = line, start = column, length = before.Length, replacement = replacement,
                @operator = op, before = op is "literal" or "clause removed" or "line removed" ? before : before.Trim(),
            };
            var file = new FileInput { path = "Probe.cs", assembly = "Probe", text = text, mutants = new List<MutantInput> { mutant } };
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
            var context = new Program.CompileContext
            {
                ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp10),
                References = references,
                Options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            };
            context.Sources.Add((Path.GetFullPath(Path.Combine(Project, "Probe.cs")).Replace('\\', '/'), text));
            var declined = new SortedDictionary<int, string>();
            var result = Program.RewriteSources(new Input { project = Project, env = "VELVET_MUTANT" }, "Probe", context,
                                                new List<FileInput> { file }, declined);
            lines[line - 1] = lines[line - 1].Remove(column, before.Length).Insert(column, replacement);
            return new Rewritten { Result = result, Declined = declined, Direct = string.Join("\n", lines) };
        }

        // A binary operator mutant as mutation_check.py writes it: the operator with a space either side.
        private static Rewritten Operator(string text, int line, string op, string replacement, int occurrence = 0)
        {
            var source = text.Split('\n')[line - 1];
            var column = -1;
            for (var i = 0; i <= occurrence; i++) column = source.IndexOf(" " + op + " ", column + 1, StringComparison.Ordinal);
            return Rewrite(text, line, " " + op + " ", " " + replacement + " ", "logic", column);
        }

        // The method named F with the guard's branch selected: `armed` true for the mutant's program.
        private static string Selected(Rewritten rewritten, bool armed)
        {
            var root = CSharpSyntaxTree.ParseText(rewritten.Result.Files["Probe.cs"]).GetRoot();
            var guard = root.DescendantNodes().OfType<ConditionalExpressionSyntax>()
                .First(c => c.Condition is BinaryExpressionSyntax b && b.Left.ToString().EndsWith(".Active", StringComparison.Ordinal)
                            && b.Right.ToString() == Id.ToString());
            var chosen = root.ReplaceNode(guard, armed ? guard.WhenTrue : guard.WhenFalse);
            return Program.Canonical(chosen.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "F"));
        }

        private static string Method(string text) =>
            Program.Canonical(CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == "F"));

        private const string Chain = "class P\n{\n    static bool F(bool a, bool b, bool c, bool d) => a || b && c || d;\n}\n";

        [Fact]
        public void Given_AFlipInsideAHigherPrecedenceOperand_When_Armed_Then_TheProgramIsTheTextualMutant()
        {
            // Arrange — `b * c` flipped to `-` reads `a - b - c` as text; wrapping the `b * c` node alone
            // would compile `a - (b - c)`, which is why the site climbs to the whole expression.
            const string text = "class P\n{\n    static int F(int a, int b, int c) => a - b * c;\n}\n";
            var rewritten = Operator(text, 3, "*", "-");

            // Act
            var armed = Selected(rewritten, armed: true);

            // Assert
            Assert.Equal(Method(rewritten.Direct), armed);
        }

        [Fact]
        public void Given_AFlipOfTheSecondOrInAMixedChain_When_Unarmed_Then_TheProgramIsTheOriginal()
        {
            // Arrange
            var rewritten = Operator(Chain, 3, "||", "&&", occurrence: 1);

            // Act
            var unarmed = Selected(rewritten, armed: false);

            // Assert
            Assert.Equal(Method(Chain), unarmed);
        }

        [Fact]
        public void Given_AClauseCutAcrossAPrecedenceBoundary_When_Armed_Then_TheProgramIsTheTextualMutant()
        {
            // Arrange — removing `a || ` leaves `b && c`, which no node of the original is on its own.
            const string text = "class P\n{\n    static bool F(bool a, bool b, bool c) => a || b && c;\n}\n";
            var column = text.Split('\n')[2].IndexOf("a || ", StringComparison.Ordinal);
            var rewritten = Rewrite(text, 3, "a || ", "", "clause removed", column);

            // Act
            var armed = Selected(rewritten, armed: true);

            // Assert
            Assert.Equal(Method(rewritten.Direct), armed);
        }

        [Fact]
        public void Given_AFlipWhoseGuardWouldDeclareAnOutVariableTwice_When_Rewritten_Then_ItIsDeclined()
        {
            // Arrange
            const string text = "class P\n{\n    static int F(string s) { if (int.TryParse(s, out var n) && n > 0) return n; return 0; }\n}\n";

            // Act
            var rewritten = Operator(text, 3, "&&", "||");

            // Assert
            Assert.Contains(Id, rewritten.Declined.Keys);
        }

        [Fact]
        public void Given_AMutantInAConstantInitializer_When_Rewritten_Then_ItIsDeclined()
        {
            // Arrange — a guard is not a constant expression.
            const string text = "class P\n{\n    const int K = 1 + 2;\n    static int F() => K;\n}\n";

            // Act
            var rewritten = Operator(text, 3, "+", "-");

            // Assert
            Assert.Contains(Id, rewritten.Declined.Keys);
        }

        [Fact]
        public void Given_AMutantInAComponentBody_When_Rewritten_Then_ItIsDeclinedForTheWeaver()
        {
            // Arrange
            const string text = "class ComponentAttribute : System.Attribute { }\nclass P\n{\n" +
                                "    [Component] static bool F(int a, int b) => a < b;\n}\n";

            // Act
            var rewritten = Operator(text, 4, "<", "<=");

            // Assert
            Assert.Contains("[Component]", rewritten.Declined.GetValueOrDefault(Id, ""));
        }

        [Fact]
        public void Given_AMutantInsideAnExpressionTree_When_Rewritten_Then_ItIsDeclined()
        {
            // Arrange
            const string text = "using System;\nusing System.Linq.Expressions;\nclass P\n{\n" +
                                "    static Expression<Func<int, bool>> F() => x => x < 3;\n}\n";

            // Act
            var rewritten = Operator(text, 5, "<", "<=");

            // Assert
            Assert.Contains("expression tree", rewritten.Declined.GetValueOrDefault(Id, ""));
        }

        [Fact]
        public void Given_AConstantThatOverflowsItsCastOnceMutated_When_Rewritten_Then_ItIsDeclined()
        {
            // Arrange — `(byte)(200 + 100)` does not compile, so its own launch names it uncompilable.
            const string text = "class P\n{\n    static byte F() => (byte)(200 - 100);\n}\n";

            // Act
            var rewritten = Operator(text, 3, "-", "+");

            // Assert
            Assert.Contains("direct mutant does not compile", rewritten.Declined.GetValueOrDefault(Id, ""));
        }

        [Fact]
        public void Given_AConstantArgumentAnOverloadIsChosenBy_When_Rewritten_Then_ItIsDeclined()
        {
            // Arrange — `1 + 2` converts to short as a constant; wrapped in a guard it is a non-constant int
            // and binds the long overload, so the unarmed program would not be the original.
            const string text = "class P\n{\n    static void G(short s) { }\n    static void G(long l) { }\n" +
                                "    static void F() { G(1 + 2); }\n}\n";

            // Act
            var rewritten = Operator(text, 5, "+", "-");

            // Assert
            Assert.Contains("binds", rewritten.Declined.GetValueOrDefault(Id, ""));
        }

        [Fact]
        public void Given_ANonConstantArgumentToAnOverloadedCall_When_Rewritten_Then_ItIsPlaced()
        {
            // Arrange — the control for the case above: nothing a guard takes away decides this call.
            const string text = "class P\n{\n    static void G(short s) { }\n    static void G(long l) { }\n" +
                                "    static void F(long x) { G(x + 2); }\n}\n";

            // Act
            var rewritten = Operator(text, 5, "+", "-");

            // Assert
            Assert.Empty(rewritten.Declined);
        }
    }
}
