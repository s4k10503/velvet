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
            // __pycache__ is written beside a hook by anything that imports it, so its contents are
            // output rather than scripts. Counting them made this fixture fail for the presence of a
            // sibling fixture's bytecode, which says nothing about whether a guard is wired.
            var scripts = Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*", SearchOption.AllDirectories)
                .Select(RelativeToHookDirectory)
                .Where(script => !script.Contains("__pycache__", StringComparison.Ordinal))
                .ToList();
            Assume.That(scripts, Is.Not.Empty, "the hook directory is empty");

            // A file a wired hook sources is reached the same way a wired one is, so it is not an orphan.
            // Read out of the hooks rather than exempted by directory: a shared file that stops being
            // sourced is the same dead script as one that stops being wired.
            var sourced = string.Concat(scripts
                .Where(script => wired.Contains(script))
                .Select(script => File.ReadAllText(Path.GetFullPath(HookDirectory + "/" + script))));

            // Python imports name the module, not the file, so a shared file under lib/ is reached
            // by its stem and by nothing a wiring holds. Stem matching is looser than the full name
            // — a hook quoting a stem in prose would read as sourced when it is not — which is
            // accepted because reporting a genuinely imported file as an orphan blocks the sharing
            // lib/ exists for.
            var orphans = scripts
                .Where(script => !wired.Contains(script))
                .Where(script =>
                    !sourced.Contains(Path.GetFileName(script), StringComparison.Ordinal)
                    && !sourced.Contains(Path.GetFileNameWithoutExtension(script), StringComparison.Ordinal))
                .ToList();

            // Assert
            Assert.That(orphans, Is.Empty,
                $"nothing runs these, so whatever they guard is unguarded:\n{string.Join("\n", orphans)}");
        }

        // A file name a hook builds a path from, rather than one a wiring names. The lookbehind is
        // what forces a whole name instead of its tail — `merged.py` out of `branch_from_unmerged.py`
        // — and it must not list `/`, or a name written inside a path stops matching at every
        // position and the reference goes unread. Three of the five in the tree are path-qualified.
        private static readonly Regex NamedScriptPattern =
            new(@"(?<![.\w-])([A-Za-z0-9_][A-Za-z0-9_-]*\.(?:py|sh|bash|awk|ps1))", RegexOptions.Compiled);

        // Where a hook's siblings live. A name resolving to neither is either a typo or a script
        // somewhere new, and both want the failure: adding the directory here is what says the
        // second one was meant.
        private static readonly string[] SearchedDirectories = { HookDirectory, "scripts" };

        [Test]
        public void Given_TheHookScripts_When_EachScriptNameTheyBuildAPathFromIsResolved_Then_EveryOneExists()
        {
            // Arrange
            // The two tests above pair hooks against what runs them, and both stayed green while one
            // hook went on naming the shell file a port had replaced with a Python one. Nothing
            // compiles a name in a string, so the guard's whole deferral path went dead: it refused
            // the creation a live deferral had been armed for, and printed the instruction to arm
            // one.
            var known = new HashSet<string>(
                SearchedDirectories
                    .Select(Path.GetFullPath)
                    .Where(Directory.Exists)
                    .SelectMany(directory => Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    .Select(Path.GetFileName),
                StringComparer.Ordinal);
            var hooks = Directory
                .GetFiles(Path.GetFullPath(HookDirectory), "*.py", SearchOption.AllDirectories)
                .Where(hook => !hook.Contains("__pycache__", StringComparison.Ordinal))
                .ToList();
            Assume.That(hooks, Is.Not.Empty, "no hook scripts were found to read");

            // Act
            var dangling = (from hook in hooks
                            from Match match in NamedScriptPattern.Matches(File.ReadAllText(hook))
                            let named = match.Groups[1].Value
                            where !known.Contains(named)
                            select $"{RepoRelative(hook)} names {named}")
                .Distinct()
                .ToList();

            // Assert
            Assert.That(dangling, Is.Empty,
                "a name nothing compiles outlives the file it named, and the hook goes quiet rather than "
                + "failing:\n" + string.Join("\n", dangling));
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
