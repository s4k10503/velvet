using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the ignore file against the bootstrap scene a PlayMode run writes into <c>Assets/</c>, and
    /// against what the same directory ships. A rule that covers neither leaves two files nobody wrote
    /// reporting as untracked; one that covers both by naming the directory hides the next file added to
    /// the starter sample beside them.
    /// </summary>
    /// <remarks>
    /// The scene's path is read out of the test framework's own source rather than spelled here, so a
    /// release that renames it reddens this instead of leaving a rule that matches nothing. Whether a
    /// path is ignored is put to git, in a repository this fixture builds around a copy of this
    /// repository's ignore file. Asked of this worktree, git would read the machine's own exclusions
    /// beside it, and the copy is what a contributor's clone gets.
    /// </remarks>
    [TestFixture]
    internal sealed class TestRunArtifactIgnoreTests
    {
        private const string TestFrameworkPackage = "com.unity.test-framework";

        private const string IgnoreFile = ".gitignore";
        private const string AssetsRoot = "Assets";

        // Excluded for the reason StarterSampleShippingTests gives beside the same name.
        private const string OsNoiseFile = ".DS_Store";

        // `=(?!=)` so a comparison against the same field is not read as an assignment to it.
        private static readonly Regex ScenePathAssignment =
            new(@"InitTestScenePath\s*=(?!=)([^;]*);", RegexOptions.Compiled);

        private static readonly Regex StringLiteral = new("\"([^\"]*)\"", RegexOptions.Compiled);

        [Test]
        public void Given_TheBootstrapScenePathTheTestFrameworkWrites_When_PutToTheIgnoreFile_Then_TheSceneAndItsMetaAreHidden()
        {
            // Arrange
            var written = WrittenByARun();

            // Act
            var reported = written.Except(Ignored(written)).OrderBy(path => path, StringComparer.Ordinal);

            // Assert — the count rides along because a reading that yields no path leaves an empty set,
            // and an empty set has nothing left reporting as untracked.
            Assert.That((written.Count, string.Join("\n", reported)), Is.EqualTo((2, string.Empty)),
                written.Count == 0
                    ? $"no bootstrap scene path came out of {TestFrameworkPackage}, so none was put to {IgnoreFile}"
                    : $"{IgnoreFile} leaves a path a PlayMode run writes into {AssetsRoot}/ reporting as untracked");
        }

        // GREEN_ON_BASE(characterization): the sample tree the narrow rule leaves visible, which an
        // Assets-wide rule would hide instead.
        [Test]
        public void Given_EveryFileUnderAssetsThisRepositoryCarries_When_PutToTheIgnoreFile_Then_NoneIsHidden()
        {
            // Arrange
            var carried = Carried();

            // Act
            var hidden = Ignored(carried).OrderBy(path => path, StringComparer.Ordinal);

            // Assert — the count rides along because an empty walk reports nothing hidden.
            Assert.That((carried.Count > 0, string.Join("\n", hidden)), Is.EqualTo((true, string.Empty)),
                carried.Count == 0
                    ? $"no file came out of the walk of {AssetsRoot}/, so none was put to {IgnoreFile}"
                    : $"{IgnoreFile} hides a file found under {AssetsRoot}/");
        }

        /// <summary>The paths a PlayMode run puts in the working tree, as the framework spells them.</summary>
        private static IReadOnlyCollection<string> WrittenByARun()
        {
            var literals = ScenePathLiterals();
            if (literals.Count == 0)
            {
                return Array.Empty<string>();
            }

            var scene = string.Join(Guid.NewGuid().ToString(), literals);
            return new[] { scene, scene + ".meta" };
        }

        /// <summary>
        /// The string literals the framework builds its bootstrap scene path out of, in source order.
        /// </summary>
        /// <remarks>
        /// More than one assignment would mean two spellings to cover and one of them unread, so the
        /// reading is withdrawn rather than narrowed to whichever the walk reached first.
        /// </remarks>
        private static IReadOnlyList<string> ScenePathLiterals()
        {
            var package = PackageInfo.FindForPackageName(TestFrameworkPackage);
            if (package == null || !Directory.Exists(package.resolvedPath))
            {
                return Array.Empty<string>();
            }

            var assignments =
                (from file in Directory.EnumerateFiles(package.resolvedPath, "*.cs", SearchOption.AllDirectories)
                 from Match assignment in ScenePathAssignment.Matches(File.ReadAllText(file))
                 select StringLiteral.Matches(assignment.Groups[1].Value)
                     .Select(literal => literal.Groups[1].Value)
                     .ToList()).ToList();

            return assignments.Count == 1 ? assignments[0] : Array.Empty<string>();
        }

        /// <summary>What is left under <c>Assets/</c> once the OS's files and a run's are set aside.</summary>
        /// <remarks>
        /// Walked on disk rather than read from <c>git ls-files</c>, which would have to read this
        /// checkout's own repository — the reading <see cref="Ignored"/> is built to avoid.
        /// </remarks>
        private static IReadOnlyCollection<string> Carried()
        {
            var root = Path.GetFullPath(AssetsRoot);
            var stem = ScenePathLiterals().FirstOrDefault() ?? string.Empty;
            return (from file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    let relative = AssetsRoot + "/" + Path.GetRelativePath(root, file).Replace('\\', '/')
                    where Path.GetFileName(file) != OsNoiseFile
                    where stem.Length == 0 || !relative.StartsWith(stem, StringComparison.Ordinal)
                    select relative).ToList();
        }

        /// <summary>Which of these paths this repository's ignore file hides, according to git.</summary>
        private static IReadOnlyCollection<string> Ignored(IReadOnlyCollection<string> paths)
        {
            if (paths.Count == 0)
            {
                return Array.Empty<string>();
            }

            var root = Path.Combine(Path.GetTempPath(), "velvet-ignore-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                if (Git(root, out _, "init", "-q") != 0)
                {
                    throw new InvalidOperationException($"git built no repository under {root}");
                }

                File.Copy(Path.GetFullPath(IgnoreFile), Path.Combine(root, IgnoreFile));
                // Emptied rather than left as git wrote it, so the copy above is what answers.
                var exclude = Path.Combine(root, ".git", "info", "exclude");
                Directory.CreateDirectory(Path.GetDirectoryName(exclude));
                File.WriteAllText(exclude, string.Empty);

                var arguments = new List<string>
                {
                    "-c", "core.excludesFile=" + Path.Combine(root, "no-such-global-excludes"),
                    "check-ignore", "--",
                };
                arguments.AddRange(paths);

                // 1 is the answer that none of them is ignored; any other non-zero status is a reading
                // nobody took, which nothing below may be built on.
                var status = Git(root, out var output, arguments.ToArray());
                if (status != 0 && status != 1)
                {
                    throw new InvalidOperationException($"git check-ignore exited {status}");
                }

                return output.Replace("\r\n", "\n").Split('\n')
                    .Where(line => line.Length > 0)
                    .ToHashSet(StringComparer.Ordinal);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static int Git(string root, out string output, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(root);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(30000) ? process.ExitCode : -1;
        }
    }
}
