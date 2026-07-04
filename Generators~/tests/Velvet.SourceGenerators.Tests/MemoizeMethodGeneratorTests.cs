using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Compares MemoizeMethodGenerator output against the golden files under Snapshots/Memoize/.
    /// On failure, writes the actual output via _testOutputHelper so the diff can be inspected from CI logs.
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
        [global::Velvet.Memoize]
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
        [global::Velvet.Memoize]
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
        [global::Velvet.Memoize]
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
        [global::Velvet.Memoize]
        private partial global::Velvet.VNode BuildHeader(string title);

        [global::Velvet.Memoize]
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
            [global::Velvet.Memoize]
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
        [global::Velvet.Memoize]
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
        [global::Velvet.Memoize]
        private partial global::Velvet.VNode Build(int x);

        private global::Velvet.VNode Build_Impl(int x) => null;
    }
}",
                expectedHintName: "MyApp.Container_T2.Memoize.g.cs",
                snapshotFile: "GenericContainerT2.verified.cs");
        }

        [Theory]
        // VEL001 (arity 0) is exercised by the dedicated Memoize_Arity0_* tests because the new behaviour generates
        // a wrapper alongside the warning, which AssertOnlyDiagnostic's "no generated sources" precondition rejects.
        [InlineData("VEL002", "private partial global::Velvet.VNode Build(int a, int b, int c, int d, int e, int f, int g, int h, int i);", "public partial class Page")]
        [InlineData("VEL003", "private partial global::Velvet.VNode Build<T>(T value);", "public partial class Page")]
        [InlineData("VEL004", "private partial global::System.Threading.Tasks.Task<global::Velvet.VNode> Build(int x);", "public partial class Page")]
        [InlineData("VEL005", "private partial global::Velvet.VNode Build(ref int x);", "public partial class Page")]
        [InlineData("VEL005", "private partial global::Velvet.VNode Build(in int x);", "public partial class Page")]
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
        [global::Velvet.Memoize]
        {methodDecl}
    }}
}}",
                expectedId: expectedId);
        }

        [Fact]
        public void Memoize_Arity0_PureImpl_GeneratesDepsLessMemoCall()
        {
            // arity 0 with a Pure _Impl is allowed; the generated wrapper omits the deps argument
            // so V.Memoized caches the VNode forever (factory is deterministic).
            var result = GeneratorTestHelper.Run(@"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.Memoize]
        private static partial global::Velvet.VNode BuildBanner();

        private static global::Velvet.VNode BuildBanner_Impl() => null;
    }
}");
            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedSources);
            Assert.Contains("V.Memoized(() => BuildBanner_Impl());", result.GeneratedSources[0].Source);
        }

        [Fact]
        public void Memoize_Arity0_ImpureImpl_WarnsButGenerates()
        {
            // Deps-less memoization warns but allows: VEL001 fires, yet the wrapper is still generated
            // so callers compile. The user is trusted to fix the impure _Impl or accept the stale cache.
            var result = GeneratorTestHelper.Run(@"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.Memoize]
        private static partial global::Velvet.VNode BuildBanner();

        private static global::Velvet.VNode BuildBanner_Impl() { throw new System.Exception(); }
    }
}");
            Assert.Contains(result.Diagnostics, d => d.Id == "VEL001");
            Assert.Single(result.GeneratedSources);
            Assert.Contains("V.Memoized(() => BuildBanner_Impl());", result.GeneratedSources[0].Source);
        }

        [Fact]
        public void Memoize_Arity0_MissingImpl_WarnsButGenerates()
        {
            // arity 0 with no _Impl member: cannot prove purity → VEL001. Wrapper is still generated; the missing
            // _Impl call is surfaced as a regular C# compile error in user code.
            var result = GeneratorTestHelper.Run(@"
namespace MyApp.Pages
{
    public partial class HomePage
    {
        [global::Velvet.Memoize]
        private static partial global::Velvet.VNode BuildBanner();
    }
}");
            Assert.Contains(result.Diagnostics, d => d.Id == "VEL001");
            Assert.Single(result.GeneratedSources);
        }

        [Fact]
        public void Memoize_NonPartialMethod_NoGenerationNoCrash()
        {
            var result = GeneratorTestHelper.Run(@"
namespace MyApp
{
    public partial class Page
    {
        [global::Velvet.Memoize]
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
