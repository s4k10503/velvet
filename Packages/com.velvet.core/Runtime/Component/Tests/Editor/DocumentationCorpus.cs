using System;
using System.Collections.Generic;
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

        /// <summary>Top-level directories holding markdown that the walk does not reach.</summary>
        /// <remarks>
        /// The walk is rooted, so a document under a directory nobody added to the roots is scanned by
        /// nothing and no drift guard sees it. Asking the question over every top-level directory cannot be
        /// done: a developer machine carries untracked ones a runner does not, so such a check would be red
        /// here and green in CI — which is the asymmetry the rooting exists to avoid, one level up.
        /// <para>
        /// The population is therefore the directories that hold markdown, minus what .gitignore already
        /// excludes. Both halves are read off the repository: the ignore file is where a machine-local
        /// directory is already declared, so one appearing tomorrow excuses itself, and a root holding a
        /// document does not.
        /// </para>
        /// </remarks>
        internal static IReadOnlyList<string> UnwalkedMarkdownRoots()
        {
            var ignored = IgnoredRoots();
            var walked = new HashSet<string>(BaseWalkedRoots.Append(".claude"), StringComparer.Ordinal);
            var found = new List<string>();
            foreach (var directory in Directory.EnumerateDirectories(Path.GetFullPath(".")))
            {
                var name = Path.GetFileName(directory);
                if (walked.Contains(name) || BaseUnwalkedDirectories.Contains(name) || ignored.Contains(name))
                {
                    continue;
                }
                if (Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories).Any())
                {
                    found.Add(name);
                }
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        // Unity's own template writes /[Ll]ibrary/, so a root is a character class rather than a name;
        // one alternative compared case-insensitively covers both spellings.
        private static readonly Regex CharacterClassPattern = new(@"\[(\w)\w*\]", RegexOptions.Compiled);

        private static HashSet<string> IgnoredRoots() =>
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

        // Build output and generated documentation: nothing a document names lives there, DocFX's api/ and
        // _site/ carry a stale copy of every runtime type name until docs/build.py is re-run, and Library
        // alone would make the walk the slowest thing in this fixture.
        // StrykerOutput is here for a different reason, and it is the reason to be strict about the rest:
        // a mutation report is a couple of megabytes of source excerpts, it is gitignored, and it survives
        // the run that made it. One left over from three days earlier put the word this fixture was asked
        // about into the corpus, so the check passed on the machine that had it and failed on CI.
        // .pytest_cache is the same shape, and it is in this list rather than left to .gitignore: Walk
        // below reads no ignore file, so an entry there cannot keep anything out of the corpus.
        private static readonly HashSet<string> BaseUnwalkedDirectories =
            new()
            {
                ".git", "Library", "Temp", "Logs", "Build", "UserSettings",
                "obj", "bin", "api", "_site", "StrykerOutput", ".pytest_cache",
            };

        private static readonly Lazy<List<string>> DocumentationWalk = new(() => Walk(includeClaude: false));
        private static readonly Lazy<List<string>> ClaudeAwareWalk = new(() => Walk(includeClaude: true));

        private static List<string> Walk(bool includeClaude)
        {
            // The walk is rooted rather than filtered because this repo's own workflow puts full checkouts of
            // itself under .claude/worktrees/ while a suite runs: an exclusion list has to anticipate every such
            // directory, and one it misses resolves every name the docs carry against a copy of the very sources
            // the rename was supposed to remove them from — leaving the check structurally green on a developer
            // machine and red only on CI.
            var walkedRoots = includeClaude
                ? BaseWalkedRoots.Append(".claude").ToArray()
                : BaseWalkedRoots;
            var unwalked = new HashSet<string>(BaseUnwalkedDirectories);
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
            entries.AddRange(Directory.EnumerateFiles(root).Select(file => Path.GetFileName(file)));
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
                    if (unwalked.Contains(Path.GetFileName(entry)))
                    {
                        continue;
                    }
                    entries.Add(Path.GetRelativePath(root, entry).Replace('\\', '/'));
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
