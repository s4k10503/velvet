using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Machine-checks the shipped Documentation~ guides (plus both README.md files and CLAUDE.md) against the actual
    /// runtime API surface, so a doc referencing a renamed/removed <c>V.*</c> factory or <c>Hooks.*</c> hook,
    /// or an index that has drifted from the files on disk, fails a test instead of shipping silently wrong.
    /// Each check pins a failure mode that has actually shipped: a guide referencing a never-implemented
    /// factory, a hook table drifting from the real hook surface, and an index missing real guide files.
    /// </summary>
    [TestFixture]
    internal sealed class DocumentationDriftTests
    {
        // "V.X" appears only in react-migration.md's React-syntax comparison tables (and its mirrored rows in
        // both README.md files) as a meta-syntactic placeholder standing in for an arbitrary user component —
        // the same role "<X/>" plays in the JSX column of the same row — not a reference to a real V.* factory.
        private static readonly HashSet<string> VReferenceAllowlist = new() { "X" };

        private static readonly Regex VReferencePattern = new(@"\bV\.([A-Z][A-Za-z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex BacktickSpanPattern = new(@"`([^`\n]*)`", RegexOptions.Compiled);
        private static readonly Regex HookReferencePattern = new(@"\bUse[A-Z]\w*", RegexOptions.Compiled);
        private static readonly Regex DocLinkPattern = new(@"\]\(([A-Za-z0-9_.-]+\.md)\)", RegexOptions.Compiled);

        // Unity's CWD during a test run is the project root (see CLAUDE.md), so these resolve the same way
        // whether the suite runs from the Editor or from -runTests batchmode.
        private static string DocumentationDirectory => Path.GetFullPath("Packages/com.velvet.core/Documentation~");

        // Yields (path, label) pairs. The label disambiguates the two identically-named README.md files
        // (repo root vs the package) in failure messages, since Path.GetFileName alone collapses both to
        // the same string.
        private static IEnumerable<(string Path, string Label)> TargetMarkdownFiles()
        {
            foreach (var file in Directory.GetFiles(DocumentationDirectory, "*.md"))
            {
                yield return (file, "Documentation~/" + Path.GetFileName(file));
            }
            yield return (Path.GetFullPath("README.md"), "README.md (repo root)");
            yield return (Path.GetFullPath("Packages/com.velvet.core/README.md"), "Packages/com.velvet.core/README.md");
            yield return (Path.GetFullPath("CLAUDE.md"), "CLAUDE.md");
        }

        [Test]
        public void Given_DocumentationMarkdown_When_ScannedForVDotReferences_Then_EveryReferenceExistsOnV()
        {
            // Arrange — the real V surface: every public static factory (including ones woven into V by a
            // partial file like V.Mount.cs or by the Memoized<T1..T8> source generator) plus any public
            // nested type V might declare.
            var knownVMembers = new HashSet<string>(
                typeof(V).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name)
                    .Concat(typeof(V).GetNestedTypes(BindingFlags.Public).Select(t => t.Name)));

            // Act
            var unresolved = FindUnresolvedReferences(
                VReferencePattern, text => text, name => knownVMembers.Contains(name) || VReferenceAllowlist.Contains(name),
                prefix: "V.");

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation references V.* members that do not exist on typeof(V):\n" + string.Join("\n", unresolved));
        }

        [Test]
        public void Given_DocumentationMarkdown_When_ScannedForBacktickedHookReferences_Then_EveryReferenceExistsOnHooks()
        {
            // Arrange — restricting extraction to backtick spans excludes react-migration.md's prose describing
            // React's own lowercase `useXxx` hooks (which never matches Use[A-Z] anyway) while also skipping
            // incidental "Use" occurrences in running text that are not meant as an API reference.
            var knownHooks = new HashSet<string>(
                typeof(Hooks).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name));

            // Act
            var unresolved = FindUnresolvedReferences(
                HookReferencePattern,
                text => string.Join("\n", BacktickSpanPattern.Matches(text).Select(m => m.Groups[1].Value)),
                knownHooks.Contains,
                prefix: string.Empty);

            // Assert
            Assert.That(unresolved, Is.Empty,
                "Documentation references Hooks.* members that do not exist on typeof(Hooks):\n" + string.Join("\n", unresolved));
        }

        [Test]
        public void Given_DocumentationReadmeIndex_When_ComparedAgainstDirectoryContents_Then_LinksAndFilesMatchExactly()
        {
            // Arrange
            var readmePath = Path.Combine(DocumentationDirectory, "README.md");
            var linkedFiles = new HashSet<string>(
                DocLinkPattern.Matches(File.ReadAllText(readmePath)).Select(m => m.Groups[1].Value));
            var actualFiles = new HashSet<string>(
                Directory.GetFiles(DocumentationDirectory, "*.md")
                    .Select(Path.GetFileName)
                    .Where(name => name != "README.md"));

            // Act
            var missingFromIndex = actualFiles.Except(linkedFiles).Select(f => $"missing from index: {f}");
            var deadIndexLinks = linkedFiles.Except(actualFiles).Select(f => $"dead index link (no such file): {f}");
            var diff = missingFromIndex.Concat(deadIndexLinks).ToList();

            // Assert
            Assert.That(diff, Is.Empty,
                "Documentation~/README.md's index is out of sync with the directory's actual .md files:\n" + string.Join("\n", diff));
        }

        // Shared scan: for every target markdown file, project its text through `select` (identity for the
        // V.* scan, backtick-span extraction for the Hooks scan), extract every reference `pattern` matches,
        // and report ones `isKnown` rejects as "file: reference" so a failure message names both the
        // offending file and the exact unresolved identifier.
        private static List<string> FindUnresolvedReferences(
            Regex pattern, Func<string, string> select, Func<string, bool> isKnown, string prefix)
        {
            var unresolved = new List<string>();
            foreach (var (path, label) in TargetMarkdownFiles())
            {
                var haystack = select(File.ReadAllText(path));
                foreach (Match match in pattern.Matches(haystack))
                {
                    var name = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    if (isKnown(name))
                    {
                        continue;
                    }
                    unresolved.Add($"{label}: {prefix}{name}");
                }
            }
            return unresolved.Distinct().ToList();
        }
    }
}
