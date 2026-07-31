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
    /// The three rules live in one fixture because they share a declaration surface and a backlog: splitting
    /// them would mean separate scans that can disagree about what a member is.
    /// </para>
    /// </summary>
    public sealed class CodeShapeBacklogDriftTests
    {
        [Fact]
        public void Given_ThePackageSources_When_Measured_Then_NoMemberExceedsTheNestingDepthLimit()
        {
            // Arrange
            var members = Scan.Value.Bodies;

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
            var members = Scan.Value.Bodies;

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
        public void Given_ThePackageSources_When_Measured_Then_NoMemberExceedsTheParameterCountLimit()
        {
            // Arrange
            var declarations = Scan.Value.Parameters;

            // Act
            var over = declarations
                .Where(d => d.Required > ParameterCountAnalyzer.MaxParameters)
                .Select(d => $"{d.File} {d.Display} demands {d.Required}")
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
            var members = Scan.Value.Bodies;

            // Act
            var count = members.Count;

            // Assert
            Assert.True(count >= 5000, $"Expected at least 5000 measurable member bodies, found {count}.");
        }

        [Fact]
        public void Given_ThePackageSources_When_Enumerated_Then_TheScanFoundParameterListsToMeasure()
        {
            // The parameter surface is enumerated separately from the body surface — an abstract method has
            // a list and no body — so emptying one leaves the other's canary green.
            // Arrange
            var declarations = Scan.Value.Parameters;

            // Act
            var count = declarations.Count;

            // Assert
            Assert.True(count >= 5000, $"Expected at least 5000 measurable parameter lists, found {count}.");
        }

        private static readonly Lazy<(
            IReadOnlyList<(string File, string Display, int Depth, int Branches)> Bodies,
            IReadOnlyList<(string File, string Display, int Required)> Parameters)> Scan = new(Measure);

        /// <summary>
        /// Unity compiles the editor-platform assemblies with <c>UNITY_EDITOR</c> defined and the rest
        /// without, so a body inside an <c>#if UNITY_EDITOR</c> is real code in one assembly and disabled
        /// text in another. Parsing with no symbols would hide it — several hundred members of this package
        /// — from a guard whose whole job is to see everything that compiles.
        /// </summary>
        private static readonly CSharpParseOptions[] Configurations =
        {
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols("UNITY_EDITOR"),
        };

        /// <summary>
        /// Every body and every parameter list the analyzers would see, in either configuration.
        /// `Generators~` is excluded because its own solution loads the analyzers and is held to the limits
        /// at compile time, and generated files because the analyzers opt out of generated code.
        /// </summary>
        private static (
            IReadOnlyList<(string File, string Display, int Depth, int Branches)> Bodies,
            IReadOnlyList<(string File, string Display, int Required)> Parameters) Measure()
        {
            var packageRoot = Path.GetFullPath(Path.Combine(SolutionPaths.GeneratorsRoot(), ".."));
            // Keyed on where the measured construct starts: a body or a parameter list outside any #if is
            // parsed by both configurations and would otherwise be measured, and reported, twice.
            var bodies = new Dictionary<(string File, int Start), (string, string, int, int)>();
            var parameters = new Dictionary<(string File, int Start), (string, string, int)>();
            foreach (var file in Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}Generators~{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)) continue;
                if (file.EndsWith(".g.cs", StringComparison.Ordinal)) continue;

                var text = File.ReadAllText(file);
                var relative = file.Substring(packageRoot.Length + 1);
                foreach (var options in Configurations)
                {
                    MeasureTree(CSharpSyntaxTree.ParseText(text, options), relative, bodies, parameters);
                }
            }

            return (bodies.Values.ToList(), parameters.Values.ToList());
        }

        private static void MeasureTree(
            SyntaxTree tree,
            string relative,
            Dictionary<(string File, int Start), (string, string, int, int)> bodies,
            Dictionary<(string File, int Start), (string, string, int)> parameters)
        {
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                RecordBodies(node, relative, bodies);
                RecordParameters(node, relative, parameters);
            }
        }

        private static void RecordBodies(
            SyntaxNode node,
            string relative,
            Dictionary<(string File, int Start), (string, string, int, int)> bodies)
        {
            if (!CodeShapeMembers.MemberKinds.Contains(node.Kind())) return;

            foreach (var (body, _, display) in CodeShapeMembers.BodiesOf(node))
            {
                bodies[(relative, body.SpanStart)] = (relative, display,
                    NestingDepthAnalyzer.Measure(body), BranchCountAnalyzer.Measure(body));
            }
        }

        private static void RecordParameters(
            SyntaxNode node,
            string relative,
            Dictionary<(string File, int Start), (string, string, int)> parameters)
        {
            if (!CodeShapeMembers.ParameterizedKinds.Contains(node.Kind())) return;

            var declared = CodeShapeMembers.ParametersOf(node);
            if (declared == null) return;

            parameters[(relative, declared.Value.Name.SpanStart)] = (relative,
                declared.Value.Display, ParameterCountAnalyzer.Measure(declared.Value.Parameters));
        }
    }
}
