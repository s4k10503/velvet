using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// A project on disk that the solution does not name is never restored, never built and never analyzed,
    /// so every guard that asks whether its sources satisfy a rule answers about sources no compiler read.
    /// The suite stays green for the same reason it would if the project were deleted, which is the failure
    /// mode the opt-in guards were added to close — one level further out, where they cannot see it.
    /// </summary>
    /// <remarks>
    /// Membership alone is not enough: a solution keeps a project's configuration mapping separately, and one
    /// listed without a <c>Build.0</c> row for a configuration is skipped in exactly that configuration while
    /// still appearing in every listing of the solution's projects.
    /// <para>
    /// The reverse direction — a solution entry naming a project that is not on disk — is deliberately not
    /// asserted here. MSBuild refuses to load such a solution, so the whole suite fails to build and no
    /// assertion in this file is ever reached; a case for it would be one that cannot report.
    /// </para>
    /// </remarks>
    public sealed class SolutionProjectMembershipDriftTests
    {
        // A project mapped into one configuration and not the other builds under a plain `dotnet test` and
        // not under `-c Release`, or the reverse. CI runs only the latter, so a gap in Release is the one
        // nobody meets locally and a gap in Debug is the one nobody meets on CI.
        private static readonly string[] Configurations = { "Debug", "Release" };

        [Fact]
        public void Given_TheProjectsOnDisk_When_ComparedAgainstTheSolution_Then_EveryOneIsAMember()
        {
            // Arrange
            var members = SolutionProjects(File.ReadAllText(SolutionPath()))
                .Select(entry => entry.Path)
                .ToHashSet(StringComparer.Ordinal);

            // Act
            var absent = ProjectsOnDisk()
                .Where(project => !members.Contains(project))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal(Array.Empty<string?>(), absent);
        }

        [Fact]
        public void Given_TheSolutionsProjects_When_TheirConfigurationMappingIsRead_Then_EveryOneBuildsInEvery()
        {
            // Arrange
            var solution = File.ReadAllText(SolutionPath());

            // Act
            var unbuilt = SolutionProjects(solution)
                .SelectMany(entry => Configurations.Select(configuration => (entry.Guid, configuration)))
                .Where(pair => !BuildsIn(solution, pair.Guid, pair.configuration))
                .Select(pair => $"{pair.Guid} {pair.configuration}")
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal(Array.Empty<string>(), unbuilt);
        }

        [Fact]
        public void Given_TheProjectSearch_When_Run_Then_ItFoundProjectsToCheck()
        {
            // The membership comparison passes over an empty set as readily as over a satisfied one, so the
            // population it walks is pinned rather than left implied.
            // Arrange
            var projects = ProjectsOnDisk();

            // Act
            var count = projects.Count;

            // Assert
            Assert.NotEqual(0, count);
        }

        [Fact]
        public void Given_TheSolutionParse_When_Run_Then_ItFoundOneEntryPerProjectOnDisk()
        {
            // Both guards above read the same parse, so an entry it drops is exempt from both at once while
            // each still reports an empty difference.
            // Arrange
            var solution = File.ReadAllText(SolutionPath());

            // Act
            var entries = SolutionProjects(solution).Count;

            // Assert
            Assert.Equal(ProjectsOnDisk().Count, entries);
        }

        [Fact]
        public void Given_ASolutionFolderEntry_When_Parsed_Then_ItIsNotCountedAsAProject()
        {
            // A solution folder is written as a `Project` entry too, with the folder's own name where a
            // project's relative path goes. Counting one would demand build rows that no folder has and would
            // put a directory into the membership comparison.
            // Arrange
            var solution = "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"src\", \"src\", "
                + "\"{827E0CD3-B72D-47B6-A68D-7590B98EB39B}\"\nEndProject\n";

            // Act
            var entries = SolutionProjects(solution);

            // Assert
            Assert.Empty(entries);
        }

        private static string SolutionPath() =>
            Path.Combine(SolutionPaths.GeneratorsRoot(), "Velvet.SourceGenerators.sln");

        /// <summary>
        /// Every project file the repository carries under the solution's own directory. <c>bin</c> and
        /// <c>obj</c> are build output rather than sources, and are regenerated by a build that already
        /// happened, so a project found there says nothing about what the solution should name.
        /// </summary>
        private static IReadOnlyCollection<string> ProjectsOnDisk() =>
            Directory.EnumerateFiles(SolutionPaths.GeneratorsRoot(), "*.csproj", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.Ordinal);

        private static bool IsBuildOutput(string path) =>
            path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

        /// <summary>
        /// Each project entry as the absolute path it names and the identifier its configuration rows key on,
        /// read in one pass so the two guards above cannot come to disagree about what the solution contains.
        /// Paths are written with backslashes whatever wrote the file.
        /// </summary>
        private static IReadOnlyList<(string Path, string Guid)> SolutionProjects(string solution) =>
            Regex.Matches(solution, @"^Project\(""\{[^}]+\}""\) = ""[^""]+"", ""([^""]+)"", ""(\{[^}]+\})""",
                    RegexOptions.Multiline)
                .Select(match => (
                    Relative: match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar),
                    Guid: match.Groups[2].Value))
                .Where(entry => entry.Relative.EndsWith(".csproj", StringComparison.Ordinal))
                .Select(entry => (
                    Path.GetFullPath(Path.Combine(SolutionPaths.GeneratorsRoot(), entry.Relative)), entry.Guid))
                .ToList();

        private static bool BuildsIn(string solution, string guid, string configuration) =>
            solution.Contains($"{guid}.{configuration}|Any CPU.Build.0 = ", StringComparison.Ordinal);
    }
}
