using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Velvet.SourceGenerators.CodeShape;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// A project here declares two MSBuild items to be held to the code-shape rules — the opt-in marker and
    /// an analyzer reference to the bootstrap — and a project carrying neither compiles clean, so it is exempt
    /// with nothing anywhere to say so. That is the state the whole solution was in before the wiring existed,
    /// and a project added later would return to it silently.
    /// </summary>
    /// <remarks>
    /// Neither half is decided by reading the project file. Both items can arrive from a
    /// <c>Directory.Build.props</c>, from an imported <c>.targets</c>, through a condition or through a
    /// property, and the marker can additionally be declared and then never emitted — so a reader of the XML
    /// answers a different question from the one the build answers, in both directions. That is the drift
    /// <see cref="CodeShapeOptInDriftTests"/> already found once, on this same question.
    /// <para>
    /// The two halves therefore use the two different instruments that have already resolved it: the marker
    /// is read out of the built assembly and put to the analyzer's own gate, and the analyzer reference is
    /// read out of MSBuild's evaluation. The marker cannot use evaluation alone, because an item that
    /// survives evaluation still reaches no assembly under <c>GenerateAssemblyInfo=false</c>; the reference
    /// cannot use metadata at all, because <c>ReferenceOutputAssembly=false</c> is what keeps it out.
    /// </para>
    /// <para>
    /// Declaring both is necessary and not sufficient. Measured on a project carrying both, with a
    /// parameter-count violation planted in it, <c>dotnet build -p:RunAnalyzers=false</c> and
    /// <c>dotnet build -p:NoWarn=VEL502</c> each build it clean — properties, and no guard here reads a
    /// property.
    /// </para>
    /// </remarks>
    public sealed class GeneratorProjectOptInDriftTests
    {
        private const string BootstrapProjectName = "Velvet.SourceGenerators.Bootstrap";

        [Fact]
        public void Given_TheSolutionsProjects_When_Enumerated_Then_EveryOneDeclaresTheCodeShapeMarker()
        {
            // Arrange
            var projects = EnforcedProjects();

            // Act
            var unmarked = projects
                .Where(project => !OptsInPerTheAnalyzersOwnGate(project))
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
                .Where(project => !EvaluatesToAnAnalyzerReferenceOnTheBootstrap(project))
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
            // stopped matching leaves an analyzer assembly holding no analyzers: a violation of each rule
            // planted in all four projects then raises no error anywhere, and only this comparison notices.
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
                .Where(path => !string.Equals(Path.GetFileNameWithoutExtension(path), BootstrapProjectName,
                    StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// The reference assemblies are not optional scaffolding: the gate compares the attribute's fully
        /// qualified name, and without a corlib to bind it to, every marker resolves to an error type and
        /// every project reads as exempt.
        /// </summary>
        private static bool OptsInPerTheAnalyzersOwnGate(string projectFile)
        {
            var reference = MetadataReference.CreateFromFile(BuiltAssemblyPath(projectFile));
            var compilation = CSharpCompilation.Create(
                "MarkerProbe", references: GeneratorTestHelper.ReferenceAssemblies().Append(reference));
            var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;

            return CodeShapeMembers.OptsIntoCodeShapeRules(assembly);
        }

        private static bool EvaluatesToAnAnalyzerReferenceOnTheBootstrap(string projectFile)
        {
            using var document = JsonDocument.Parse(EvaluateItem(projectFile, "ProjectReference"));
            if (!document.RootElement.GetProperty("Items").TryGetProperty("ProjectReference", out var items))
            {
                return false;
            }

            return items.EnumerateArray().Any(item =>
                string.Equals(item.GetProperty("Filename").GetString(), BootstrapProjectName,
                    StringComparison.Ordinal)
                && string.Equals(item.GetProperty("OutputItemType").GetString(), "Analyzer",
                    StringComparison.Ordinal));
        }

        /// <summary>
        /// Evaluation only — no target runs, so this costs about a second per project and cannot be
        /// perturbed by, or perturb, the build the test host is running inside.
        /// </summary>
        private static string EvaluateItem(string projectFile, string itemType)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(projectFile)!,
            };
            foreach (var argument in new[]
                     { "msbuild", projectFile, $"-getItem:{itemType}", "-nologo", "-nodeReuse:false" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start 'dotnet msbuild'.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Evaluating '{projectFile}' failed with exit code {process.ExitCode}: {error}{output}");
            }

            return output;
        }

        /// <summary>
        /// Narrowed to the configuration this run was built in, because a stale build of the other one would
        /// otherwise be a candidate and could answer for sources nobody had changed.
        /// </summary>
        private static string BuiltAssemblyPath(string projectFile)
        {
            var root = Path.Combine(Path.GetDirectoryName(projectFile)!, "bin", Configuration());
            var assemblyName = Path.GetFileNameWithoutExtension(projectFile) + ".dll";
            return SingleFileUnder(root, assemblyName);
        }

        private static string BootstrapAssemblyPath() => SingleFileUnder(
            Path.Combine(SolutionPaths.GeneratorsRoot(), "src", BootstrapProjectName, "bin", Configuration()),
            BootstrapProjectName + ".dll");

        private static string Configuration() =>
            Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!);

        private static string SingleFileUnder(string root, string fileName)
        {
            var found = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).ToList()
                : new List<string>();
            if (found.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one '{fileName}' under '{root}', found {found.Count}.");
            }

            return found[0];
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
