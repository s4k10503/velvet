using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// One case per member shape of a props type: each compiles the generator's output as a C# 9 consumer,
    /// loads it, and asks the registered comparer about a pair of instances. The answers are the ones the
    /// runtime's props walk gives, which <c>ComponentPropsComparerTests</c> specifies, so a case here fails
    /// where the emitted code answers otherwise.
    /// </summary>
    public sealed class PropsComparerGeneratorTests
    {
        private const string Probe = @"
public static class Probe
{
    private static bool Same(object a, object b)
    {
        var comparers = (global::System.Collections.IDictionary)typeof(global::Velvet.PropsComparerRegistry)
            .GetField(""s_comparers"", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static)
            .GetValue(null);
        return ((global::System.Func<object, object, bool>)comparers[a.GetType()])(a, b);
    }

    public static string Run() => string.Join("","", new[] { CASES });
}";

        private static string Answers(string types, string cases)
        {
            var source = "namespace App\n{\n" + types
                + "\npublic static class Components\n{\n"
                + "[global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode Render(Props props) => null;\n}\n"
                + Probe.Replace("CASES", cases) + "\n}";
            var run = GeneratorTestHelper.RunAsCSharp9Consumer(source, new PropsComparerGenerator());
            return (string)GeneratorTestHelper.Invoke(run, "App.Probe", "Run")!;
        }

        // A sample given to this method carries a control props type that is emitted, so a case that expects a type to
        // decline cannot pass on a sample that produced nothing at all.
        private static string[] EmittedTypes(string types)
        {
            var source = "namespace App\n{\n"
                + "public sealed record Control(int Id);\n" + types
                + "\ninternal static class Components\n{\n"
                + "[global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode RenderControl(Control props) => null;\n"
                + "[global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode Render(Props props) => null;\n}\n}";
            var run = GeneratorTestHelper.Run(source, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new PropsComparerGenerator() });
            if (!run.CompilationErrors.IsEmpty)
            {
                throw new System.InvalidOperationException("The sample does not compile: " + string.Join("; ", run.CompilationErrors));
            }

            return run.GeneratedSources
                .SelectMany(generated => Regex.Matches(generated.Source, @"typeof\((?<type>[^)]*)\)").Cast<Match>())
                .Select(match => match.Groups["type"].Value)
                .ToArray();
        }

        [Fact]
        public void Given_IntMember_When_TheValuesDiffer_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed record Props(int Count);";

            // Act
            var answers = Answers(types, "Same(new Props(1), new Props(1)).ToString(), Same(new Props(1), new Props(2)).ToString()");

            // Assert
            Assert.Equal("True,False", answers);
        }

        [Fact]
        public void Given_StringMember_When_TheTextsAreEqualButTheInstancesAreNot_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed record Props(string Label);";

            // Act
            var answers = Answers(types, "Same(new Props(new string('a', 3)), new Props(new string('a', 3))).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_ReferenceMember_When_TheInstancesAreDistinctButEqualInContent_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed record Inner(int Id); public sealed record Props(Inner Held);";

            // Act
            var answers = Answers(types, "Same(new Props(new Inner(1)), new Props(new Inner(1))).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_ReferenceMember_When_TheInstanceIsTheSame_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed record Inner(int Id); public sealed record Props(Inner Held); public static class S { public static readonly Inner One = new Inner(1); }";

            // Act
            var answers = Answers(types, "Same(new Props(S.One), new Props(S.One)).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_FloatMember_When_BothAreNaN_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed record Props(float X);";

            // Act
            var answers = Answers(types, "Same(new Props(float.NaN), new Props(float.NaN)).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_FloatMember_When_ZeroChangesSign_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed record Props(float X);";

            // Act
            var answers = Answers(types, "Same(new Props(0f), new Props(-0f)).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_DoubleMember_When_ZeroChangesSign_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed record Props(double X);";

            // Act
            var answers = Answers(types, "Same(new Props(0d), new Props(-0d)).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_NullableIntMember_When_OneSideHasNoValue_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed record Props(int? N);";

            // Act
            var answers = Answers(types, "Same(new Props(null), new Props(3)).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_NullableIntMember_When_BothHaveNoValue_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed record Props(int? N);";

            // Act
            var answers = Answers(types, "Same(new Props(null), new Props(null)).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_NullableFloatMember_When_ZeroChangesSign_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed record Props(float? X);";

            // Act
            var answers = Answers(types, "Same(new Props(0f), new Props(-0f)).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_NullableFloatMember_When_BothAreNaN_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed record Props(float? X);";

            // Act
            var answers = Answers(types, "Same(new Props(float.NaN), new Props(float.NaN)).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_EnumMember_When_TheValuesDiffer_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public enum Kind { A, B } public sealed record Props(Kind K);";

            // Act
            var answers = Answers(types, "Same(new Props(Kind.A), new Props(Kind.B)).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_StructMemberHoldingFloats_When_ZeroChangesSignInsideIt_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public readonly struct Pair { public Pair(float a, float b) { A = a; B = b; } public float A { get; } public float B { get; } } public sealed record Props(Pair P);";

            // Act
            var answers = Answers(types, "Same(new Props(new Pair(0f, 1f)), new Props(new Pair(-0f, 1f))).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_StructMemberHoldingFloats_When_TheFieldsAreEqual_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public readonly struct Pair { public Pair(float a, float b) { A = a; B = b; } public float A { get; } public float B { get; } } public sealed record Props(Pair P);";

            // Act
            var answers = Answers(types, "Same(new Props(new Pair(0f, 1f)), new Props(new Pair(0f, 1f))).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_NullableStructMemberHoldingFloats_When_ZeroChangesSignInsideIt_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public readonly struct Pair { public Pair(float a, float b) { A = a; B = b; } public float A { get; } public float B { get; } } public sealed record Props(Pair? P);";

            // Act
            var answers = Answers(types, "Same(new Props(new Pair(0f, 1f)), new Props(new Pair(-0f, 1f))).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_StructMemberHoldingAStruct_When_ZeroChangesSignInTheInnerOne_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = @"public readonly struct Pair { public Pair(float a, float b) { A = a; B = b; } public float A { get; } public float B { get; } }
public readonly struct Outer { public Outer(Pair inner, int n) { Inner = inner; N = n; } public Pair Inner { get; } public int N { get; } }
public sealed record Props(Outer O);";

            // Act
            var answers = Answers(types, "Same(new Props(new Outer(new Pair(1f, 0f), 1)), new Props(new Outer(new Pair(1f, -0f), 1))).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_StructMemberWithAPublicFloatField_When_ZeroChangesSign_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public struct Cell { public float F; } public sealed record Props(Cell C);";

            // Act
            var answers = Answers(types, "Same(new Props(new Cell { F = 0f }), new Props(new Cell { F = -0f })).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_ObjectMember_When_BothHoldEqualBoxedInts_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed record Props(object Held);";

            // Act
            var answers = Answers(types, "Same(new Props(1), new Props(1)).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_InterfaceMember_When_BothHoldEqualBoxedInts_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed record Props(System.IComparable Held);";

            // Act
            var answers = Answers(types, "Same(new Props(1), new Props(1)).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_DelegateMember_When_TheInstancesAreDistinct_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed record Props(System.Action OnClick);";

            // Act
            var answers = Answers(types, "Same(new Props(() => { }), new Props(() => { })).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_PublicFields_When_OneDiffers_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed class Props { public int A; public string B; }";

            // Act
            var answers = Answers(types, "Same(new Props { A = 1, B = \"x\" }, new Props { A = 1, B = \"y\" }).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_InheritedProperty_When_OnlyTheBaseMemberDiffers_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public class Base { public int Id { get; init; } } public sealed class Props : Base { public int Own { get; init; } }";

            // Act
            var answers = Answers(types, "Same(new Props { Id = 1, Own = 2 }, new Props { Id = 3, Own = 2 }).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_OverriddenProperty_When_TheDerivedValueDiffers_Then_TheComparerSaysChangedAndTheTypeStillCompiles()
        {
            // Arrange
            const string types = @"public class Base { public virtual int Id { get; init; } }
public sealed class Props : Base { public override int Id { get; init; } }";

            // Act
            var answers = Answers(types, "Same(new Props { Id = 1 }, new Props { Id = 2 }).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_MemberNamedAfterAKeyword_When_TheValuesDiffer_Then_TheComparerSaysChanged()
        {
            // Arrange
            const string types = "public sealed class Props { public int @event { get; init; } }";

            // Act
            var answers = Answers(types, "Same(new Props { @event = 1 }, new Props { @event = 2 }).ToString()");

            // Assert
            Assert.Equal("False", answers);
        }

        [Fact]
        public void Given_PropsWithNoMembers_When_TwoInstancesAreCompared_Then_TheComparerSaysUnchanged()
        {
            // Arrange
            const string types = "public sealed class Props { }";

            // Act
            var answers = Answers(types, "Same(new Props(), new Props()).ToString()");

            // Assert
            Assert.Equal("True", answers);
        }

        [Fact]
        public void Given_InternalPropsType_When_TheComponentIsMemoized_Then_ItIsRegisteredByItsFullName()
        {
            // Arrange
            const string types = "internal sealed record Props(int N);";

            // Act
            var emitted = EmittedTypes(types);

            // Assert
            Assert.Equal(new[] { "global::App.Control", "global::App.Props" }, emitted);
        }

        [Theory]
        [InlineData("public readonly record struct Props(int N);")]
        [InlineData("public sealed class Props : System.Collections.Generic.List<int> { }")]
        [InlineData("public abstract class Props { public int N { get; init; } }")]
        [InlineData("public interface Props { int N { get; } }")]
        public void Given_PropsTypeTheRuntimeDoesNotWalk_When_TheComponentIsMemoized_Then_NoComparerIsEmittedForIt(string types)
        {
            // Arrange — the sample's own Props is the declined shape, Control the emitted one

            // Act
            var emitted = EmittedTypes(types);

            // Assert
            Assert.Equal(new[] { "global::App.Control" }, emitted);
        }

        [Theory]
        [InlineData("public sealed class Props { public int N { get; init; } private int _hidden; public int Write { set { _hidden = value; } } }", "global::App.Control,global::App.Props")]
        [InlineData("public sealed class Props { public int N { private get; set; } }", "global::App.Control")]
        [InlineData("public sealed class Props { public ref int N => ref _n; private int _n; }", "global::App.Control")]
        [InlineData("public sealed class Props { public System.TimeSpan Span { get; init; } }", "global::App.Control")]
        [InlineData("public struct Cell { private float _f; } public sealed class Props { public Cell C { get; init; } }", "global::App.Control")]
        [InlineData("public sealed class Props { public int this[int i] => i; public int N { get; init; } }", "global::App.Control,global::App.Props")]
        [InlineData("public class Base { public int N { get; init; } } public sealed class Props : Base { public new int N { get; init; } }", "global::App.Control")]
        public void Given_PropsMember_When_TheRuntimeWalkCannotBeWrittenForIt_Then_TheWholeTypeIsLeftToTheRuntime(
            string types, string expected)
        {
            // Arrange

            // Act
            var emitted = string.Join(",", EmittedTypes(types));

            // Assert
            Assert.Equal(expected, emitted);
        }

        [Fact]
        public void Given_PropsTypePrivateToItsHost_When_TheComponentIsMemoized_Then_NoComparerIsEmittedForIt()
        {
            // Arrange
            const string source = "namespace App { public sealed record Control(int Id); "
                + "public static class Host { private sealed record Props(int N); "
                + "[global::Velvet.Component(Memoize = true)] private static global::Velvet.VNode Render(Props props) => null; "
                + "[global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode RenderControl(Control props) => null; } }";

            // Act
            var run = GeneratorTestHelper.Run(source, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new PropsComparerGenerator() });
            var emitted = string.Join(",", run.GeneratedSources.SelectMany(generated =>
                Regex.Matches(generated.Source, @"typeof\((?<type>[^)]*)\)").Cast<Match>()).Select(match => match.Groups["type"].Value));

            // Assert
            Assert.Equal("global::App.Control", emitted);
        }

        [Fact]
        public void Given_RecordStructMemberHoldingFloats_When_TheGeneratorRuns_Then_ItsFloatsAreReadThroughTheirProperties()
        {
            // Arrange
            const string source = "namespace App { public readonly record struct Pair(float A, float B); public sealed record Props(Pair P); "
                + "public static class C { [global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode Render(Props props) => null; } }";

            // Act
            var run = GeneratorTestHelper.Run(source, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new PropsComparerGenerator() });

            // Assert
            Assert.Contains("SingleToInt32Bits(a0.A)", run.GeneratedSources.Single().Source);
        }

        [Fact]
        public void Given_RecordStructMemberHoldingFloats_When_TheGeneratorRuns_Then_TheOutputCompiles()
        {
            // Arrange
            const string source = "namespace App { public readonly record struct Pair(float A, float B); public sealed record Props(Pair P); "
                + "public static class C { [global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode Render(Props props) => null; } }";

            // Act
            var run = GeneratorTestHelper.Run(source, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new PropsComparerGenerator() });

            // Assert
            Assert.Empty(run.CompilationErrors);
        }

        [Fact]
        public void Given_ComponentWithoutMemoize_When_TheGeneratorRuns_Then_NothingIsEmitted()
        {
            // Arrange
            const string source = "namespace App { public sealed record Props(int N); public static class C { [global::Velvet.Component] public static global::Velvet.VNode Render(Props props) => null; } }";

            // Act
            var run = GeneratorTestHelper.Run(source, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new PropsComparerGenerator() });

            // Assert
            Assert.Empty(run.GeneratedSources);
        }

        [Fact]
        public void Given_TwoComponentsSharingOnePropsType_When_TheGeneratorRuns_Then_ItIsRegisteredOnce()
        {
            // Arrange
            const string source = "namespace App { public sealed record Props(int N); public static class C { "
                + "[global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode A(Props props) => null; "
                + "[global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode B(Props props) => null; } }";

            // Act
            var run = GeneratorTestHelper.Run(source, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new PropsComparerGenerator() });

            // Assert
            Assert.Equal(1, Regex.Matches(run.GeneratedSources.Single().Source, "typeof").Count);
        }

        [Fact]
        public void Given_ComponentTakingNoProps_When_TheGeneratorRuns_Then_NothingIsEmitted()
        {
            // Arrange
            const string source = "namespace App { public static class C { [global::Velvet.Component(Memoize = true)] public static global::Velvet.VNode A() => null; } }";

            // Act
            var run = GeneratorTestHelper.Run(source, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new PropsComparerGenerator() });

            // Assert
            Assert.Empty(run.GeneratedSources);
        }
    }
}
