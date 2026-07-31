using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Velvet.SourceGenerators.CodeShape;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins what VEL503 treats as a tuple and as a tolerance, one case per shape the definition had to rule
    /// on. Each case is a whole assertion rather than a constraint alone, so a form that stopped reaching the
    /// analyzer changes whether anything is reported rather than only the type named in the message.
    /// </summary>
    public sealed class TupleToleranceAnalyzerTests
    {
        [Fact]
        public void Given_AToleranceOnATupleLiteral_When_Analyzed_Then_ItIsReported()
        {
            // Arrange
            var source = Assertion("(0.99999f, 0.00001f)", "Is.EqualTo((1f, 0f)).Within(1e-4f)");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Equal(Message("(float, float)"), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AToleranceOnAScalar_When_Analyzed_Then_NothingIsReported()
        {
            // The tolerance NUnit does apply. Reporting it would make the rule an argument against tolerances.
            // Arrange
            var source = Assertion("0.99999f", "Is.EqualTo(1f).Within(1e-4f)");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AToleranceOnATupleHeldInALocal_When_Analyzed_Then_ItIsReported()
        {
            // The trap is the expected value's type, not the parentheses at the call site, so a local reaches
            // it identically — which is why the type is asked of the semantic model rather than of the syntax.
            // Arrange
            var source = Assertion(
                "(0.99999f, 0.00001f)",
                "Is.EqualTo(expected).Within(1e-4f)",
                locals: "var expected = (1f, 0f);");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Equal(Message("(float, float)"), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ATupleComparedWithoutATolerance_When_Analyzed_Then_NothingIsReported()
        {
            // A bit-exact tuple comparison is the correct thing to write; only the suffix claiming otherwise
            // is the defect.
            // Arrange
            var source = Assertion("(1f, 0f)", "Is.EqualTo((1f, 0f))");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AToleranceSeparatedFromEqualToByAnotherModifier_When_Analyzed_Then_ItIsReported()
        {
            // The constraint surface is chainable, so the equality is not always the tolerance's immediate
            // receiver; stopping at the first link would exempt every assertion carrying a second modifier.
            // Arrange
            var source = Assertion("(1f, 0f)", "Is.EqualTo((1f, 0f)).IgnoreCase.Within(1e-4f)");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Equal(Message("(float, float)"), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ATupleEqualityChainedIntoAnotherConstraint_When_Analyzed_Then_NothingIsReported()
        {
            // Each invocation in a constraint chain is reached from the same equality, so a rule that only
            // looked for one below it would report the chain's continuation as a tolerance.
            // Arrange
            var source = Assertion("(1f, 0f)", "Is.EqualTo((1f, 0f)).And.GreaterThan(0f)");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ANamedTupleExpectedValue_When_Analyzed_Then_TheNamesAppearInTheMessage()
        {
            // Arrange
            var source = Assertion("(1f, 0f)", "Is.EqualTo((x: 1f, y: 0f)).Within(1e-4f)");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Equal(Message("(float x, float y)"), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_AnEqualityImportedWithUsingStatic_When_Analyzed_Then_ItIsReported()
        {
            // Arrange
            var source = OptIn + NUnitStub + @"
namespace Fixture
{
    using NUnit.Framework;
    using static NUnit.Framework.Is;

    public static class Shape
    {
        public static void Run()
        {
            Assert.That((0.99999f, 0.00001f), EqualTo((1f, 0f)).Within(1e-4f));
        }
    }
}";

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Equal(Message("(float, float)"), Assert.Single(diagnostics).GetMessage());
        }

        [Fact]
        public void Given_ANonNUnitFluentEqualToWithin_When_Analyzed_Then_NothingIsReported()
        {
            // Nothing is known about another library's comparison, so the rule declines rather than guessing
            // from two method names.
            // Arrange
            var source = OptIn + NUnitStub + @"
public static class Other
{
    public sealed class Check { public Check Within(float t) => this; }
    public static Check EqualTo(object expected) => new Check();
}

public static class Shape
{
    public static void Run()
    {
        var c = Other.EqualTo((1f, 0f)).Within(1e-4f);
    }
}";

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_ACastToATupleMemberType_When_Analyzed_Then_NothingIsReported()
        {
            // A parenthesised type name in front of the expected value is the shape a syntax-only rule would
            // have had to tell apart from a tuple literal, and would eventually get wrong.
            // Arrange
            var source = Assertion("0.99999f", "Is.EqualTo((float)1).Within(1e-4f)");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AnAssemblyWithoutTheMarker_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            var source = NUnitStub + Fixture("(0.99999f, 0.00001f)", "Is.EqualTo((1f, 0f)).Within(1e-4f)", "");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void Given_AnAssemblyMarkedWithAnotherValue_When_Analyzed_Then_NothingIsReported()
        {
            // Arrange
            var source = "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"off\")]\n"
                + NUnitStub + Fixture("(0.99999f, 0.00001f)", "Is.EqualTo((1f, 0f)).Within(1e-4f)", "");

            // Act
            var diagnostics = Vel503(source);

            // Assert
            Assert.Empty(diagnostics);
        }

        private static string Assertion(string actual, string constraint, string locals = "") =>
            OptIn + NUnitStub + Fixture(actual, constraint, locals);

        /// <summary>
        /// The <c>using</c> sits inside the namespace because the stub above it already declares one, and a
        /// compilation unit accepts no using directive after a namespace — a fixture that wrote it at the top
        /// would not compile, and an analyzer reading an unresolved call reports nothing.
        /// </summary>
        private static string Fixture(string actual, string constraint, string locals) => @"
namespace Fixture
{
    using NUnit.Framework;

    public static class Shape
    {
        public static void Run()
        {
            " + locals + @"
            Assert.That(" + actual + ", " + constraint + @");
        }
    }
}";

        private static string Message(string tuple) =>
            $"The tolerance on this comparison of '{tuple}' is never applied; its members are compared bit-exactly";

        /// <summary>
        /// Stands in for <c>com.unity.ext.nunit</c>. The rule keys on the declaring namespace, so the surface
        /// only has to be shaped and named like the real one; what the constraint does with the tolerance is
        /// the run-time behaviour the rule exists to describe and is not modelled here.
        /// </summary>
        private const string NUnitStub = @"
namespace NUnit.Framework
{
    public static class Assert
    {
        public static void That(object actual, Constraints.Constraint constraint) { }
    }

    public static class Is
    {
        public static Constraints.EqualConstraint EqualTo(object expected) => new Constraints.EqualConstraint();
    }

    namespace Constraints
    {
        public abstract class Constraint { }

        public sealed class EqualConstraint : Constraint
        {
            public EqualConstraint IgnoreCase => this;
            public EqualConstraint Within(object amount) => this;
            public ConstraintExpression And => new ConstraintExpression();
        }

        public sealed class ConstraintExpression
        {
            public Constraint GreaterThan(object expected) => null;
        }
    }
}
";

        private static List<Diagnostic> Vel503(string source) =>
            GeneratorTestHelper.RunAnalyzerOnCompilingSource(source, new TupleToleranceAnalyzer())
                .Where(diagnostic => diagnostic.Id == "VEL503")
                .ToList();

        private const string OptIn =
            "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"enforce\")]\n";
    }
}
