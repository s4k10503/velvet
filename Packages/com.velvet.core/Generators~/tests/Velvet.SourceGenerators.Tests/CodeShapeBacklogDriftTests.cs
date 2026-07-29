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
    /// Re-measures the code-shape limits over the package's own sources, because nothing else does without
    /// a Unity license: `unity-tests` is skipped unless a `UNITY_LICENSE` secret is set, so on a fork or an
    /// outside contributor's PR the only job that compiles the marked assemblies never runs. Without this
    /// the rules are enforced exactly where they are least needed and unverified where they ship from —
    /// and `Velvet.asmdef` compiles in every consumer's project, so a member that crosses a limit becomes
    /// their build error, not ours.
    /// <para>
    /// Both rules live in one fixture because they share a member surface and a backlog: splitting them
    /// would mean two scans that can disagree about what a member is.
    /// </para>
    /// </summary>
    public sealed class CodeShapeBacklogDriftTests
    {
        [Fact]
        public void Given_ThePackageSources_When_Measured_Then_NoMemberExceedsTheNestingDepthLimit()
        {
            // Arrange
            var members = PackageMembers.Value;

            // Act
            var over = members
                .Where(m => m.Depth > NestingDepthAnalyzer.MaxDepth)
                .Select(m => $"{m.File} {m.Display} nests {m.Depth}")
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal(Array.Empty<string>(), over);
        }

        [Fact]
        public void Given_ThePackageSources_When_Measured_Then_NoMemberExceedsTheBranchCountLimit()
        {
            // Arrange
            var members = PackageMembers.Value;

            // Act
            var over = members
                .Where(m => m.Branches > BranchCountAnalyzer.MaxBranches)
                .Select(m => $"{m.File} {m.Display} makes {m.Branches}")
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal(Array.Empty<string>(), over);
        }

        [Fact]
        public void Given_ThePackageSources_When_Enumerated_Then_TheScanFoundBodiesToMeasure()
        {
            // A path change that emptied the scan would make both guards above pass having measured nothing.
            // Arrange
            var members = PackageMembers.Value;

            // Act
            var count = members.Count;

            // Assert
            Assert.True(count >= 5000, $"Expected at least 5000 measurable member bodies, found {count}.");
        }

        private static readonly Lazy<IReadOnlyList<(string File, string Display, int Depth, int Branches)>>
            PackageMembers = new(Measure);

        /// <summary>
        /// Every body the analyzers would see. `Generators~` is excluded because it is a separate solution
        /// that never references Velvet and so never loads these analyzers, and generated files because the
        /// analyzers opt out of generated code.
        /// </summary>
        private static IReadOnlyList<(string File, string Display, int Depth, int Branches)> Measure()
        {
            var packageRoot = Path.GetFullPath(Path.Combine(SolutionPaths.GeneratorsRoot(), ".."));
            var rows = new List<(string, string, int, int)>();
            foreach (var file in Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}Generators~{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)) continue;
                if (file.EndsWith(".g.cs", StringComparison.Ordinal)) continue;

                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file),
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
                var relative = file.Substring(packageRoot.Length + 1);
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    if (!CodeShapeMembers.MemberKinds.Contains(node.Kind())) continue;
                    foreach (var (body, _, display) in CodeShapeMembers.BodiesOf(node))
                    {
                        rows.Add((relative, display, NestingDepthAnalyzer.Measure(body),
                            BranchCountAnalyzer.Measure(body)));
                    }
                }
            }

            return rows;
        }
    }
}
