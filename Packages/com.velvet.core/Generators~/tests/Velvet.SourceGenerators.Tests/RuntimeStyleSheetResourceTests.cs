using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins the chain runtime code walks to reach the bundled utilities: the <c>Resources</c> path constant,
    /// the asset it names, and the aggregator that asset imports.
    /// </summary>
    /// <remarks>
    /// Every link fails silently on its own. A constant naming an asset that is not there throws only when
    /// something calls it, which no compile does. An import left behind by a move of the aggregator still
    /// imports successfully and still loads — the asset simply carries no rules, so the utilities it declares
    /// resolve to nothing while the C#-realised families and arbitrary-value classes keep working, and the
    /// result reads as a partial styling bug. The license-free generator workflow fires on any change under
    /// Runtime/, so a break is caught there rather than only in a Unity run.
    /// </remarks>
    public sealed class RuntimeStyleSheetResourceTests
    {
        private static readonly Regex ImportPattern = new(@"@import\s+url\(""([^""]+)""\)", RegexOptions.Compiled);

        private static readonly Regex ResourcePathPattern =
            new(@"ResourcePath\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        [Fact]
        public void Given_TheResourcePathConstant_When_ResolvedUnderRuntimeResources_Then_TheAssetIsThere()
        {
            // Arrange
            var runtimeRoot = SolutionPaths.RuntimeRoot();

            // Act
            var resourcePath = DeclaredResourcePath(runtimeRoot);
            var asset = Path.Combine(runtimeRoot, "Resources", resourcePath + ".uss");

            // Assert — the constant is carried alongside the resolved location, so a failure says which of
            // the two moved rather than only that they disagree.
            Assert.Equal(("Velvet/StyleUtilities", true), (resourcePath, File.Exists(asset)));
        }

        [Fact]
        public void Given_TheResourcesStyleSheet_When_ItsImportIsResolved_Then_ItReachesTheBundledAggregator()
        {
            // Arrange — the entry point is derived from the constant rather than written again here, so this
            // guard cannot pass against an asset the runtime does not actually load.
            var runtimeRoot = SolutionPaths.RuntimeRoot();
            var entryPoint = Path.Combine(runtimeRoot, "Resources", DeclaredResourcePath(runtimeRoot) + ".uss");

            // Act
            var match = ImportPattern.Match(File.ReadAllText(entryPoint));
            var imported = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(entryPoint)!, match.Groups[1].Value));

            // Assert — the target is reported relative to the runtime root so a failure names the sheet that
            // was imported instead of two absolute paths that differ past the width the runner prints.
            // Existence is folded in because comparing two paths that are both stale passes: a move of the
            // aggregator that leaves this import behind agrees with an expectation built from the same stale
            // literal.
            Assert.Equal(
                (Path.Combine("Styles", "StyleUtilities.uss"), true),
                (Path.GetRelativePath(runtimeRoot, imported), File.Exists(imported)));
        }

        private static string DeclaredResourcePath(string runtimeRoot) =>
            ResourcePathPattern
                .Match(File.ReadAllText(Path.Combine(runtimeRoot, "Styling", "VelvetStyleUtilities.cs")))
                .Groups[1].Value;
    }
}
