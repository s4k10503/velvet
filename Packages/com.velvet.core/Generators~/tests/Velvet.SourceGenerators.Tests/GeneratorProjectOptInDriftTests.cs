using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Velvet.SourceGenerators.CodeShape;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// The code-shape rules reach this solution through two MSBuild items per project — the opt-in marker and
    /// an analyzer reference to the bootstrap — and a project carrying neither compiles clean, so it is exempt
    /// with nothing anywhere to say so. That is the state the whole solution was in before the wiring existed,
    /// and a project added later would return to it silently.
    /// </summary>
    /// <remarks>
    /// Read out of the project files rather than out of a built assembly because only one of the two halves
    /// leaves a trace in metadata: an assembly carrying the marker but loading no analyzer is byte-for-byte
    /// indistinguishable from an enforced one.
    /// </remarks>
    public sealed class GeneratorProjectOptInDriftTests
    {
        private const string BootstrapProjectFile = "Velvet.SourceGenerators.Bootstrap.csproj";

        [Fact]
        public void Given_TheSolutionsProjects_When_Enumerated_Then_EveryOneDeclaresTheCodeShapeMarker()
        {
            // Arrange
            var projects = EnforcedProjects();

            // Act
            var unmarked = projects
                .Where(project => !DeclaresMarker(project))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal(Array.Empty<string?>(), unmarked);
        }

        [Fact]
        public void Given_TheSolutionsProjects_When_Enumerated_Then_EveryOneLoadsTheAnalyzers()
        {
            // Arrange
            var projects = EnforcedProjects();

            // Act
            var unanalyzed = projects
                .Where(project => !ReferencesTheBootstrapAsAnAnalyzer(project))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal(Array.Empty<string?>(), unanalyzed);
        }

        [Fact]
        public void Given_TheSolutionsProjects_When_Enumerated_Then_TheGuardFoundSomeToCheck()
        {
            // A changed search root would leave both guards above passing over an empty set.
            // Arrange
            var projects = EnforcedProjects();

            // Act
            var count = projects.Count;

            // Assert
            Assert.True(count >= 4, $"Expected at least 4 enforced projects, found {count}.");
        }

        [Fact]
        public void Given_TheBootstrapAssembly_When_Compared_Then_ItDeclaresTheSameTypesAsTheProjectItStandsIn()
        {
            // The bootstrap reaches its sources through a glob into the sibling's directory. A glob that
            // stopped matching would leave an analyzer assembly holding no analyzers, and every project in
            // the solution would then compile clean with nothing enforced and nothing failing.
            // Arrange
            var project = DeclaredTypeNames(Path.Combine(AppContext.BaseDirectory, "Velvet.SourceGenerators.dll"));

            // Act
            var bootstrap = DeclaredTypeNames(BootstrapAssemblyPath());

            // Assert
            Assert.Equal(project, bootstrap);
        }

        /// <summary>
        /// Every project under <c>Generators~</c> that the rules apply to. The bootstrap is not one of them:
        /// it compiles the sibling's sources, which the sibling's own compile already measures, so enforcing
        /// there would report each violation twice at one remove from the file that holds it.
        /// </summary>
        private static List<string> EnforcedProjects() =>
            Directory.EnumerateFiles(SolutionPaths.GeneratorsRoot(), "*.csproj", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Where(path => !path.EndsWith(BootstrapProjectFile, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

        private static bool DeclaresMarker(string projectFile) =>
            XDocument.Load(projectFile).Descendants("AssemblyMetadata").Any(item =>
                string.Equals(item.Attribute("Include")?.Value, CodeShapeMembers.MarkerKey, StringComparison.Ordinal)
                && string.Equals(item.Attribute("Value")?.Value, CodeShapeMembers.MarkerValue,
                    StringComparison.Ordinal));

        private static bool ReferencesTheBootstrapAsAnAnalyzer(string projectFile) =>
            XDocument.Load(projectFile).Descendants("ProjectReference").Any(item =>
                (item.Attribute("Include")?.Value ?? string.Empty)
                .EndsWith(BootstrapProjectFile, StringComparison.Ordinal)
                && string.Equals(item.Attribute("OutputItemType")?.Value, "Analyzer", StringComparison.Ordinal));

        /// <summary>
        /// Narrowed to the configuration this run was built in, because a stale build of the other one would
        /// otherwise be a candidate and could fail the comparison over sources nobody had changed.
        /// </summary>
        private static string BootstrapAssemblyPath()
        {
            var output = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var configuration = Path.GetFileName(Path.GetDirectoryName(output)!);
            var root = Path.Combine(
                SolutionPaths.GeneratorsRoot(), "src", "Velvet.SourceGenerators.Bootstrap", "bin", configuration);
            var built = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "Velvet.SourceGenerators.Bootstrap.dll",
                    SearchOption.AllDirectories).ToList()
                : new List<string>();
            if (built.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one bootstrap assembly under '{root}', found {built.Count}.");
            }

            return built[0];
        }

        /// <summary>
        /// Read as metadata rather than by loading the assembly: it targets <c>netstandard2.0</c> against a
        /// Roslyn version this test host does not carry, so resolving its references would throw.
        /// </summary>
        private static List<string> DeclaredTypeNames(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
            {
                throw new InvalidOperationException($"Expected an assembly at '{assemblyPath}'.");
            }

            var reference = MetadataReference.CreateFromFile(assemblyPath);
            var compilation = CSharpCompilation.Create("TypeNameProbe", references: new[] { reference });
            var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;

            var names = new List<string>();
            CollectTypeNames(assembly.GlobalNamespace, names);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static void CollectTypeNames(INamespaceSymbol ns, List<string> names)
        {
            foreach (var type in ns.GetTypeMembers())
            {
                names.Add(type.ToDisplayString());
            }

            foreach (var nested in ns.GetNamespaceMembers())
            {
                CollectTypeNames(nested, names);
            }
        }
    }
}
