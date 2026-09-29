using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// CI has no interactive diff viewer, so on failure this writes the actual output via _testOutputHelper
    /// where it is visible in the CI logs. Compares MemoizeMethodGenerator output against the golden files
    /// under Snapshots/Memoize/.
    /// </summary>
    public sealed class MemoizeMethodGeneratorTests
    {
        private readonly ITestOutputHelper _output;

        public MemoizeMethodGeneratorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Memoize_Arity1_GeneratesMemoCallWithSingleDep()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode BuildHeader(string title);

        private global::Velvet.VNode BuildHeader_Impl(string title) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "Arity1.verified.cs");
        }

        [Fact]
        public void Memoize_Arity3_GeneratesMemoCallWithThreeDeps()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode BuildHeader(string title, int count, bool visible);

        private global::Velvet.VNode BuildHeader_Impl(string title, int count, bool visible) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "Arity3.verified.cs");
        }

        [Fact]
        public void Memoize_Arity8_GeneratesMemoCallWithEightDeps()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build(int a, int b, int c, int d, int e, int f, int g, int h);

        private global::Velvet.VNode Build_Impl(int a, int b, int c, int d, int e, int f, int g, int h) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "Arity8.verified.cs");
        }

        [Fact]
        public void Memoize_MultipleMethodsSameClass_GeneratesSingleFile()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode BuildHeader(string title);

        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode BuildFooter(int count);

        private global::Velvet.VNode BuildHeader_Impl(string title) => null;
        private global::Velvet.VNode BuildFooter_Impl(int count) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "MultipleMethods.verified.cs");
        }

        [Fact]
        public void Memoize_NestedClass_GeneratesCorrectClassChain()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp
{
    public partial class Outer
    {
        public partial class Inner
        {
            [global::Velvet.MemoizeMethod]
            private partial global::Velvet.VNode Build(int x);

            private global::Velvet.VNode Build_Impl(int x) => null;
        }
    }
}",
                expectedHintName: "MyApp.Outer_Inner.Memoize.g.cs",
                snapshotFile: "NestedClass.verified.cs");
        }

        [Fact]
        public void Memoize_StaticPartial_EmitsStaticModifier()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class StaticHost
    {
        [global::Velvet.MemoizeMethod]
        public static partial global::Velvet.VNode Build(int x);

        private static global::Velvet.VNode Build_Impl(int x) => null;
    }
}",
                expectedHintName: "MyApp.Pages.StaticHost.Memoize.g.cs",
                snapshotFile: "StaticPartial.verified.cs");
        }

        [Fact]
        public void Memoize_GenericContainer_T2_GeneratesArityFilename()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp
{
    public partial class Container<TKey, TValue>
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build(int x);

        private global::Velvet.VNode Build_Impl(int x) => null;
    }
}",
                expectedHintName: "MyApp.Container_T2.Memoize.g.cs",
                snapshotFile: "GenericContainerT2.verified.cs");
        }

        [Fact]
        public void Memoize_Arity9_GeneratesMemoCallWithNineDeps()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build(int a, int b, int c, int d, int e, int f, int g, int h, int i);

        private global::Velvet.VNode Build_Impl(int a, int b, int c, int d, int e, int f, int g, int h, int i) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "Arity9.verified.cs");
        }

        [Fact]
        public void Memoize_GenericMethod_RepeatsTypeParametersAndConstraints()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
#nullable enable
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build<TItem, TKey, TRow, TCell>(TItem item, TKey key, TRow row, TCell cell)
            where TItem : class?, global::System.IComparable<TItem>, new()
            where TKey : notnull
            where TRow : struct
            where TCell : unmanaged;

        private global::Velvet.VNode Build_Impl<TItem, TKey, TRow, TCell>(TItem item, TKey key, TRow row, TCell cell)
            where TItem : class?, global::System.IComparable<TItem>, new()
            where TKey : notnull
            where TRow : struct
            where TCell : unmanaged => null!;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "GenericMethod.verified.cs");
        }

        [Fact]
        public void Memoize_InParameter_MemoizesOnACopy()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public readonly struct Row { public readonly int Id; }

    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build(in Row row, string title, in int rowValue);

        private global::Velvet.VNode Build_Impl(in Row row, string title, int rowValue) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "InParameter.verified.cs");
        }

        [Fact]
        public void Memoize_InParameterNamedLikeATypeParameter_CopiesUnderAnotherName()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build<TValue>(in int T, TValue value);

        private global::Velvet.VNode Build_Impl<TValue>(int T, TValue value) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "InParameterTypeParameterName.verified.cs");
        }

        [Fact]
        public void Memoize_ParamsParameter_IsRepeated()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build(string title, params string[] items);

        private global::Velvet.VNode Build_Impl(string title, string[] items) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "ParamsParameter.verified.cs");
        }

        [Fact]
        public void Memoize_KeywordNamedParameter_IsEscaped()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build(string @class);

        private global::Velvet.VNode Build_Impl(string @class) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "KeywordNamedParameter.verified.cs");
        }

        [Fact]
        public void Memoize_ExtensionMethod_RepeatsThis()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public static partial class RowViews
    {
        [global::Velvet.MemoizeMethod]
        public static partial global::Velvet.VNode ToView(this string title);

        private static global::Velvet.VNode ToView_Impl(string title) => null;
    }
}",
                expectedHintName: "MyApp.Pages.RowViews.Memoize.g.cs",
                snapshotFile: "ExtensionMethod.verified.cs");
        }

        [Fact]
        public void Memoize_GenericOverride_RepeatsModifiers()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public abstract class PageBase
    {
        public abstract global::Velvet.VNode Header<T>(T title) where T : global::System.IComparable<T>;
    }

    public partial class HomePage : PageBase
    {
        [global::Velvet.MemoizeMethod]
        public sealed override partial global::Velvet.VNode Header<T>(T title);

        private global::Velvet.VNode Header_Impl<T>(T title) where T : global::System.IComparable<T> => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "GenericOverride.verified.cs");
        }

        [Fact]
        public void Memoize_NewVirtual_RepeatsModifiers()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public class PageBase
    {
        public global::Velvet.VNode Footer(int count) => null;
    }

    public partial class HomePage : PageBase
    {
        [global::Velvet.MemoizeMethod]
        public new virtual partial global::Velvet.VNode Footer(int count);

        private global::Velvet.VNode Footer_Impl(int count) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "NewVirtual.verified.cs");
        }

        [Fact]
        public void Memoize_ReadOnlyStructInstanceMember_CallsImplOnACopyOfThis()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public readonly partial struct RowView
    {
        [global::Velvet.MemoizeMethod]
        public partial global::Velvet.VNode Build(int self, in int row);

        private global::Velvet.VNode Build_Impl(int self, int row) => null;
    }
}",
                expectedHintName: "MyApp.Pages.RowView.Memoize.g.cs",
                snapshotFile: "StructInstanceMember.verified.cs");
        }

        [Fact]
        public void Memoize_ReadOnlyStructMember_RepeatsReadOnly()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial struct RowView
    {
        [global::Velvet.MemoizeMethod]
        public readonly partial global::Velvet.VNode Build(int x);

        private readonly global::Velvet.VNode Build_Impl(int x) => null;
    }
}",
                expectedHintName: "MyApp.Pages.RowView.Memoize.g.cs",
                snapshotFile: "ReadOnlyStructMember.verified.cs");
        }

        [Fact]
        public void Memoize_GenericOverrideOfNullableReference_RepeatsItsClassConstraint()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
#nullable enable
namespace MyApp.Pages
{
    public abstract class PageBase
    {
        public abstract global::Velvet.VNode Header<T>(T? title) where T : class;
    }

    public partial class HomePage : PageBase
    {
        [global::Velvet.MemoizeMethod]
        public override partial global::Velvet.VNode Header<T>(T? title) where T : class;

        private global::Velvet.VNode Header_Impl<T>(T? title) where T : class => null!;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "GenericOverrideClass.verified.cs");
        }

        [Fact]
        public void Memoize_GenericOverrideOfUnconstrainedNullable_RepeatsItsDefaultConstraint()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
#nullable enable
namespace MyApp.Pages
{
    public abstract class PageBase
    {
        public abstract global::Velvet.VNode Header<T>(T? title);
    }

    public partial class HomePage : PageBase
    {
        [global::Velvet.MemoizeMethod]
        public override partial global::Velvet.VNode Header<T>(T? title) where T : default;

        private global::Velvet.VNode Header_Impl<T>(T? title) => null!;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "GenericOverrideDefault.verified.cs");
        }

        [Fact]
        public void Memoize_ExtensionInParameter_RepeatsThisAndIn()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public readonly struct Row { public readonly int Id; }

    public static partial class RowViews
    {
        [global::Velvet.MemoizeMethod]
        public static partial global::Velvet.VNode ToView(this in Row row);

        private static global::Velvet.VNode ToView_Impl(in Row row) => null;
    }
}",
                expectedHintName: "MyApp.Pages.RowViews.Memoize.g.cs",
                snapshotFile: "ExtensionInParameter.verified.cs");
        }

        [Fact]
        public void Memoize_UnsafeModifier_IsRepeated()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private unsafe partial global::Velvet.VNode Build(int x);

        private global::Velvet.VNode Build_Impl(int x) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "UnsafeModifier.verified.cs");
        }

        [Fact]
        public void Memoize_DynamicParameter_IsKeyedWithoutDynamicDispatch()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build(dynamic model);

        private global::Velvet.VNode Build_Impl(dynamic model) => null;
    }
}",
                expectedHintName: "MyApp.Pages.HomePage.Memoize.g.cs",
                snapshotFile: "DynamicParameter.verified.cs");
        }

        [Fact]
        public void Memoize_InterfaceMember_IsEmittedIntoAPartialInterface()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.Pages
{
    public partial interface IPage
    {
        [global::Velvet.MemoizeMethod]
        public partial global::Velvet.VNode Build(int x);

        private global::Velvet.VNode Build_Impl(int x) => null;
    }
}",
                expectedHintName: "MyApp.Pages.IPage.Memoize.g.cs",
                snapshotFile: "InterfaceMember.verified.cs");
        }

        [Fact]
        public void Memoize_KeywordNamedTypesAndTypeParameters_AreEscaped()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.@event
{
    public sealed class @delegate { }

    public partial class @class<@struct>
    {
        [global::Velvet.MemoizeMethod]
        private partial global::Velvet.VNode Build<@int>(@int x, @delegate y, @struct z);

        private global::Velvet.VNode Build_Impl<@int>(@int x, @delegate y, @struct z) => null;
    }
}",
                expectedHintName: "MyApp.event.class_T1.Memoize.g.cs",
                snapshotFile: "KeywordNamedTypes.verified.cs");
        }

        [Fact]
        public void Memoize_ContextualKeywordNamedTypesAndTypeParameters_AreEscaped()
        {
            AssertGeneratedMatchesSnapshot(
                inputSource: @"
namespace MyApp.@record
{
    public static partial class @record<@required>
    {
        [global::Velvet.MemoizeMethod]
        private static partial global::Velvet.VNode Build<@scoped>(@scoped x, @required y);

        private static global::Velvet.VNode Build_Impl<@scoped>(@scoped x, @required y) => null;
    }
}",
                expectedHintName: "MyApp.record.record_T1.Memoize.g.cs",
                snapshotFile: "ContextualKeywordNamedTypes.verified.cs");
        }

        [Fact]
        public void Memoize_VNodeSubtypeOtherThanMemoNode_ReportsVel008()
        {
            AssertOnlyDiagnostic(
                inputSource: @"
namespace MyApp
{
    public sealed class Card : global::Velvet.VNode { }

    public partial class Page
    {
        [global::Velvet.MemoizeMethod]
        private partial Card Build(int x);
    }
}",
                expectedId: "VEL008");
        }

        [Theory]
        [InlineData("VEL004", "private partial global::System.Threading.Tasks.Task<global::Velvet.VNode> Build(int x);", "public partial class Page")]
        [InlineData("VEL004", "private partial global::Velvet.VelvetTask<global::Velvet.VNode> Build(int x);", "public partial class Page")]
        [InlineData("VEL005", "private partial global::Velvet.VNode Build(ref int x);", "public partial class Page")]
        [InlineData("VEL005", "private partial global::Velvet.VNode Build(out int x);", "public partial class Page")]
        [InlineData("VEL010", "private partial global::Velvet.VNode Build(global::System.ReadOnlySpan<int> x);", "public partial class Page")]
        [InlineData("VEL010", "private unsafe partial global::Velvet.VNode Build(int* x);", "public partial class Page")]
        [InlineData("VEL010", "private unsafe partial global::Velvet.VNode Build(int*[] x);", "public partial class Page")]
        [InlineData("VEL008", "private partial ref global::Velvet.VNode Build(int x);", "public partial class Page")]
        [InlineData("VEL011", "public partial global::Velvet.VNode Build(int x);", "public partial struct Page")]
        [InlineData("VEL011", "public readonly partial global::Velvet.VNode Build(int x);", "public ref partial struct Page")]
        [InlineData("VEL006", "partial global::Velvet.VNode Build(int x);", "public partial class Page")]
        [InlineData("VEL007", "private partial global::Velvet.VNode Build(int x);", "public class Page")]
        [InlineData("VEL008", "private partial string Build(int x);", "public partial class Page")]
        [InlineData("VEL009", "private partial global::Velvet.VNode Build(int x) => null;", "public partial class Page")]
        public void Memoize_InvalidShape_ReportsExpectedDiagnostic(string expectedId, string methodDecl, string classDecl)
        {
            AssertOnlyDiagnostic(
                inputSource: $@"
namespace MyApp
{{
    {classDecl}
    {{
        [global::Velvet.MemoizeMethod]
        {methodDecl}
    }}
}}",
                expectedId: expectedId);
        }

        [Fact]
        public void Memoize_Arity0_PureImpl_GeneratesEmptyDepsMemoCall()
        {
            var result = GeneratorTestHelper.Run(@"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.MemoizeMethod]
        private static partial global::Velvet.VNode BuildBanner();

        private static global::Velvet.VNode BuildBanner_Impl() => null;
    }
}");
            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedSources);
            Assert.Contains(
                "V.Memoized(() => BuildBanner_Impl(), global::System.Array.Empty<object>());",
                result.GeneratedSources[0].Source);
        }

        [Fact]
        public void Memoize_Arity0_ImpureImpl_GeneratesWithoutDiagnostic()
        {
            // The same as useMemo(factory, []): an empty dependency list is a complete declaration whatever the
            // factory does, so nothing is reported about it.
            var result = GeneratorTestHelper.Run(@"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        private static int _renders;

        [global::Velvet.MemoizeMethod]
        private static partial global::Velvet.VNode BuildBanner();

        private static global::Velvet.VNode BuildBanner_Impl() { _renders++; return null; }
    }
}");
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void Memoize_NonPartialMethod_NoGenerationNoCrash()
        {
            var result = GeneratorTestHelper.Run(@"
namespace MyApp
{
    public partial class Page
    {
        [global::Velvet.MemoizeMethod]
        private global::Velvet.VNode Build(int x) => null;
    }
}");
            Assert.Empty(result.GeneratedSources);
            Assert.Empty(result.Diagnostics);
        }

        private void AssertGeneratedMatchesSnapshot(string inputSource, string expectedHintName, string snapshotFile)
        {
            var result = GeneratorTestHelper.Run(inputSource);

            if (!result.CompilationErrors.IsEmpty)
            {
                _output.WriteLine("Compilation errors detected in generated output:");
                foreach (var d in result.CompilationErrors)
                {
                    _output.WriteLine(d.ToString());
                }
            }
            Assert.Empty(result.CompilationErrors);
            Assert.Empty(result.Diagnostics);

            // The same source as a Unity consumer compiles it: C# 9, against the V.Memoized overloads the
            // package ships. The run above compiles at the latest version against the stub alone.
            var consumer = GeneratorTestHelper.RunAsCSharp9Consumer(inputSource);
            foreach (var d in consumer.CompilationErrors)
            {
                _output.WriteLine("C# 9 consumer: " + d);
            }
            Assert.Empty(consumer.CompilationErrors);

            var single = Assert.Single(result.GeneratedSources);
            Assert.Equal(expectedHintName, single.HintName);

            var snapshotPath = Path.Combine(
                AppContext.BaseDirectory,
                "Snapshots",
                "Memoize",
                snapshotFile);

            if (!File.Exists(snapshotPath))
            {
                _output.WriteLine("Snapshot missing. Actual output below:");
                _output.WriteLine(single.Source);
                throw new Xunit.Sdk.XunitException($"Snapshot not found: {snapshotPath}");
            }

            var expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n").TrimEnd();
            var actual = single.Source.Replace("\r\n", "\n").TrimEnd();

            if (expected != actual)
            {
                _output.WriteLine("--- EXPECTED ---");
                _output.WriteLine(expected);
                _output.WriteLine("--- ACTUAL ---");
                _output.WriteLine(actual);
            }

            Assert.Equal(expected, actual);
        }

        private void AssertOnlyDiagnostic(string inputSource, string expectedId)
        {
            var result = GeneratorTestHelper.Run(inputSource);
            Assert.Empty(result.GeneratedSources);

            if (result.Diagnostics.Length != 1 || result.Diagnostics[0].Id != expectedId)
            {
                var observed = string.Join(", ", result.Diagnostics.Select(d => d.Id));
                _output.WriteLine($"Expected exactly one {expectedId} but observed [{observed}]:");
                foreach (var d in result.Diagnostics)
                {
                    _output.WriteLine(d.ToString());
                }
            }

            var diag = Assert.Single(result.Diagnostics);
            Assert.Equal(expectedId, diag.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        }
    }
}
