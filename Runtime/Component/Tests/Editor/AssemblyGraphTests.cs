using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the package asmdef reference graph so every assembly addition and dependency change is a
    /// reviewable diff, and blocks an all-platform assembly from referencing an Editor-only one in this
    /// package — a player-build failure Unity does not reject at import.
    /// </summary>
    [TestFixture]
    internal sealed class AssemblyGraphTests
    {
        // Regeneration is opt-in so a normal test run never rewrites the pin file.
        private const string UpdateEnvironmentVariable = "VELVET_UPDATE_ASSEMBLY_GRAPH";

        private static readonly string AssemblyGraphPath =
            Path.GetFullPath("Packages/com.velvet.core/AssemblyGraph.txt");

        [Test]
        public void Given_PackageAsmdefs_When_AssemblyGraphIsRendered_Then_ItMatchesAssemblyGraphTxt()
        {
            // Arrange
            var rendered = AssemblyGraph.Render().ToArray();

            // Act
            if (string.Equals(
                    Environment.GetEnvironmentVariable(UpdateEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                File.WriteAllLines(AssemblyGraphPath, rendered);
            }

            var onDisk = File.Exists(AssemblyGraphPath)
                ? File.ReadAllLines(AssemblyGraphPath)
                : Array.Empty<string>();
            var added = rendered.Except(onDisk, StringComparer.Ordinal).OrderBy(line => line, StringComparer.Ordinal)
                .ToArray();
            var removed = onDisk.Except(rendered, StringComparer.Ordinal)
                .OrderBy(line => line, StringComparer.Ordinal).ToArray();

            // Assert
            Assert.That(
                (added.Length, removed.Length),
                Is.EqualTo((0, 0)),
                BuildDriftMessage(added, removed));
        }

        [Test]
        public void Given_PackageAsmdefs_When_AllPlatformReferencesAreChecked_Then_NoneReferenceEditorOnlyPackageAssembly()
        {
            // Arrange
            var graph = AssemblyGraph.Load();

            // Act
            var violations = graph.FindAllPlatformToEditorOnlyViolations();

            // Assert
            Assert.That(
                (violations.Count, graph.AssemblyCount > 0),
                Is.EqualTo((0, true)),
                violations.Count > 0
                    ? "All-platform assemblies must not reference Editor-only package assemblies:\n"
                      + string.Join("\n", violations)
                    : null);
        }

        private static string BuildDriftMessage(IReadOnlyList<string> added, IReadOnlyList<string> removed)
        {
            var message = "Assembly graph drifted from Packages/com.velvet.core/AssemblyGraph.txt.";
            if (added.Count > 0)
            {
                message += "\n\nAdded:\n" + string.Join("\n", added);
            }

            if (removed.Count > 0)
            {
                message += "\n\nRemoved:\n" + string.Join("\n", removed);
            }

            message += "\n\nTo regenerate AssemblyGraph.txt, run:\n"
                       + "VELVET_UPDATE_ASSEMBLY_GRAPH=1 \"$UNITY\" -runTests -batchmode -projectPath \"$PWD\" "
                       + "-testPlatform EditMode -testFilter Velvet.Tests.AssemblyGraphTests";
            return message;
        }
    }

    internal static class AssemblyGraph
    {
        private const string PackageRoot = "Packages/com.velvet.core/";

        public static AssemblyGraphSnapshot Load() =>
            new(LoadAssemblyDefinitions());

        public static IReadOnlyList<string> Render() =>
            Load().RenderLines();

        private static List<AssemblyDefinitionInfo> LoadAssemblyDefinitions()
        {
            var assemblies = new List<AssemblyDefinitionInfo>();
            foreach (var entry in DocumentationCorpus.RepoEntries(includeClaude: false).Where(
                         path => path.StartsWith(PackageRoot, StringComparison.Ordinal)
                                  && path.EndsWith(".asmdef", StringComparison.Ordinal)
                                  && File.Exists(path)))
            {
                var document = JsonUtility.FromJson<AsmdefDocument>(File.ReadAllText(entry));
                assemblies.Add(new AssemblyDefinitionInfo(
                    document.name,
                    document.references ?? Array.Empty<string>(),
                    document.includePlatforms ?? Array.Empty<string>()));
            }

            assemblies.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            return assemblies;
        }

        [Serializable]
        private sealed class AsmdefDocument
        {
            public string name;
            public string[] references;
            public string[] includePlatforms;
        }
    }

    internal sealed class AssemblyGraphSnapshot
    {
        private readonly IReadOnlyList<AssemblyDefinitionInfo> assemblies;
        private readonly Dictionary<string, AssemblyDefinitionInfo> byName;

        internal AssemblyGraphSnapshot(IReadOnlyList<AssemblyDefinitionInfo> assemblies)
        {
            this.assemblies = assemblies;
            byName = assemblies.ToDictionary(assembly => assembly.Name, StringComparer.Ordinal);
        }

        public int AssemblyCount => assemblies.Count;

        public IReadOnlyList<string> RenderLines() =>
            assemblies.Select(RenderLine).ToArray();

        public IReadOnlyList<string> FindAllPlatformToEditorOnlyViolations()
        {
            var violations = new List<string>();
            foreach (var assembly in assemblies)
            {
                if (!assembly.IsAllPlatforms)
                {
                    continue;
                }

                foreach (var reference in assembly.References)
                {
                    if (!byName.TryGetValue(reference, out var referenced))
                    {
                        continue;
                    }

                    if (referenced.IsEditorOnly)
                    {
                        violations.Add($"{assembly.Name} references {referenced.Name}");
                    }
                }
            }

            violations.Sort(StringComparer.Ordinal);
            return violations;
        }

        private static string RenderLine(AssemblyDefinitionInfo assembly)
        {
            var references = string.Join(
                ",",
                assembly.References.OrderBy(reference => reference, StringComparer.Ordinal));
            var platforms = assembly.IsAllPlatforms
                ? "all"
                : string.Join(
                    ",",
                    assembly.IncludePlatforms.OrderBy(platform => platform, StringComparer.Ordinal));
            return assembly.Name + " refs " + references + " platforms " + platforms;
        }
    }

    internal sealed class AssemblyDefinitionInfo
    {
        public AssemblyDefinitionInfo(string name, IReadOnlyList<string> references, IReadOnlyList<string> includePlatforms)
        {
            Name = name;
            References = references;
            IncludePlatforms = includePlatforms;
        }

        public string Name { get; }

        public IReadOnlyList<string> References { get; }

        public IReadOnlyList<string> IncludePlatforms { get; }

        public bool IsAllPlatforms => IncludePlatforms.Count == 0;

        public bool IsEditorOnly =>
            IncludePlatforms.Count > 0
            && IncludePlatforms.All(platform => platform == "Editor");
    }
}
