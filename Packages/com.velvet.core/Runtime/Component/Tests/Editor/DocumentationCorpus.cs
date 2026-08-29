using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Velvet.Tests
{
    /// <summary>
    /// The markdown files and repository paths these fixtures machine-check. One enumeration serves the
    /// fixtures that read from it, so neither the markdown set nor the walk can be widened without the
    /// others that share it following.
    /// </summary>
    internal static class DocumentationCorpus
    {
        // Unity's CWD during a test run is the project root (see CLAUDE.md), so these resolve the same way
        // whether the suite runs from the Editor or from -runTests batchmode.
        internal static string DocumentationDirectory =>
            Path.GetFullPath("Packages/com.velvet.core/Documentation~");

        // Every markdown file under the walked roots, repo-relative and slash-separated — the same
        // spelling RepoEntries yields, which is what a caller reads and reports. Derived from the walk
        // rather than listed, so a document added under a walked root is scanned without anyone
        // remembering to add it: the list this replaced named four files beside a Documentation~ glob and
        // left fourteen documents unread, one of them naming two types that no longer exist.
        internal static IEnumerable<string> Files() =>
            RepoEntries(includeClaude: true)
                .Where(entry => entry.EndsWith(".md", StringComparison.Ordinal) && File.Exists(entry));

        // Every entry under the walked roots, repo-relative and slash-separated. includeClaude adds
        // .claude/skills and agent definitions to the tree; worktrees is skipped there because each is a
        // full checkout of this repository and would report its own copy of every path.
        internal static IReadOnlyList<string> RepoEntries(bool includeClaude) =>
            (includeClaude ? ClaudeAwareWalk : DocumentationWalk).Value;

        /// <summary>Top-level directories holding tracked markdown that the walk does not reach.</summary>
        /// <remarks>
        /// The walk is rooted, so a document under a directory nobody added to the roots is scanned by
        /// nothing and no drift guard sees it.
        /// <para>
        /// Asked of what git tracks rather than of the filesystem. A developer machine carries untracked
        /// directories a runner does not — a scratch note, a vendored tool, an agent harness — and the
        /// filesystem reading reported one of those exactly as it reports a documentation root somebody
        /// forgot, which are the two answers that must differ. Measured: an untracked `.agents/` holding
        /// two documents reddened this here and left it green in CI, where only tracked files exist.
        /// </para>
        /// <para>
        /// Nothing is lost by the narrowing. A documentation directory is tracked by the commit that adds
        /// it, which is before CI and before review. The `.gitignore` reading it used to need goes with
        /// the filesystem walk: an ignored directory is untracked by construction. `IgnoredRoots` stays
        /// for its other reader.
        /// </para>
        /// <para>
        /// The listing is handed in rather than read here, because reading it is what
        /// `DocumentationDriftTests.TrackedFiles` already does — including the safe.directory argument a
        /// checkout the process does not own needs, which is every checkout the Unity job runs in.
        /// </para>
        /// </remarks>
        internal static IReadOnlyList<string> UnwalkedMarkdownRoots(IEnumerable<string> tracked)
        {
            var walked = new HashSet<string>(WalkedRoots(includeClaude: true), StringComparer.Ordinal);
            var found = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in tracked ?? Enumerable.Empty<string>())
            {
                if (!path.EndsWith(".md", StringComparison.Ordinal))
                {
                    continue;
                }
                var cut = path.IndexOf('/');
                if (cut <= 0)
                {
                    // A document at the top level sits under no directory, so it names no unwalked root.
                    continue;
                }
                var root = path.Substring(0, cut);
                if (walked.Contains(root) || BaseUnwalkedDirectories.Contains(root))
                {
                    continue;
                }
                found.Add(root);
            }

            return found.ToList();
        }

        // Unity's own template writes /[Ll]ibrary/, so a root is a character class rather than a name;
        // one alternative compared case-insensitively covers both spellings.
        private static readonly Regex CharacterClassPattern = new(@"\[(\w)\w*\]", RegexOptions.Compiled);

        /// <remarks>
        /// Derived from .gitignore rather than listed, because a hand-written list would go red on a
        /// machine carrying an artifact CI does not — the asymmetry UnwalkedMarkdownRoots names above.
        /// <para>
        /// A nested pattern contributes its first segment alone, so a pattern under a walked root puts that
        /// root in here: docs and ProjectSettings are both in this set, and neither is ignored.
        /// </para>
        /// <para>
        /// WorkflowTriggerCoverageTests reads this rather than deriving it again: two answers to one question
        /// is the drift that fixture pair exists to report.
        /// </para>
        /// </remarks>
        /// <summary>Which of <paramref name="paths"/> git ignores, asked of git rather than derived.
        /// </summary>
        /// <remarks>
        /// <see cref="IgnoredRoots"/> answers a different question: it reduces each line to its first
        /// segment, which is what a caller enumerating top-level directories needs and is wrong for a
        /// caller asking whether one path is ignored — `docs/api/` there contributes `docs`, a directory
        /// the repository does not ignore. Anchoring, negation and wildcards are `.gitignore` semantics,
        /// and git is what implements them. One call for the whole set rather than one per path.
        /// </remarks>
        internal static HashSet<string> IgnoredAmong(IEnumerable<string> paths)
        {
            var asked = paths.Distinct(StringComparer.Ordinal).ToList();
            if (asked.Count == 0)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = Path.GetFullPath("."),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // Same ownership argument the tracked-listing read takes: a checkout the process does
            // not own is one git refuses to read at all, and a refusal here reports nothing ignored,
            // which is a silence that lets an ignored path through as a covered one.
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("safe.directory=" + Path.GetFullPath("."));
            start.ArgumentList.Add("check-ignore");
            start.ArgumentList.Add("--stdin");

            using var process = Process.Start(start);
            if (process == null)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var path in asked)
            {
                process.StandardInput.WriteLine(path);
            }
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd();
            var declined = process.StandardError.ReadToEnd();
            process.WaitForExit();
            // Exit 1 is "none of these is ignored", which is an answer. Anything above it is git
            // declining, and a decline read as an empty answer lets every ignored path through as one a
            // trigger has to cover.
            if (process.ExitCode > 1)
            {
                throw new InvalidOperationException(
                    $"git check-ignore declined to answer for {asked.Count} path(s): {declined.Trim()}");
            }

            return output.Split('\n')
                .Select(line => line.Trim().Replace('\\', '/'))
                .Where(line => line.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
        }

        internal static HashSet<string> IgnoredRoots() =>
            File.ReadAllLines(Path.GetFullPath(".gitignore"))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)
                               && !line.StartsWith("!", StringComparison.Ordinal))
                .Select(line => line.Trim('/').Split('/')[0])
                .Where(root => root.Length > 0 && !root.Contains('*'))
                .Select(root => CharacterClassPattern.Replace(root, match => match.Groups[1].Value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] BaseWalkedRoots =
            { "Packages", "Assets", ".github", "scripts", "ProjectSettings", "docs" };

        private static readonly string[] ClaudeAwareRoots = BaseWalkedRoots.Append(".claude").ToArray();

        /// <remarks>
        /// One derivation for the three readers — the walk, the unwalked-root report, and the caller asking
        /// which files the walk was supposed to reach. A reader deriving it again stops answering for the
        /// walk the day a root is added.
        /// </remarks>
        internal static IReadOnlyList<string> WalkedRoots(bool includeClaude) =>
            includeClaude ? ClaudeAwareRoots : BaseWalkedRoots;

        // Tool-written directories, matched on the basename wherever it appears.
        // StrykerOutput is in this list because a mutation report is a couple of megabytes of source
        // excerpts, it is gitignored, and it survives the run that made it. One left over from three days
        // earlier put the word this fixture was asked about into the corpus, so the check passed on the
        // machine that had it and failed on CI.
        // .pytest_cache is the same shape, and it is in this list rather than left to .gitignore: Walk
        // below reads no ignore file, so an entry there cannot keep anything out of the corpus.
        // obj and bin stay basenames rather than joining the path list below, because each occurs under
        // docs/ and once per project under Generators~: a path list would need a new pair the day a project
        // is added, and an entry nobody adds is a directory the walk starts reading as prose.
        private static readonly HashSet<string> BaseUnwalkedDirectories =
            new()
            {
                ".git", "Library", "Temp", "Logs", "Build", "UserSettings",
                "obj", "bin", "StrykerOutput", ".pytest_cache",
            };

        // A mutation campaign's own record, which holds the original text of the file it is mutating,
        // comments and all. StripProse takes nothing from a .json, so while a run is in flight those
        // comments are read as code — and a mutant of a file whose comments spell an allowlisted name is
        // recorded killed by the drift failure that follows, whatever the tests of the line it cut do.
        // The name is derived where it is pinned, the way the paths below are:
        // Given_ACampaignHoldsItsRecord_... in DocumentationDriftTests reads it off mutation_check.py.
        private static readonly HashSet<string> BaseUnwalkedFiles =
            new(StringComparer.Ordinal) { "MUTATION_IN_PROGRESS.json" };

        // Build output under docs/, each a generated copy that outlives the sources it was made from: DocFX
        // writes runtime type names into api/ and _site/, which hold that spelling until docs/build.py is
        // re-run, and docs/build.py stages a copy of every guide into guides/ before invoking DocFX, so a
        // walk reaching that one holds each guide twice and holds a copy staged before a rename until the
        // next build.
        // Matched on the repo-relative path rather than the basename, because `guides` and `api` are
        // plausible names for prose elsewhere in the tree and a basename entry stops the walk at every
        // directory carrying it: excluding `guides` that way would drop a Documentation~/guides/ in silence.
        // Each of the three is derived where it is pinned, in DocumentationDriftTests, rather than trusted
        // here: Given_TheDocBuildStagedTheGuides_... reads the staging path off docs/build.py and
        // Given_TheDocfxGeneratedDirectories_... reads the other two off docs/docfx.json, so a rename in
        // either owner fails there instead of re-opening the leak in silence.
        private static readonly HashSet<string> BaseUnwalkedPaths =
            new(StringComparer.Ordinal) { "docs/guides", "docs/api", "docs/_site" };

        private static readonly Lazy<List<string>> DocumentationWalk = new(() => Walk(includeClaude: false));
        private static readonly Lazy<List<string>> ClaudeAwareWalk = new(() => Walk(includeClaude: true));

        private static List<string> Walk(bool includeClaude)
        {
            // The walk is rooted rather than filtered because this repo's own workflow puts full checkouts of
            // itself under .claude/worktrees/ while a suite runs: an exclusion list has to anticipate every such
            // directory, and one it misses resolves every name the docs carry against a copy of the very sources
            // the rename was supposed to remove them from — leaving the check structurally green on a developer
            // machine and red only on CI.
            var walkedRoots = WalkedRoots(includeClaude);
            var unwalked = new HashSet<string>(BaseUnwalkedDirectories);
            unwalked.UnionWith(BaseUnwalkedFiles);
            if (includeClaude)
            {
                unwalked.Add("worktrees");
            }

            var root = Path.GetFullPath(".");
            var entries = new List<string>();
            // Depth-bounded rather than visited-set guarded: a symlink cycle yields a FRESH path string every
            // lap, so a set keyed on the path never closes it, and the link-resolving API that would is not in
            // Unity's target framework. The deepest real directory here sits at 7, so a bound of 32 stops a
            // cycle while leaving room the layout will not reach.
            const int maxDepth = 32;
            var pending = new Stack<(string Directory, int Depth)>(
                walkedRoots.Select(Path.GetFullPath).Where(Directory.Exists).Select(walked => (walked, 1)));
            entries.AddRange(walkedRoots.Where(walked => Directory.Exists(Path.GetFullPath(walked))));
            entries.AddRange(Directory.EnumerateFiles(root)
                .Select(file => Path.GetFileName(file))
                .Where(name => !unwalked.Contains(name)));
            while (pending.Count > 0)
            {
                var (directory, depth) = pending.Pop();
                string[] children;
                try
                {
                    children = Directory.GetFileSystemEntries(directory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var entry in children)
                {
                    var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                    if (unwalked.Contains(Path.GetFileName(entry)) || BaseUnwalkedPaths.Contains(relative))
                    {
                        continue;
                    }
                    entries.Add(relative);
                    if (Directory.Exists(entry) && depth < maxDepth)
                    {
                        pending.Push((entry, depth + 1));
                    }
                }
            }
            return entries;
        }
    }
}
