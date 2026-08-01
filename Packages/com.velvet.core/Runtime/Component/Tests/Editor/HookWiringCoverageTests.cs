using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pairs the hook scripts in <c>.claude/hooks</c> with the two files that run them — the project
    /// settings and the agent definitions — in both directions. A hook is wired by path, so a rename
    /// leaves the wiring naming nothing and a hook leaves the tree referenced by nothing, and neither
    /// shows up as a failure: a guard that stops being invoked reports exactly what a guard that passes
    /// reports.
    /// </summary>
    [TestFixture]
    internal sealed class HookWiringCoverageTests
    {
        private const string HookDirectory = ".claude/hooks";

        // Settings and agent frontmatter are where a hook is given an event to fire on. A skill or a guide
        // may name one in prose, and naming it there does not run it, so those are not read here — counting
        // a prose mention as wiring is how an unwired hook would pass.
        private static IEnumerable<string> WiringFiles()
        {
            yield return ".claude/settings.json";
            var agents = Path.GetFullPath(".claude/agents");
            if (Directory.Exists(agents))
            {
                foreach (var file in Directory.GetFiles(agents, "*.md"))
                {
                    yield return file;
                }
            }
        }

        private static readonly Regex HookReferencePattern =
            new(@"\.claude/hooks/([A-Za-z0-9_./-]+)", RegexOptions.Compiled);

        [Test]
        public void Given_TheHookWiring_When_EachReferencedPathIsResolved_Then_EveryOneNamesAFileThatExists()
        {
            // Arrange
            var wiring = ReadWiring();
            Assume.That(wiring, Is.Not.Empty, "no hook reference was found to check");

            // Act
            var missing = wiring
                .Where(reference => !File.Exists(Path.GetFullPath(HookDirectory + "/" + reference.Name)))
                .Select(reference => $"{reference.Source} names {HookDirectory}/{reference.Name}")
                .Distinct()
                .ToList();

            // Assert
            Assert.That(missing, Is.Empty,
                "a hook is wired by path, so one that names nothing never fires:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Given_TheHookDirectory_When_EachScriptIsTracedBack_Then_EveryOneIsWiredOrSourced()
        {
            // Arrange
            var wired = new HashSet<string>(ReadWiring().Select(reference => reference.Name), StringComparer.Ordinal);
            var scripts = Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*", SearchOption.AllDirectories)
                .Select(RelativeToHookDirectory)
                .ToList();
            Assume.That(scripts, Is.Not.Empty, "the hook directory is empty");

            // A file a wired hook sources is reached the same way a wired one is, so it is not an orphan.
            // Read out of the hooks rather than exempted by directory: a shared file that stops being
            // sourced is the same dead script as one that stops being wired.
            var sourced = string.Concat(scripts
                .Where(script => wired.Contains(script))
                .Select(script => File.ReadAllText(Path.GetFullPath(HookDirectory + "/" + script))));

            // Act
            var orphans = scripts
                .Where(script => !wired.Contains(script))
                .Where(script => !sourced.Contains(Path.GetFileName(script), StringComparison.Ordinal))
                .ToList();

            // Assert
            Assert.That(orphans, Is.Empty,
                $"nothing runs these, so whatever they guard is unguarded:\n{string.Join("\n", orphans)}");
        }

        private static List<(string Name, string Source)> ReadWiring() =>
            (from file in WiringFiles()
             let path = Path.GetFullPath(file)
             where File.Exists(path)
             from Match match in HookReferencePattern.Matches(File.ReadAllText(path))
             select (match.Groups[1].Value, RepoRelative(path))).ToList();

        private static string RelativeToHookDirectory(string path) =>
            Path.GetRelativePath(Path.GetFullPath(HookDirectory), path).Replace('\\', '/');

        private static string RepoRelative(string path) =>
            Path.GetRelativePath(Path.GetFullPath("."), path).Replace('\\', '/');
    }
}
