using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Velvet.SourceGenerators.CodeShape;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// VEL500 is an error that only fires in an assembly carrying the opt-in marker, so a package assembly
    /// that lacks it is exempt from the limit with nothing to say so. Without this guard a new asmdef starts
    /// life outside the rule and the omission is invisible — the suite stays green precisely because the
    /// analyzer never ran there.
    /// </summary>
    /// <remarks>
    /// Whether a source declares the marker is decided by calling the analyzer's own gate against a real
    /// compilation, never by re-reading the syntax here. A second implementation of that question drifted
    /// from the first in both directions — it accepted a user-defined attribute merely named
    /// <c>…AssemblyMetadata</c>, and rejected reversed named arguments, a <c>using</c> alias and a
    /// <c>const</c> indirection that the gate accepts — so a guard built that way vouches for assemblies the
    /// analyzer will not enforce, which is the one thing it exists to prevent.
    /// </remarks>
    public sealed class CodeShapeOptInDriftTests
    {
        // Unity compiles the editor-platform assemblies with UNITY_EDITOR defined and player builds without
        // it. Requiring the marker under both is what stops a `#if`-guarded opt-in from enforcing the rule
        // in one configuration and silently skipping it in the other.
        //
        // Adding a third configuration means adding a third test. A conditional spelling is visible to
        // exactly one configuration, so one case per configuration is the only coverage that holds: a single
        // case reads as covering the feature while covering one configuration's worth of it, and the
        // untested configuration can then be deleted with nothing going red.
        private static readonly string[][] PreprocessorConfigurations =
        {
            Array.Empty<string>(),
            new[] { "UNITY_EDITOR" },
        };

        [Fact]
        public void Given_ThePackageAsmdefs_When_Enumerated_Then_EveryOneOptsIntoTheCodeShapeRules()
        {
            // Arrange
            var asmdefs = PackageAsmdefs();

            // Act
            var unmarked = asmdefs
                .Where(asmdef => !DeclaresMarker(asmdef, asmdefs))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal(Array.Empty<string?>(), unmarked);
        }

        [Fact]
        public void Given_TheEnumerationRoot_When_Resolved_Then_ItIsThePackageItself()
        {
            // Re-anchoring one directory higher still enumerates the same asmdefs, so a count alone cannot
            // tell the package apart from an ancestor of it. The manifest can.
            // Arrange
            var root = PackageRoot();

            // Act
            var manifest = Path.Combine(root, "package.json");

            // Assert
            Assert.True(File.Exists(manifest), $"Expected the package manifest at '{manifest}'.");
        }

        [Fact]
        public void Given_ThePackageAsmdefs_When_Enumerated_Then_TheGuardFoundSomeToCheck()
        {
            // A changed search pattern would leave the marker guard passing over an empty set.
            // Arrange
            var asmdefs = PackageAsmdefs();

            // Act
            var count = asmdefs.Count;

            // Assert
            Assert.NotEqual(0, count);
        }

        [Fact]
        public void Given_AnAssemblyQuotingTheAttributeInAStringLiteral_When_Checked_Then_ItDoesNotCount()
        {
            // Arrange
            var source = "public static class Decoy { public const string S = "
                + "\"[assembly: System.Reflection.AssemblyMetadata(\\\"Velvet.CodeShape\\\", \\\"enforce\\\")]\"; }";

            // Act
            var declares = DeclaresMarkerInSources(new[] { source });

            // Assert
            Assert.False(declares);
        }

        [Fact]
        public void Given_AMarkerAbsentFromTheEditorBuild_When_Checked_Then_ItDoesNotCount()
        {
            // Arrange
            var source = "#if !UNITY_EDITOR\n"
                + "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"enforce\")]\n"
                + "#endif\n";

            // Act
            var declares = DeclaresMarkerInSources(new[] { source });

            // Assert
            Assert.False(declares);
        }

        [Fact]
        public void Given_AMarkerAbsentFromThePlayerBuild_When_Checked_Then_ItDoesNotCount()
        {
            // The mirror of the case above. The two look mergeable and are not: this one fails only if the
            // no-symbols configuration is consulted and the one above only if UNITY_EDITOR is, so collapsing
            // them into a single case reopens the gap that motivated both.
            // Arrange
            var source = "#if UNITY_EDITOR\n"
                + "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"enforce\")]\n"
                + "#endif\n";

            // Act
            var declares = DeclaresMarkerInSources(new[] { source });

            // Assert
            Assert.False(declares);
        }

        [Fact]
        public void Given_AnUnconditionalMarker_When_Checked_Then_ItCounts()
        {
            // The negative cases above pass for an empty file too; this pins that the probe can say yes.
            // Arrange
            var source = "[assembly: System.Reflection.AssemblyMetadata(\"Velvet.CodeShape\", \"enforce\")]\n";

            // Act
            var declares = DeclaresMarkerInSources(new[] { source });

            // Assert
            Assert.True(declares);
        }

        private static string PackageRoot() =>
            Path.GetFullPath(Path.Combine(SolutionPaths.GeneratorsRoot(), ".."));

        private static List<string> PackageAsmdefs() =>
            Directory.EnumerateFiles(PackageRoot(), "*.asmdef", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// An assembly's sources are the files below its own asmdef's directory minus those claimed by a
        /// nested asmdef, which is how Unity assigns a file to an assembly.
        /// </summary>
        private static bool DeclaresMarker(string asmdef, IReadOnlyList<string> allAsmdefs)
        {
            var root = Path.GetDirectoryName(asmdef)!;
            var nestedRoots = allAsmdefs
                .Where(other => !string.Equals(other, asmdef, StringComparison.Ordinal))
                .Select(other => Path.GetDirectoryName(other)! + Path.DirectorySeparatorChar)
                .Where(dir => dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .ToList();

            var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => !nestedRoots.Any(nested => file.StartsWith(nested, StringComparison.Ordinal)))
                .Select(File.ReadAllText)
                .ToList();

            return DeclaresMarkerInSources(sources);
        }

        private static bool DeclaresMarkerInSources(IReadOnlyCollection<string> sources) =>
            PreprocessorConfigurations.All(symbols => OptsIn(sources, symbols));

        private static bool OptsIn(IReadOnlyCollection<string> sources, string[] preprocessorSymbols)
        {
            var parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols(preprocessorSymbols);
            var compilation = CSharpCompilation.Create(
                assemblyName: "MarkerProbe",
                syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions)),
                references: GeneratorTestHelper.ReferenceAssemblies(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return NestingDepthAnalyzer.OptsIntoCodeShapeRules(compilation.Assembly);
        }
    }
}
